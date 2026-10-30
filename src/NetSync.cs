using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

namespace IncreasedEnemyCount
{
    /// <summary>
    /// Online co-op support.
    ///
    /// Every player's game makes the copies itself, in exactly the same way, so each copy gets the
    /// same UID and network view ID on every machine and the game's own syncing handles the rest
    /// (movement, health, deaths, late joiners). This class makes sure everyone uses the same
    /// settings and catches the cases where that still goes wrong.
    ///
    /// It uses Photon room/player custom properties, which every player can read and which late
    /// joiners receive automatically, so no extra network objects are needed:
    ///   room   iec.cfg   host's current settings              (used for the next area load)
    ///   room   iec.area  scene + settings the host used there (used by players joining mid-area)
    ///   room   iec.man   scene + which enemies the host copied (used to correct mismatches)
    ///   player iec.ver   this player has the mod (and which sync format)
    /// </summary>
    internal static class NetSync
    {
        public const string PROTOCOL = "5"; // bump whenever copies would come out differently (IDs, UIDs, order)

        private const string KEY_CFG = "iec.cfg";
        private const string KEY_AREA = "iec.area";
        private const string KEY_MANIFEST = "iec.man";
        private const string KEY_VERSION = "iec.ver";
        private const char SEP = '|';

        private static string s_lastRoomProcessed;
        private static string s_lastPublishedCfg;
        private static string s_trackedRoom;
        private static readonly Dictionary<int, float> s_firstSeen = new Dictionary<int, float>();
        private static readonly HashSet<int> s_warnedPlayers = new HashSet<int>();

        /// <summary>True in an online session (Photon offline mode is used for single-player).</summary>
        public static bool IsOnline => PhotonNetwork.inRoom && !PhotonNetwork.offlineMode;

        /// <summary>True when this machine decides what exists in the world.</summary>
        public static bool IsHost => !IsOnline || PhotonNetwork.isMasterClient;

        // ------------------------------------------------------------------ settings

        /// <summary>
        /// The settings to use for an area that just loaded. Null means the host doesn't have the
        /// mod, so nothing should be copied.
        /// </summary>
        public static EnemySettings ResolveSettingsForScene(string sceneName, out string source)
        {
            if (IsHost)
            {
                source = IsOnline ? "this machine (host)" : "this machine";
                return EnemySettings.FromConfig();
            }

            var room = PhotonNetwork.room;
            bool firstLoadInRoom = s_lastRoomProcessed != room.Name;
            s_lastRoomProcessed = room.Name;

            var props = room.CustomProperties;

            // Joining a session mid-area: copy with whatever the host used when it loaded this area,
            // even if the host has changed its settings since.
            if (firstLoadInRoom && TrySplit(GetString(props, KEY_AREA), out string areaScene, out string areaData) && areaScene == sceneName)
            {
                var s = EnemySettings.Deserialize(areaData);
                if (s != null)
                {
                    source = "host (settings it used for this area)";
                    return s;
                }
            }

            var cfg = EnemySettings.Deserialize(GetString(props, KEY_CFG));
            if (cfg != null)
            {
                source = "host";
                return cfg;
            }

            source = "host does not have the mod";
            return null;
        }

        /// <summary>
        /// Host: record what was done for an area so other players can match it.
        /// Manifest format: scene | candidate count | index:copies,index:copies,...
        /// </summary>
        public static void PublishArea(string sceneName, EnemySettings used, int candidateCount, IDictionary<int, int> copyCounts)
        {
            if (!IsOnline || !PhotonNetwork.isMasterClient)
                return;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var manifest = new System.Text.StringBuilder();
                manifest.Append(sceneName).Append(SEP)
                        .Append(candidateCount.ToString(inv)).Append(SEP);
                bool first = true;
                foreach (var kv in copyCounts)
                {
                    if (kv.Value <= 0)
                        continue;
                    if (!first) manifest.Append(',');
                    manifest.Append(kv.Key.ToString(inv)).Append(':').Append(kv.Value.ToString(inv));
                    first = false;
                }

                var cfg = EnemySettings.FromConfig().Serialize();
                s_lastPublishedCfg = cfg;
                PhotonNetwork.room.SetCustomProperties(new PhotonHashtable
                {
                    { KEY_CFG, cfg },
                    { KEY_AREA, sceneName + SEP + used.Serialize() },
                    { KEY_MANIFEST, manifest.ToString() },
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not share enemy copy info with other players: {e.Message}");
            }
        }

        /// <summary>Client: what the host copied in this area (enemy index -> copies), once published.</summary>
        public static bool TryGetHostManifest(string sceneName, out int candidateCount, out Dictionary<int, int> copyCounts)
        {
            candidateCount = 0;
            copyCounts = null;
            if (!IsOnline || PhotonNetwork.isMasterClient)
                return false;

            var raw = GetString(PhotonNetwork.room.CustomProperties, KEY_MANIFEST);
            if (string.IsNullOrEmpty(raw))
                return false;
            var p = raw.Split(SEP);
            if (p.Length < 3 || p[0] != sceneName)
                return false;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                candidateCount = int.Parse(p[1], inv);
                copyCounts = new Dictionary<int, int>();
                if (p[2].Length > 0)
                {
                    foreach (var entry in p[2].Split(','))
                    {
                        var kv = entry.Split(':');
                        copyCounts[int.Parse(kv[0], inv)] = int.Parse(kv[1], inv);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ who has the mod

        /// <summary>Names of other players in the session who don't have a compatible version of the mod.</summary>
        public static List<string> PlayersWithoutMod()
        {
            var list = new List<string>();
            if (!IsOnline)
                return list;
            foreach (var p in PhotonNetwork.otherPlayers)
            {
                if (p == null)
                    continue;
                if (GetString(p.CustomProperties, KEY_VERSION) != PROTOCOL)
                    list.Add(DisplayName(p));
            }
            return list;
        }

        /// <summary>Called about once a second.</summary>
        public static void Tick()
        {
            if (!IsOnline)
                return;
            try
            {
                var room = PhotonNetwork.room;
                if (s_trackedRoom != room.Name)
                {
                    s_trackedRoom = room.Name;
                    s_firstSeen.Clear();
                    s_warnedPlayers.Clear();
                    s_lastPublishedCfg = null;
                }

                // Tell everyone this player has the mod.
                if (GetString(PhotonNetwork.player.CustomProperties, KEY_VERSION) != PROTOCOL)
                    PhotonNetwork.player.SetCustomProperties(new PhotonHashtable { { KEY_VERSION, PROTOCOL } });

                if (!PhotonNetwork.isMasterClient)
                    return;

                // Keep the host's current settings published for the next area load.
                var cfg = EnemySettings.FromConfig().Serialize();
                if (cfg != s_lastPublishedCfg)
                {
                    s_lastPublishedCfg = cfg;
                    room.SetCustomProperties(new PhotonHashtable { { KEY_CFG, cfg } });
                }

                // Warn the host about players who joined without the mod. Give them a few seconds,
                // since their "I have the mod" flag arrives shortly after they join.
                foreach (var p in PhotonNetwork.otherPlayers)
                {
                    if (p == null)
                        continue;
                    if (!s_firstSeen.ContainsKey(p.ID))
                        s_firstSeen[p.ID] = Time.unscaledTime;
                    if (s_warnedPlayers.Contains(p.ID) || Time.unscaledTime - s_firstSeen[p.ID] < 8f)
                        continue;

                    string ver = GetString(p.CustomProperties, KEY_VERSION);
                    if (ver == PROTOCOL)
                        continue;

                    s_warnedPlayers.Add(p.ID);
                    string why = ver == null ? "doesn't have Increased Enemy Count" : "has an incompatible version of Increased Enemy Count";
                    string then = Plugin.RequireModOnAllPlayers.Value
                        ? "Extra enemies will be skipped from the next area on."
                        : "They won't see the extra enemies in this area or later ones.";
                    Notify($"{DisplayName(p)} {why}. {then}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"NetSync tick failed: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Log, and show an on-screen notice to the local player(s).</summary>
        public static void Notify(string message)
        {
            Plugin.Log.LogWarning(message);
            try
            {
                var c = CharacterManager.Instance ? CharacterManager.Instance.GetFirstLocalCharacter() : null;
                if (c && c.CharacterUI)
                    c.CharacterUI.ShowInfoNotification("Increased Enemy Count: " + message);
            }
            catch { }
        }

        private static string DisplayName(PhotonPlayer p)
        {
            try
            {
                if (!string.IsNullOrEmpty(p.NickName))
                    return p.NickName;
            }
            catch { }
            return "Player " + p.ID;
        }

        private static string GetString(PhotonHashtable props, string key)
        {
            if (props == null || !props.ContainsKey(key))
                return null;
            return props[key] as string;
        }

        private static bool TrySplit(string raw, out string scene, out string rest)
        {
            scene = rest = null;
            if (string.IsNullOrEmpty(raw))
                return false;
            int i = raw.IndexOf(SEP);
            if (i <= 0)
                return false;
            scene = raw.Substring(0, i);
            rest = raw.Substring(i + 1);
            return true;
        }
    }
}
