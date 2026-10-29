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
    /// </summary>
    public class EnemyDoubler : MonoBehaviour
    {
        internal static EnemyDoubler Instance;

        private const string CLONE_SUFFIX = "_x";

        private class CloneRecord
        {
            public Character Original;
            public Character Clone;
            public string OriginalUID;
            public string CloneUID;
            public Vector3 Offset;
            public bool OriginalWasActive;
        }

        private readonly List<CloneRecord> m_records = new List<CloneRecord>();
        private readonly Dictionary<string, CloneRecord> m_recordsByCloneUID = new Dictionary<string, CloneRecord>();
        private float m_nextMirrorCheck;

        private static readonly FieldInfo f_networkingPeer = AccessTools.Field(typeof(PhotonNetwork), "networkingPeer");
        private static readonly FieldInfo f_photonViewList = f_networkingPeer == null ? null : AccessTools.Field(f_networkingPeer.FieldType, "photonViewList");
        private static readonly FieldInfo f_aiStates = AccessTools.Field(typeof(CharacterAI), "m_aiStates");
        private static readonly FieldInfo f_aiStatesRoot = AccessTools.Field(typeof(CharacterAI), "m_aiStatesRoot");

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
            if (!Plugin.Enabled.Value || Plugin.EnemyMultiplier.Value <= 1)
                return;

            // Only the host decides how many enemies exist. A joining co-op client would otherwise
            // create local-only copies the host doesn't know about.
            if (PhotonNetwork.isNonMasterClientInRoom)
            {
                Plugin.Log.LogInfo($"[{scene.name}] Not the host - leaving enemy counts alone.");
                return;
            }

            var candidates = new List<Character>();
            var bakedViewIDs = new HashSet<int>();
            foreach (var root in scene.GetRootGameObjects())
            {
                candidates.AddRange(root.GetComponentsInChildren<Character>(true));
                foreach (var pv in root.GetComponentsInChildren<PhotonView>(true))
                    if (pv.viewID != 0)
                        bakedViewIDs.Add(pv.viewID);
            }

            if (candidates.Count == 0)
                return;

            int copies = Plugin.EnemyMultiplier.Value - 1;
            int cloned = 0, skippedBoss = 0, considered = 0;

            // Inactive holder: anything instantiated under it does not Awake until re-parented.
            var holder = new GameObject("IncreasedEnemyCount_Holder");
            holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, scene);

            try
            {
                foreach (var original in candidates)
                {
                    if (!IsEnemy(original))
                        continue;

                    considered++;
                    string name = SafeName(original);
                    float hp = SafeBaseMaxHealth(original);
                    bool isBoss = IsBoss(original, name, hp, out string bossReason);

                    if (Plugin.LogEachEnemy.Value)
                    {
                        Plugin.Log.LogInfo($"[{scene.name}] {name} | HP {hp:0} | faction {original.Faction} | " +
                                           (isBoss ? $"BOSS ({bossReason})" : "normal") +
                                           (original.gameObject.activeInHierarchy ? "" : " | currently inactive"));
                    }

                    if (isBoss && !Plugin.IncludeBosses.Value)
                    {
                        skippedBoss++;
                        continue;
                    }

                    for (int k = 1; k <= copies; k++)
                    {
                        if (CreateClone(scene, holder.transform, original, k, copies, bakedViewIDs))
                            cloned++;
                    }
                }
            }
            finally
            {
                Destroy(holder);
            }

            Plugin.Log.LogMessage($"[{scene.name}] Enemies found: {considered}, copies created: {cloned}, bosses left alone: {skippedBoss}.");
        }

        // ------------------------------------------------------------------ filtering

        private bool IsEnemy(Character c)
        {
            if (!c)
                return false;

            // Never copy our own copies.
            if (c.gameObject.name.Contains(CLONE_SUFFIX) && m_recordsByCloneUID.ContainsKey(c.UID.Value ?? ""))
                return false;

            // AI only (players, and anything without an AI brain, are skipped).
            if (!c.GetComponent<CharacterAI>() && !c.GetComponent<EditorCharacterAILoadAI>())
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

            if (NameMatchesList(SafeName(c), Plugin.ExcludedNames.Value))
                return false;

            return true;
        }

        private static bool IsBoss(Character c, string name, float hp, out string reason)
        {
            reason = null;
            if (Plugin.QuestTargetsAreBosses.Value && c.GetComponent<QuestEventOnDeath>())
            {
                reason = "quest target";
                return true;
            }
            if (Plugin.BossHealthThreshold.Value > 0f && hp >= Plugin.BossHealthThreshold.Value)
            {
                reason = $"health >= {Plugin.BossHealthThreshold.Value:0}";
                return true;
            }
            if (NameMatchesList(name, Plugin.BossNameKeywords.Value))
            {
                reason = "name keyword";
                return true;
            }
            return false;
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

        private bool CreateClone(Scene scene, Transform holder, Character original, int index, int totalCopies, HashSet<int> bakedViewIDs)
        {
            GameObject cloneGO = null;
            try
            {
                Transform origT = original.transform;
                Vector3 offset = ComputeOffset(origT, index, totalCopies);

                cloneGO = Instantiate(original.gameObject, origT.position, origT.rotation, holder);
                cloneGO.name = original.gameObject.name + CLONE_SUFFIX + index;
                cloneGO.SetActive(original.gameObject.activeSelf);

                var clone = cloneGO.GetComponent<Character>();

                // --- unique character UID (deterministic, so the area save keeps track of it)
                string origUID = original.UID.Value;
                string cloneUID = string.IsNullOrEmpty(origUID) ? null : origUID + CLONE_SUFFIX + index;
                clone.SetUID(cloneUID == null ? UID.Empty : new UID(cloneUID));

                // --- unique Photon view IDs (a duplicate ID makes Photon destroy the ORIGINAL)
                foreach (var pv in cloneGO.GetComponentsInChildren<PhotonView>(true))
                {
                    if (pv.viewID == 0)
                        continue;
                    int newID = AllocateFreeViewID(bakedViewIDs);
                    if (newID <= 0)
                    {
                        Plugin.Log.LogWarning($"No free network view IDs left; not copying {original.name}.");
                        DestroyImmediate(cloneGO);
                        return false;
                    }
                    pv.viewID = newID;
                    bakedViewIDs.Add(newID);
                }

                // --- other things that carry their own UID
                foreach (var holderComp in cloneGO.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (holderComp is Character || holderComp is AISquad)
                        continue;
                    if (holderComp is Item item)
                    {
                        // Let the game generate fresh item UIDs.
                        item.SetHolderUID(UID.Empty);
                        continue;
                    }
                    if (holderComp is IUIDHolder uidHolder && !uidHolder.HolderUID.IsNull)
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

                var record = new CloneRecord
                {
                    Original = original,
                    Clone = clone,
                    OriginalUID = origUID,
                    CloneUID = cloneUID,
                    Offset = offset,
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

        /// <summary>Spread copies around the original, snapped to ground / nav mesh.</summary>
        private static Vector3 ComputeOffset(Transform origT, int index, int totalCopies)
        {
            float spacing = Plugin.SpawnSpacing.Value;
            // Deterministic per enemy so reloads put copies in the same spot.
            float baseAngle = Mathf.Abs(origT.position.x * 13.7f + origT.position.z * 7.3f) % 360f;
            float angle = baseAngle + index * (360f / (totalCopies + 1));
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 target = origT.position + dir * spacing;

            if (Physics.Raycast(target + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, Global.FullEnvironmentMask))
                target = hit.point + Vector3.up * 0.1f;

            if (NavMesh.SamplePosition(target, out NavMeshHit navHit, 2.5f, NavMesh.AllAreas))
                target = navHit.position + Vector3.up * 0.1f;
            else if (!NavMesh.SamplePosition(origT.position, out _, 2.5f, NavMesh.AllAreas))
            {
                // Original isn't on a nav mesh either (flying / scripted enemy) - just keep the raw offset.
            }
            else
            {
                // Couldn't find walkable ground at the offset: stay close to the original instead.
                target = origT.position + dir * 0.75f;
            }

            return target - origT.position;
        }

        private static void RegisterWithSquad(GameObject cloneGO, Character original)
        {
            var member = cloneGO.GetComponent<AISquadMember>();
            if (!member)
                return;

            var squad = original.GetComponentInParent<AISquad>();
            if (!squad)
            {
                // GetComponentInParent ignores inactive parents; walk up manually.
                for (var t = original.transform.parent; t && !squad; t = t.parent)
                    squad = t.GetComponent<AISquad>();
            }
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

        private static int AllocateFreeViewID(HashSet<int> bakedViewIDs)
        {
            Dictionary<int, PhotonView> live = null;
            try
            {
                var peer = f_networkingPeer?.GetValue(null);
                if (peer != null)
                    live = f_photonViewList?.GetValue(peer) as Dictionary<int, PhotonView>;
            }
            catch { }

            // Scene-owned IDs (owner 0) keep the copy behaving exactly like the original.
            // Search from the top of the range down, away from the low IDs the scenes use.
            for (int id = PhotonNetwork.MAX_VIEW_IDS - 1; id > 0; id--)
            {
                if (bakedViewIDs.Contains(id))
                    continue;
                if (live != null && live.ContainsKey(id))
                    continue;
                return id;
            }
            return -1;
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

        // ------------------------------------------------------------------ activation mirroring

        // Some enemies start switched off and are switched on by a script (ambushes, co-op extras,
        // event spawns). Those scripts only know about the original, so when the original turns
        // on, turn its copies on too.
        private void Update()
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
                        Vector3 pos = r.Original.transform.position + ComputeOffset(r.Original.transform, 1, 1);
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
