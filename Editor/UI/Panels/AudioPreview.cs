#nullable enable
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityGit.UI
{
    /// <summary>
    /// Plays an audio clip in the editor outside Play Mode, the way the Inspector's audio preview does. Unity has no public
    /// API for this, so it calls the internal <c>UnityEditor.AudioUtil</c> by reflection (spec §5.4). When a method is
    /// missing (a Unity version changed it), <see cref="IsAvailable"/> is false and nothing plays.
    /// </summary>
    internal static class AudioPreview
    {
        public const string Unavailable = "Unity's audio preview isn't available in this Unity version";

        static readonly MethodInfo? s_play;
        static readonly MethodInfo? s_stop;
        static readonly MethodInfo? s_isPlaying;

        static AudioPreview()
        {
            var util = typeof(EditorWindow).Assembly.GetType("UnityEditor.AudioUtil");
            if (util == null)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((util = assembly.GetType("UnityEditor.AudioUtil")) != null) break;
            if (util == null) return;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            s_play = util.GetMethod("PlayPreviewClip", flags, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
            s_stop = util.GetMethod("StopAllPreviewClips", flags, null, Type.EmptyTypes, null);
            s_isPlaying = util.GetMethod("IsPreviewClipPlaying", flags, null, Type.EmptyTypes, null);
        }

        public static bool IsAvailable => s_play != null && s_stop != null && s_isPlaying != null;

        /// <summary>Plays the clip once from the start, stopping any other preview first.</summary>
        public static void Play(AudioClip clip)
        {
            if (!IsAvailable) return;
            s_stop!.Invoke(null, null);
            s_play!.Invoke(null, new object[] { clip, 0, false });
        }

        public static void Stop()
        {
            if (IsAvailable) s_stop!.Invoke(null, null);
        }

        public static bool IsPlaying => IsAvailable && s_isPlaying!.Invoke(null, null) is true;
    }
}
