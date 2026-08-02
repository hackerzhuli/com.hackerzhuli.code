using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;
using Object = UnityEngine.Object;
using static Hackerzhuli.Code.Editor.AutomationValueFormatter;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Handles the loopback-only messages that let a client see what is inside the open scenes:
    ///     scene and GameObject hierarchies, GameObject lookup, and GameObject details.
    /// </summary>
    /// <remarks>
    ///     Stateless, and called from the editor main thread by <see cref="CodeEditorIntegrationCore" />.
    ///     All four messages work in both Edit and Play Mode, because inspecting the scene never changes it.
    ///     <para>
    ///         Every GameObject is reported with an opaque id, which is stable for as long as the object
    ///         lives in the current session and is the only unambiguous way to address one. A <c>path</c> is
    ///         accepted everywhere as a convenience, but two objects can share one, in which case the request
    ///         fails with <c>ambiguous_path</c> and lists the candidate ids so the next call can be precise.
    ///     </para>
    ///     <para>
    ///         Responses are capped on three axes so a large scene cannot produce an unusable answer: at most
    ///         <see cref="ChildLimit" /> children per object, <see cref="RootLimit" /> scene roots, and
    ///         <see cref="HierarchyObjectLimit" /> objects in total, the last of which is enforced by
    ///         lowering the depth before anything is written.
    ///     </para>
    /// </remarks>
    internal static class GameObjectAutomation
    {
        /// <summary>
        ///     The most objects a single hierarchy response may contain.
        /// </summary>
        private const int HierarchyObjectLimit = 200;

        /// <summary>
        ///     The most children of one object that are listed.
        /// </summary>
        private const int ChildLimit = 20;

        /// <summary>
        ///     The most root objects of one scene that are listed.
        /// </summary>
        private const int RootLimit = 50;

        /// <summary>
        ///     The most matches <see cref="MessageType.GameObjectFind" /> reports.
        /// </summary>
        private const int FindResultLimit = 100;

        /// <summary>
        ///     The most candidates named when a path is ambiguous.
        /// </summary>
        private const int AmbiguityCandidateLimit = 20;

        /// <summary>
        ///     Property getters that create a new object as a side effect, which an inspection must never do.
        /// </summary>
        /// <remarks>
        ///     <c>Renderer.material</c>, <c>Renderer.materials</c> and <c>MeshFilter.mesh</c> instantiate a
        ///     copy of the shared asset and assign it back, which leaks an object and dirties the scene.
        ///     Their <c>shared</c> counterparts are reported instead, and carry the same information.
        /// </remarks>
        private static readonly HashSet<string> SideEffectPropertyNames = new(StringComparer.Ordinal)
        {
            "material", "materials", "mesh"
        };

        /// <summary>
        ///     Types whose members describe the component plumbing rather than the component, and are
        ///     therefore the same noise on every single component.
        /// </summary>
        private static readonly HashSet<Type> BoilerplateDeclaringTypes = new()
        {
            typeof(Component), typeof(Behaviour), typeof(MonoBehaviour), typeof(Object)
        };

        /// <summary>
        ///     The members worth reporting by default for the built-in components a project uses most.
        /// </summary>
        /// <remarks>
        ///     Reflecting over a built-in component gives everything Unity exposes, which for something
        ///     like <c>Camera</c> is around sixty members including several matrices, and buries the dozen
        ///     values that describe how the component is actually set up. These lists are what the
        ///     inspector would show, and a client that wants the rest asks for it with
        ///     <c>fullDetailComponents</c>.
        ///     <para>
        ///         Keyed by type full name and looked up along the whole base chain, so a
        ///         <c>BoxCollider</c> gets its own entry plus <c>Collider</c>'s. Names that a Unity version
        ///         does not have, or has marked obsolete, are skipped silently, which is what lets one
        ///         list cover renamed members such as <c>drag</c> and <c>linearDamping</c>. Types from
        ///         packages this assembly does not reference are matched by name, so no dependency is
        ///         needed to describe them.
        ///     </para>
        ///     <para>
        ///         A component with no entry here, which includes every user script, still reports all of
        ///         its public instance members.
        ///     </para>
        /// </remarks>
        private static readonly Dictionary<string, string[]> CommonComponentMembers =
            new(StringComparer.Ordinal)
            {
                ["UnityEngine.Transform"] = new[]
                {
                    "localPosition", "localEulerAngles", "localScale", "position", "eulerAngles",
                    "lossyScale"
                },
                ["UnityEngine.RectTransform"] = new[]
                {
                    "anchoredPosition", "sizeDelta", "anchorMin", "anchorMax", "pivot", "offsetMin",
                    "offsetMax"
                },
                ["UnityEngine.Camera"] = new[]
                {
                    "clearFlags", "backgroundColor", "cullingMask", "orthographic", "orthographicSize",
                    "fieldOfView", "nearClipPlane", "farClipPlane", "rect", "depth", "renderingPath",
                    "targetTexture", "allowHDR", "allowMSAA", "aspect"
                },
                ["UnityEngine.Renderer"] = new[]
                {
                    "sharedMaterial", "sharedMaterials", "shadowCastingMode", "receiveShadows",
                    "sortingLayerName", "sortingOrder", "lightProbeUsage", "bounds"
                },
                ["UnityEngine.MeshFilter"] = new[] { "sharedMesh" },
                ["UnityEngine.SkinnedMeshRenderer"] = new[]
                {
                    "sharedMesh", "rootBone", "bones", "updateWhenOffscreen"
                },
                ["UnityEngine.SpriteRenderer"] = new[]
                {
                    "sprite", "color", "flipX", "flipY", "drawMode", "size"
                },
                ["UnityEngine.Light"] = new[]
                {
                    "type", "color", "intensity", "range", "spotAngle", "shadows", "shadowStrength",
                    "bounceIntensity", "cullingMask", "renderMode"
                },
                ["UnityEngine.Collider"] = new[] { "isTrigger", "sharedMaterial", "bounds" },
                ["UnityEngine.BoxCollider"] = new[] { "center", "size" },
                ["UnityEngine.SphereCollider"] = new[] { "center", "radius" },
                ["UnityEngine.CapsuleCollider"] = new[] { "center", "radius", "height", "direction" },
                ["UnityEngine.MeshCollider"] = new[] { "sharedMesh", "convex" },
                ["UnityEngine.Rigidbody"] = new[]
                {
                    "mass", "drag", "linearDamping", "angularDrag", "angularDamping", "useGravity",
                    "isKinematic", "interpolation", "collisionDetectionMode", "constraints", "velocity",
                    "linearVelocity"
                },
                ["UnityEngine.Collider2D"] = new[] { "isTrigger", "sharedMaterial", "offset", "bounds" },
                ["UnityEngine.Rigidbody2D"] = new[]
                {
                    "bodyType", "mass", "gravityScale", "constraints", "interpolation",
                    "collisionDetectionMode", "velocity", "linearVelocity"
                },
                ["UnityEngine.Animator"] = new[]
                {
                    "runtimeAnimatorController", "avatar", "applyRootMotion", "updateMode", "cullingMode",
                    "speed"
                },
                ["UnityEngine.AudioSource"] = new[]
                {
                    "clip", "outputAudioMixerGroup", "volume", "pitch", "loop", "playOnAwake",
                    "spatialBlend", "priority", "mute"
                },
                ["UnityEngine.Canvas"] = new[]
                {
                    "renderMode", "worldCamera", "planeDistance", "sortingLayerName", "sortingOrder",
                    "pixelPerfect", "referencePixelsPerUnit", "scaleFactor"
                },
                ["UnityEngine.CanvasGroup"] = new[]
                {
                    "alpha", "interactable", "blocksRaycasts", "ignoreParentGroups"
                },
                ["UnityEngine.UIElements.UIDocument"] = new[]
                {
                    "panelSettings", "visualTreeAsset", "sortingOrder"
                },
                ["UnityEngine.UI.Graphic"] = new[] { "color", "raycastTarget", "raycastPadding" },
                ["UnityEngine.UI.Image"] = new[]
                {
                    "sprite", "type", "preserveAspect", "fillMethod", "fillAmount", "pixelsPerUnitMultiplier"
                },
                ["UnityEngine.UI.RawImage"] = new[] { "texture", "uvRect" },
                ["UnityEngine.UI.Text"] = new[]
                {
                    "text", "font", "fontSize", "fontStyle", "alignment", "resizeTextForBestFit"
                },
                ["UnityEngine.UI.Selectable"] = new[]
                {
                    "interactable", "transition", "targetGraphic", "navigation"
                },
                ["UnityEngine.UI.Toggle"] = new[] { "isOn", "group", "graphic" },
                ["UnityEngine.UI.Slider"] = new[]
                {
                    "value", "minValue", "maxValue", "wholeNumbers", "direction"
                },
                ["UnityEngine.UI.InputField"] = new[]
                {
                    "text", "characterLimit", "contentType", "lineType", "placeholder"
                },
                ["UnityEngine.UI.ScrollRect"] = new[]
                {
                    "content", "viewport", "horizontal", "vertical", "movementType", "scrollSensitivity"
                },
                ["UnityEngine.UI.CanvasScaler"] = new[]
                {
                    "uiScaleMode", "referenceResolution", "screenMatchMode", "matchWidthOrHeight",
                    "referencePixelsPerUnit"
                }
            };

        /// <summary>
        ///     One GameObject in a hierarchy response, decoupled from Unity so the YAML formatting can be
        ///     tested without building a scene.
        /// </summary>
        internal sealed class GameObjectNode
        {
            internal GameObjectNode(string name, string id, bool isActive, int totalChildCount,
                IReadOnlyList<GameObjectNode> children, bool depthLimited)
            {
                Name = name;
                Id = id;
                IsActive = isActive;
                TotalChildCount = totalChildCount;
                Children = children ?? Array.Empty<GameObjectNode>();
                DepthLimited = depthLimited;
            }

            internal string Name { get; }

            /// <summary>
            ///     The opaque id, usable to address this object in a later request.
            /// </summary>
            internal string Id { get; }

            /// <summary>
            ///     The object's own active state, not the resolved one.
            /// </summary>
            internal bool IsActive { get; }

            /// <summary>
            ///     The real number of children, which can exceed the number of <see cref="Children" />.
            /// </summary>
            internal int TotalChildCount { get; }

            /// <summary>
            ///     The children that are written, already capped to <see cref="ChildLimit" />.
            /// </summary>
            internal IReadOnlyList<GameObjectNode> Children { get; }

            /// <summary>
            ///     Whether the children are missing because the depth limit was reached rather than the
            ///     child limit, which is the difference between the two omission reasons.
            /// </summary>
            internal bool DepthLimited { get; }
        }

        /// <summary>
        ///     The document level facts of a hierarchy response.
        /// </summary>
        internal readonly struct HierarchyHeader
        {
            internal HierarchyHeader(string scene, string path, bool isPlaying, int maxDepth,
                string depthLimit, int rootCount, int rootsOmitted)
            {
                Scene = scene;
                Path = path;
                IsPlaying = isPlaying;
                MaxDepth = maxDepth;
                DepthLimit = depthLimit;
                RootCount = rootCount;
                RootsOmitted = rootsOmitted;
            }

            internal string Scene { get; }

            /// <summary>
            ///     The path of the requested object, or null for a scene hierarchy.
            /// </summary>
            internal string Path { get; }

            internal bool IsPlaying { get; }

            /// <summary>
            ///     The depth that was actually written.
            /// </summary>
            internal int MaxDepth { get; }

            /// <summary>
            ///     Why the depth stopped where it did, <c>dynamic</c> or <c>requested</c>, or null when the
            ///     whole tree fit and nothing was cut.
            /// </summary>
            internal string DepthLimit { get; }

            /// <summary>
            ///     The real number of scene roots, or -1 for a GameObject hierarchy.
            /// </summary>
            internal int RootCount { get; }

            /// <summary>
            ///     The number of roots left out, 0 when all of them fit.
            /// </summary>
            internal int RootsOmitted { get; }
        }

        /// <summary>
        ///     One match of <see cref="MessageType.GameObjectFind" />, decoupled from Unity for testing.
        /// </summary>
        internal readonly struct FindEntry
        {
            internal FindEntry(string name, string id, string scene, string path, bool isActive,
                bool isActiveInHierarchy, int componentCount)
            {
                Name = name;
                Id = id;
                Scene = scene;
                Path = path;
                IsActive = isActive;
                IsActiveInHierarchy = isActiveInHierarchy;
                ComponentCount = componentCount;
            }

            internal string Name { get; }
            internal string Id { get; }
            internal string Scene { get; }
            internal string Path { get; }
            internal bool IsActive { get; }
            internal bool IsActiveInHierarchy { get; }
            internal int ComponentCount { get; }
        }

        /// <summary>
        ///     A scene that can be searched, together with how it is addressed in requests and responses.
        /// </summary>
        private readonly struct SceneScope
        {
            internal SceneScope(Scene scene)
            {
                Scene = scene;
                Identifier = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
            }

            internal Scene Scene { get; }

            /// <summary>
            ///     The asset path, falling back to the name for a scene that was never saved and for
            ///     <c>DontDestroyOnLoad</c>.
            /// </summary>
            internal string Identifier { get; }
        }

        /// <summary>
        ///     The depth a hierarchy is written at, and why it stopped there.
        /// </summary>
        private readonly struct DepthDecision
        {
            internal DepthDecision(int maxDepth, string limit)
            {
                MaxDepth = maxDepth;
                Limit = limit;
            }

            internal int MaxDepth { get; }

            /// <summary>
            ///     <c>dynamic</c>, <c>requested</c>, or null when nothing was cut off by depth.
            /// </summary>
            internal string Limit { get; }
        }

        /// <summary>
        ///     Processes a scene or GameObject inspection message and answers the requesting client.
        /// </summary>
        /// <param name="message">The incoming message.</param>
        /// <param name="answer">The callback used to send the response.</param>
        internal static void Process(Message message, Action<IPEndPoint, MessageType, string> answer)
        {
            if (!AutomationProtocol.IsLoopback(message.Origin))
            {
                Reply(answer, message, AutomationProtocol.Error(
                    AutomationProtocol.TryReadRequestId(message.Value), "forbidden",
                    "GameObject requests are only accepted from loopback clients."));
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
                    case MessageType.SceneHierarchy:
                        ProcessSceneHierarchy(answer, message, request, requestId);
                        break;
                    case MessageType.GameObjectHierarchy:
                        ProcessGameObjectHierarchy(answer, message, request, requestId);
                        break;
                    case MessageType.GameObjectFind:
                        ProcessFind(answer, message, request, requestId);
                        break;
                    case MessageType.GameObjectInspect:
                        ProcessInspect(answer, message, request, requestId);
                        break;
                }
            }
            catch (Exception exception)
            {
                Reply(answer, message,
                    AutomationProtocol.Error(requestId, "internal_error", exception.Message));
            }
        }

        #region Message handlers

        /// <summary>
        ///     Answers with the GameObject tree of one open scene, defaulting to the active one.
        /// </summary>
        private static void ProcessSceneHierarchy(Action<IPEndPoint, MessageType, string> answer,
            Message message, JObject request, JToken requestId)
        {
            if (!TryReadDepth(request, out var requestedDepth, out var depthError))
            {
                ReplyError(answer, message, requestId, "invalid_request", depthError);
                return;
            }

            var scopes = CollectScopes();
            if (!TryResolveScope(request.Value<string>("scene"), scopes, out var scope, out var scopeError))
            {
                ReplyError(answer, message, requestId, "not_found", scopeError);
                return;
            }

            var roots = CollectRoots(scope);
            var decision = DetermineDepth(roots, RootLimit, requestedDepth);
            var nodes = new List<GameObjectNode>();
            for (var index = 0; index < roots.Count && index < RootLimit; index++)
                nodes.Add(BuildNode(roots[index], decision.MaxDepth));

            var header = new HierarchyHeader(scope.Identifier, null, EditorApplication.isPlaying,
                decision.MaxDepth, decision.Limit, roots.Count, Math.Max(0, roots.Count - RootLimit));
            Reply(answer, message,
                AutomationProtocol.Success(requestId, "hierarchy", BuildHierarchyYaml(header, nodes)));
        }

        /// <summary>
        ///     Answers with the descendant tree of one GameObject, the requested object being the root.
        /// </summary>
        private static void ProcessGameObjectHierarchy(Action<IPEndPoint, MessageType, string> answer,
            Message message, JObject request, JToken requestId)
        {
            if (!TryReadDepth(request, out var requestedDepth, out var depthError))
            {
                ReplyError(answer, message, requestId, "invalid_request", depthError);
                return;
            }

            if (!TryResolveTarget(request, out var target, out var errorCode, out var error))
            {
                ReplyError(answer, message, requestId, errorCode, error);
                return;
            }

            var roots = new List<GameObject> { target };
            var decision = DetermineDepth(roots, RootLimit, requestedDepth);
            var nodes = new List<GameObjectNode> { BuildNode(target, decision.MaxDepth) };

            var header = new HierarchyHeader(DescribeScene(target.scene), GetPath(target),
                EditorApplication.isPlaying, decision.MaxDepth, decision.Limit, -1, 0);
            Reply(answer, message,
                AutomationProtocol.Success(requestId, "hierarchy", BuildHierarchyYaml(header, nodes)));
        }

        /// <summary>
        ///     Answers with every GameObject matching a name or an exact path.
        /// </summary>
        private static void ProcessFind(Action<IPEndPoint, MessageType, string> answer, Message message,
            JObject request, JToken requestId)
        {
            var name = request.Value<string>("name");
            var path = request.Value<string>("path");
            var hasName = !string.IsNullOrEmpty(name);
            var hasPath = !string.IsNullOrEmpty(path);

            if (hasName == hasPath)
            {
                ReplyError(answer, message, requestId, "invalid_request",
                    "Exactly one of name or path is required.");
                return;
            }

            if (!TryReadMatchMode(request, hasPath, out var contains, out var matchError))
            {
                ReplyError(answer, message, requestId, "invalid_request", matchError);
                return;
            }

            var scopes = CollectScopes();
            var sceneFilter = request.Value<string>("scene");
            if (!string.IsNullOrEmpty(sceneFilter))
            {
                if (!TryResolveScope(sceneFilter, scopes, out var scope, out var scopeError))
                {
                    ReplyError(answer, message, requestId, "not_found", scopeError);
                    return;
                }

                scopes = new List<SceneScope> { scope };
            }

            List<GameObject> matches;
            if (hasPath)
            {
                if (!TryParsePath(path, out var segments, out var pathError))
                {
                    ReplyError(answer, message, requestId, "invalid_request", pathError);
                    return;
                }

                matches = FindByPath(scopes, segments);
            }
            else
            {
                matches = FindByName(scopes, name, contains);
            }

            var entries = matches.Take(FindResultLimit).Select(ToFindEntry).ToList();
            Reply(answer, message, AutomationProtocol.Success(requestId, "gameObjects",
                BuildFindYaml(hasPath ? path : name, hasPath ? "path" : "name",
                    contains ? "contains" : "exact", entries, matches.Count)));
        }

        /// <summary>
        ///     Answers with the main properties of one GameObject and the members of all its components.
        /// </summary>
        private static void ProcessInspect(Action<IPEndPoint, MessageType, string> answer, Message message,
            JObject request, JToken requestId)
        {
            if (!TryReadFullDetailComponents(request, out var fullDetail, out var detailError))
            {
                ReplyError(answer, message, requestId, "invalid_request", detailError);
                return;
            }

            if (!TryResolveTarget(request, out var target, out var errorCode, out var error))
            {
                ReplyError(answer, message, requestId, errorCode, error);
                return;
            }

            Reply(answer, message,
                AutomationProtocol.Success(requestId, "gameObject", BuildInspection(target, fullDetail)));
        }

        #endregion

        #region Request parameters

        /// <summary>
        ///     Reads the optional <c>depth</c> property.
        /// </summary>
        /// <param name="request">The parsed request.</param>
        /// <param name="depth">The requested depth, null when the property is absent.</param>
        /// <param name="error">A human readable reason when the property is not usable.</param>
        /// <returns>True when the property is absent or a non-negative integer.</returns>
        private static bool TryReadDepth(JObject request, out int? depth, out string error)
        {
            depth = null;
            error = null;

            var token = request["depth"];
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type != JTokenType.Integer || !long.TryParse(token.ToString(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
                parsed < 0 || parsed > int.MaxValue)
            {
                error = "depth must be a non-negative integer.";
                return false;
            }

            depth = (int)parsed;
            return true;
        }

        /// <summary>
        ///     Reads the optional <c>match</c> property of a find request.
        /// </summary>
        /// <param name="request">The parsed request.</param>
        /// <param name="hasPath">Whether the request searches by path rather than by name.</param>
        /// <param name="contains">Whether a substring match was requested.</param>
        /// <param name="error">A human readable reason when the property is not usable.</param>
        /// <returns>True when the property is absent or valid for this kind of query.</returns>
        /// <remarks>
        ///     A path always identifies exactly one place in the hierarchy, so matching it loosely would
        ///     make it something other than a path. Fuzzy searching is what <c>name</c> is for.
        /// </remarks>
        private static bool TryReadMatchMode(JObject request, bool hasPath, out bool contains,
            out string error)
        {
            contains = false;
            error = null;

            var value = request.Value<string>("match");
            if (string.IsNullOrEmpty(value))
                return true;

            switch (value)
            {
                case "exact":
                    return true;
                case "contains":
                    if (hasPath)
                    {
                        error = "match \"contains\" applies to name only, because a path is always matched " +
                                "in full. Search by name instead.";
                        return false;
                    }

                    contains = true;
                    return true;
                default:
                    error = $"match must be \"exact\" or \"contains\", but was '{value}'.";
                    return false;
            }
        }

        /// <summary>
        ///     Reads the optional <c>fullDetailComponents</c> property.
        /// </summary>
        /// <param name="request">The parsed request.</param>
        /// <param name="names">The component type names to report in full detail, never null.</param>
        /// <param name="error">A human readable reason when the property is not usable.</param>
        /// <returns>True when the property is absent or an array of non-empty strings.</returns>
        private static bool TryReadFullDetailComponents(JObject request, out HashSet<string> names,
            out string error)
        {
            names = new HashSet<string>(StringComparer.Ordinal);
            error = null;

            var token = request["fullDetailComponents"];
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type != JTokenType.Array)
            {
                error = "fullDetailComponents must be an array of component type names.";
                return false;
            }

            foreach (var item in token)
            {
                if (item.Type != JTokenType.String || string.IsNullOrWhiteSpace(item.Value<string>()))
                {
                    error = "fullDetailComponents entries must be non-empty strings.";
                    return false;
                }

                names.Add(item.Value<string>().Trim());
            }

            return true;
        }

        /// <summary>
        ///     Splits a GameObject path into its name segments.
        /// </summary>
        /// <param name="raw">The path from the request, a leading slash is tolerated.</param>
        /// <param name="segments">The names from the root down to the target.</param>
        /// <param name="error">A human readable reason when the path is not usable.</param>
        /// <returns>True when the path could be split.</returns>
        /// <remarks>
        ///     A GameObject whose name contains a slash cannot be addressed this way, which is one of the
        ///     reasons every response also carries an opaque id.
        /// </remarks>
        internal static bool TryParsePath(string raw, out string[] segments, out string error)
        {
            segments = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "A non-empty path is required.";
                return false;
            }

            var trimmed = raw.Trim();
            if (trimmed.StartsWith("/", StringComparison.Ordinal))
                trimmed = trimmed.Substring(1);

            var parts = trimmed.Split('/');
            if (parts.Any(string.IsNullOrEmpty))
            {
                error = $"The path '{raw}' has an empty name segment.";
                return false;
            }

            segments = parts;
            return true;
        }

        #endregion

        #region Target resolution

        /// <summary>
        ///     Resolves the GameObject a request is about, from either an opaque id or an exact path.
        /// </summary>
        /// <param name="request">The parsed request.</param>
        /// <param name="target">The resolved GameObject.</param>
        /// <param name="errorCode">The machine readable error code when resolution failed.</param>
        /// <param name="error">A human readable reason when resolution failed.</param>
        /// <returns>True when exactly one GameObject was identified.</returns>
        private static bool TryResolveTarget(JObject request, out GameObject target, out string errorCode,
            out string error)
        {
            target = null;
            errorCode = null;
            error = null;

            var idToken = request["id"];
            var hasId = idToken != null && idToken.Type != JTokenType.Null;
            var path = request.Value<string>("path");
            var hasPath = !string.IsNullOrEmpty(path);

            if (hasId == hasPath)
            {
                errorCode = "invalid_request";
                error = "Exactly one of id or path is required.";
                return false;
            }

            if (hasId)
                return TryResolveById(idToken, out target, out errorCode, out error);

            if (!TryParsePath(path, out var segments, out error))
            {
                errorCode = "invalid_request";
                return false;
            }

            var scopes = CollectScopes();
            var sceneFilter = request.Value<string>("scene");
            if (!string.IsNullOrEmpty(sceneFilter))
            {
                if (!TryResolveScope(sceneFilter, scopes, out var scope, out var scopeError))
                {
                    errorCode = "not_found";
                    error = scopeError;
                    return false;
                }

                scopes = new List<SceneScope> { scope };
            }

            var matches = FindByPath(scopes, segments);
            switch (matches.Count)
            {
                case 1:
                    target = matches[0];
                    return true;
                case 0:
                    errorCode = "not_found";
                    error = $"No GameObject matches path '{path}'.";
                    return false;
                default:
                    errorCode = "ambiguous_path";
                    error = DescribeAmbiguity(path, matches);
                    return false;
            }
        }

        /// <summary>
        ///     Resolves an opaque object id, accepting a component's id as a reference to its GameObject.
        /// </summary>
        private static bool TryResolveById(JToken idToken, out GameObject target, out string errorCode,
            out string error)
        {
            target = null;
            errorCode = null;
            error = null;

            if (idToken.Type != JTokenType.String ||
                !UnityObjectId.TryResolve(idToken.Value<string>(), out var resolved))
            {
                errorCode = "invalid_request";
                error = "id must be a non-empty hexadecimal string.";
                return false;
            }

            var id = idToken.Value<string>();
            switch (resolved)
            {
                case GameObject gameObject:
                    target = gameObject;
                    return true;
                case Component component:
                    target = component.gameObject;
                    return true;
                case null:
                    errorCode = "not_found";
                    error = $"No object has id '{id}'. " +
                            "Object ids do not survive a domain reload or entering and leaving Play Mode, " +
                            "so look the object up by path again.";
                    return false;
                default:
                    errorCode = "not_found";
                    error = $"Object id '{id}' identifies a " +
                            $"{resolved.GetType().Name}, not a GameObject or a component.";
                    return false;
            }
        }

        /// <summary>
        ///     Builds the message that lists the candidates of an ambiguous path.
        /// </summary>
        private static string DescribeAmbiguity(string path, IReadOnlyList<GameObject> matches)
        {
            var builder = new StringBuilder();
            builder.Append(matches.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append(" GameObjects match path '").Append(path);
            builder.Append("'. Retry with one of these ids:");

            var shown = Math.Min(matches.Count, AmbiguityCandidateLimit);
            for (var index = 0; index < shown; index++)
            {
                var match = matches[index];
                builder.Append("\n  id=").Append(QuoteYamlString(UnityObjectId.Get(match)));
                builder.Append(" scene=\"").Append(DescribeScene(match.scene)).Append('"');
                builder.Append(" path=\"").Append(GetPath(match)).Append('"');
            }

            if (matches.Count > shown)
                builder.Append("\n  and ")
                    .Append((matches.Count - shown).ToString(CultureInfo.InvariantCulture))
                    .Append(" more.");

            return builder.ToString();
        }

        #endregion

        #region Scene traversal

        /// <summary>
        ///     Collects every scene whose contents can be walked right now.
        /// </summary>
        private static List<SceneScope> CollectScopes()
        {
            var scopes = new List<SceneScope>();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                scopes.Add(new SceneScope(scene));
            }

            if (EditorApplication.isPlaying && TryGetDontDestroyOnLoadScene(out var persistent))
                scopes.Add(new SceneScope(persistent));

            return scopes;
        }

        /// <summary>
        ///     Gets the scene that holds objects marked <see cref="Object.DontDestroyOnLoad" />.
        /// </summary>
        /// <param name="scene">The persistent scene.</param>
        /// <returns>True when the scene could be reached.</returns>
        /// <remarks>
        ///     <see cref="SceneManager" /> never returns this scene, and there is no API that hands out its
        ///     handle. Marking a throwaway object persistent and reading which scene Unity moved it to is
        ///     the only supported way to get it. Without this, objects that survive scene loads, which is
        ///     most of a running game's managers, would be invisible.
        /// </remarks>
        private static bool TryGetDontDestroyOnLoadScene(out Scene scene)
        {
            scene = default;
            GameObject probe = null;
            try
            {
                probe = new GameObject(nameof(GameObjectAutomation))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                Object.DontDestroyOnLoad(probe);
                scene = probe.scene;
                return scene.IsValid();
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (probe != null)
                    Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        ///     Picks the scene a request is about, defaulting to the active scene.
        /// </summary>
        /// <param name="requested">The scene asset path or name from the request, may be empty.</param>
        /// <param name="scopes">The scenes that can be walked.</param>
        /// <param name="scope">The resolved scene.</param>
        /// <param name="error">A human readable reason when no scene matches.</param>
        /// <returns>True when a scene was resolved.</returns>
        private static bool TryResolveScope(string requested, List<SceneScope> scopes, out SceneScope scope,
            out string error)
        {
            scope = default;
            error = null;

            if (scopes.Count == 0)
            {
                error = "No loaded scene is open.";
                return false;
            }

            if (string.IsNullOrEmpty(requested))
            {
                var active = SceneManager.GetActiveScene();
                foreach (var candidate in scopes)
                    if (candidate.Scene == active)
                    {
                        scope = candidate;
                        return true;
                    }

                scope = scopes[0];
                return true;
            }

            var normalized = requested.Trim().NormalizeWindowsToUnix();
            foreach (var candidate in scopes)
                if (string.Equals(candidate.Identifier, normalized, StringComparison.Ordinal) ||
                    string.Equals(candidate.Scene.name, normalized, StringComparison.Ordinal))
                {
                    scope = candidate;
                    return true;
                }

            error = $"No open scene matches '{requested}'. Open scenes are " +
                    string.Join(", ", scopes.Select(candidate => $"'{candidate.Identifier}'")) + ".";
            return false;
        }

        /// <summary>
        ///     Gets the root objects of a scene that are meant to be seen.
        /// </summary>
        private static List<GameObject> CollectRoots(SceneScope scope)
        {
            return scope.Scene.GetRootGameObjects().Where(IsVisible).ToList();
        }

        /// <summary>
        ///     Gets the children of an object that are meant to be seen, in hierarchy order.
        /// </summary>
        private static List<GameObject> CollectChildren(GameObject gameObject)
        {
            var transform = gameObject.transform;
            var children = new List<GameObject>(transform.childCount);
            for (var index = 0; index < transform.childCount; index++)
            {
                var child = transform.GetChild(index).gameObject;
                if (IsVisible(child))
                    children.Add(child);
            }

            return children;
        }

        /// <summary>
        ///     Determines whether an object is part of the hierarchy a user sees, as opposed to an editor
        ///     internal object that only exists to make the editor work.
        /// </summary>
        private static bool IsVisible(GameObject gameObject)
        {
            return gameObject != null && (gameObject.hideFlags & HideFlags.HideInHierarchy) == 0;
        }

        /// <summary>
        ///     Builds the slash separated path of an object, from its scene root down.
        /// </summary>
        /// <param name="gameObject">The object to describe.</param>
        /// <returns>The path.</returns>
        internal static string GetPath(GameObject gameObject)
        {
            var segments = new List<string>();
            for (var transform = gameObject.transform; transform != null; transform = transform.parent)
                segments.Add(transform.name);
            segments.Reverse();
            return string.Join("/", segments);
        }

        /// <summary>
        ///     Describes the scene of an object the same way a request addresses one.
        /// </summary>
        private static string DescribeScene(Scene scene)
        {
            if (!scene.IsValid())
                return string.Empty;
            return string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
        }

        /// <summary>
        ///     Collects every object whose full path equals the requested one.
        /// </summary>
        private static List<GameObject> FindByPath(IReadOnlyList<SceneScope> scopes, string[] segments)
        {
            var matches = new List<GameObject>();
            foreach (var scope in scopes)
            foreach (var root in CollectRoots(scope))
                MatchPath(root, segments, 0, matches);
            return matches;
        }

        /// <summary>
        ///     Walks one branch as long as it keeps matching the path segments.
        /// </summary>
        private static void MatchPath(GameObject gameObject, string[] segments, int index,
            List<GameObject> matches)
        {
            if (!string.Equals(gameObject.name, segments[index], StringComparison.Ordinal))
                return;

            if (index == segments.Length - 1)
            {
                matches.Add(gameObject);
                return;
            }

            foreach (var child in CollectChildren(gameObject))
                MatchPath(child, segments, index + 1, matches);
        }

        /// <summary>
        ///     Collects every object whose name matches, anywhere in the searched scenes.
        /// </summary>
        private static List<GameObject> FindByName(IReadOnlyList<SceneScope> scopes, string name,
            bool contains)
        {
            var matches = new List<GameObject>();
            foreach (var scope in scopes)
            foreach (var root in CollectRoots(scope))
                MatchName(root, name, contains, matches);
            return matches;
        }

        /// <summary>
        ///     Walks a whole branch, collecting every object whose name matches.
        /// </summary>
        private static void MatchName(GameObject gameObject, string name, bool contains,
            List<GameObject> matches)
        {
            var isMatch = contains
                ? gameObject.name.IndexOf(name, StringComparison.Ordinal) >= 0
                : string.Equals(gameObject.name, name, StringComparison.Ordinal);
            if (isMatch)
                matches.Add(gameObject);

            foreach (var child in CollectChildren(gameObject))
                MatchName(child, name, contains, matches);
        }

        /// <summary>
        ///     Summarizes a match for a find response.
        /// </summary>
        private static FindEntry ToFindEntry(GameObject gameObject)
        {
            return new FindEntry(gameObject.name, UnityObjectId.Get(gameObject),
                DescribeScene(gameObject.scene), GetPath(gameObject), gameObject.activeSelf,
                gameObject.activeInHierarchy, gameObject.GetComponents<Component>().Length);
        }

        #endregion

        #region Hierarchy building

        /// <summary>
        ///     Chooses how deep a hierarchy can go without exceeding the object budget.
        /// </summary>
        /// <param name="roots">The roots that will be written.</param>
        /// <param name="rootLimit">The most roots that will be written.</param>
        /// <param name="requestedDepth">The depth the client asked for, null when it did not.</param>
        /// <returns>The depth to write and why it stopped there.</returns>
        /// <remarks>
        ///     The depth is raised one level at a time and the resulting size measured, rather than
        ///     measuring the whole tree, because a deep scene has far more objects than the budget and
        ///     counting all of them would cost more than the response is worth.
        /// </remarks>
        private static DepthDecision DetermineDepth(IReadOnlyList<GameObject> roots, int rootLimit,
            int? requestedDepth)
        {
            var hardLimit = requestedDepth ?? int.MaxValue;
            var depth = 0;
            var previous = CountObjects(roots, rootLimit, 0, HierarchyObjectLimit + 1);

            while (depth < hardLimit)
            {
                var next = CountObjects(roots, rootLimit, depth + 1, HierarchyObjectLimit + 1);
                if (next > HierarchyObjectLimit)
                    return new DepthDecision(depth, "dynamic");
                if (next == previous)
                    return new DepthDecision(depth, null);
                previous = next;
                depth++;
            }

            var deeper = CountObjects(roots, rootLimit, depth + 1, HierarchyObjectLimit + 1);
            return new DepthDecision(depth, deeper > previous ? "requested" : null);
        }

        /// <summary>
        ///     Counts the objects a hierarchy of the given depth would contain, stopping early once the
        ///     answer can no longer change the caller's decision.
        /// </summary>
        private static int CountObjects(IReadOnlyList<GameObject> roots, int rootLimit, int remainingDepth,
            int stopAfter)
        {
            var count = 0;
            var outputCount = Math.Min(roots.Count, rootLimit);
            for (var index = 0; index < outputCount; index++)
            {
                count += CountObjects(roots[index], remainingDepth, stopAfter - count);
                if (count >= stopAfter)
                    return count;
            }

            return count;
        }

        /// <summary>
        ///     Counts one object and as much of its subtree as the depth allows.
        /// </summary>
        private static int CountObjects(GameObject gameObject, int remainingDepth, int stopAfter)
        {
            var count = 1;
            if (remainingDepth <= 0 || count >= stopAfter)
                return count;

            var children = CollectChildren(gameObject);
            var outputCount = Math.Min(children.Count, ChildLimit);
            for (var index = 0; index < outputCount; index++)
            {
                count += CountObjects(children[index], remainingDepth - 1, stopAfter - count);
                if (count >= stopAfter)
                    return count;
            }

            return count;
        }

        /// <summary>
        ///     Builds the node tree that is written, applying the child and depth limits.
        /// </summary>
        private static GameObjectNode BuildNode(GameObject gameObject, int remainingDepth)
        {
            var children = CollectChildren(gameObject);
            if (remainingDepth <= 0)
                return new GameObjectNode(gameObject.name, UnityObjectId.Get(gameObject),
                    gameObject.activeSelf, children.Count, null, children.Count > 0);

            var outputCount = Math.Min(children.Count, ChildLimit);
            var nodes = new List<GameObjectNode>(outputCount);
            for (var index = 0; index < outputCount; index++)
                nodes.Add(BuildNode(children[index], remainingDepth - 1));

            return new GameObjectNode(gameObject.name, UnityObjectId.Get(gameObject), gameObject.activeSelf,
                children.Count, nodes, false);
        }

        /// <summary>
        ///     Builds the YAML document of a hierarchy response.
        /// </summary>
        /// <param name="header">The document level facts.</param>
        /// <param name="roots">The nodes to write, already capped.</param>
        /// <returns>The YAML document.</returns>
        /// <remarks>
        ///     Why the depth stopped is stated once in the header, and every other omission is stated as
        ///     properties on the object it happened to, so nothing is ever explained twice.
        /// </remarks>
        internal static string BuildHierarchyYaml(HierarchyHeader header, IReadOnlyList<GameObjectNode> roots)
        {
            var builder = new StringBuilder();
            builder.Append("scene: ").Append(QuoteYamlString(header.Scene)).Append('\n');
            if (header.Path != null)
                builder.Append("path: ").Append(QuoteYamlString(header.Path)).Append('\n');
            builder.Append("mode: ").Append(header.IsPlaying ? "Play" : "Edit").Append('\n');
            builder.Append("maxDepth: ")
                .Append(header.MaxDepth.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (header.DepthLimit != null)
                builder.Append("depthLimit: ").Append(QuoteYamlString(header.DepthLimit)).Append('\n');
            if (header.RootCount >= 0)
                builder.Append("rootCount: ")
                    .Append(header.RootCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (header.RootsOmitted > 0)
                builder.Append("rootsOmitted: ")
                    .Append(header.RootsOmitted.ToString(CultureInfo.InvariantCulture)).Append('\n');

            if (roots.Count == 0)
            {
                builder.Append("gameObjects: []");
                return builder.ToString();
            }

            builder.Append("gameObjects:\n");
            foreach (var node in roots)
                AppendNode(builder, node, 1);
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        ///     Writes one node and, when they were not omitted, its children.
        /// </summary>
        private static void AppendNode(StringBuilder builder, GameObjectNode node, int indent)
        {
            builder.Append(' ', indent * 2);
            builder.Append("- ").Append(QuoteYamlString(node.Name));
            AppendProperty(builder, "id", QuoteYamlString(node.Id));
            if (!node.IsActive)
                AppendProperty(builder, "active", "false");

            // One property says everything: how many children are missing. Whether they are missing
            // because of the depth limit or the child limit needs no name, because a node stopped by
            // the depth limit lists no children at all while one stopped by the child limit lists
            // the first twenty, and the depth itself is stated once in the header.
            var omitted = node.DepthLimited
                ? node.TotalChildCount
                : node.TotalChildCount - node.Children.Count;
            if (omitted > 0)
                AppendProperty(builder, "omittedChildCount",
                    omitted.ToString(CultureInfo.InvariantCulture));

            if (node.Children.Count == 0)
            {
                builder.Append('\n');
                return;
            }

            builder.Append(":\n");
            foreach (var child in node.Children)
                AppendNode(builder, child, indent + 1);
        }

        /// <summary>
        ///     Appends an inline <c>[name=value]</c> property to the current line.
        /// </summary>
        private static void AppendProperty(StringBuilder builder, string name, string value)
        {
            builder.Append(" [").Append(name).Append('=').Append(value).Append(']');
        }

        #endregion

        #region Find building

        /// <summary>
        ///     Builds the YAML document of a find response.
        /// </summary>
        /// <param name="query">The name or path that was searched for.</param>
        /// <param name="queryKind">Whether the query was a <c>name</c> or a <c>path</c>.</param>
        /// <param name="match">The matching mode that was used.</param>
        /// <param name="entries">The matches to write, already capped.</param>
        /// <param name="totalCount">The real number of matches.</param>
        /// <returns>The YAML document.</returns>
        internal static string BuildFindYaml(string query, string queryKind, string match,
            IReadOnlyList<FindEntry> entries, int totalCount)
        {
            var builder = new StringBuilder();
            builder.Append("query: ").Append(QuoteYamlString(query)).Append('\n');
            builder.Append("queryKind: ").Append(QuoteYamlString(queryKind)).Append('\n');
            builder.Append("match: ").Append(QuoteYamlString(match)).Append('\n');
            builder.Append("count: ").Append(totalCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (totalCount > entries.Count)
                builder.Append("matchesOmitted: ")
                    .Append((totalCount - entries.Count).ToString(CultureInfo.InvariantCulture))
                    .Append('\n');

            if (entries.Count == 0)
            {
                builder.Append("gameObjects: []");
                return builder.ToString();
            }

            builder.Append("gameObjects:");
            foreach (var entry in entries)
            {
                builder.Append("\n  - name: ").Append(QuoteYamlString(entry.Name));
                builder.Append("\n    id: ").Append(QuoteYamlString(entry.Id));
                builder.Append("\n    scene: ").Append(QuoteYamlString(entry.Scene));
                builder.Append("\n    path: ").Append(QuoteYamlString(entry.Path));
                builder.Append("\n    active: ").Append(entry.IsActive ? "true" : "false");
                builder.Append("\n    activeInHierarchy: ")
                    .Append(entry.IsActiveInHierarchy ? "true" : "false");
                builder.Append("\n    componentCount: ")
                    .Append(entry.ComponentCount.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        #endregion

        #region Inspection building

        /// <summary>
        ///     Builds the YAML document describing one GameObject and its components.
        /// </summary>
        /// <param name="gameObject">The object to describe.</param>
        /// <param name="fullDetailComponents">Component type names to report every instance member of.</param>
        /// <returns>The YAML document.</returns>
        private static string BuildInspection(GameObject gameObject, HashSet<string> fullDetailComponents)
        {
            var builder = new StringBuilder();
            builder.Append("name: ").Append(QuoteYamlString(gameObject.name)).Append('\n');
            builder.Append("id: ").Append(QuoteYamlString(UnityObjectId.Get(gameObject))).Append('\n');
            builder.Append("scene: ").Append(QuoteYamlString(DescribeScene(gameObject.scene))).Append('\n');
            builder.Append("path: ").Append(QuoteYamlString(GetPath(gameObject))).Append('\n');
            builder.Append("mode: ").Append(EditorApplication.isPlaying ? "Play" : "Edit").Append('\n');
            builder.Append("active: ").Append(gameObject.activeSelf ? "true" : "false").Append('\n');
            builder.Append("activeInHierarchy: ")
                .Append(gameObject.activeInHierarchy ? "true" : "false").Append('\n');
            builder.Append("tag: ").Append(QuoteYamlString(gameObject.tag)).Append('\n');
            builder.Append("layer: ")
                .Append(gameObject.layer.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("layerName: ")
                .Append(QuoteYamlString(LayerMask.LayerToName(gameObject.layer))).Append('\n');
            builder.Append("isStatic: ").Append(gameObject.isStatic ? "true" : "false").Append('\n');

            var parent = gameObject.transform.parent;
            if (parent != null)
                builder.Append("parentId: ")
                    .Append(QuoteYamlString(UnityObjectId.Get(parent.gameObject))).Append('\n');
            builder.Append("childCount: ")
                .Append(gameObject.transform.childCount.ToString(CultureInfo.InvariantCulture)).Append('\n');

            AppendPrefab(builder, gameObject);
            AppendComponents(builder, gameObject, fullDetailComponents);
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        ///     Writes the prefab section, which is omitted for an object that is not a prefab instance.
        /// </summary>
        private static void AppendPrefab(StringBuilder builder, GameObject gameObject)
        {
            var status = PrefabUtility.GetPrefabInstanceStatus(gameObject);
            if (status == PrefabInstanceStatus.NotAPrefab)
                return;

            builder.Append("prefab:\n");
            AppendYamlValue(builder, 1, "status", status.ToString());
            AppendYamlValue(builder, 1, "assetPath",
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject));
        }

        /// <summary>
        ///     Writes every component of an object, the transform included, since it is one too.
        /// </summary>
        private static void AppendComponents(StringBuilder builder, GameObject gameObject,
            HashSet<string> fullDetailComponents)
        {
            var components = gameObject.GetComponents<Component>();
            if (components.Length == 0)
            {
                builder.Append("components: []\n");
                return;
            }

            builder.Append("components:\n");
            foreach (var component in components)
            {
                // A component whose script is missing survives as a null entry, and saying so is more
                // useful than silently listing one component fewer than the inspector shows.
                if (component == null)
                {
                    builder.Append("  - type: \"<missing script>\"\n");
                    continue;
                }

                var type = component.GetType();
                var full = fullDetailComponents.Contains(type.Name) ||
                           type.FullName != null && fullDetailComponents.Contains(type.FullName);
                var common = full ? null : GetCommonMemberNames(type);

                builder.Append("  - type: ").Append(QuoteYamlString(type.Name)).Append('\n');
                AppendYamlValue(builder, 2, "id", UnityObjectId.Get(component));
                if (component is Behaviour behaviour)
                    AppendYamlValue(builder, 2, "enabled", behaviour.enabled);
                if (full)
                    AppendYamlValue(builder, 2, "detail", "full");
                else if (common != null)
                    AppendYamlValue(builder, 2, "detail", "common");

                AppendComponentMembers(builder, component, type, full, common);
            }
        }

        /// <summary>
        ///     Gets the curated member names of a built-in component type, merged along its base chain.
        /// </summary>
        /// <param name="type">The component's concrete type.</param>
        /// <returns>The member names to report, or null when the type has no curated list.</returns>
        private static HashSet<string> GetCommonMemberNames(Type type)
        {
            HashSet<string> names = null;
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.FullName == null ||
                    !CommonComponentMembers.TryGetValue(current.FullName, out var entry))
                    continue;

                names ??= new HashSet<string>(StringComparer.Ordinal);
                foreach (var name in entry)
                    names.Add(name);
            }

            return names;
        }

        /// <summary>
        ///     Writes the instance fields and properties of one component.
        /// </summary>
        /// <param name="builder">The document being built.</param>
        /// <param name="component">The component to reflect over.</param>
        /// <param name="type">The component's concrete type.</param>
        /// <param name="full">Whether non-public members are included too.</param>
        /// <param name="common">The curated member names to keep, or null to keep all of them.</param>
        private static void AppendComponentMembers(StringBuilder builder, Component component, Type type,
            bool full, HashSet<string> common)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public;
            if (full)
                flags |= BindingFlags.NonPublic;

            var members = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);

            foreach (var field in type.GetFields(flags))
            {
                if (!IsExportable(field))
                    continue;
                Consider(field);
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (property.GetMethod == null || property.GetMethod.IsStatic ||
                    property.GetIndexParameters().Length > 0)
                    continue;
                if (!IsExportable(property) || IsSideEffectProperty(property))
                    continue;
                Consider(property);
            }

            if (members.Count == 0)
            {
                builder.Append("    members: {}\n");
                return;
            }

            builder.Append("    members:\n");
            foreach (var name in members.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                builder.Append("      ").Append(name).Append(": ");
                builder.Append(ReadMemberValue(members[name], component)).Append('\n');
            }

            // A derived type can hide a base member of the same name; the one the caller would get from
            // the concrete type is the one worth reporting.
            void Consider(MemberInfo member)
            {
                if (common != null && !common.Contains(member.Name))
                    return;
                if (!members.TryGetValue(member.Name, out var existing) ||
                    GetInheritanceDepth(member.DeclaringType) > GetInheritanceDepth(existing.DeclaringType))
                    members[member.Name] = member;
            }
        }

        /// <summary>
        ///     Determines whether a member says something about this component in particular.
        /// </summary>
        /// <remarks>
        ///     Members declared by the component base types are the same on every component and are already
        ///     covered by the surrounding response, and reading an obsolete member can throw or log.
        ///     Compiler generated members, such as the backing field of an auto property, only duplicate
        ///     the member they belong to under an unreadable name.
        /// </remarks>
        private static bool IsExportable(MemberInfo member)
        {
            if (member.DeclaringType != null && BoilerplateDeclaringTypes.Contains(member.DeclaringType))
                return false;
            if (member.IsDefined(typeof(CompilerGeneratedAttribute), true))
                return false;
            return !member.IsDefined(typeof(ObsoleteAttribute), true);
        }

        /// <summary>
        ///     Determines whether reading a property would change the project.
        /// </summary>
        private static bool IsSideEffectProperty(PropertyInfo property)
        {
            return SideEffectPropertyNames.Contains(property.Name) &&
                   property.DeclaringType?.Namespace != null &&
                   property.DeclaringType.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal);
        }

        /// <summary>
        ///     Reads a member, turning any failure into a value rather than losing the whole response.
        /// </summary>
        private static string ReadMemberValue(MemberInfo member, object target)
        {
            try
            {
                var value = member is FieldInfo field
                    ? field.GetValue(target)
                    : ((PropertyInfo)member).GetValue(target);
                return FormatValue(value);
            }
            catch (Exception exception)
            {
                var cause = exception is TargetInvocationException { InnerException: not null }
                    ? exception.InnerException
                    : exception;

                // Unity throws rather than returning null for a reference field that was never assigned,
                // and for one whose target has been destroyed. Both describe the value, not a failure.
                return cause switch
                {
                    UnassignedReferenceException => "null",
                    MissingReferenceException => QuoteYamlString("<missing reference>"),
                    _ => QuoteYamlString($"<error: {cause.GetType().Name}: {cause.Message}>")
                };
            }
        }

        /// <summary>
        ///     Counts how far a type is from the root of the type hierarchy.
        /// </summary>
        private static int GetInheritanceDepth(Type type)
        {
            var depth = 0;
            for (; type != null; type = type.BaseType)
                depth++;
            return depth;
        }

        #endregion

        #region Replies

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

        #endregion
    }
}
