using System;
using System.Reflection;
using UnityEditor;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Reads Unity's <c>Script Changes While Playing</c> preference
    ///     (Preferences > General), which decides what the editor does when scripts
    ///     change while the game is running.
    /// </summary>
    internal static class ScriptChangesDuringPlay
    {
        private const string PrefKey = "ScriptCompilationDuringPlay";

        /// <summary>
        ///     The value the preference holds for <c>Recompile After Finished Playing</c>.
        /// </summary>
        /// <remarks>
        ///     internal enum UnityEditor.ScriptChangesDuringPlayOptions
        ///     The fallback is the position of the option in the dropdown, which is what the preference stores.
        /// </remarks>
        private static readonly int _recompileAfterFinishedPlaying = ReadRecompileAfterFinishedPlaying();

        /// <summary>
        ///     Whether Unity holds off compiling scripts until play mode ends.
        ///     While this is true, refreshing the asset database in play mode cannot compile scripts,
        ///     so it can neither reload the domain nor stop play mode.
        /// </summary>
        /// <remarks>
        ///     The preference is read every time, the user can change it at any moment.
        /// </remarks>
        public static bool IsCompilationDeferred =>
            EditorPrefs.GetInt(PrefKey, 0) == _recompileAfterFinishedPlaying;

        private static int ReadRecompileAfterFinishedPlaying()
        {
            var options = typeof(EditorWindow).Assembly.GetType("UnityEditor.ScriptChangesDuringPlayOptions");
            var field = options?.GetField("RecompileAfterFinishedPlaying", BindingFlags.Static | BindingFlags.Public);
            if (field == null)
                return 1;

            return Convert.ToInt32(field.GetValue(null));
        }
    }
}
