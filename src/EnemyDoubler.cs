using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace IncreasedEnemyCount
{
    /// <summary>
    /// Clones every eligible enemy in a freshly loaded area.
    ///
    /// Outward places all of its enemies in the scene ahead of time (including the roaming
    /// "squads", which sit in a reserve and are switched on later), so the mod works by copying
    /// those placed enemies the moment the scene finishes loading - after Awake, before Start.
    /// The copy is made under an inactive holder so none of its scripts run until its identity
    /// (UID, network view ID, etc.) has been made unique.
    ///
    /// Everything here is deterministic: given the same scene and the same settings, every
    /// player's machine produces the same copies with the same UIDs and network view IDs. That is
    /// what makes online co-op work (see NetSync).
    /// </summary>
    public class EnemyDoubler : MonoBehaviour
    {
        internal static EnemyDoubler Instance;

        private const string CLONE_SUFFIX = "_x";

        /// <summary>
        /// Photon reads a view ID as (creating player number x 1000) + slot. The game, other mods and
        /// real players only ever use small player numbers, so copies get IDs in blocks belonging to
        /// player numbers from 2,000,000 up, which can never exist (Photon numbers players from 1,
        /// one per join). Each area gets its own run of 1,000 such numbers (up to 999,000 IDs), so
        /// copies from the previous area that are still being unloaded can never clash.
        /// The copies' owner is then set to 0, the same as the area's own enemies, so the host
        /// controls them and they don't belong to any real player.
        /// </summary>
        private const int COPY_OWNER_BASE = 2000000;
        private const int OWNERS_PER_AREA = 1000;
        private const int AREA_BLOCKS = 100; // 2,000,000 + 100 x 1,000 owners -> IDs stay below int.MaxValue

        private class CloneRecord
        {
            public string SceneName;
            public int CandidateIndex;
            public Character Original;
            public Character Clone;
            public string OriginalUID;
            public string CloneUID;
            public Vector3 Offset;
            public float Spacing;
            public int CopyIndex;
            public int TotalCopies;
            public bool OriginalWasActive;
        }

        private class SceneState
        {
            public string SceneName;
            public int CandidateCount;
            /// <summary>Candidate index -> how many copies were made of it.</summary>
            public Dictionary<int, int> CopyCounts = new Dictionary<int, int>();
            public bool Reconciled;
        }

        private readonly List<CloneRecord> m_records = new List<CloneRecord>();
        private readonly Dictionary<string, CloneRecord> m_recordsByCloneUID = new Dictionary<string, CloneRecord>();
        private SceneState m_currentScene;
        private float m_nextMirrorCheck;
        private float m_nextNetTick;

        private static readonly FieldInfo f_networkingPeer = AccessTools.Field(typeof(PhotonNetwork), "networkingPeer");
        private static readonly FieldInfo f_photonViewList = f_networkingPeer == null ? null : AccessTools.Field(f_networkingPeer.FieldType, "photonViewList");
        private static readonly FieldInfo f_aiStates = AccessTools.Field(typeof(CharacterAI), "m_aiStates");
        private static readonly FieldInfo f_aiStatesRoot = AccessTools.Field(typeof(CharacterAI), "m_aiStatesRoot");
        private static readonly FieldInfo f_nameLocKey = AccessTools.Field(typeof(Character), "m_nameLocKey");
        private static readonly FieldInfo f_name = AccessTools.Field(typeof(Character), "m_name");

        private void Awake()
        {
            Instance = this;
        }

        // ------------------------------------------------------------------ scene hooks

        internal void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                ProcessScene(scene);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Error while multiplying enemies in '{scene.name}': {e}");
            }
        }

        internal void OnSceneUnloaded(Scene scene)
        {
            PurgeDeadRecords();
        }

        private void ProcessScene(Scene scene)
        {
            var roots = scene.GetRootGameObjects();

            // ---- 1. Which enemies could be copied. Uses only things that are identical on every
            //         machine (components, factions), never names, so the list and its order match.
            var candidates = new List<Character>();
            foreach (var root in roots)
                foreach (var c in root.GetComponentsInChildren<Character>(true))
                    if (IsCandidate(c))
                        candidates.Add(c);
            if (candidates.Count == 0)
                return;

            // ---- 2. Which settings to use (in co-op: always the host's).
            var settings = NetSync.ResolveSettingsForScene(scene.name, out string settingsSource);
            if (settings == null)
            {
                Plugin.Log.LogMessage($"[{scene.name}] The host doesn't have Increased Enemy Count - leaving enemies alone.");
                m_currentScene = null;
                return;
            }

            if (settings.AnyCopies && NetSync.IsOnline && NetSync.IsHost && Plugin.RequireModOnAllPlayers.Value)
            {
                var missing = NetSync.PlayersWithoutMod();
                if (missing.Count > 0)
                {
                    NetSync.Notify($"{string.Join(", ", missing.ToArray())} {(missing.Count == 1 ? "doesn't" : "don't")} have Increased Enemy Count, so no extra enemies were added in this area.");
                    settings = settings.WithEnabled(false);
                }
            }

            var state = new SceneState { SceneName = scene.name, CandidateCount = candidates.Count };
            m_currentScene = state;

            if (!settings.AnyCopies)
            {
                NetSync.PublishArea(scene.name, settings, candidates.Count, state.CopyCounts);
                return;
            }

            if (NetSync.IsOnline)
                Plugin.Log.LogInfo($"[{scene.name}] Using settings from {settingsSource}: {settings}.");

            // ---- 3. Copy.
            var allocator = new ViewIDAllocator(FirstCopyOwnerFor(scene));
            var liveViews = GetLivePhotonViews();
            int cloned = 0, skippedExcluded = 0, outOfIDs = 0;
            var perCategory = new Dictionary<EnemyCategory, int>();

            // Inactive holder: anything instantiated under it does not Awake until re-parented.
            var holder = new GameObject("IncreasedEnemyCount_Holder");
            holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, scene);

            try
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var original = candidates[i];

                    // Network IDs are reserved for the maximum number of copies of every candidate,
                    // whatever its type or the sliders say. That way a different decision about one
                    // enemy on one machine (e.g. name matching in another game language) can't shift
                    // the IDs of every enemy after it.
                    int viewsPerCopy = CountViews(original);
                    int[] ids = allocator.Take(viewsPerCopy * EnemySettings.MAX_COPIES);

                    string name = SafeName(original);
                    float hp = SafeBaseMaxHealth(original);
                    var category = Classify(original, settings, out string how);
                    int copies = how == "excluded" ? 0 : settings.CopiesFor(category);

                    if (Plugin.LogEachEnemy.Value)
                    {
                        Plugin.Log.LogInfo($"[{scene.name}] {name} | HP {hp:0} | faction {original.Faction} | " +
                                           $"{category}{(how != null ? $" ({how})" : "")} | {copies} cop{(copies == 1 ? "y" : "ies")}" +
                                           (original.gameObject.activeInHierarchy ? "" : " | currently inactive"));
                    }

                    if (how == "excluded")
                    {
                        skippedExcluded++;
                        continue;
                    }
                    if (copies <= 0)
                        continue;
                    if (ids == null)
                    {
                        outOfIDs++;
                        continue;
                    }
                    if (string.IsNullOrEmpty(original.UID.Value) && NetSync.IsOnline)
                        continue; // its copies' UIDs couldn't be made to match across machines

                    int made = 0;
                    for (int k = 1; k <= copies; k++)
                    {
                        var slice = new int[viewsPerCopy];
                        Array.Copy(ids, (k - 1) * viewsPerCopy, slice, 0, viewsPerCopy);
                        if (CreateClone(scene, holder.transform, original, i, k, copies, slice, settings.Spacing, liveViews))
                            made++;
                    }
                    if (made > 0)
                    {
                        state.CopyCounts[i] = made;
                        cloned += made;
                        perCategory[category] = (perCategory.TryGetValue(category, out int n) ? n : 0) + made;
                    }
                }
            }
            finally
            {
                Destroy(holder);
            }

            NetSync.PublishArea(scene.name, settings, candidates.Count, state.CopyCounts);

            int Cat(EnemyCategory c) => perCategory.TryGetValue(c, out int n) ? n : 0;
            Plugin.Log.LogMessage($"[{scene.name}] Enemies found: {candidates.Count}, copies created: {cloned} " +
                                  $"(standard {Cat(EnemyCategory.Standard)}, unique {Cat(EnemyCategory.Unique)}, bosses {Cat(EnemyCategory.Boss)})" +
                                  (skippedExcluded > 0 ? $", excluded by name: {skippedExcluded}" : "") + ".");
            if (outOfIDs > 0)
                Plugin.Log.LogWarning($"[{scene.name}] Ran out of network IDs for this area: {outOfIDs} enemies weren't copied.");
        }

        // ------------------------------------------------------------------ filtering

        /// <summary>Structural check only - must give the same answer on every machine.</summary>
        private bool IsCandidate(Character c)
        {
            if (!c)
                return false;

            // Never copy our own copies.
            if (m_recordsByCloneUID.ContainsKey(c.UID.Value ?? ""))
                return false;

            // AI only. Uses the same marker the game's own Character.IsAI checks. Not CharacterAI:
            // on co-op clients the game deletes that component during loading (the host runs the AI),
            // so checking it could give a different answer on different machines.
            if (!c.GetComponent<EditorCharacterAILoadAI>())
                return false;

            // Friendly town NPCs, merchants and unaligned characters.
            if (c.Faction == Character.Factions.NONE || c.Faction == Character.Factions.Player || c.Faction == Character.Factions.Merchants)
                return false;

            // Anyone you can talk to, trade with or train under.
            if (c.GetComponentInChildren<Merchant>(true) || c.GetComponentInChildren<Trainer>(true))
                return false;
            foreach (var comp in c.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (comp && comp.GetType().Name.IndexOf("Dialogue", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Standard, Unique or Boss. "Never copy" keywords win, then "Always standard" keywords,
        /// then the wiki lists. <paramref name="how"/> is "excluded", "always standard", "wiki list" or null.
        /// </summary>
        private static EnemyCategory Classify(Character c, EnemySettings s, out string how)
        {
            if (MatchesAnyName(c, s.ExcludedNames))
            {
                how = "excluded";
                return EnemyCategory.Standard;
            }

            var listed = EnemyCategories.Lookup(SafeName(c));
            if (listed == EnemyCategory.Standard)
                listed = EnemyCategories.Lookup(f_name?.GetValue(c) as string);

            if (MatchesAnyName(c, s.IncludedNames))
            {
                how = listed != EnemyCategory.Standard ? $"always standard, wiki says {listed}" : "always standard";
                return EnemyCategory.Standard;
            }

            how = listed != EnemyCategory.Standard ? "wiki list" : null;
            return listed;
        }

        /// <summary>Checks the displayed name and the game's internal names.</summary>
        private static bool MatchesAnyName(Character c, string commaList)
        {
            if (string.IsNullOrEmpty(commaList))
                return false;
            return NameMatchesList(SafeName(c), commaList)
                || NameMatchesList(c.gameObject.name, commaList)
                || NameMatchesList(f_name?.GetValue(c) as string, commaList)
                || NameMatchesList(f_nameLocKey?.GetValue(c) as string, commaList);
        }

        private static bool NameMatchesList(string name, string commaList)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(commaList))
                return false;
            foreach (var raw in commaList.Split(','))
            {
                var word = raw.Trim();
                if (word.Length > 0 && name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string SafeName(Character c)
        {
            string n = null;
            try { n = c.Name; } catch { }
            if (string.IsNullOrEmpty(n))
                n = c.gameObject.name;
            return n;
        }

        private static float SafeBaseMaxHealth(Character c)
        {
            try
            {
                var stats = c.GetComponent<CharacterStats>();
                if (stats)
                    return stats.BaseMaxHealth;
            }
            catch { }
            return 0f;
        }

        // ------------------------------------------------------------------ cloning

        private bool CreateClone(Scene scene, Transform holder, Character original, int candidateIndex, int index, int totalCopies,
                                 int[] viewIDs, float spacing, Dictionary<int, PhotonView> liveViews)
        {
            GameObject cloneGO = null;
            try
            {
                Transform origT = original.transform;
                Vector3 offset = ComputeOffset(origT, index, totalCopies, spacing);

                cloneGO = Instantiate(original.gameObject, origT.position, origT.rotation, holder);
                cloneGO.name = original.gameObject.name + CLONE_SUFFIX + index;
                cloneGO.SetActive(original.gameObject.activeSelf);

                var clone = cloneGO.GetComponent<Character>();

                // --- unique character UID (deterministic: matches across machines and save files)
                string origUID = original.UID.Value;
                string cloneUID = string.IsNullOrEmpty(origUID) ? null : origUID + CLONE_SUFFIX + index;
                clone.SetUID(cloneUID == null ? UID.Empty : new UID(cloneUID));

                // --- network view IDs (pre-assigned; a duplicate ID makes Photon destroy the older object)
                int v = 0;
                foreach (var pv in cloneGO.GetComponentsInChildren<PhotonView>(true))
                {
                    if (pv.viewID == 0)
                        continue;
                    int newID = viewIDs[v++];
                    if (liveViews != null && liveViews.TryGetValue(newID, out var occupant) && occupant && occupant.gameObject.scene != scene)
                    {
                        // Something outside this area (most likely another mod on this machine only)
                        // already uses this ID. Taking it would make Photon destroy that object.
                        Plugin.Log.LogWarning($"Network ID {newID} is already used by '{occupant.name}' on this machine; skipping a copy of {original.name}. " +
                                              "In co-op this copy will be missing for this player.");
                        DestroyImmediate(cloneGO);
                        return false;
                    }
                    pv.viewID = newID;
                    // The setter derived a (fictional) owner from the ID; make it an area object
                    // instead, owned by nobody, so whoever hosts controls it - like the original.
                    pv.ownerId = 0;
                }

                // --- other things that carry their own UID (deterministic too)
                foreach (var comp in cloneGO.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (comp is Character || comp is AISquad)
                        continue;
                    if (comp is IUIDHolder uidHolder && !uidHolder.HolderUID.IsNull)
                        uidHolder.SetHolderUID(new UID(uidHolder.HolderUID.Value + CLONE_SUFFIX + index));
                }

                // --- remove runtime-added components that the clone's own Awake will add again
                var charAI = cloneGO.GetComponent<CharacterAI>();
                if (charAI && charAI.InstantiateCharAIDisable)
                {
                    foreach (var d in cloneGO.GetComponents<CharAIDisable>())
                        DestroyImmediate(d);
                }

                // --- give the copy back its full set of AI behaviours (wander, combat, flee...)
                RestoreAIStates(original, cloneGO);

                // --- quest kill-events stay with the original only
                foreach (var q in cloneGO.GetComponentsInChildren<QuestEventOnDeath>(true))
                    DestroyImmediate(q);

                // --- move into place
                cloneGO.transform.position = origT.position + offset;
                cloneGO.transform.SetParent(origT.parent, true);
                if (!origT.parent)
                    SceneManager.MoveGameObjectToScene(cloneGO, scene);
                cloneGO.transform.SetSiblingIndex(origT.GetSiblingIndex() + 1);
                cloneGO.transform.localScale = origT.localScale;

                // From this point the clone may be awake (if its parent chain is active).
                RegisterWithSquad(cloneGO, original);

                // Co-op clients only add a character to the game's character list if Photon calls it
                // an area object, which it judges from the view ID. Copies' IDs are outside that
                // range, so add them here. (The host adds every character regardless.)
                if (cloneUID != null && NetSync.IsOnline && !PhotonNetwork.isMasterClient && CharacterManager.Instance)
                    CharacterManager.Instance.AddCharacter(clone);

                var record = new CloneRecord
                {
                    SceneName = scene.name,
                    CandidateIndex = candidateIndex,
                    Original = original,
                    Clone = clone,
                    OriginalUID = origUID,
                    CloneUID = cloneUID,
                    Offset = offset,
                    Spacing = spacing,
                    CopyIndex = index,
                    TotalCopies = totalCopies,
                    OriginalWasActive = original.gameObject.activeSelf
                };
                m_records.Add(record);
                if (cloneUID != null)
                    m_recordsByCloneUID[cloneUID] = record;

                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not copy {original?.name}: {e.Message}");
                if (cloneGO)
                    DestroyImmediate(cloneGO);
                return false;
            }
        }

        /// <summary>
        /// When an enemy's AI wakes up it finds its behaviour states (wander, combat, ...) and then
        /// switches all of them off except the current one. A copy taken after that point would only
        /// find the one state that's still on, so its "start fighting" transition would point at a
        /// state it never registered and it would stay passive. Switch the original's states back on
        /// in the copy (it hasn't woken up yet) so it registers the same set, in the same order.
        /// </summary>
        private static void RestoreAIStates(Character original, GameObject cloneGO)
        {
            var origAI = original.GetComponent<CharacterAI>();
            if (!origAI)
                return;

            var root = f_aiStatesRoot?.GetValue(origAI) as AIRoot;
            var states = f_aiStates?.GetValue(origAI) as AIState[];
            if (!root && states == null)
                return; // original hasn't woken up yet, so the copy is already untouched

            if (root)
            {
                var t = FindCounterpart(original.transform, root.transform, cloneGO.transform);
                if (t)
                    t.gameObject.SetActive(true);
            }

            if (states == null)
                return;

            int restored = 0;
            foreach (var state in states)
            {
                if (!state)
                    continue;
                var t = FindCounterpart(original.transform, state.transform, cloneGO.transform);
                if (t)
                {
                    t.gameObject.SetActive(true);
                    restored++;
                }
            }

            if (restored != states.Length)
                Plugin.Log.LogWarning($"{original.name}: only matched {restored}/{states.Length} AI states on its copy; it may behave oddly.");
        }

        /// <summary>Find the clone's equivalent of a transform inside the original, by sibling-index path.</summary>
        private static Transform FindCounterpart(Transform originalRoot, Transform target, Transform cloneRoot)
        {
            var path = new List<int>();
            for (var t = target; t != originalRoot; t = t.parent)
            {
                if (!t)
                    return null;
                path.Add(t.GetSiblingIndex());
            }

            var c = cloneRoot;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= c.childCount)
                    return null;
                c = c.GetChild(path[i]);
            }
            return c;
        }

        /// <summary>Spread copies around the original, snapped to ground / nav mesh. Deterministic.</summary>
        private static Vector3 ComputeOffset(Transform origT, int index, int totalCopies, float spacing)
        {
            float baseAngle = Mathf.Abs(origT.position.x * 13.7f + origT.position.z * 7.3f) % 360f;
            float angle = baseAngle + index * (360f / (totalCopies + 1));
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 target = origT.position + dir * spacing;

            if (Physics.Raycast(target + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, Global.FullEnvironmentMask))
                target = hit.point + Vector3.up * 0.1f;

            if (NavMesh.SamplePosition(target, out NavMeshHit navHit, 2.5f, NavMesh.AllAreas))
                target = navHit.position + Vector3.up * 0.1f;
            else if (NavMesh.SamplePosition(origT.position, out _, 2.5f, NavMesh.AllAreas))
                target = origT.position + dir * 0.75f; // no walkable ground at the offset: stay close
            // else: original isn't on a nav mesh either (flying / scripted enemy) - keep the raw offset

            return target - origT.position;
        }

        private static void RegisterWithSquad(GameObject cloneGO, Character original)
        {
            var member = cloneGO.GetComponent<AISquadMember>();
            if (!member)
                return;

            AISquad squad = null;
            for (var t = original.transform.parent; t && !squad; t = t.parent)
                squad = t.GetComponent<AISquad>();
            if (!squad)
                return;

            // If the squad hasn't woken up yet, its own Awake will pick the clone up as a child.
            if (squad.MemberCount == 0)
                return;

            member.AISquad = squad;
            var charAI = cloneGO.GetComponent<CharacterAI>();
            if (charAI)
                charAI.AISquad = squad;
            squad.AddCharAI(member);
        }

        // ------------------------------------------------------------------ network view IDs

        private static int CountViews(Character c)
        {
            int n = 0;
            foreach (var pv in c.GetComponentsInChildren<PhotonView>(true))
                if (pv.viewID != 0)
                    n++;
            return n;
        }

        /// <summary>First fictional owner number for an area. Same on every machine.</summary>
        private static int FirstCopyOwnerFor(Scene scene)
        {
            int key = scene.buildIndex >= 0 ? scene.buildIndex : StableHash(scene.name);
            return COPY_OWNER_BASE + (int)((uint)key % AREA_BLOCKS) * OWNERS_PER_AREA;
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in s ?? "")
                    h = (h ^ ch) * 16777619;
                return (int)(h & 0x7fffffff);
            }
        }

        /// <summary>Hands out IDs in a fixed order: owner x 1000 + 1..999, then the next owner.</summary>
        private sealed class ViewIDAllocator
        {
            private readonly int m_lastOwner;
            private int m_owner;
            private int m_slot;

            public ViewIDAllocator(int firstOwner)
            {
                m_owner = firstOwner;
                m_lastOwner = firstOwner + OWNERS_PER_AREA - 1;
                m_slot = 0;
            }

            /// <summary>Returns <paramref name="count"/> IDs, or null if there aren't enough left.</summary>
            public int[] Take(int count)
            {
                var result = new int[count];
                for (int i = 0; i < count; i++)
                {
                    m_slot++;
                    if (m_slot >= PhotonNetwork.MAX_VIEW_IDS)
                    {
                        m_owner++;
                        m_slot = 1; // slot 0 would make owner x 1000, fine, but skip it like Photon does
                    }
                    if (m_owner > m_lastOwner)
                        return null;
                    result[i] = m_owner * PhotonNetwork.MAX_VIEW_IDS + m_slot;
                }
                return result;
            }
        }

        private static Dictionary<int, PhotonView> GetLivePhotonViews()
        {
            try
            {
                var peer = f_networkingPeer?.GetValue(null);
                if (peer != null)
                    return f_photonViewList?.GetValue(peer) as Dictionary<int, PhotonView>;
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------ save handling

        internal void ApplySavesToClones(CharacterSaveData[] saves)
        {
            if (saves == null || m_records.Count == 0)
                return;
            try
            {
                var byUID = new Dictionary<string, CharacterSaveData>();
                foreach (var s in saves)
                    if (s != null && !string.IsNullOrEmpty(s.UID))
                        byUID[s.UID] = s;

                foreach (var r in m_records)
                {
                    if (!r.Clone || r.CloneUID == null || r.OriginalUID == null)
                        continue;
                    if (byUID.ContainsKey(r.CloneUID))
                        continue; // the game already loaded the clone's own save
                    if (!byUID.TryGetValue(r.OriginalUID, out var origSave))
                        continue;
                    if (origSave.Health > 0f)
                        continue; // original is alive - a fresh copy is correct
                    if (CharacterManager.Instance.GetCharacter(r.CloneUID) != r.Clone)
                        continue; // clone not initialised yet

                    var copy = new CharacterSaveData
                    {
                        NewSave = origSave.NewSave,
                        VisualData = origSave.VisualData,
                        UID = r.CloneUID,
                        Health = origSave.Health,
                        ManaPoint = origSave.ManaPoint,
                        Position = origSave.Position + r.Offset,
                        Forward = origSave.Forward,
                        Money = 0,
                        EncounterTime = origSave.EncounterTime,
                        IsAiActive = origSave.IsAiActive
                    };
                    r.Clone.LoadCharSave(copy);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not sync saved state to copies: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ per-frame work

        private void Update()
        {
            if (Time.unscaledTime >= m_nextNetTick)
            {
                m_nextNetTick = Time.unscaledTime + 1f;
                NetSync.Tick();
                ReconcileWithHost();
            }

            MirrorActivation();
        }

        /// <summary>
        /// Co-op client: once the host says which enemies it copied in this area, remove any copies
        /// this machine made that the host didn't (they'd be frozen, since the host never controls
        /// them), and report copies the host has that this machine is missing.
        /// </summary>
        private void ReconcileWithHost()
        {
            var state = m_currentScene;
            if (state == null || state.Reconciled || !NetSync.IsOnline || NetSync.IsHost)
                return;
            if (!NetSync.TryGetHostManifest(state.SceneName, out int hostCandidates, out var hostCounts))
                return;

            state.Reconciled = true;

            if (hostCandidates != state.CandidateCount)
            {
                NetSync.Notify($"Your copy of this area doesn't match the host's ({state.CandidateCount} enemies here, " +
                               $"{hostCandidates} on the host). Leave and re-enter the area. " +
                               "If it keeps happening, check that everyone has the same mods.");
                return;
            }

            // Remove copies the host doesn't have (an enemy it didn't copy, or fewer copies of it).
            int removed = 0;
            for (int i = m_records.Count - 1; i >= 0; i--)
            {
                var r = m_records[i];
                if (r.SceneName != state.SceneName)
                    continue;
                int hostCount = hostCounts.TryGetValue(r.CandidateIndex, out int hc) ? hc : 0;
                if (r.CopyIndex <= hostCount)
                    continue;
                if (r.Clone)
                    Destroy(r.Clone.gameObject);
                if (r.CloneUID != null)
                    m_recordsByCloneUID.Remove(r.CloneUID);
                m_records.RemoveAt(i);
                removed++;
            }

            int missing = 0;
            foreach (var kv in hostCounts)
            {
                int mine = state.CopyCounts.TryGetValue(kv.Key, out int m) ? m : 0;
                if (kv.Value > mine)
                    missing += kv.Value - mine;
            }

            if (removed > 0 || missing > 0)
            {
                Plugin.Log.LogWarning($"[{state.SceneName}] Matched the host's enemy copies: removed {removed} extra, {missing} copies are missing here.");
                if (missing > 0)
                    NetSync.Notify($"{missing} of the host's extra enemies couldn't be created on your side, so they may be invisible to you. " +
                                   "Make sure everyone uses the same game language and mods.");
            }
        }

        // Some enemies start switched off and are switched on by a script (ambushes, co-op extras,
        // event spawns). Those scripts only know about the original, so when the original turns
        // on, turn its copies on too.
        private void MirrorActivation()
        {
            if (m_records.Count == 0 || Time.unscaledTime < m_nextMirrorCheck)
                return;
            m_nextMirrorCheck = Time.unscaledTime + 0.25f;

            bool anyMissing = false;
            for (int i = 0; i < m_records.Count; i++)
            {
                var r = m_records[i];
                if (!r.Original || !r.Clone)
                {
                    anyMissing = true;
                    continue;
                }

                bool origActive = r.Original.gameObject.activeSelf;
                if (origActive && !r.OriginalWasActive && !r.Clone.gameObject.activeSelf)
                {
                    try
                    {
                        Vector3 pos = r.Original.transform.position + ComputeOffset(r.Original.transform, r.CopyIndex, r.TotalCopies, r.Spacing);
                        r.Clone.transform.position = pos;
                        r.Clone.transform.rotation = r.Original.transform.rotation;
                        r.Clone.gameObject.SetActive(true);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Could not activate copy of {r.Original.name}: {e.Message}");
                    }
                }
                r.OriginalWasActive = origActive;
            }

            if (anyMissing)
                PurgeDeadRecords();
        }

        private void PurgeDeadRecords()
        {
            for (int i = m_records.Count - 1; i >= 0; i--)
            {
                var r = m_records[i];
                if (!r.Clone || !r.Original)
                {
                    if (r.CloneUID != null)
                        m_recordsByCloneUID.Remove(r.CloneUID);
                    m_records.RemoveAt(i);
                }
            }
        }
    }
}
