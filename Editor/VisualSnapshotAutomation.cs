using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
    ///     Handles the loopback-only message that reports what a camera actually draws on screen: the
    ///     2D and 3D GameObjects that are visible, with their screen space bounds.
    /// </summary>
    /// <remarks>
    ///     Stateless, and called from the editor main thread by <see cref="CodeEditorIntegrationCore" />.
    ///     This is the scene counterpart of <see cref="MessageType.UiSnapshot" />, which only ever sees
    ///     UI Toolkit. uGUI is covered by neither, and needs no filtering here either, because uGUI draws
    ///     through <c>CanvasRenderer</c>, which is not a <see cref="Renderer" /> and therefore never turns
    ///     up in the search below.
    ///     <para>
    ///         The answer is a hierarchy rather than a flat list: every visible object is reported together
    ///         with its ancestors up to the scene root, so a client learns both what is on screen and where
    ///         it sits in the scene. Ancestors carry no bounds, which is what distinguishes a node that is
    ///         merely structure from one that can actually be seen.
    ///     </para>
    ///     <para>
    ///         All coordinates are screen pixels with the origin in the top left corner and y growing
    ///         downwards, the convention used by UI Toolkit's <c>worldBound</c> and by the pixels of a
    ///         <see cref="MessageType.GameViewScreenshot" /> capture, so the three can be compared directly.
    ///         Depth is expressed in the same unit through a single factor per camera, see
    ///         <see cref="ComputePixelsPerUnit" />.
    ///     </para>
    /// </remarks>
    internal static class VisualSnapshotAutomation
    {
        /// <summary>
        ///     The most visible objects a single response may report. Ancestors do not count against it,
        ///     because they are structure the client did not ask for and must never crowd out content.
        /// </summary>
        private const int VisibleObjectLimit = 200;

        /// <summary>
        ///     The sorting layer every renderer starts out on, whose name is not worth reporting.
        /// </summary>
        private const string DefaultSortingLayer = "Default";

        /// <summary>
        ///     The start corner of each of the twelve edges of a box, indexed the same way as
        ///     <see cref="BoxEdgeEnds" />. Corner indices are bit masks: bit 0 is x, bit 1 is y, bit 2 is z,
        ///     so two corners share an edge exactly when their indices differ in a single bit.
        /// </summary>
        private static readonly int[] BoxEdgeStarts = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };

        /// <summary>
        ///     The end corner of each of the twelve edges of a box.
        /// </summary>
        private static readonly int[] BoxEdgeEnds = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };

        /// <summary>
        ///     Processes a visual snapshot message and answers the requesting client.
        /// </summary>
        /// <param name="message">The incoming message.</param>
        /// <param name="answer">The callback used to send the response.</param>
        internal static void Process(Message message, Action<IPEndPoint, MessageType, string> answer)
        {
            if (!AutomationProtocol.IsLoopback(message.Origin))
            {
                Reply(answer, message, AutomationProtocol.Error(
                    AutomationProtocol.TryReadRequestId(message.Value), "forbidden",
                    "Visual snapshot requests are only accepted from loopback clients."));
                return;
            }

            if (!AutomationProtocol.TryParseRequest(message.Value, out var request, out var requestId,
                    out var parseError))
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "invalid_request", parseError));
                return;
            }

            // Outside Play Mode there is no Game View to be a screen, and Screen.width reports the size of
            // whichever editor window happens to be focused, which would make every coordinate a fiction.
            if (!EditorApplication.isPlaying)
            {
                Reply(answer, message, AutomationProtocol.Error(requestId, "not_playing",
                    "A visual snapshot is only available while the Editor is in Play Mode."));
                return;
            }

            try
            {
                ProcessSnapshot(answer, message, request, requestId);
            }
            catch (Exception exception)
            {
                Reply(answer, message,
                    AutomationProtocol.Error(requestId, "internal_error", exception.Message));
            }
        }

        #region Message handler

        /// <summary>
        ///     Answers with the hierarchy of everything one camera can currently see.
        /// </summary>
        private static void ProcessSnapshot(Action<IPEndPoint, MessageType, string> answer, Message message,
            JObject request, JToken requestId)
        {
            if (!TryReadRendererScope(request, out var coreOnly, out var scopeError))
            {
                ReplyError(answer, message, requestId, "invalid_request", scopeError);
                return;
            }

            if (!TryResolveCamera(request.Value<string>("camera"), out var camera, out var errorCode,
                    out var cameraError))
            {
                ReplyError(answer, message, requestId, errorCode, cameraError);
                return;
            }

            var visible = CollectVisibleObjects(camera, coreOnly, out var totalCount);
            var roots = BuildTree(visible);
            var multipleScenes = roots.Select(root => root.Scene).Distinct(StringComparer.Ordinal).Count() > 1;

            var header = new VisualSnapshotHeader(Screen.width, Screen.height, camera.name,
                UnityObjectId.Get(camera), camera.orthographic, camera.depth, ComputePixelsPerUnit(camera),
                totalCount, totalCount - visible.Count, multipleScenes);
            Reply(answer, message,
                AutomationProtocol.Success(requestId, "visualSnapshot", BuildSnapshotYaml(header, roots)));
        }

        /// <summary>
        ///     Reads which renderers the client wants to hear about.
        /// </summary>
        /// <param name="request">The parsed request.</param>
        /// <param name="coreOnly">True to report only the renderers that carry meaning, the default.</param>
        /// <param name="error">A human readable reason when the value is not one of the two known ones.</param>
        /// <returns>True when the request is valid.</returns>
        private static bool TryReadRendererScope(JObject request, out bool coreOnly, out string error)
        {
            coreOnly = true;
            error = null;
            var requested = request.Value<string>("renderers");
            if (string.IsNullOrEmpty(requested) || string.Equals(requested, "core", StringComparison.Ordinal))
                return true;

            if (string.Equals(requested, "all", StringComparison.Ordinal))
            {
                coreOnly = false;
                return true;
            }

            error = $"renderers must be \"core\" or \"all\", not '{requested}'.";
            return false;
        }

        #endregion

        #region Camera

        /// <summary>
        ///     Picks the camera whose view the snapshot describes.
        /// </summary>
        /// <param name="requested">The camera name the client asked for, or null for the default one.</param>
        /// <param name="camera">The resolved camera, null when none matches.</param>
        /// <param name="errorCode">The machine readable reason when no camera matches.</param>
        /// <param name="error">A human readable reason when no camera matches.</param>
        /// <returns>True when a camera was resolved.</returns>
        /// <remarks>
        ///     Only cameras that draw to the screen can be described in screen coordinates, so one rendering
        ///     into a <see cref="RenderTexture" /> is never a candidate, not even when asked for by name.
        /// </remarks>
        private static bool TryResolveCamera(string requested, out Camera camera, out string errorCode,
            out string error)
        {
            camera = null;
            errorCode = null;
            error = null;

            // Camera.allCameras already excludes disabled cameras.
            var candidates = Camera.allCameras
                .Where(candidate => candidate != null && candidate.targetTexture == null)
                .ToList();
            if (candidates.Count == 0)
            {
                errorCode = "no_camera";
                error = "No enabled camera renders to the screen.";
                return false;
            }

            if (!string.IsNullOrEmpty(requested))
            {
                camera = candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.name, requested, StringComparison.Ordinal));
                if (camera != null)
                    return true;

                errorCode = "not_found";
                error = $"No enabled camera named '{requested}' renders to the screen. Cameras are " +
                        string.Join(", ", candidates.Select(candidate => $"'{candidate.name}'")) + ".";
                return false;
            }

            // The tagged main camera is the one a project considers its point of view. Falling back to the
            // lowest depth picks the camera that draws first, which is the scene rather than an overlay.
            var main = Camera.main;
            camera = main != null && candidates.Contains(main)
                ? main
                : candidates.OrderBy(candidate => candidate.depth).First();
            return true;
        }

        /// <summary>
        ///     Computes the single factor that turns a world distance from the camera into the same pixel
        ///     unit the x and y coordinates use.
        /// </summary>
        /// <param name="camera">The camera being described.</param>
        /// <returns>The number of pixels one world unit is worth.</returns>
        /// <remarks>
        ///     For an orthographic camera this is exact: it is the very factor that scales x and y, so the
        ///     reported box has the proportions of the real one. A perspective camera has no such factor,
        ///     because its scale falls off with distance, so the focal length is used instead. That keeps
        ///     depths comparable between objects and keeps the ordering meaningful, at the price of
        ///     stretching a far away box along z.
        /// </remarks>
        private static float ComputePixelsPerUnit(Camera camera)
        {
            var pixelHeight = camera.pixelHeight;
            if (camera.orthographic)
                return camera.orthographicSize > 0f ? pixelHeight / (2f * camera.orthographicSize) : 0f;

            var tangent = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return tangent > 0f ? pixelHeight / (2f * tangent) : 0f;
        }

        #endregion

        #region Collecting

        /// <summary>
        ///     Finds every GameObject the camera can currently see, most prominent first.
        /// </summary>
        /// <param name="camera">The camera being described.</param>
        /// <param name="coreOnly">True to skip the renderers that are pure decoration.</param>
        /// <param name="totalCount">The real number of visible objects, before the limit is applied.</param>
        /// <returns>At most <see cref="VisibleObjectLimit" /> objects.</returns>
        private static List<VisualNode> CollectVisibleObjects(Camera camera, bool coreOnly, out int totalCount)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var worldToCamera = camera.worldToCameraMatrix;
            var projection = camera.projectionMatrix;
            var pixelRect = camera.pixelRect;
            var nearClip = camera.nearClipPlane;
            var cullingMask = camera.cullingMask;
            var pixelsPerUnit = ComputePixelsPerUnit(camera);
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);

            var owners = new List<GameObject>();
            var groups = new Dictionary<GameObject, List<Renderer>>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (!IsCandidate(renderer, coreOnly, cullingMask))
                    continue;

                var owner = renderer.gameObject;
                if (!groups.TryGetValue(owner, out var group))
                {
                    group = new List<Renderer>(1);
                    groups.Add(owner, group);
                    owners.Add(owner);
                }

                group.Add(renderer);
            }

            var visible = new List<VisualNode>();
            foreach (var owner in owners)
            {
                var group = groups[owner];
                var bounds = group[0].bounds;
                for (var index = 1; index < group.Count; index++)
                    bounds.Encapsulate(group[index].bounds);

                // A renderer without anything to draw, such as a MeshRenderer whose filter has no mesh,
                // reports an empty box at its own position. It is not on screen, it is not anywhere.
                if (bounds.size == Vector3.zero)
                    continue;

                if (!GeometryUtility.TestPlanesAABB(planes, bounds))
                    continue;

                if (!TryProjectBounds(bounds, worldToCamera, projection, pixelRect, screen.height, nearClip,
                        out var rect, out var eyeDepthNear, out var eyeDepthFar))
                    continue;

                // Being inside the frustum is not the same as being on screen: a camera with a partial
                // viewport rect draws into a corner of it, and the frustum test knows nothing about that.
                var onScreen = Intersect(rect, screen);
                if (onScreen.width <= 0f || onScreen.height <= 0f)
                    continue;

                visible.Add(CreateVisibleNode(owner, group, rect, screen, eyeDepthNear, eyeDepthFar,
                    pixelsPerUnit));
            }

            totalCount = visible.Count;
            visible.Sort(CompareProminence);
            if (visible.Count > VisibleObjectLimit)
                visible.RemoveRange(VisibleObjectLimit, visible.Count - VisibleObjectLimit);
            return visible;
        }

        /// <summary>
        ///     Determines whether a renderer could contribute to what the camera draws.
        /// </summary>
        private static bool IsCandidate(Renderer renderer, bool coreOnly, int cullingMask)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff)
                return false;

            if (coreOnly && !IsCoreRenderer(renderer))
                return false;

            var owner = renderer.gameObject;
            if ((owner.hideFlags & HideFlags.HideInHierarchy) != 0)
                return false;

            if ((cullingMask & (1 << owner.layer)) == 0)
                return false;

            // uGUI cannot reach this far, because it draws through CanvasRenderer. A real Renderer parented
            // under a Canvas still can, and belongs to the UI rather than to the scene.
            return renderer.GetComponentInParent<Canvas>() == null;
        }

        /// <summary>
        ///     Determines whether a renderer draws something a client would call an object.
        /// </summary>
        /// <param name="renderer">The renderer to classify.</param>
        /// <returns>True for the three renderers that draw content rather than decoration.</returns>
        /// <remarks>
        ///     Particles, trails, lines, billboards and visual effects are motion, not objects: their bounds
        ///     change every frame and say nothing about what the game is showing. Tilemaps and sprite shapes
        ///     are a single box covering a whole level, which is just as uninformative. Reporting them would
        ///     spend the response on decoration, so they are left out unless explicitly asked for.
        /// </remarks>
        private static bool IsCoreRenderer(Renderer renderer)
        {
            return renderer is MeshRenderer or SkinnedMeshRenderer or SpriteRenderer;
        }

        /// <summary>
        ///     Describes one visible GameObject.
        /// </summary>
        private static VisualNode CreateVisibleNode(GameObject owner, IReadOnlyList<Renderer> group, Rect rect,
            Rect screen, float eyeDepthNear, float eyeDepthFar, float pixelsPerUnit)
        {
            var renderer = group[0];
            var transform = owner.transform;
            var node = new VisualNode(owner.name, UnityObjectId.Get(owner))
            {
                Owner = transform,
                SiblingIndex = transform.GetSiblingIndex(),
                Scene = DescribeScene(owner.scene),
                IsVisible = true,
                Rect = ToPixelRect(rect),
                ZNear = Mathf.RoundToInt(pixelsPerUnit * eyeDepthNear),
                ZFar = Mathf.RoundToInt(pixelsPerUnit * eyeDepthFar),
                RendererType = renderer.GetType().Name,
                RendererCount = group.Count,
                SortingLayerName = renderer.sortingLayerName,
                SortingLayerValue = SortingLayer.GetLayerValueFromName(renderer.sortingLayerName),
                SortingOrder = renderer.sortingOrder,
                Clipped = rect.xMin < screen.xMin || rect.yMin < screen.yMin ||
                          rect.xMax > screen.xMax || rect.yMax > screen.yMax
            };

            // A sprite is always sorted, everything else only when it was set up to be. Reporting the
            // untouched default on every mesh would say nothing and cost a property per line.
            node.HasSorting = renderer is SpriteRenderer || renderer.sortingOrder != 0 ||
                              !string.Equals(renderer.sortingLayerName, DefaultSortingLayer,
                                  StringComparison.Ordinal);
            return node;
        }

        /// <summary>
        ///     Orders visible objects by how much they matter to what the player sees, so the limit drops
        ///     the least interesting ones.
        /// </summary>
        /// <remarks>
        ///     Depth decides first, but in a 2D game every sprite sits at very nearly the same depth, which
        ///     is exactly when the sorting layer and order take over: there they, not z, are what decides
        ///     which object hides which.
        /// </remarks>
        private static int CompareProminence(VisualNode first, VisualNode second)
        {
            var byDepth = first.ZNear.CompareTo(second.ZNear);
            if (byDepth != 0)
                return byDepth;

            var byLayer = second.SortingLayerValue.CompareTo(first.SortingLayerValue);
            if (byLayer != 0)
                return byLayer;

            var byOrder = second.SortingOrder.CompareTo(first.SortingOrder);
            if (byOrder != 0)
                return byOrder;

            var byName = string.CompareOrdinal(first.Name, second.Name);
            return byName != 0 ? byName : first.Id.CompareTo(second.Id);
        }

        #endregion

        #region Projection

        /// <summary>
        ///     Projects a world space box onto the screen, as the rectangle it covers and the depth range
        ///     it spans.
        /// </summary>
        /// <param name="bounds">The world space box to project.</param>
        /// <param name="worldToCamera">The camera's world to camera matrix.</param>
        /// <param name="projection">The camera's projection matrix, the CPU side one.</param>
        /// <param name="pixelRect">The camera's viewport in screen pixels.</param>
        /// <param name="screenHeight">The height of the screen, used to flip y.</param>
        /// <param name="nearClip">The camera's near clip distance.</param>
        /// <param name="rect">The covered rectangle, top left origin, not clipped to the screen.</param>
        /// <param name="eyeDepthNear">The distance of the nearest visible part of the box.</param>
        /// <param name="eyeDepthFar">The distance of the furthest visible part of the box.</param>
        /// <returns>False when no part of the box is in front of the near plane.</returns>
        /// <remarks>
        ///     This takes matrices rather than a <see cref="Camera" /> so it can be tested without a Game
        ///     View, whose size <see cref="Camera.pixelRect" /> would otherwise depend on.
        ///     <para>
        ///         Camera space in Unity is right handed and looks down -Z, so a point's distance in front of
        ///         the camera is <c>-z</c>. The box is clipped against the near plane before anything is
        ///         projected, because the perspective divide mirrors points that lie behind the camera and
        ///         would otherwise place them on screen.
        ///     </para>
        ///     <para>
        ///         The projection matrix must be the one from <see cref="Camera.projectionMatrix" />, whose
        ///         clip space follows the OpenGL convention, and never the result of
        ///         <c>GL.GetGPUProjectionMatrix</c>, which bakes in per platform depth reversal and
        ///         y flipping.
        ///     </para>
        /// </remarks>
        internal static bool TryProjectBounds(Bounds bounds, Matrix4x4 worldToCamera, Matrix4x4 projection,
            Rect pixelRect, float screenHeight, float nearClip, out Rect rect, out float eyeDepthNear,
            out float eyeDepthFar)
        {
            rect = default;
            eyeDepthNear = 0f;
            eyeDepthFar = 0f;

            var center = bounds.center;
            var extents = bounds.extents;
            var corners = new Vector3[8];
            for (var index = 0; index < corners.Length; index++)
                corners[index] = worldToCamera.MultiplyPoint3x4(new Vector3(
                    center.x + ((index & 1) == 0 ? -extents.x : extents.x),
                    center.y + ((index & 2) == 0 ? -extents.y : extents.y),
                    center.z + ((index & 4) == 0 ? -extents.z : extents.z)));

            var points = new List<Vector3>(24);
            for (var edge = 0; edge < BoxEdgeStarts.Length; edge++)
            {
                var start = corners[BoxEdgeStarts[edge]];
                var end = corners[BoxEdgeEnds[edge]];
                var startInFront = -start.z >= nearClip;
                var endInFront = -end.z >= nearClip;
                if (!startInFront && !endInFront)
                    continue;

                if (startInFront)
                    points.Add(start);
                if (endInFront)
                    points.Add(end);
                if (startInFront == endInFront)
                    continue;

                // Where the edge crosses the near plane, z becomes -nearClip by construction.
                var inside = startInFront ? start : end;
                var outside = startInFront ? end : start;
                var travel = (inside.z + nearClip) / (inside.z - outside.z);
                points.Add(Vector3.LerpUnclamped(inside, outside, travel));
            }

            if (points.Count == 0)
                return false;

            var xMin = float.MaxValue;
            var yMin = float.MaxValue;
            var xMax = float.MinValue;
            var yMax = float.MinValue;
            var depthMin = float.MaxValue;
            var depthMax = float.MinValue;
            var projected = false;

            foreach (var point in points)
            {
                var clip = projection * new Vector4(point.x, point.y, point.z, 1f);
                if (clip.w == 0f)
                    continue;

                var x = pixelRect.x + (clip.x / clip.w * 0.5f + 0.5f) * pixelRect.width;
                var y = screenHeight - (pixelRect.y + (clip.y / clip.w * 0.5f + 0.5f) * pixelRect.height);
                var depth = -point.z;

                xMin = Mathf.Min(xMin, x);
                yMin = Mathf.Min(yMin, y);
                xMax = Mathf.Max(xMax, x);
                yMax = Mathf.Max(yMax, y);
                depthMin = Mathf.Min(depthMin, depth);
                depthMax = Mathf.Max(depthMax, depth);
                projected = true;
            }

            if (!projected)
                return false;

            rect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            eyeDepthNear = depthMin;
            eyeDepthFar = depthMax;
            return true;
        }

        /// <summary>
        ///     Rounds a rectangle to whole pixels.
        /// </summary>
        /// <remarks>
        ///     Both edges are rounded before the size is taken from them, because rounding the position and
        ///     the size on their own would let the right and bottom edges drift by a pixel. A box smaller
        ///     than a pixel is still reported as one pixel, so it never reads as having no size at all.
        /// </remarks>
        private static RectInt ToPixelRect(Rect rect)
        {
            var xMin = Mathf.RoundToInt(rect.xMin);
            var yMin = Mathf.RoundToInt(rect.yMin);
            var xMax = Mathf.RoundToInt(rect.xMax);
            var yMax = Mathf.RoundToInt(rect.yMax);
            return new RectInt(xMin, yMin, Mathf.Max(1, xMax - xMin), Mathf.Max(1, yMax - yMin));
        }

        /// <summary>
        ///     Intersects two rectangles, returning an empty one when they do not overlap.
        /// </summary>
        private static Rect Intersect(Rect first, Rect second)
        {
            var xMin = Mathf.Max(first.xMin, second.xMin);
            var yMin = Mathf.Max(first.yMin, second.yMin);
            var xMax = Mathf.Min(first.xMax, second.xMax);
            var yMax = Mathf.Min(first.yMax, second.yMax);
            return new Rect(xMin, yMin, Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
        }

        #endregion

        #region Tree building

        /// <summary>
        ///     Turns a flat set of visible objects into the hierarchy they live in, adding the ancestors
        ///     needed to connect them to their scene roots.
        /// </summary>
        /// <param name="visible">The visible objects, already capped.</param>
        /// <returns>The roots of the resulting tree, in hierarchy order.</returns>
        private static List<VisualNode> BuildTree(IReadOnlyList<VisualNode> visible)
        {
            var byId = new Dictionary<string, VisualNode>(StringComparer.Ordinal);
            foreach (var node in visible)
                byId[node.Id] = node;

            var roots = new List<VisualNode>();
            foreach (var node in visible)
                Attach(node, byId, roots);

            SortTree(roots);
            return roots;
        }

        /// <summary>
        ///     Links a node to its parent, creating the structural ancestors it needs on the way up.
        /// </summary>
        /// <remarks>
        ///     Walking stops at the first node that is already linked, because everything above it was
        ///     linked in the same pass.
        /// </remarks>
        private static void Attach(VisualNode node, Dictionary<string, VisualNode> byId,
            List<VisualNode> roots)
        {
            for (var current = node; !current.Attached;)
            {
                current.Attached = true;

                var parentTransform = current.Owner != null ? current.Owner.parent : null;
                if (parentTransform == null)
                {
                    roots.Add(current);
                    return;
                }

                var parentId = UnityObjectId.Get(parentTransform.gameObject);
                if (!byId.TryGetValue(parentId, out var parent))
                {
                    parent = new VisualNode(parentTransform.name, parentId)
                    {
                        Owner = parentTransform,
                        SiblingIndex = parentTransform.GetSiblingIndex(),
                        Scene = DescribeScene(parentTransform.gameObject.scene)
                    };
                    byId.Add(parentId, parent);
                }

                parent.Children.Add(current);
                current = parent;
            }
        }

        /// <summary>
        ///     Sorts a level of the tree and everything below it into hierarchy order.
        /// </summary>
        private static void SortTree(List<VisualNode> nodes)
        {
            nodes.Sort(CompareHierarchyOrder);
            foreach (var node in nodes)
                if (node.Children.Count > 0)
                    SortTree(node.Children);
        }

        /// <summary>
        ///     Orders siblings the way the Hierarchy window does, so a response can be compared to what a
        ///     user sees. Comparing the scene first only ever matters for roots, since siblings share one.
        /// </summary>
        private static int CompareHierarchyOrder(VisualNode first, VisualNode second)
        {
            var byScene = string.CompareOrdinal(first.Scene ?? string.Empty, second.Scene ?? string.Empty);
            if (byScene != 0)
                return byScene;

            var byIndex = first.SiblingIndex.CompareTo(second.SiblingIndex);
            if (byIndex != 0)
                return byIndex;

            var byName = string.CompareOrdinal(first.Name, second.Name);
            return byName != 0 ? byName : first.Id.CompareTo(second.Id);
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

        #endregion

        #region Yaml building

        /// <summary>
        ///     Builds the YAML document of a visual snapshot response.
        /// </summary>
        /// <param name="header">The document level facts.</param>
        /// <param name="roots">The roots of the tree, already capped and sorted.</param>
        /// <returns>The YAML document.</returns>
        /// <remarks>
        ///     A node carries bounds exactly when it can be seen, so the presence of <c>rect</c> is what
        ///     tells a client apart the objects that are on screen from the ancestors that merely hold them.
        /// </remarks>
        internal static string BuildSnapshotYaml(VisualSnapshotHeader header, IReadOnlyList<VisualNode> roots)
        {
            var builder = new StringBuilder();
            builder.Append("screen: [").Append(Integer(header.ScreenWidth)).Append(',')
                .Append(Integer(header.ScreenHeight)).Append("]\n");

            builder.Append("camera: ").Append(QuoteYamlString(header.CameraName));
            AppendProperty(builder, "id", QuoteYamlString(header.CameraId));
            AppendProperty(builder, "projection", header.IsOrthographic ? "Orthographic" : "Perspective");
            AppendProperty(builder, "depth", header.CameraDepth.ToString("0.##", CultureInfo.InvariantCulture));
            AppendProperty(builder, "pxPerUnit",
                header.PixelsPerUnit.ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append('\n');

            builder.Append("count: ").Append(Integer(header.TotalCount)).Append('\n');
            if (header.Omitted > 0)
                builder.Append("omitted: ").Append(Integer(header.Omitted)).Append('\n');

            if (roots.Count == 0)
            {
                builder.Append("gameObjects: []");
                return builder.ToString();
            }

            builder.Append("gameObjects:\n");
            foreach (var node in roots)
                AppendNode(builder, node, 1, header.MultipleScenes);
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        ///     Writes one node and its children.
        /// </summary>
        private static void AppendNode(StringBuilder builder, VisualNode node, int indent, bool writeScene)
        {
            builder.Append(' ', indent * 2);
            builder.Append("- ").Append(QuoteYamlString(node.Name));
            AppendProperty(builder, "id", QuoteYamlString(node.Id));

            // Which scene a root belongs to only needs saying when more than one is open, and never on a
            // child, which cannot be in a different scene than its parent.
            if (writeScene && indent == 1)
                AppendProperty(builder, "scene", QuoteYamlString(node.Scene));

            if (node.IsVisible)
            {
                AppendProperty(builder, "rect", FormatPixelRect(node.Rect));
                AppendProperty(builder, "z",
                    string.Concat("[", Integer(node.ZNear), ",", Integer(node.ZFar), "]"));
                AppendProperty(builder, "renderer", node.RendererType);
                if (node.RendererCount > 1)
                    AppendProperty(builder, "rendererCount", Integer(node.RendererCount));
                if (node.HasSorting)
                {
                    AppendProperty(builder, "sortingLayer", QuoteYamlString(node.SortingLayerName));
                    AppendProperty(builder, "sortingOrder", Integer(node.SortingOrder));
                }

                if (node.Clipped)
                    AppendProperty(builder, "clipped", "true");
            }

            if (node.Children.Count == 0)
            {
                builder.Append('\n');
                return;
            }

            builder.Append(":\n");
            foreach (var child in node.Children)
                AppendNode(builder, child, indent + 1, writeScene);
        }

        /// <summary>
        ///     Formats a pixel rectangle as an inline YAML sequence of x, y, width and height.
        /// </summary>
        private static string FormatPixelRect(RectInt value)
        {
            return string.Concat("[", Integer(value.x), ",", Integer(value.y), ",", Integer(value.width), ",",
                Integer(value.height), "]");
        }

        /// <summary>
        ///     Appends an inline <c>[name=value]</c> property to the current line.
        /// </summary>
        private static void AppendProperty(StringBuilder builder, string name, string value)
        {
            builder.Append(" [").Append(name).Append('=').Append(value).Append(']');
        }

        /// <summary>
        ///     Formats an integer in the invariant culture.
        /// </summary>
        private static string Integer(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        #endregion

        #region Model

        /// <summary>
        ///     One GameObject in a visual snapshot, either one that can be seen or an ancestor that only
        ///     holds ones that can.
        /// </summary>
        internal sealed class VisualNode
        {
            internal VisualNode(string name, string id)
            {
                Name = name;
                Id = id;
            }

            internal string Name { get; }

            /// <summary>
            ///     The opaque id, usable to address this object in a later request.
            /// </summary>
            internal string Id { get; }

            /// <summary>
            ///     The object's transform, used to walk to its parent. Null in a hand built tree.
            /// </summary>
            internal Transform Owner { get; set; }

            /// <summary>
            ///     The object's position among its siblings, which is the order the Hierarchy window uses.
            /// </summary>
            internal int SiblingIndex { get; set; }

            internal string Scene { get; set; }

            /// <summary>
            ///     Whether this node was actually drawn, as opposed to being an ancestor of one that was.
            ///     Only a visible node carries bounds.
            /// </summary>
            internal bool IsVisible { get; set; }

            /// <summary>
            ///     The screen rectangle the object covers, top left origin, not clipped to the screen.
            /// </summary>
            internal RectInt Rect { get; set; }

            /// <summary>
            ///     The distance of the nearest part of the object, in the same pixel unit as the rectangle.
            /// </summary>
            internal int ZNear { get; set; }

            /// <summary>
            ///     The distance of the furthest part of the object, in the same pixel unit as the rectangle.
            /// </summary>
            internal int ZFar { get; set; }

            internal string RendererType { get; set; }

            /// <summary>
            ///     How many renderers the object itself has, which is more than one only rarely.
            /// </summary>
            internal int RendererCount { get; set; }

            internal string SortingLayerName { get; set; }

            /// <summary>
            ///     The sorting layer's position in the project's layer order, not its id.
            /// </summary>
            internal int SortingLayerValue { get; set; }

            internal int SortingOrder { get; set; }

            /// <summary>
            ///     Whether the sorting layer and order are worth reporting for this object.
            /// </summary>
            internal bool HasSorting { get; set; }

            /// <summary>
            ///     Whether part of the object lies outside the screen.
            /// </summary>
            internal bool Clipped { get; set; }

            /// <summary>
            ///     Whether this node has already been linked to its parent, so a second visit can stop.
            /// </summary>
            internal bool Attached { get; set; }

            internal List<VisualNode> Children { get; } = new();
        }

        /// <summary>
        ///     The facts about a visual snapshot as a whole, which every coordinate in it is relative to.
        /// </summary>
        internal readonly struct VisualSnapshotHeader
        {
            internal VisualSnapshotHeader(int screenWidth, int screenHeight, string cameraName,
                string cameraId,
                bool isOrthographic, float cameraDepth, float pixelsPerUnit, int totalCount, int omitted,
                bool multipleScenes)
            {
                ScreenWidth = screenWidth;
                ScreenHeight = screenHeight;
                CameraName = cameraName;
                CameraId = cameraId;
                IsOrthographic = isOrthographic;
                CameraDepth = cameraDepth;
                PixelsPerUnit = pixelsPerUnit;
                TotalCount = totalCount;
                Omitted = omitted;
                MultipleScenes = multipleScenes;
            }

            internal int ScreenWidth { get; }

            internal int ScreenHeight { get; }

            internal string CameraName { get; }

            internal string CameraId { get; }

            internal bool IsOrthographic { get; }

            internal float CameraDepth { get; }

            /// <summary>
            ///     The factor that turns a world distance into the pixel unit the z values use.
            /// </summary>
            internal float PixelsPerUnit { get; }

            /// <summary>
            ///     The real number of visible objects, which can exceed the number reported.
            /// </summary>
            internal int TotalCount { get; }

            /// <summary>
            ///     How many visible objects the limit left out.
            /// </summary>
            internal int Omitted { get; }

            /// <summary>
            ///     Whether the roots span more than one scene, in which case each says which it is in.
            /// </summary>
            internal bool MultipleScenes { get; }
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
