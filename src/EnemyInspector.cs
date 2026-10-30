using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IncreasedEnemyCount
{
    /// <summary>
    /// Debugging aid, off unless Debug.InspectEnemies has keywords in it.
    /// For each matching enemy it logs what the enemy is made of, which objects in the area refer
    /// to it (quest/dialogue scripts that control it), and then, while you play, every change in
    /// the original's and its copy's state, so it's visible what keeps the original calm.
    /// </summary>
    internal static class EnemyInspector
    {
        private class Tracked
        {
            public string Label;
            public Character Character;
            public string Last;
        }

        private static readonly List<Tracked> s_tracked = new List<Tracked>();
        private static float s_nextTick;

        private const BindingFlags FIELDS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static bool Wants(Character c, string name)
        {
            var list = Plugin.InspectEnemies.Value;
            if (string.IsNullOrEmpty(list))
                return false;
            foreach (var raw in list.Split(','))
            {
                var w = raw.Trim();
                if (w.Length > 0 && ((name ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     c.gameObject.name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }
            return false;
        }

        public static void ClearTracking() => s_tracked.Clear();

        /// <summary>Log a full report for an enemy, before it's copied.</summary>
        public static void Report(Scene scene, Character c, string name)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"===== INSPECT [{scene.name}] {name} =====");
                sb.AppendLine($"  path: {PathOf(c.transform)} | activeSelf {c.gameObject.activeSelf} | activeInHierarchy {c.gameObject.activeInHierarchy}");
                sb.AppendLine($"  faction {c.Faction} | UID {c.UID.Value}");

                var ts = c.GetComponent<TargetingSystem>();
                if (ts)
                    sb.AppendLine($"  targeting: StartAlliedFactions [{Join(ts.StartAlliedFactions)}] AlliedToSameFaction {ts.AlliedToSameFaction}");

                var ai = c.GetComponent<CharacterAI>();
                if (ai)
                {
                    var qe = AccessField(ai, "m_aiActiveOnQuestEvent");
                    var qeUid = qe != null ? AccessField(qe, "m_eventUID") ?? qe.GetType().GetProperty("EventUID")?.GetValue(qe, null) : null;
                    sb.AppendLine($"  AI: enabled {ai.enabled} | AIStatesPrefab {(ai.AIStatesPrefab ? ai.AIStatesPrefab.name : "none")} | active-on-quest-event '{qeUid}'");
                    foreach (var st in c.GetComponentsInChildren<AIState>(true))
                        sb.AppendLine($"    state {st.GetType().Name} '{st.name}' active {st.gameObject.activeSelf}");
                }

                sb.AppendLine("  components (type x count):");
                foreach (var g in c.GetComponentsInChildren<Component>(true).Where(x => x).GroupBy(x => x.GetType().Name).OrderBy(g => g.Key))
                    sb.AppendLine($"    {g.Key} x{g.Count()}");

                sb.AppendLine("  referenced by (outside its own object):");
                int refs = 0;
                foreach (var line in FindReferences(scene, c.transform))
                {
                    sb.AppendLine("    " + line);
                    if (++refs >= 60) { sb.AppendLine("    ..."); break; }
                }
                if (refs == 0)
                    sb.AppendLine("    (nothing found)");

                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Inspect failed for {name}: {e}");
            }
        }

        /// <summary>Watch an original and its copies; changes are logged as they happen.</summary>
        public static void Track(Character original, string name, IEnumerable<Character> clones)
        {
            s_tracked.Add(new Tracked { Label = $"{name} (original)", Character = original });
            int i = 1;
            foreach (var c in clones)
                s_tracked.Add(new Tracked { Label = $"{name} (copy {i++})", Character = c });
        }

        public static void Tick()
        {
            if (s_tracked.Count == 0 || Time.unscaledTime < s_nextTick)
                return;
            s_nextTick = Time.unscaledTime + 1f;

            for (int i = s_tracked.Count - 1; i >= 0; i--)
            {
                var t = s_tracked[i];
                if (!t.Character)
                {
                    s_tracked.RemoveAt(i);
                    continue;
                }
                string now = Snapshot(t.Character);
                if (now != t.Last)
                {
                    Plugin.Log.LogInfo($"[inspect] {t.Label}: {now}");
                    t.Last = now;
                }
            }
        }

        private static string Snapshot(Character c)
        {
            var parts = new List<string>();
            void Add(string k, Func<object> f)
            {
                try { parts.Add($"{k}={f()}"); } catch { parts.Add($"{k}=?"); }
            }
            Add("active", () => c.gameObject.activeInHierarchy);
            Add("alive", () => c.Alive);
            Add("faction", () => c.Faction);
            Add("inCombat", () => c.InCombat);
            var ai = c.GetComponent<CharacterAI>();
            Add("ai", () => ai ? (ai.enabled ? "on" : "off") : "none");
            Add("aiRoot", () => ai && ai.AIStatesRoot ? (ai.AIStatesRoot.gameObject.activeSelf ? "on" : "off") : "-");
            Add("state", () => ai ? ai.CurrentAiState.name : "-");
            Add("closeToPlayer", () => ai ? (object)ai.CloseToPlayer : "-");
            var ts = c.TargetingSystem;
            Add("targets", () => ts ? Join(ts.TargetableFactions) : "-");
            Add("locked", () => ts && ts.LockedCharacter ? ts.LockedCharacter.Name : "none");
            return string.Join(" ", parts.ToArray());
        }

        // ------------------------------------------------------------------ reference search

        private static IEnumerable<string> FindReferences(Scene scene, Transform target)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!mb || mb.transform.IsChildOf(target))
                        continue;

                    var found = new List<string>();

                    // NodeCanvas blackboards keep their variables in a dictionary, not in fields.
                    if (mb.GetType().Name.Contains("Blackboard"))
                    {
                        try
                        {
                            var vars = mb.GetType().GetProperty("variables")?.GetValue(mb, null) as IDictionary;
                            if (vars != null)
                                foreach (DictionaryEntry kv in vars)
                                {
                                    var val = kv.Value?.GetType().GetProperty("value")?.GetValue(kv.Value, null);
                                    if (Points(val, target))
                                        found.Add($"blackboard var '{kv.Key}'");
                                }
                        }
                        catch { }
                    }

                    for (var type = mb.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                    {
                        foreach (var f in type.GetFields(FIELDS))
                        {
                            object val;
                            try { val = f.GetValue(mb); } catch { continue; }
                            if (val == null)
                                continue;
                            if (Points(val, target))
                                found.Add(f.Name);
                            else if (val is IEnumerable seq && !(val is string))
                            {
                                int n = 0;
                                try
                                {
                                    foreach (var item in seq)
                                    {
                                        if (++n > 300) break;
                                        if (Points(item, target)) { found.Add(f.Name + "[]"); break; }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    if (found.Count > 0)
                        yield return $"{mb.GetType().Name} on '{PathOf(mb.transform)}' -> {string.Join(", ", found.ToArray())}";
                }
            }
        }

        private static bool Points(object val, Transform target)
        {
            try
            {
                if (val is GameObject go) return go && go.transform.IsChildOf(target);
                if (val is Component comp) return comp && comp.transform.IsChildOf(target);
            }
            catch { }
            return false;
        }

        // ------------------------------------------------------------------ helpers

        private static object AccessField(object obj, string name)
        {
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(obj);
            }
            return null;
        }

        private static string Join(Character.Factions[] fs) => fs == null ? "" : string.Join(",", fs.Select(f => f.ToString()).ToArray());

        private static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (; t; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names.ToArray());
        }
    }
}
