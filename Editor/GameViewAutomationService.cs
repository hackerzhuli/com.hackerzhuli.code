using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;
using static Hackerzhuli.Code.Editor.AutomationValueFormatter;
using static Hackerzhuli.Code.Editor.AutomationValueParser;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    /// Handles loopback-only automation of the runtime UI Toolkit hierarchy and Game View.
    /// All methods are called from the editor main thread by <see cref="CodeEditorIntegrationCore"/>.
    /// </summary>
    internal sealed class GameViewAutomationService : IDisposable
    {
        private const double ScreenshotTimeoutSeconds = 10.0;
        private const int HierarchyElementLimit = 200;
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        // These CreateProperty members are declared by VisualElement or one of its base types.
        // They are either
        // represented more clearly in the debugger-style sections below, duplicate other
        // geometry, expose a large implementation object, or carry arbitrary user state.
        // Properties declared by derived controls and custom elements are never filtered by
        // this list, even if they happen to use one of the same names.
        private static readonly HashSet<string> VisualElementInspectionPropertyBlacklist =
            new(StringComparer.Ordinal)
            {
                "contentRect", "dataSource", "dataSourcePath", "enabledInHierarchy", "enabledSelf",
                "focusable", "layout", "localBound", "name", "panel", "pickingMode",
                "resolvedStyle", "style", "styleSheets", "tabIndex", "tooltip", "usageHints",
                "userData", "viewDataKey", "visible", "worldBound", "worldTransform",
                "disablePlayModeTint"
            };

        // Maps a USS property name to the inline style property that overrides it, so a matched rule
        // can be reported as losing to an inline style.
        private static readonly Dictionary<string, PropertyInfo> InlineStyleProperties =
            BuildInlineStyleProperties();

        private readonly Action<IPEndPoint, MessageType, string> _answer;
        private readonly Dictionary<VisualElement, string> _elementRefs = new();
        private readonly Dictionary<string, VisualElement> _refElements = new(StringComparer.Ordinal);
        private readonly Queue<ScreenshotRequest> _screenshotQueue = new();
        private int _nextRef = 1;
        private ScreenshotRequest _activeScreenshot;

        internal GameViewAutomationService(Action<IPEndPoint, MessageType, string> answer)
        {
            _answer = answer ?? throw new ArgumentNullException(nameof(answer));
        }

        internal void Process(Message message)
        {
            if (!AutomationProtocol.IsLoopback(message.Origin))
            {
                ReplyError(message, AutomationProtocol.TryReadRequestId(message.Value), "forbidden",
                    "Game View automation requests are only accepted from loopback clients.");
                return;
            }

            if (!AutomationProtocol.TryParseRequest(message.Value, out var request, out var requestId,
                    out var parseError))
            {
                ReplyError(message, requestId, "invalid_request", parseError);
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                ReplyError(message, requestId, "not_playing",
                    "Game View automation is only available while the Editor is in Play Mode.");
                return;
            }

            try
            {
                switch (message.Type)
                {
                    case MessageType.UiSnapshot:
                        var snapshot = BuildRuntimeSnapshot();
                        if (snapshot == null)
                            ReplyError(message, requestId, "panel_missing",
                                "No active UIDocument is attached to a runtime panel.");
                        else
                            ReplySuccess(message, requestId, "snapshot", snapshot);
                        break;
                    case MessageType.UiClick:
                        ProcessPointerAction(message, request, requestId, true);
                        break;
                    case MessageType.UiHover:
                        ProcessPointerAction(message, request, requestId, false);
                        break;
                    case MessageType.GameViewScreenshot:
                        if (request["path"] != null || request["fileName"] != null || request["filename"] != null)
                        {
                            ReplyError(message, requestId, "invalid_request",
                                "Screenshot paths and file names are generated by Unity and cannot be supplied.");
                            return;
                        }

                        _screenshotQueue.Enqueue(new ScreenshotRequest(message.Origin, message.Type, requestId));
                        break;
                    case MessageType.UiHierarchy:
                        ProcessHierarchy(message, request, requestId);
                        break;
                    case MessageType.UiInspect:
                        ProcessInspect(message, request, requestId);
                        break;
                    case MessageType.UiSetValue:
                        ProcessSetValue(message, request, requestId);
                        break;
                }
            }
            catch (Exception exception)
            {
                ReplyError(message, requestId, "internal_error", exception.Message);
            }
        }

        internal void Update()
        {
            if (_activeScreenshot == null)
            {
                if (_screenshotQueue.Count == 0)
                    return;

                if (!EditorApplication.isPlaying)
                {
                    FailQueuedScreenshots("not_playing",
                        "Play Mode ended before the Game View screenshot could be captured.");
                    return;
                }

                StartNextScreenshot();
            }

            if (_activeScreenshot == null)
                return;

            if (!EditorApplication.isPlaying)
            {
                CompleteActiveScreenshotError("not_playing",
                    "Play Mode ended before the Game View screenshot was written.");
                FailQueuedScreenshots("not_playing",
                    "Play Mode ended before the Game View screenshot could be captured.");
                return;
            }

            if (EditorApplication.timeSinceStartup - _activeScreenshot.StartTime > ScreenshotTimeoutSeconds)
            {
                CompleteActiveScreenshotError("capture_timeout",
                    "Timed out waiting for Unity to finish writing the Game View screenshot.");
                return;
            }

            try
            {
                if (!File.Exists(_activeScreenshot.Path))
                    return;

                var length = new FileInfo(_activeScreenshot.Path).Length;
                if (length <= 0)
                    return;

                if (length == _activeScreenshot.LastLength)
                    _activeScreenshot.StableLengthChecks++;
                else
                {
                    _activeScreenshot.LastLength = length;
                    _activeScreenshot.StableLengthChecks = 1;
                }

                if (_activeScreenshot.StableLengthChecks < 2 || !IsCompletePng(_activeScreenshot.Path))
                    return;

                var completed = _activeScreenshot;
                _activeScreenshot = null;
                _answer(completed.EndPoint, completed.MessageType,
                    AutomationProtocol.Success(completed.RequestId, "path", Path.GetFullPath(completed.Path)));
            }
            catch (UnauthorizedAccessException exception)
            {
                CompleteActiveScreenshotError("write_failed", exception.Message);
            }
            catch (IOException exception)
            {
                // CaptureScreenshot may still have the file open. Keep polling until timeout.
                FileLogger.Log($"Waiting for screenshot file: {exception.Message}");
            }
        }

        internal void OnExitingPlayMode()
        {
            CompleteActiveScreenshotError("not_playing",
                "Play Mode ended before the Game View screenshot was written.");
            FailQueuedScreenshots("not_playing",
                "Play Mode ended before the Game View screenshot could be captured.");
        }

        public void Dispose()
        {
            CompleteActiveScreenshotError("internal_error", "Game View automation was stopped.");
            FailQueuedScreenshots("internal_error", "Game View automation was stopped.");
            _elementRefs.Clear();
            _refElements.Clear();
        }

        /// <summary>
        ///     Moves the synthetic pointer onto an element, and optionally presses and releases a
        ///     mouse button on it.
        /// </summary>
        /// <param name="message">The incoming message.</param>
        /// <param name="request">The parsed request.</param>
        /// <param name="requestId">The request id to echo back.</param>
        /// <param name="click">True to press and release, false to only move.</param>
        /// <remarks>
        ///     Every element and every mouse button takes this one path. Activating a
        ///     <see cref="Button" /> through its <c>NavigationSubmitEvent</c> instead would be a
        ///     second, differently behaving implementation of the same request, and it could not
        ///     express a button at all.
        /// </remarks>
        private void ProcessPointerAction(Message message, JObject request, JToken requestId, bool click)
        {
            var reference = request.Value<string>("ref");
            if (string.IsNullOrEmpty(reference))
            {
                ReplyError(message, requestId, "invalid_request", "A non-empty ref is required.");
                return;
            }

            var mouseButton = 0;
            if (click && !TryReadMouseButton(request, out mouseButton, out var buttonError))
            {
                ReplyError(message, requestId, "invalid_request", buttonError);
                return;
            }

            if (!_refElements.TryGetValue(reference, out var element))
            {
                ReplyError(message, requestId, "unknown_ref", $"Unknown UI element ref '{reference}'.");
                return;
            }

            if (element.panel == null)
            {
                ReplyError(message, requestId, "stale_ref",
                    "The referenced element is no longer attached to a runtime panel.");
                return;
            }

            if (!IsVisible(element))
            {
                ReplyError(message, requestId, "not_visible", "The referenced element is not visible.");
                return;
            }

            if (!element.enabledInHierarchy)
            {
                ReplyError(message, requestId, "disabled", "The referenced element is disabled.");
                return;
            }

            if (!TryFindHittablePoint(element, out var point))
            {
                ReplyError(message, requestId, "not_hittable",
                    "No visible point on the referenced element can receive pointer events.");
                return;
            }

            // A move never carries a button, Unity forces it to -1.
            SendMouseEvent(element.panel, EventType.MouseMove, point, 0);
            if (click)
                try
                {
                    SendMouseEvent(element.panel, EventType.MouseDown, point, mouseButton);
                }
                finally
                {
                    // PointerDownEvent presses the button in the process wide PointerDeviceState.
                    // Without the release a throw in between would leave it pressed for every later
                    // event, synthetic or real.
                    SendMouseEvent(element.panel, EventType.MouseUp, point, mouseButton);
                }

            ReplySuccess(message, requestId);
        }

        /// <summary>
        ///     Reads the optional mouse button of a click request.
        /// </summary>
        /// <param name="request">The parsed request.</param>
        /// <param name="mouseButton">The button index, 0 when the property is absent.</param>
        /// <param name="error">A human readable reason when the property is not a valid button.</param>
        /// <returns>True when the request carries no button or a valid one.</returns>
        /// <remarks>
        ///     The indices are Unity's own, as used by <see cref="Event.button" />: 0 is the left
        ///     button, 1 the right one and 2 the middle one.
        /// </remarks>
        private static bool TryReadMouseButton(JObject request, out int mouseButton, out string error)
        {
            mouseButton = 0;
            error = null;
            var token = request["button"];
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type != JTokenType.Integer)
            {
                error = "button must be an integer: 0 for left, 1 for right, 2 for middle.";
                return false;
            }

            mouseButton = token.Value<int>();
            if (mouseButton is >= 0 and <= 2)
                return true;

            error = $"button {mouseButton} is not supported, use 0 for left, 1 for right, 2 for middle.";
            return false;
        }

        private void ProcessHierarchy(Message message, JObject request, JToken requestId)
        {
            var reference = request.Value<string>("ref");
            if (string.IsNullOrEmpty(reference))
            {
                ReplyError(message, requestId, "invalid_request", "A non-empty ref is required.");
                return;
            }

            if (!_refElements.TryGetValue(reference, out var element))
            {
                ReplyError(message, requestId, "unknown_ref", $"Unknown UI element ref '{reference}'.");
                return;
            }

            if (element.panel == null)
            {
                ReplyError(message, requestId, "stale_ref",
                    "The referenced element is no longer attached to a runtime panel.");
                return;
            }

            if (IsHierarchyRoot(element))
            {
                ReplyError(message, requestId, "forbidden",
                    "UiHierarchy cannot be requested for a UIDocument or Panel root element. Select one of its descendants.");
                return;
            }

            int? requestedDepth = null;
            var depthToken = request["depth"];
            if (depthToken != null)
            {
                if (depthToken.Type != JTokenType.Integer ||
                    !long.TryParse(depthToken.ToString(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var parsedDepth) ||
                    parsedDepth < 0 || parsedDepth > int.MaxValue)
                {
                    ReplyError(message, requestId, "invalid_request",
                        "depth must be a non-negative integer.");
                    return;
                }

                requestedDepth = (int)parsedDepth;
            }

            ReplySuccess(message, requestId, "hierarchy",
                BuildHierarchy(element, requestedDepth));
        }

        private static bool IsHierarchyRoot(VisualElement element)
        {
            if (element.panel != null && element.panel.visualTree == element)
                return true;

            return UnityEngine.Object.FindObjectsByType<UIDocument>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Any(document => document != null && document.rootVisualElement == element);
        }

        private void ProcessInspect(Message message, JObject request, JToken requestId)
        {
            var reference = request.Value<string>("ref");
            if (string.IsNullOrEmpty(reference))
            {
                ReplyError(message, requestId, "invalid_request", "A non-empty ref is required.");
                return;
            }

            if (!_refElements.TryGetValue(reference, out var element))
            {
                ReplyError(message, requestId, "unknown_ref", $"Unknown UI element ref '{reference}'.");
                return;
            }

            if (element.panel == null)
            {
                ReplyError(message, requestId, "stale_ref",
                    "The referenced element is no longer attached to a runtime panel.");
                return;
            }

            ReplySuccess(message, requestId, "inspection", BuildInspection(element));
        }

        private void ProcessSetValue(Message message, JObject request, JToken requestId)
        {
            var reference = request.Value<string>("ref");
            if (string.IsNullOrEmpty(reference))
            {
                ReplyError(message, requestId, "invalid_request", "A non-empty ref is required.");
                return;
            }

            if (!_refElements.TryGetValue(reference, out var element))
            {
                ReplyError(message, requestId, "unknown_ref", $"Unknown UI element ref '{reference}'.");
                return;
            }

            if (element.panel == null)
            {
                ReplyError(message, requestId, "stale_ref",
                    "The referenced element is no longer attached to a runtime panel.");
                return;
            }

            if (!element.enabledInHierarchy)
            {
                ReplyError(message, requestId, "disabled", "The referenced element is disabled.");
                return;
            }

            if (!TryGetBaseFieldValueProperty(element, out var valueType, out var valueProperty))
            {
                ReplyError(message, requestId, "invalid_request",
                    $"Element type '{element.GetType().Name}' is not a BaseField<T>.");
                return;
            }

            var readOnlyProperty = element.GetType().GetProperty("isReadOnly",
                BindingFlags.Instance | BindingFlags.Public);
            if (readOnlyProperty?.PropertyType == typeof(bool) &&
                (bool)readOnlyProperty.GetValue(element))
            {
                ReplyError(message, requestId, "read_only",
                    "The referenced field is read-only.");
                return;
            }

            if (!request.TryGetValue("value", out var valueToken))
            {
                ReplyError(message, requestId, "invalid_request", "A value is required.");
                return;
            }

            var input = valueToken.Type == JTokenType.Null
                ? null
                : valueToken.Type == JTokenType.String
                    ? valueToken.Value<string>()
                    : valueToken.ToString(Formatting.None);
            object currentValue;
            try
            {
                currentValue = valueProperty.GetValue(element);
            }
            catch (Exception exception)
            {
                ReplyError(message, requestId, "internal_error",
                    UnwrapReflectionException(exception).Message);
                return;
            }

            if (!TryConvertValue(valueType, input, currentValue, out var converted,
                    out var errorCode, out var conversionError))
            {
                ReplyError(message, requestId, errorCode, conversionError);
                return;
            }

            if (Equals(currentValue, converted))
            {
                ReplySuccess(message, requestId);
                return;
            }

            try
            {
                valueProperty.SetValue(element, converted);
            }
            catch (Exception exception)
            {
                var cause = UnwrapReflectionException(exception);
                ReplyError(message, requestId, "invalid_value",
                    $"Could not set {element.GetType().Name}.value: {cause.Message}");
                return;
            }

            // BaseField<T>.value already marks its visual content dirty. An inactive Game View
            // may still defer presenting that frame, and forcing another element repaint does not
            // address the view's presentation timing.
            // element.MarkDirtyRepaint();
            ReplySuccess(message, requestId);
        }

        private static bool TryGetBaseFieldValueProperty(VisualElement element, out Type valueType,
            out PropertyInfo valueProperty)
        {
            for (var type = element.GetType(); type != null; type = type.BaseType)
            {
                if (!type.IsGenericType ||
                    type.GetGenericTypeDefinition() != typeof(BaseField<>))
                    continue;

                valueType = type.GetGenericArguments()[0];
                valueProperty = element.GetType().GetProperty("value",
                    BindingFlags.Instance | BindingFlags.Public);
                return valueProperty?.SetMethod != null;
            }

            valueType = null;
            valueProperty = null;
            return false;
        }

        private string BuildRuntimeSnapshot()
        {
            var documents = UnityEngine.Object.FindObjectsByType<UIDocument>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(document => document != null && document.isActiveAndEnabled &&
                                   document.rootVisualElement != null &&
                                   document.rootVisualElement.panel != null)
                .OrderBy(document => document.panelSettings != null
                    ? document.panelSettings.sortingOrder
                    : 0f)
                .ThenBy(document => document.panelSettings != null
                    ? document.panelSettings.GetInstanceID()
                    : 0)
                .ThenBy(document => document.sortingOrder)
                .ThenBy(document => document.GetInstanceID())
                .ToArray();

            var liveElements = new HashSet<VisualElement>();
            if (documents.Length == 0)
            {
                RemoveInvalidRefs(liveElements);
                return null;
            }

            var documentRoots = new HashSet<VisualElement>(
                documents.Select(document => document.rootVisualElement));

            var builder = new StringBuilder();
            foreach (var panelDocuments in documents.GroupBy(document => document.rootVisualElement.panel))
            {
                foreach (var document in panelDocuments)
                {
                    builder.Append("- UIDocument");
                    if (!string.IsNullOrEmpty(document.name))
                        AppendStringProperty(builder, "name", document.name);
                    builder.Append(":\n");
                    AppendElement(builder, document.rootVisualElement, 1, liveElements, false, int.MaxValue);
                }

                AppendPanelOverlays(builder, panelDocuments.Key, documentRoots, liveElements);
            }

            RemoveInvalidRefs(liveElements);
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// An open dropdown attaches its menu directly to the panel root instead of a
        /// <see cref="UIDocument"/> root, so walking documents alone would hide the whole menu
        /// hierarchy from the snapshot.
        /// </summary>
        private void AppendPanelOverlays(StringBuilder builder, IPanel panel,
            HashSet<VisualElement> documentRoots, HashSet<VisualElement> liveElements)
        {
            var visualTree = panel?.visualTree;
            if (visualTree == null)
                return;

            foreach (var child in visualTree.Children())
            {
                if (documentRoots.Contains(child))
                    continue;

                builder.Append("- PanelOverlay");
                if (!string.IsNullOrEmpty(visualTree.name))
                    AppendStringProperty(builder, "panel", visualTree.name);
                builder.Append(":\n");
                AppendElement(builder, child, 1, liveElements, false, int.MaxValue);
            }
        }

        /// <summary>
        /// Builds the same compact tree without requiring a live panel. This keeps serialization
        /// rules independently testable in EditMode; production snapshots always use UIDocuments.
        /// </summary>
        internal string BuildSnapshotForTests(VisualElement root)
        {
            var liveElements = new HashSet<VisualElement>();
            var builder = new StringBuilder("- UIDocument:\n");
            AppendElement(builder, root, 1, liveElements, false, int.MaxValue);

            var invalid = _elementRefs.Keys.Where(element => !liveElements.Contains(element)).ToArray();
            foreach (var element in invalid)
            {
                var reference = _elementRefs[element];
                _elementRefs.Remove(element);
                _refElements.Remove(reference);
            }

            return builder.ToString().TrimEnd();
        }

        internal string BuildHierarchyForTests(VisualElement root, int? requestedDepth = null)
        {
            return BuildHierarchy(root, requestedDepth);
        }

        private string BuildHierarchy(VisualElement root, int? requestedDepth)
        {
            var elements = new HashSet<VisualElement>();
            var builder = new StringBuilder();
            var dynamicMaximumDepth = DetermineHierarchyDepth(root);
            var maximumDepth = requestedDepth.HasValue
                ? Math.Min(requestedDepth.Value, dynamicMaximumDepth)
                : dynamicMaximumDepth;

            // The depth limit is explained once here rather than on every element it truncates,
            // because repeating the same sentence per element only costs the reader context.
            if (maximumDepth < GetMaximumOutputDepth(root))
            {
                builder.Append("# maxDepth ");
                builder.Append(maximumDepth.ToString(CultureInfo.InvariantCulture));
                builder.Append(requestedDepth.HasValue && requestedDepth.Value < dynamicMaximumDepth
                    ? " (requested)"
                    : $" (dynamic, {HierarchyElementLimit} element budget)");
                builder.Append("; elements cut off by it carry omittedChildCount without a reason\n");
            }

            AppendElement(builder, root, 0, elements, true, maximumDepth);
            return builder.ToString().TrimEnd();
        }

        internal string BuildInspectionForTests(VisualElement element)
        {
            return BuildInspection(element);
        }

        private string BuildInspection(VisualElement element)
        {
            var builder = new StringBuilder();
            builder.Append("type: ");
            builder.Append(element.GetType().Name);
            builder.Append("\nref: ");
            builder.Append(GetOrCreateRef(element));
            AppendInspectionLayout(builder, element);
            AppendInspectionGeometry(builder, element);
            AppendInspectionElement(builder, element);
            AppendInspectionUss(builder, element);
            builder.Append("properties:\n");
            AppendInspectionProperties(builder, element);
            return builder.ToString().TrimEnd();
        }

        private static void AppendInspectionLayout(StringBuilder builder, VisualElement element)
        {
            var style = element.resolvedStyle;
            builder.Append("\nlayout:\n");
            AppendYamlBox(builder, "margin", style.marginTop, style.marginRight,
                style.marginBottom, style.marginLeft);
            AppendYamlBox(builder, "border", style.borderTopWidth, style.borderRightWidth,
                style.borderBottomWidth, style.borderLeftWidth);
            AppendYamlBox(builder, "padding", style.paddingTop, style.paddingRight,
                style.paddingBottom, style.paddingLeft);
            builder.Append("  contentSize: [");
            builder.Append(element.contentRect.width.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(element.contentRect.height.ToString("R", CultureInfo.InvariantCulture));
            builder.Append("]\n");
        }

        private static void AppendYamlBox(StringBuilder builder, string name,
            float top, float right, float bottom, float left)
        {
            builder.Append("  ");
            builder.Append(name);
            builder.Append(": {top: ");
            builder.Append(top.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(", right: ");
            builder.Append(right.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(", bottom: ");
            builder.Append(bottom.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(", left: ");
            builder.Append(left.ToString("R", CultureInfo.InvariantCulture));
            builder.Append("}\n");
        }

        private static void AppendInspectionGeometry(StringBuilder builder, VisualElement element)
        {
            builder.Append("geometry:\n");
            AppendYamlValue(builder, 1, "worldBound", element.worldBound);
            AppendYamlValue(builder, 1, "worldClip", GetInternalMemberValue(element, "worldClip"));
            AppendYamlValue(builder, 1, "boundingBox", GetInternalMemberValue(element, "boundingBox"));
            AppendYamlValue(builder, 1, "layout", element.layout);
            AppendYamlValue(builder, 1, "lastLayout", GetInternalMemberValue(element, "lastLayout"));
        }

        private static void AppendInspectionElement(StringBuilder builder, VisualElement element)
        {
            builder.Append("element:\n");
            AppendYamlValue(builder, 1, "name", element.name);
            AppendYamlValue(builder, 1, "debugId", GetInternalMemberValue(element, "controlid"));
            AppendYamlValue(builder, 1, "tooltip", element.tooltip);
            AppendYamlValue(builder, 1, "viewDataKey", element.viewDataKey);
            AppendYamlValue(builder, 1, "dataSourceType", element.dataSource?.GetType().FullName);
            AppendYamlValue(builder, 1, "dataSourcePath", element.dataSourcePath);
            AppendYamlValue(builder, 1, "pickingMode", element.pickingMode);
            AppendYamlValue(builder, 1, "pseudoStates", GetInternalMemberValue(element, "pseudoStates"));
            AppendYamlValue(builder, 1, "focusable", element.focusable);
            AppendYamlValue(builder, 1, "usageHints", element.usageHints);
            AppendYamlValue(builder, 1, "tabIndex", element.tabIndex);
            AppendYamlValue(builder, 1, "display", element.resolvedStyle.display);
            AppendYamlValue(builder, 1, "visibility", element.resolvedStyle.visibility);
            AppendYamlValue(builder, 1, "opacity", element.resolvedStyle.opacity);
            AppendYamlValue(builder, 1, "enabledSelf", element.enabledSelf);
            AppendYamlValue(builder, 1, "enabledInHierarchy", element.enabledInHierarchy);
            AppendYamlValue(builder, 1, "visible", element.visible);
        }

        private static void AppendInspectionUss(StringBuilder builder, VisualElement element)
        {
            builder.Append("uss:\n");
            builder.Append("  classes: ");
            builder.Append(FormatStringCollection(element.GetClasses()));
            builder.Append('\n');
            AppendStyleSheets(builder, element);
            AppendMatchedSelectors(builder, element);
            AppendStyleProperties(builder, "inlineStyles", element.style, typeof(IStyle), true);
            AppendStyleProperties(builder, "resolvedStyles", element.resolvedStyle,
                typeof(IResolvedStyle), false);
        }

        private static void AppendStyleSheets(StringBuilder builder, VisualElement element)
        {
            var entries = new List<string>();
            for (var current = element; current != null; current = current.parent)
            {
                var set = current.styleSheets;
                for (var index = 0; index < set.count; index++)
                {
                    var sheet = set[index];
                    if (sheet == null)
                        continue;
                    var entry = $"{sheet.name} (owner={current.GetType().Name}, name={current.name})";
                    if (!entries.Contains(entry))
                        entries.Add(entry);
                }
            }

            if (entries.Count == 0)
            {
                builder.Append("  styleSheets: []\n");
                return;
            }

            builder.Append("  styleSheets:\n");
            foreach (var entry in entries)
            {
                builder.Append("    - ");
                builder.Append(QuoteYamlString(entry));
                builder.Append('\n');
            }
        }

        /// <summary>
        ///     Writes the USS rules matching the element, in the order Unity applies them, and marks
        ///     which of their declarations actually survive the cascade.
        /// </summary>
        /// <remarks>
        ///     Declarations are compared by the property name as written, so a shorthand such as
        ///     <c>margin</c> and a longhand such as <c>margin-left</c> can both be applied, each winning
        ///     for the part of the box it writes.
        /// </remarks>
        private static void AppendMatchedSelectors(StringBuilder builder, VisualElement element)
        {
            if (!UssMatchedSelectors.TryGetMatchedRules(element, out var rules, out var error))
            {
                builder.Append("  matchedSelectors: ");
                builder.Append(QuoteYamlString($"<unavailable: {error}>"));
                builder.Append('\n');
                return;
            }

            if (rules.Count == 0)
            {
                builder.Append("  matchedSelectors: []\n");
                return;
            }

            var winners = FindWinningDeclarations(element, rules);
            builder.Append("  matchedSelectors:\n");
            for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                var rule = rules[ruleIndex];
                builder.Append("    - selector: ");
                builder.Append(QuoteYamlString(rule.Selector));
                builder.Append("\n      source: ");
                builder.Append(QuoteYamlString(rule.Source));
                builder.Append("\n      specificity: ");
                builder.Append(rule.Specificity.ToString(CultureInfo.InvariantCulture));
                builder.Append('\n');

                var applied = new List<UssMatchedSelectors.UssDeclaration>();
                var overridden = new List<KeyValuePair<UssMatchedSelectors.UssDeclaration, string>>();
                for (var index = 0; index < rule.Declarations.Count; index++)
                {
                    var declaration = rule.Declarations[index];
                    var winner = winners[declaration.Name];
                    if (winner.RuleIndex == ruleIndex && winner.DeclarationIndex == index &&
                        !winner.IsInline)
                        applied.Add(declaration);
                    else
                        overridden.Add(
                            new KeyValuePair<UssMatchedSelectors.UssDeclaration, string>(declaration,
                                winner.IsInline ? "inline" : rules[winner.RuleIndex].Source));
                }

                AppendDeclarations(builder, "appliedDeclarations", applied, null);
                if (overridden.Count > 0)
                    AppendDeclarations(builder, "overriddenDeclarations",
                        overridden.Select(entry => entry.Key).ToList(),
                        overridden.Select(entry => entry.Value).ToList());
            }
        }

        /// <summary>
        ///     Resolves who wins for every declared property name. The rules arrive from lowest to
        ///     highest priority, so the last declaration seen for a name wins, unless an inline style
        ///     overrides the whole cascade.
        /// </summary>
        private static Dictionary<string, DeclarationWinner> FindWinningDeclarations(
            VisualElement element, IReadOnlyList<UssMatchedSelectors.UssMatchedRule> rules)
        {
            var winners = new Dictionary<string, DeclarationWinner>(StringComparer.Ordinal);
            for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                var declarations = rules[ruleIndex].Declarations;
                for (var index = 0; index < declarations.Count; index++)
                    winners[declarations[index].Name] = new DeclarationWinner(ruleIndex, index, false);
            }

            foreach (var name in winners.Keys.ToList())
                if (IsInlineStyleSet(element, name))
                    winners[name] = new DeclarationWinner(-1, -1, true);

            return winners;
        }

        private static void AppendDeclarations(StringBuilder builder, string sectionName,
            IReadOnlyList<UssMatchedSelectors.UssDeclaration> declarations,
            IReadOnlyList<string> overriddenBy)
        {
            builder.Append("      ");
            builder.Append(sectionName);
            if (declarations.Count == 0)
            {
                builder.Append(": []\n");
                return;
            }

            builder.Append(":\n");
            for (var index = 0; index < declarations.Count; index++)
            {
                var declaration = declarations[index];
                builder.Append("        - {property: ");
                builder.Append(QuoteYamlString(declaration.Name));
                builder.Append(", value: ");
                builder.Append(QuoteYamlString(declaration.Value));
                if (declaration.Line > 0)
                {
                    builder.Append(", line: ");
                    builder.Append(declaration.Line.ToString(CultureInfo.InvariantCulture));
                }

                if (overriddenBy != null)
                {
                    builder.Append(", overriddenBy: ");
                    builder.Append(QuoteYamlString(overriddenBy[index]));
                }

                builder.Append("}\n");
            }
        }

        /// <summary>
        ///     Tells whether an inline style is set for a USS property name, which makes it beat every
        ///     matched rule. Shorthand names have no inline counterpart and are never reported as set.
        /// </summary>
        private static bool IsInlineStyleSet(VisualElement element, string ussPropertyName)
        {
            if (!InlineStyleProperties.TryGetValue(ussPropertyName, out var property))
                return false;

            var value = property.GetValue(element.style);
            var keyword = value?.GetType().GetProperty("keyword",
                BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
            // Inline access reports Null for a property that was never assigned. Every other keyword,
            // including the Undefined that a plain value carries, means an inline style is set and
            // therefore beats the matched rules.
            return keyword != null &&
                   !string.Equals(keyword.ToString(), "Null", StringComparison.Ordinal);
        }

        private static Dictionary<string, PropertyInfo> BuildInlineStyleProperties()
        {
            var properties = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            foreach (var property in typeof(IStyle).GetProperties(
                         BindingFlags.Instance | BindingFlags.Public))
                if (property.GetMethod != null)
                    properties[ToUssPropertyName(property.Name)] = property;

            return properties;
        }

        private readonly struct DeclarationWinner
        {
            internal DeclarationWinner(int ruleIndex, int declarationIndex, bool isInline)
            {
                RuleIndex = ruleIndex;
                DeclarationIndex = declarationIndex;
                IsInline = isInline;
            }

            internal int RuleIndex { get; }
            internal int DeclarationIndex { get; }
            internal bool IsInline { get; }
        }

        private static void AppendStyleProperties(StringBuilder builder, string sectionName,
            object source, Type interfaceType, bool skipUnsetInlineValues)
        {
            var values = new List<KeyValuePair<string, object>>();
            foreach (var property in interfaceType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(property => property.GetMethod != null)
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                try
                {
                    var value = property.GetValue(source);
                    if (skipUnsetInlineValues && IsUnsetInlineStyleValue(value))
                        continue;
                    values.Add(new KeyValuePair<string, object>(ToUssPropertyName(property.Name), value));
                }
                catch (Exception exception)
                {
                    var cause = exception is TargetInvocationException { InnerException: not null }
                        ? exception.InnerException
                        : exception;
                    values.Add(new KeyValuePair<string, object>(ToUssPropertyName(property.Name),
                        $"<error: {cause.GetType().Name}: {cause.Message}>"));
                }
            }

            if (!skipUnsetInlineValues)
            {
                var resolvedValues = values.ToDictionary(pair => pair.Key, pair => pair.Value,
                    StringComparer.Ordinal);
                values.RemoveAll(pair =>
                    ShouldOmitResolvedStyleProperty(pair.Key, pair.Value, resolvedValues));
            }

            if (values.Count == 0)
            {
                builder.Append("  ");
                builder.Append(sectionName);
                builder.Append(": {}\n");
                return;
            }

            builder.Append("  ");
            builder.Append(sectionName);
            builder.Append(":\n");
            foreach (var pair in values)
                AppendYamlValue(builder, 2, pair.Key, pair.Value);
        }

        private static bool ShouldOmitResolvedStyleProperty(string name, object value,
            IReadOnlyDictionary<string, object> values)
        {
            // The box model is already presented by the layout section.
            if (name.StartsWith("margin-", StringComparison.Ordinal) ||
                name.StartsWith("padding-", StringComparison.Ordinal) ||
                name.EndsWith("-width", StringComparison.Ordinal) &&
                name.StartsWith("border-", StringComparison.Ordinal))
                return true;

            // These values are already presented in the element section.
            if (name is "display" or "visibility" or "opacity")
                return true;

            var hasBackgroundImage = values.TryGetValue("background-image", out var backgroundImage) &&
                                     !IsAbsentBackgroundImage(backgroundImage);
            if (name == "background-image")
                return !hasBackgroundImage;
            if (!hasBackgroundImage &&
                (name.StartsWith("background-position-", StringComparison.Ordinal) ||
                 name is "background-repeat" or "background-size" or
                     "-unity-background-image-tint-color" or "-unity-background-scale-mode" or
                     "-unity-slice-bottom" or "-unity-slice-left" or "-unity-slice-right" or
                     "-unity-slice-scale" or "-unity-slice-top" or "-unity-slice-type"))
                return true;

            if (name == "background-color" && IsTransparentColor(value))
                return true;

            if (TryGetBorderSide(name, "-color", out var side) &&
                values.TryGetValue($"border-{side}-width", out var width) &&
                IsZeroNumber(width))
                return true;
            if (name.StartsWith("border-", StringComparison.Ordinal) &&
                name.EndsWith("-radius", StringComparison.Ordinal) && IsZeroNumber(value))
                return true;

            var isDefaultPosition = values.TryGetValue("position", out var position) &&
                                    IsNamedValue(position, "Relative");
            if (name is "left" or "right" or "top" or "bottom")
                return isDefaultPosition && IsZeroNumber(value);

            var hasTransition = values.TryGetValue("transition-duration", out var durations) &&
                                !IsZeroTimeCollection(durations);
            if (!hasTransition && name.StartsWith("transition-", StringComparison.Ordinal))
                return true;

            var hasTransform = HasEffectiveTransform(values);
            if (!hasTransform &&
                name is "rotate" or "scale" or "translate" or "transform-origin")
                return true;

            switch (name)
            {
                case "max-height":
                case "max-width":
                    return IsNamedValue(value, "None");
                case "min-height":
                case "min-width":
                    return IsNamedValue(value, "Auto");
                case "letter-spacing":
                case "word-spacing":
                case "-unity-paragraph-spacing":
                    return IsZeroNumber(value);
                case "-unity-font":
                case "-unity-font-definition":
                    return value == null || string.IsNullOrEmpty(value.ToString());
                case "-unity-text-outline-color":
                    return values.TryGetValue("-unity-text-outline-width", out var outlineWidth) &&
                           IsZeroNumber(outlineWidth);
                case "-unity-text-outline-width":
                    return IsZeroNumber(value);
                default:
                    return false;
            }
        }

        private static bool TryGetBorderSide(string propertyName, string suffix, out string side)
        {
            const string prefix = "border-";
            if (propertyName.StartsWith(prefix, StringComparison.Ordinal) &&
                propertyName.EndsWith(suffix, StringComparison.Ordinal))
            {
                side = propertyName.Substring(prefix.Length,
                    propertyName.Length - prefix.Length - suffix.Length);
                return side is "top" or "right" or "bottom" or "left";
            }

            side = null;
            return false;
        }

        private static bool IsAbsentBackgroundImage(object value)
        {
            return value == null || string.IsNullOrEmpty(value.ToString());
        }

        private static bool IsTransparentColor(object value)
        {
            return value is Color color && Mathf.Approximately(color.a, 0f);
        }

        private static bool IsZeroNumber(object value)
        {
            return IsNumber(value, 0f);
        }

        private static bool IsNumber(object value, float expected)
        {
            try
            {
                return value is byte or sbyte or short or ushort or int or uint or long or ulong or
                           float or double or decimal &&
                       Mathf.Approximately(Convert.ToSingle(value, CultureInfo.InvariantCulture),
                           expected);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsNamedValue(object value, string expected)
        {
            return string.Equals(value?.ToString(), expected, StringComparison.Ordinal);
        }

        private static bool IsZeroTimeCollection(object value)
        {
            if (value is not System.Collections.IEnumerable items)
                return false;

            var foundAny = false;
            foreach (var item in items)
            {
                foundAny = true;
                var text = item?.ToString();
                if (!string.Equals(text, "0s", StringComparison.Ordinal) &&
                    !string.Equals(text, "0ms", StringComparison.Ordinal))
                    return false;
            }

            return foundAny;
        }

        private static bool HasEffectiveTransform(IReadOnlyDictionary<string, object> values)
        {
            return !values.TryGetValue("rotate", out var rotate) ||
                   rotate?.ToString().StartsWith("0 ", StringComparison.Ordinal) != true ||
                   !values.TryGetValue("scale", out var scale) ||
                   !IsNamedValue(scale, "(1.00, 1.00, 1.00)") ||
                   !values.TryGetValue("translate", out var translate) ||
                   translate is not Vector3 translation ||
                   translation != Vector3.zero;
        }

        private static bool IsUnsetInlineStyleValue(object value)
        {
            if (value == null)
                return true;
            var keyword = value.GetType().GetProperty("keyword",
                BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
            return keyword != null &&
                   (string.Equals(keyword.ToString(), "Null", StringComparison.Ordinal) ||
                    string.Equals(keyword.ToString(), "Undefined", StringComparison.Ordinal));
        }

        private static string ToUssPropertyName(string propertyName)
        {
            var builder = new StringBuilder(propertyName.Length + 4);
            foreach (var character in propertyName)
            {
                if (char.IsUpper(character))
                    builder.Append('-').Append(char.ToLowerInvariant(character));
                else
                    builder.Append(character);
            }

            var result = builder.ToString();
            return result.StartsWith("unity-", StringComparison.Ordinal)
                ? string.Concat("-", result)
                : result;
        }

        private static object GetInternalMemberValue(object target, string memberName)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                try
                {
                    var property = type.GetProperty(memberName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                    if (property?.GetMethod != null)
                        return property.GetValue(target);

                    var field = type.GetField(memberName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                    if (field != null)
                        return field.GetValue(target);
                }
                catch (Exception exception)
                {
                    var cause = exception is TargetInvocationException { InnerException: not null }
                        ? exception.InnerException
                        : exception;
                    return $"<error: {cause.GetType().Name}: {cause.Message}>";
                }
            }

            return "<unavailable>";
        }

        private void AppendChildren(StringBuilder builder, VisualElement parent, int depth,
            HashSet<VisualElement> liveElements, bool detailed, int detailedMaximumDepth)
        {
            var children = GetTraversalChildren(parent, detailed);
            var truncateSimilarChildren = children.Count >= 20 &&
                                          children.All(child => child.GetType() == children[0].GetType());
            var outputCount = truncateSimilarChildren ? 10 : children.Count;
            for (var index = 0; index < outputCount; index++)
                AppendElement(builder, children[index], depth, liveElements, detailed,
                    detailedMaximumDepth);
        }

        private void AppendElement(StringBuilder builder, VisualElement element, int depth,
            HashSet<VisualElement> liveElements, bool detailed, int detailedMaximumDepth)
        {
            liveElements.Add(element);
            var children = GetTraversalChildren(element, detailed);
            var hiddenChildren = !detailed && children.Count > 0 && IsHiddenForCompactSnapshot(element);
            var builtInChildren = !detailed && children.Count > 0 && !hiddenChildren &&
                                  ShouldOmitBuiltInChildren(element);
            var depthLimitedChildren = detailed && children.Count > 0 &&
                                       depth >= detailedMaximumDepth;
            var similarChildren = !hiddenChildren && !builtInChildren && !depthLimitedChildren &&
                                  children.Count >= 20 &&
                                  children.All(child => child.GetType() == children[0].GetType());
            var omittedChildCount = hiddenChildren || builtInChildren || depthLimitedChildren
                ? children.Count
                : similarChildren
                    ? children.Count - 10
                    : 0;

            builder.Append(' ', depth * 2);
            builder.Append("- ");
            builder.Append(element.GetType().Name);
            if (detailed)
                AppendHierarchyProperties(builder, element);
            else
                AppendProperties(builder, element);
            if (detailed && omittedChildCount > 0)
            {
                AppendNumberProperty(builder, "omittedChildCount", omittedChildCount);

                // The depth limit needs no reason on the element: it is stated once in the header,
                // and there can be a great many elements at that depth. The other three reasons are
                // per element and nothing else would reveal them.
                if (!depthLimitedChildren)
                    AppendStringProperty(builder, "omissionReason",
                        hiddenChildren ? "hidden" :
                        builtInChildren ? "built_in_implementation" :
                        "similar_children");
            }
            builder.Append(" [ref=");
            builder.Append(GetOrCreateRef(element));
            builder.Append(']');

            // Omitted children are already described by the properties on this line, so no comment
            // repeats them below it.
            if (children.Count == 0 || hiddenChildren || builtInChildren || depthLimitedChildren)
            {
                builder.Append('\n');
            }
            else
            {
                builder.Append(":\n");
                AppendChildren(builder, element, depth + 1, liveElements, detailed,
                    detailedMaximumDepth);
            }
        }

        private static int DetermineHierarchyDepth(VisualElement root)
        {
            var maximumDepth = GetMaximumOutputDepth(root);
            while (maximumDepth > 1 &&
                   CountOutputElements(root, maximumDepth, HierarchyElementLimit + 1) >
                   HierarchyElementLimit)
                maximumDepth--;
            return maximumDepth;
        }

        private static int GetMaximumOutputDepth(VisualElement element)
        {
            var children = GetOutputChildren(element);
            if (children.Count == 0)
                return 0;
            return 1 + children.Max(GetMaximumOutputDepth);
        }

        private static int CountOutputElements(VisualElement element, int remainingDepth, int stopAfter)
        {
            var count = 1;
            if (remainingDepth <= 0)
                return count;

            foreach (var child in GetOutputChildren(element))
            {
                count += CountOutputElements(child, remainingDepth - 1, stopAfter - count);
                if (count >= stopAfter)
                    return count;
            }

            return count;
        }

        /// <summary>
        /// Compact snapshots follow <see cref="VisualElement.Children"/>, the contentContainer view,
        /// so composite controls stay readable. Detailed hierarchies must follow the real visual tree
        /// instead: otherwise a ScrollView jumps straight to its content items and hides the viewport,
        /// the content container and both scrollers.
        /// </summary>
        private static List<VisualElement> GetTraversalChildren(VisualElement parent, bool detailed)
        {
            return detailed
                ? parent.hierarchy.Children().ToList()
                : parent.Children().ToList();
        }

        private static List<VisualElement> GetOutputChildren(VisualElement parent)
        {
            var children = parent.hierarchy.Children().ToList();
            if (children.Count >= 20 &&
                children.All(child => child.GetType() == children[0].GetType()))
                return children.Take(10).ToList();
            return children;
        }

        private static bool ShouldOmitBuiltInChildren(VisualElement element)
        {
            // Foldout and ScrollView are semantic containers: their contentContainer holds
            // user-authored hierarchy that must remain in the snapshot.
            if (element is Foldout or ScrollView)
                return false;

            return element is Button || IsBaseField(element) || element is ProgressBar ||
                   element is BaseVerticalCollectionView || element is TextElement;
        }

        private static bool IsHiddenForCompactSnapshot(VisualElement element)
        {
            // Opacity is deliberately excluded: fully transparent UI is a distinct state.
            return element.resolvedStyle.display == DisplayStyle.None ||
                   element.resolvedStyle.visibility == Visibility.Hidden;
        }

        private static bool IsBaseField(VisualElement element)
        {
            for (var type = element.GetType(); type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BaseField<>))
                    return true;
            return false;
        }

        private static void AppendProperties(StringBuilder builder, VisualElement element)
        {
            if (!string.IsNullOrEmpty(element.name))
                AppendStringProperty(builder, "name", element.name);
            if (!string.IsNullOrEmpty(element.tooltip))
                AppendStringProperty(builder, "tooltip", element.tooltip);
            if (!element.enabledSelf)
                AppendRawProperty(builder, "enabledSelf", "false");
            if (element.resolvedStyle.display == DisplayStyle.None)
                AppendRawProperty(builder, "display", DisplayStyle.None.ToString());
            if (element.resolvedStyle.visibility == Visibility.Hidden)
                AppendRawProperty(builder, "visibility", Visibility.Hidden.ToString());

            if (element is not DropdownField && IsPopupField(element))
            {
                AppendReflectedPopupProperties(builder, element);
                return;
            }

            switch (element)
            {
                case Button button:
                    AppendStringProperty(builder, "text", button.text);
                    break;
                case RadioButton radioButton:
                    AppendStringProperty(builder, "label", radioButton.label);
                    AppendBoolProperty(builder, "value", radioButton.value);
                    break;
                case Toggle toggle:
                    AppendStringProperty(builder, "label", toggle.label);
                    AppendBoolProperty(builder, "value", toggle.value);
                    break;
                case TextField textField:
                    AppendStringProperty(builder, "label", textField.label);
                    AppendStringProperty(builder, "value", textField.value);
                    AppendBoolProperty(builder, "isReadOnly", textField.isReadOnly);
                    AppendBoolProperty(builder, "multiline", textField.multiline);
                    break;
                case IntegerField integerField:
                    AppendNumericField(builder, integerField.label, integerField.value, integerField.isReadOnly);
                    break;
                case LongField longField:
                    AppendNumericField(builder, longField.label, longField.value, longField.isReadOnly);
                    break;
                case FloatField floatField:
                    AppendNumericField(builder, floatField.label, floatField.value, floatField.isReadOnly);
                    break;
                case DoubleField doubleField:
                    AppendNumericField(builder, doubleField.label, doubleField.value, doubleField.isReadOnly);
                    break;
                case Slider slider:
                    AppendStringProperty(builder, "label", slider.label);
                    AppendNumberProperty(builder, "value", slider.value);
                    AppendNumberProperty(builder, "lowValue", slider.lowValue);
                    AppendNumberProperty(builder, "highValue", slider.highValue);
                    break;
                case SliderInt sliderInt:
                    AppendStringProperty(builder, "label", sliderInt.label);
                    AppendNumberProperty(builder, "value", sliderInt.value);
                    AppendNumberProperty(builder, "lowValue", sliderInt.lowValue);
                    AppendNumberProperty(builder, "highValue", sliderInt.highValue);
                    break;
                case MinMaxSlider minMaxSlider:
                    AppendStringProperty(builder, "label", minMaxSlider.label);
                    AppendRawProperty(builder, "value", FormatVector2(minMaxSlider.value));
                    AppendNumberProperty(builder, "lowLimit", minMaxSlider.lowLimit);
                    AppendNumberProperty(builder, "highLimit", minMaxSlider.highLimit);
                    break;
                case DropdownField dropdown:
                    AppendStringProperty(builder, "label", dropdown.label);
                    AppendStringProperty(builder, "value", dropdown.value);
                    AppendNumberProperty(builder, "index", dropdown.index);
                    break;
                case Foldout foldout:
                    AppendStringProperty(builder, "text", foldout.text);
                    AppendBoolProperty(builder, "value", foldout.value);
                    break;
                case ProgressBar progress:
                    AppendStringProperty(builder, "title", progress.title);
                    AppendNumberProperty(builder, "value", progress.value);
                    AppendNumberProperty(builder, "lowValue", progress.lowValue);
                    AppendNumberProperty(builder, "highValue", progress.highValue);
                    break;
                case BaseVerticalCollectionView collection:
                    AppendRawProperty(builder, "selectionType", collection.selectionType.ToString());
                    AppendNumberProperty(builder, "selectedIndex", collection.selectedIndex);
                    break;
                case ScrollView scroll:
                    AppendRawProperty(builder, "mode", scroll.mode.ToString());
                    AppendRawProperty(builder, "scrollOffset", FormatVector2(scroll.scrollOffset));
                    break;
                case TextElement text:
                    AppendStringProperty(builder, "text", text.text);
                    break;
                default:
                    if (element.focusable)
                        AppendBoolProperty(builder, "focusable", true);
                    break;
            }
        }

        private static void AppendHierarchyProperties(StringBuilder builder, VisualElement element)
        {
            if (!string.IsNullOrEmpty(element.name))
                AppendStringProperty(builder, "name", element.name);
            var classes = element.GetClasses().ToArray();
            if (classes.Length > 0)
                AppendRawProperty(builder, "ussClasses", FormatStringCollection(classes));
        }

        private static void AppendInspectionProperties(StringBuilder builder, VisualElement element)
        {
            var properties = element.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetMethod != null && !property.GetMethod.IsStatic)
                .Where(HasCreatePropertyAttribute)
                .Where(property => !IsBlacklistedVisualElementInspectionProperty(property))
                .GroupBy(property => property.Name, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(property => GetInheritanceDepth(property.DeclaringType))
                    .First())
                .OrderBy(property => property.Name, StringComparer.Ordinal);

            foreach (var property in properties)
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    AppendInspectionProperty(builder, property.Name, "<indexed property>");
                    continue;
                }

                try
                {
                    AppendInspectionProperty(builder, property.Name, property.GetValue(element));
                }
                catch (Exception exception)
                {
                    var cause = exception is TargetInvocationException { InnerException: not null }
                        ? exception.InnerException
                        : exception;
                    AppendInspectionProperty(builder, property.Name,
                        $"<error: {cause.GetType().Name}: {cause.Message}>");
                }
            }
        }

        private static bool IsBlacklistedVisualElementInspectionProperty(PropertyInfo property)
        {
            return property.DeclaringType != null &&
                   property.DeclaringType.IsAssignableFrom(typeof(VisualElement)) &&
                   VisualElementInspectionPropertyBlacklist.Contains(property.Name);
        }

        private static bool HasCreatePropertyAttribute(PropertyInfo property)
        {
            return property.GetCustomAttributes(true).Any(IsCreatePropertyAttribute) ||
                   property.GetMethod != null &&
                   property.GetMethod.GetCustomAttributes(true).Any(IsCreatePropertyAttribute);
        }

        private static bool IsCreatePropertyAttribute(object attribute)
        {
            return string.Equals(attribute.GetType().FullName,
                "Unity.Properties.CreatePropertyAttribute", StringComparison.Ordinal);
        }

        private static int GetInheritanceDepth(Type type)
        {
            var depth = 0;
            for (; type != null; type = type.BaseType)
                depth++;
            return depth;
        }

        private static void AppendInspectionProperty(StringBuilder builder, string name, object value)
        {
            builder.Append("  ");
            builder.Append(name);
            builder.Append(": ");
            builder.Append(FormatValue(value));
            builder.Append('\n');
        }

        private static bool IsPopupField(VisualElement element)
        {
            for (var type = element.GetType(); type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PopupField<>))
                    return true;
            return false;
        }

        private static void AppendReflectedPopupProperties(StringBuilder builder, VisualElement element)
        {
            // PopupField<T> has no non-generic interface. Reflect only its three protocol-whitelisted
            // properties so custom T and custom derived fields retain native values without dumping state.
            var type = element.GetType();
            AppendStringProperty(builder, "label", type.GetProperty("label")?.GetValue(element)?.ToString());
            AppendStringProperty(builder, "value", type.GetProperty("value")?.GetValue(element)?.ToString());
            var index = type.GetProperty("index")?.GetValue(element);
            if (index is IFormattable formattable)
                AppendRawProperty(builder, "index", formattable.ToString(null, CultureInfo.InvariantCulture));
        }

        private static void AppendNumericField<T>(StringBuilder builder, string label, T value, bool isReadOnly)
            where T : IFormattable
        {
            AppendStringProperty(builder, "label", label);
            AppendRawProperty(builder, "value", value.ToString(null, CultureInfo.InvariantCulture));
            AppendBoolProperty(builder, "isReadOnly", isReadOnly);
        }

        private static void AppendStringProperty(StringBuilder builder, string name, string value)
        {
            builder.Append(" [");
            builder.Append(name);
            builder.Append("=\"");
            builder.Append(AutomationProtocol.EscapeYamlString(value ?? string.Empty));
            builder.Append("\"]");
        }

        private static void AppendBoolProperty(StringBuilder builder, string name, bool value)
        {
            AppendRawProperty(builder, name, value ? "true" : "false");
        }

        private static void AppendNumberProperty<T>(StringBuilder builder, string name, T value)
            where T : IFormattable
        {
            AppendRawProperty(builder, name, value.ToString(null, CultureInfo.InvariantCulture));
        }

        private static void AppendRawProperty(StringBuilder builder, string name, string value)
        {
            builder.Append(" [");
            builder.Append(name);
            builder.Append('=');
            builder.Append(value);
            builder.Append(']');
        }

        private string GetOrCreateRef(VisualElement element)
        {
            if (_elementRefs.TryGetValue(element, out var reference))
                return reference;

            reference = string.Concat("e", _nextRef++.ToString(CultureInfo.InvariantCulture));
            _elementRefs[element] = reference;
            _refElements[reference] = element;
            return reference;
        }

        private void RemoveInvalidRefs(HashSet<VisualElement> liveElements)
        {
            var invalid = _elementRefs.Keys
                .Where(element => element == null || element.panel == null || !liveElements.Contains(element))
                .ToArray();
            foreach (var element in invalid)
            {
                var reference = _elementRefs[element];
                _elementRefs.Remove(element);
                _refElements.Remove(reference);
            }
        }

        private static bool IsVisible(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                var style = current.resolvedStyle;
                if (style.display == DisplayStyle.None || style.visibility == Visibility.Hidden ||
                    style.opacity <= 0f)
                    return false;
            }

            var bounds = GetClippedWorldBounds(element);
            return bounds.width > 0f && bounds.height > 0f;
        }

        private static Rect GetClippedWorldBounds(VisualElement element)
        {
            var result = element.worldBound;
            for (var parent = element.parent; parent != null; parent = parent.parent)
            {
                // IResolvedStyle does not expose overflow. The inline value still covers
                // programmatically configured runtime controls; Panel.Pick performs the
                // authoritative clipping check for USS-computed overflow.
                if (parent.style.overflow.value == Overflow.Hidden)
                    result = Intersect(result, parent.worldBound);
            }

            return result;
        }

        private static Rect Intersect(Rect left, Rect right)
        {
            var xMin = Mathf.Max(left.xMin, right.xMin);
            var yMin = Mathf.Max(left.yMin, right.yMin);
            var xMax = Mathf.Min(left.xMax, right.xMax);
            var yMax = Mathf.Min(left.yMax, right.yMax);
            return xMax <= xMin || yMax <= yMin
                ? Rect.zero
                : Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool TryFindHittablePoint(VisualElement element, out Vector2 point)
        {
            var rect = GetClippedWorldBounds(element);
            var xs = new[] { 0.5f, 0.2f, 0.8f };
            var ys = new[] { 0.5f, 0.2f, 0.8f };
            foreach (var y in ys)
            foreach (var x in xs)
            {
                var candidate = new Vector2(
                    Mathf.Lerp(rect.xMin, rect.xMax, x),
                    Mathf.Lerp(rect.yMin, rect.yMax, y));
                var picked = element.panel.Pick(candidate);
                if (picked != null && (picked == element || element.Contains(picked)))
                {
                    point = candidate;
                    return true;
                }
            }

            point = default;
            return false;
        }

        /// <summary>
        ///     Sends one step of a pointer interaction through a panel.
        /// </summary>
        /// <param name="panel">The runtime panel to dispatch through.</param>
        /// <param name="type">The step: a move, a button press, or a button release.</param>
        /// <param name="point">The panel space point to act on.</param>
        /// <param name="button">The mouse button index, as used by <see cref="Event.button" />.</param>
        /// <remarks>
        ///     Both the pointer event and the legacy mouse event are sent, because controls in the
        ///     wild listen for either one.
        /// </remarks>
        private static void SendMouseEvent(IPanel panel, EventType type, Vector2 point, int button)
        {
            var target = panel.Pick(point) ?? panel.visualTree;
            var systemEvent = new Event
            {
                type = type,
                mousePosition = point,
                button = button,
                clickCount = 1
            };

            switch (type)
            {
                case EventType.MouseMove:
                    using (var pointerMove = PointerMoveEvent.GetPooled(systemEvent))
                    {
                        pointerMove.target = target;
                        panel.visualTree.SendEvent(pointerMove);
                    }
                    using (var mouseMove = MouseMoveEvent.GetPooled(systemEvent))
                    {
                        mouseMove.target = target;
                        panel.visualTree.SendEvent(mouseMove);
                    }
                    break;
                case EventType.MouseDown:
                    using (var pointerDown = PointerDownEvent.GetPooled(systemEvent))
                    {
                        pointerDown.target = target;
                        panel.visualTree.SendEvent(pointerDown);
                    }
                    using (var mouseDown = MouseDownEvent.GetPooled(systemEvent))
                    {
                        mouseDown.target = target;
                        panel.visualTree.SendEvent(mouseDown);
                    }
                    break;
                case EventType.MouseUp:
                    using (var pointerUp = PointerUpEvent.GetPooled(systemEvent))
                    {
                        pointerUp.target = target;
                        panel.visualTree.SendEvent(pointerUp);
                    }
                    using (var mouseUp = MouseUpEvent.GetPooled(systemEvent))
                    {
                        mouseUp.target = target;
                        panel.visualTree.SendEvent(mouseUp);
                    }
                    break;
            }
        }

        private void StartNextScreenshot()
        {
            var request = _screenshotQueue.Dequeue();
            try
            {
                var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp",
                    "GameViewScreenshots"));
                Directory.CreateDirectory(directory);
                var name = string.Concat("game-view-",
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture), "-",
                    Guid.NewGuid().ToString("N").Substring(0, 6), ".png");
                request.Path = Path.Combine(directory, name);
                request.StartTime = EditorApplication.timeSinceStartup;
                _activeScreenshot = request;
                ScreenCapture.CaptureScreenshot(request.Path, 1);
            }
            catch (UnauthorizedAccessException exception)
            {
                _answer(request.EndPoint, request.MessageType,
                    AutomationProtocol.Error(request.RequestId, "write_failed", exception.Message));
            }
            catch (IOException exception)
            {
                _answer(request.EndPoint, request.MessageType,
                    AutomationProtocol.Error(request.RequestId, "write_failed", exception.Message));
            }
            catch (Exception exception)
            {
                _answer(request.EndPoint, request.MessageType,
                    AutomationProtocol.Error(request.RequestId, "capture_failed", exception.Message));
            }
        }

        internal static bool IsCompletePng(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 20)
                return false;

            var signature = new byte[PngSignature.Length];
            if (stream.Read(signature, 0, signature.Length) != signature.Length ||
                !signature.SequenceEqual(PngSignature))
                return false;

            stream.Seek(-12, SeekOrigin.End);
            var tail = new byte[8];
            if (stream.Read(tail, 0, tail.Length) != tail.Length)
                return false;
            return tail[0] == 0 && tail[1] == 0 && tail[2] == 0 && tail[3] == 0 &&
                   tail[4] == (byte)'I' && tail[5] == (byte)'E' &&
                   tail[6] == (byte)'N' && tail[7] == (byte)'D';
        }

        private void CompleteActiveScreenshotError(string code, string message)
        {
            if (_activeScreenshot == null)
                return;
            var request = _activeScreenshot;
            _activeScreenshot = null;
            _answer(request.EndPoint, request.MessageType, AutomationProtocol.Error(request.RequestId, code, message));
        }

        private void FailQueuedScreenshots(string code, string message)
        {
            while (_screenshotQueue.Count > 0)
            {
                var request = _screenshotQueue.Dequeue();
                _answer(request.EndPoint, request.MessageType, AutomationProtocol.Error(request.RequestId, code, message));
            }
        }

        private void ReplySuccess(Message message, JToken requestId, string propertyName = null,
            string propertyValue = null)
        {
            _answer(message.Origin, message.Type,
                AutomationProtocol.Success(requestId, propertyName, propertyValue));
        }

        private void ReplyError(Message message, JToken requestId, string code, string errorMessage)
        {
            _answer(message.Origin, message.Type, AutomationProtocol.Error(requestId, code, errorMessage));
        }

        private sealed class ScreenshotRequest
        {
            internal ScreenshotRequest(IPEndPoint endPoint, MessageType messageType, JToken requestId)
            {
                EndPoint = endPoint;
                MessageType = messageType;
                RequestId = requestId.DeepClone();
            }

            internal IPEndPoint EndPoint { get; }
            internal MessageType MessageType { get; }
            internal JToken RequestId { get; }
            internal string Path { get; set; }
            internal double StartTime { get; set; }
            internal long LastLength { get; set; } = -1;
            internal int StableLengthChecks { get; set; }
        }
    }
}
