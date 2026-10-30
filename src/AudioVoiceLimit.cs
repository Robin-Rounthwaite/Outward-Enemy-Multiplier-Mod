using System;
using System.Collections.Generic;
using UnityEngine;

namespace IncreasedEnemyCount
{
    /// <summary>
    /// Unity can only make a limited number of sounds audible at once ("real voices"). Past that,
    /// it mutes the quietest/lowest-priority ones and keeps them running silently ("virtual voices")
    /// until a slot frees up, which made enemy voices come through chopped up in big groups.
    /// Raising the limit means restarting Unity's audio system, so it's done once, at startup.
    /// </summary>
    internal static class AudioVoiceLimit
    {
        // Unity's own caps for these settings.
        private const int MAX_REAL = 255;
        private const int MAX_VIRTUAL = 4095;

        public static void Apply(int wantedRealVoices)
        {
            try
            {
                var config = AudioSettings.GetConfiguration();
                Plugin.Log.LogInfo($"Game audio: {config.numRealVoices} audible sounds at once, {config.numVirtualVoices} tracked.");

                int real = Mathf.Clamp(wantedRealVoices, 1, MAX_REAL);
                // Sounds over the tracked limit are stopped outright, so keep plenty of headroom.
                int virt = Mathf.Clamp(Math.Max(config.numVirtualVoices, real * 8), real, MAX_VIRTUAL);

                if (real <= config.numRealVoices && virt <= config.numVirtualVoices)
                {
                    Plugin.Log.LogInfo("Audio limits already at or above the requested values - leaving them alone.");
                    return;
                }

                real = Math.Max(real, config.numRealVoices);
                config.numRealVoices = real;
                config.numVirtualVoices = virt;

                // Resetting stops every playing sound. Note what's playing so it can be resumed.
                var playing = new List<KeyValuePair<AudioSource, float>>();
                foreach (var src in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                    if (src && src.isPlaying)
                        playing.Add(new KeyValuePair<AudioSource, float>(src, src.time));

                if (!AudioSettings.Reset(config))
                {
                    Plugin.Log.LogWarning("Unity refused the new audio limits; keeping the game's defaults.");
                    return;
                }

                foreach (var kv in playing)
                {
                    try
                    {
                        if (!kv.Key)
                            continue;
                        kv.Key.Play();
                        if (kv.Key.clip && kv.Value < kv.Key.clip.length)
                            kv.Key.time = kv.Value;
                    }
                    catch { }
                }

                var now = AudioSettings.GetConfiguration();
                Plugin.Log.LogMessage($"Raised audio limits: {now.numRealVoices} audible sounds at once, {now.numVirtualVoices} tracked.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not change audio limits: {e.Message}");
            }
        }
    }
}
