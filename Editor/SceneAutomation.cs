using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Handles loopback-only scene messages: listing the open scenes and opening a scene asset.
    /// </summary>
    /// <remarks>
    ///     Stateless, and called from the editor main thread by <see cref="CodeEditorIntegrationCore" />.
    ///     <see cref="MessageType.SceneList" /> works in both Edit and Play Mode,
    ///     <see cref="MessageType.SceneOpen" /> only in Edit Mode, because changing scenes while playing
    ///     is a game concern rather than an editor one.
    /// </remarks>
    internal static class SceneAutomation
    {
        /// <summary>
        ///     A snapshot of one open scene, decoupled from Unity's <see cref="Scene" /> so the YAML
        ///     formatting can be tested without opening scenes.
        /// </summary>
        internal readonly struct SceneInfo
        {
            internal SceneInfo(string name, string path, bool isActive, bool isLoaded, bool isDirty,
                bool isSubScene, int buildIndex, int rootCount)
            {
                Name = name;
                Path = path;
                IsActive = isActive;
                IsLoaded = isLoaded;
                IsDirty = isDirty;
                IsSubScene = isSubScene;
                BuildIndex = buildIndex;
                RootCount = rootCount;
            }

            internal string Name { get; }

            /// <summary>
            ///     The project relative asset path, empty for a scene that was never saved.
            /// </summary>
            internal string Path { get; }

            internal bool IsActive { get; }
            internal bool IsLoaded { get; }
            internal bool IsDirty { get; }
            internal bool IsSubScene { get; }

            /// <summary>
            ///     The index in the build settings, or -1 when the scene is not part of them.
            /// </summary>
            internal int BuildIndex { get; }

            /// <summary>
            ///     The number of root GameObjects, or -1 when the scene is not loaded and it cannot be read.
            /// </summary>
            internal int RootCount { get; }
        }

        /// <summary>
        ///     Processes a scene message and answers the requesting client.
        /// </summary>
        /// <param name="message">The incoming message.</param>
        /// <param name="answer">The callback used to send the response.</param>
        internal static void Process(Message message, Action<IPEndPoint, MessageType, string> answer)
        {
            if (!AutomationProtocol.IsLoopback(message.Origin))
            {
                Reply(answer, message, AutomationProtocol.Error(
                    AutomationProtocol.TryReadRequestId(message.Value), "forbidden",
                    "Scene requests are only accepted from loopback clients."));
                return;
            }

            if (!AutomationProtocol.TryParseRequest(message.Value, out var request, out var requestId,
                    out var parseError))
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "invalid_request", parseError));
                return;
            }

            try
            {
                switch (message.Type)
                {
                    case MessageType.SceneList:
                        ReplyWithScenes(answer, message, requestId);
                        break;
                    case MessageType.SceneOpen:
                        ProcessOpen(answer, message, request, requestId);
                        break;
                }
            }
            catch (Exception exception)
            {
                Reply(answer, message,
                    AutomationProtocol.Error(requestId, "internal_error", exception.Message));
            }
        }

        /// <summary>
        ///     Opens a scene asset, after making sure no unsaved work is silently lost.
        /// </summary>
        private static void ProcessOpen(Action<IPEndPoint, MessageType, string> answer, Message message,
            JObject request, JToken requestId)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ReplyError(answer, message, requestId, "is_playing",
                    "Opening a scene is only available in Edit Mode. Exit Play Mode first.");
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ReplyError(answer, message, requestId, "busy",
                    "Unity is compiling or importing assets. Try again once it is idle.");
                return;
            }

            if (!TryResolveScenePath(request.Value<string>("path"), out var assetPath, out var pathError))
            {
                ReplyError(answer, message, requestId, "invalid_request", pathError);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(assetPath) == null)
            {
                ReplyError(answer, message, requestId, "not_found",
                    $"No scene asset exists at '{assetPath}'.");
                return;
            }

            if (!TryParseOpenSceneMode(request.Value<string>("mode"), out var openMode, out var modeError))
            {
                ReplyError(answer, message, requestId, "invalid_request", modeError);
                return;
            }

            if (!TryParseUnsavedChanges(request.Value<string>("unsavedChanges"), out var unsavedChanges,
                    out var unsavedError))
            {
                ReplyError(answer, message, requestId, "invalid_request", unsavedError);
                return;
            }

            // Only Single closes the currently open scenes, so only Single can lose unsaved work.
            if (openMode == OpenSceneMode.Single)
            {
                var dirtyScenes = CollectOpenScenes().Where(scene => scene.IsDirty).ToList();
                if (dirtyScenes.Count > 0)
                    switch (unsavedChanges)
                    {
                        case "refuse":
                            ReplyError(answer, message, requestId, "unsaved_changes",
                                "Opening this scene would discard unsaved changes in " +
                                string.Join(", ", dirtyScenes.Select(DescribeScene)) +
                                ". Set unsavedChanges to \"save\" or \"discard\" to continue.");
                            return;
                        case "save":
                            // SaveOpenScenes is non modal, unlike SaveCurrentModifiedScenesIfUserWantsTo,
                            // which would block automation on a dialog nobody can answer.
                            if (!EditorSceneManager.SaveOpenScenes())
                            {
                                ReplyError(answer, message, requestId, "save_failed",
                                    "Unity could not save the modified scenes, so the scene was not opened.");
                                return;
                            }

                            break;
                    }
            }

            EditorSceneManager.OpenScene(assetPath, openMode);
            ReplyWithScenes(answer, message, requestId);
        }

        /// <summary>
        ///     Answers with the current scene state, used by both messages so a client always sees the
        ///     result of what it asked for.
        /// </summary>
        private static void ReplyWithScenes(Action<IPEndPoint, MessageType, string> answer, Message message,
            JToken requestId)
        {
            Reply(answer, message, AutomationProtocol.Success(requestId, "scenes",
                BuildScenesYaml(CollectOpenScenes(), EditorApplication.isPlaying)));
        }

        /// <summary>
        ///     Normalizes a requested scene path into a project relative asset path.
        /// </summary>
        /// <param name="requestedPath">The path from the request, absolute or project relative.</param>
        /// <param name="assetPath">The normalized asset path, using forward slashes.</param>
        /// <param name="error">A human readable reason when the path is not usable.</param>
        /// <returns>True when the path could be normalized.</returns>
        internal static bool TryResolveScenePath(string requestedPath, out string assetPath, out string error)
        {
            assetPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                error = "A non-empty scene path is required.";
                return false;
            }

            var relativePath = FileUtility.MakeRelativeToProjectPath(requestedPath.Trim());
            if (string.IsNullOrEmpty(relativePath))
            {
                error = $"The scene path '{requestedPath}' is outside of the Unity project.";
                return false;
            }

            relativePath = relativePath.NormalizeWindowsToUnix();
            if (!relativePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                error = $"The scene path '{requestedPath}' must point at a .unity scene asset.";
                return false;
            }

            assetPath = relativePath;
            return true;
        }

        /// <summary>
        ///     Parses the optional <c>mode</c> property, defaulting to <see cref="OpenSceneMode.Single" />.
        /// </summary>
        private static bool TryParseOpenSceneMode(string value, out OpenSceneMode mode, out string error)
        {
            mode = OpenSceneMode.Single;
            error = null;

            if (string.IsNullOrWhiteSpace(value))
                return true;

            switch (value.Trim().ToLowerInvariant())
            {
                case "single":
                    mode = OpenSceneMode.Single;
                    return true;
                case "additive":
                    mode = OpenSceneMode.Additive;
                    return true;
                case "additivewithoutloading":
                    mode = OpenSceneMode.AdditiveWithoutLoading;
                    return true;
                default:
                    error = $"mode must be Single, Additive or AdditiveWithoutLoading, but was '{value}'.";
                    return false;
            }
        }

        /// <summary>
        ///     Parses the optional <c>unsavedChanges</c> property, defaulting to refusing the request.
        /// </summary>
        private static bool TryParseUnsavedChanges(string value, out string unsavedChanges, out string error)
        {
            unsavedChanges = "refuse";
            error = null;

            if (string.IsNullOrWhiteSpace(value))
                return true;

            switch (value.Trim().ToLowerInvariant())
            {
                case "refuse":
                case "save":
                case "discard":
                    unsavedChanges = value.Trim().ToLowerInvariant();
                    return true;
                default:
                    error = $"unsavedChanges must be refuse, save or discard, but was '{value}'.";
                    return false;
            }
        }

        /// <summary>
        ///     Collects every scene that is currently open in the Editor, in hierarchy order.
        /// </summary>
        private static List<SceneInfo> CollectOpenScenes()
        {
            var scenes = new List<SceneInfo>();
            var activeScene = SceneManager.GetActiveScene();

            for (var index = 0; index < EditorSceneManager.sceneCount; index++)
            {
                var scene = EditorSceneManager.GetSceneAt(index);
                if (!scene.IsValid())
                    continue;

                // rootCount is only meaningful once the scene's contents are loaded.
                var rootCount = scene.isLoaded ? scene.rootCount : -1;
                scenes.Add(new SceneInfo(scene.name, scene.path, scene == activeScene, scene.isLoaded,
                    scene.isDirty, scene.isSubScene, scene.buildIndex, rootCount));
            }

            return scenes;
        }

        /// <summary>
        ///     Builds the compact YAML document describing the open scenes.
        /// </summary>
        /// <param name="scenes">The open scenes, in hierarchy order.</param>
        /// <param name="isPlaying">Whether the Editor is in Play Mode.</param>
        /// <returns>The YAML document.</returns>
        internal static string BuildScenesYaml(IReadOnlyList<SceneInfo> scenes, bool isPlaying)
        {
            var builder = new StringBuilder();
            builder.Append("mode: ").Append(isPlaying ? "Play" : "Edit").Append('\n');

            var active = scenes.FirstOrDefault(scene => scene.IsActive);
            builder.Append("activeScene: ")
                .Append(scenes.Any(scene => scene.IsActive) ? Quote(active.Path) : "null")
                .Append('\n');

            if (scenes.Count == 0)
            {
                builder.Append("scenes: []");
                return builder.ToString();
            }

            builder.Append("scenes:");
            foreach (var scene in scenes)
            {
                builder.Append("\n  - name: ").Append(Quote(scene.Name));
                builder.Append("\n    path: ").Append(Quote(scene.Path));
                builder.Append("\n    isActive: ").Append(Bool(scene.IsActive));
                builder.Append("\n    isLoaded: ").Append(Bool(scene.IsLoaded));
                builder.Append("\n    isDirty: ").Append(Bool(scene.IsDirty));
                builder.Append("\n    isSubScene: ").Append(Bool(scene.IsSubScene));
                builder.Append("\n    buildIndex: ")
                    .Append(scene.BuildIndex.ToString(CultureInfo.InvariantCulture));
                if (scene.RootCount >= 0)
                    builder.Append("\n    rootCount: ")
                        .Append(scene.RootCount.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string DescribeScene(SceneInfo scene)
        {
            return string.IsNullOrEmpty(scene.Path) ? $"'{scene.Name}' (never saved)" : $"'{scene.Path}'";
        }

        private static string Quote(string value)
        {
            return string.Concat("\"", AutomationProtocol.EscapeYamlString(value ?? string.Empty), "\"");
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static void ReplyError(Action<IPEndPoint, MessageType, string> answer, Message message,
            JToken requestId, string code, string errorMessage)
        {
            Reply(answer, message, AutomationProtocol.Error(requestId, code, errorMessage));
        }

        private static void Reply(Action<IPEndPoint, MessageType, string> answer, Message message,
            string payload)
        {
            answer(message.Origin, message.Type, payload);
        }
    }
}
