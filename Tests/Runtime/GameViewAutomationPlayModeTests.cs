using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hackerzhuli.Code.PlayModeTests
{
    [TestFixture]
    public class GameViewAutomationPlayModeTests
    {
        private static readonly List<string> Responses = new();

        [UnityTest]
        public IEnumerator RuntimeDocument_SnapshotHoverAndClickUsePanelEvents()
        {
            Responses.Clear();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var gameObject = new GameObject("Automation Test UIDocument");
            object service = null;
            Type serviceType = null;
            try
            {
                var document = gameObject.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                var visualTree = Resources.Load<VisualTreeAsset>("GameViewAutomationDemo");
                Assert.That(visualTree, Is.Not.Null);
                visualTree.CloneTree(document.rootVisualElement);
                var button = document.rootVisualElement.Q<Button>("primary-action");
                Assert.That(button, Is.Not.Null);
                var missionType = document.rootVisualElement.Q<DropdownField>("mission-type");
                missionType.choices = new List<string> { "Survey", "Cargo", "Rescue" };
                missionType.index = 2;
                var music = document.rootVisualElement.Q<Toggle>("music");
                var crewSize = document.rootVisualElement.Q<IntegerField>("crew-size");
                var volume = document.rootVisualElement.Q<Slider>("volume");
                var safeRange = document.rootVisualElement.Q<MinMaxSlider>("safe-range");
                var operatorName = document.rootVisualElement.Q<TextField>("operator-name");
                var readOnlyId = document.rootVisualElement.Q<TextField>("read-only-id");
                readOnlyId.isReadOnly = true;
                var musicChanges = 0;
                var crewChanges = 0;
                var volumeChanges = 0;
                var rangeChanges = 0;
                var nameChanges = 0;
                music.RegisterValueChangedCallback(_ => musicChanges++);
                crewSize.RegisterValueChangedCallback(_ => crewChanges++);
                volume.RegisterValueChangedCallback(_ => volumeChanges++);
                safeRange.RegisterValueChangedCallback(_ => rangeChanges++);
                operatorName.RegisterValueChangedCallback(_ => nameChanges++);
                var clicks = 0;
                var pointerDowns = 0;
                var pointerUps = 0;
                var enters = 0;
                var lastDownButton = -1;
                var lastUpButton = -1;
                button.clicked += () => clicks++;
                // Clickable handles these in the target phase and stops immediate propagation, so a
                // callback registered after it would never run. Trickle down happens first.
                button.RegisterCallback<PointerDownEvent>(evt =>
                {
                    pointerDowns++;
                    lastDownButton = evt.button;
                }, TrickleDown.TrickleDown);
                button.RegisterCallback<PointerUpEvent>(evt =>
                {
                    pointerUps++;
                    lastUpButton = evt.button;
                }, TrickleDown.TrickleDown);
                button.RegisterCallback<PointerEnterEvent>(_ => enters++);

                yield return null;
                yield return null;
                Assert.That(document.rootVisualElement.panel, Is.Not.Null);

                serviceType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "Hackerzhuli.Code.Editor.GameViewAutomationService", false))
                    .FirstOrDefault(type => type != null);
                Assert.That(serviceType, Is.Not.Null, "The Editor automation service assembly was not loaded.");
                service = CreateService(serviceType);

                InvokeRequest(serviceType, service, "UiSnapshot",
                    "{\"requestId\":\"snapshot\"}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                var response = JsonUtility.FromJson<SnapshotResponse>(Responses[^1]);
                Assert.That(response.ok, Is.True);
                Assert.That(response.snapshot, Does.Contain("Mission Control"));
                Assert.That(response.snapshot, Does.Not.Contain("unity-checkmark"));
                Assert.That(response.snapshot, Does.Contain("Foldout [name=\"advanced-settings\"]"));
                Assert.That(response.snapshot, Does.Contain("Slider [name=\"volume\"]"));
                Assert.That(response.snapshot, Does.Contain("ScrollView [name=\"event-log\"]"));
                Assert.That(response.snapshot, Does.Contain("Navigation"));
                Assert.That(response.snapshot,
                    Does.Contain("VisualElement [name=\"display-none-panel\"] [display=None]"));
                Assert.That(response.snapshot,
                    Does.Contain("VisualElement [name=\"visibility-hidden-panel\"] [visibility=Hidden]"));
                Assert.That(response.snapshot, Does.Not.Contain("Display-none child must be omitted"));
                Assert.That(response.snapshot, Does.Not.Contain("Visibility-hidden child must be omitted"));
                Assert.That(response.snapshot, Does.Contain("Transparent child remains in the snapshot"));
                Assert.That(response.snapshot,
                    Does.Contain("Button [name=\"disabled-action\"] [enabledSelf=false]"));
                Assert.That(response.snapshot, Does.Not.Contain("enabledInHierarchy"));
                Assert.That(response.snapshot, Does.Not.Contain("childrenOmitted"));
                Assert.That(response.snapshot, Does.Not.Contain("children omitted"));
                Assert.That(response.snapshot, Does.Not.Contain("Queue item 11"));
                var outputDirectory = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "..", "Temp", "GameViewAutomationDemo"));
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "game-view-snapshot.yaml"), response.snapshot);
                File.WriteAllText(Path.Combine(outputDirectory, "raw-visual-tree.txt"),
                    DumpTree(document.rootVisualElement));

                var documentRootMatch = Regex.Match(response.snapshot,
                    @"TemplateContainer[^\r\n]*\[ref=(e\d+)\]");
                Assert.That(documentRootMatch.Success, Is.True, response.snapshot);
                InvokeRequest(serviceType, service, "UiHierarchy",
                    $"{{\"requestId\":\"root-hierarchy\",\"ref\":\"{documentRootMatch.Groups[1].Value}\"}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":false"));
                Assert.That(Responses[^1], Does.Contain("\"code\":\"forbidden\""));

                var rootMatch = Regex.Match(response.snapshot,
                    @"VisualElement[^\r\n]*\[name=""automation-demo""\][^\r\n]*\[ref=(e\d+)\]");
                Assert.That(rootMatch.Success, Is.True, response.snapshot);
                InvokeRequest(serviceType, service, "UiHierarchy",
                    $"{{\"requestId\":\"inspect\",\"ref\":\"{rootMatch.Groups[1].Value}\"}}");
                var hierarchyResponse = JsonUtility.FromJson<HierarchyResponse>(Responses[^1]);
                Assert.That(hierarchyResponse.ok, Is.True, Responses[^1]);
                Assert.That(hierarchyResponse.hierarchy, Does.Contain("ussClasses="));
                Assert.That(hierarchyResponse.hierarchy, Does.Contain("unity-checkmark"));
                Assert.That(hierarchyResponse.hierarchy, Does.Not.Contain("[panel="));
                Assert.That(hierarchyResponse.hierarchy, Does.Not.Contain("[resolvedStyle="));
                Assert.That(hierarchyResponse.hierarchy, Does.Not.Contain("[text="));
                Assert.That(hierarchyResponse.hierarchy, Does.Not.Contain("[value="));
                Assert.That(hierarchyResponse.hierarchy, Does.Not.Contain("[display="));
                Assert.That(Regex.IsMatch(hierarchyResponse.hierarchy,
                        @"VisualElement \[name=""display-none-panel""\][^\r\n]*:\r?\n\s+- Label [^\r\n]*\[ref=e\d+\]"),
                    Is.True, hierarchyResponse.hierarchy);
                Assert.That(hierarchyResponse.hierarchy, Does.Contain("[omittedChildCount=12]"));
                Assert.That(hierarchyResponse.hierarchy,
                    Does.Contain("[omissionReason=\"similar_children\"]"));
                Assert.That(Regex.Matches(hierarchyResponse.hierarchy, @"(?m)^\s*- ").Count,
                    Is.LessThanOrEqualTo(200));
                File.WriteAllText(Path.Combine(outputDirectory, "game-view-hierarchy.yaml"),
                    hierarchyResponse.hierarchy);

                InvokeRequest(serviceType, service, "UiInspect",
                    $"{{\"requestId\":\"inspect-properties\",\"ref\":\"{rootMatch.Groups[1].Value}\"}}");
                var inspectionResponse = JsonUtility.FromJson<InspectionResponse>(Responses[^1]);
                Assert.That(inspectionResponse.ok, Is.True, Responses[^1]);
                Assert.That(inspectionResponse.inspection,
                    Does.StartWith("type: VisualElement\nref: "));
                Assert.That(inspectionResponse.inspection, Does.Contain("\nlayout:\n"));
                Assert.That(inspectionResponse.inspection, Does.Contain("geometry:\n"));
                Assert.That(inspectionResponse.inspection, Does.Contain("element:\n"));
                Assert.That(inspectionResponse.inspection, Does.Contain("uss:\n"));
                Assert.That(inspectionResponse.inspection, Does.Contain("  resolvedStyles:\n"));
                Assert.That(inspectionResponse.inspection, Does.Contain("properties:\n"));
                Assert.That(inspectionResponse.inspection, Does.Not.Contain("\n  panel: "));
                Assert.That(Regex.Matches(inspectionResponse.inspection, @"(?m)^type: ").Count,
                    Is.EqualTo(1));
                File.WriteAllText(Path.Combine(outputDirectory, "game-view-inspection.yaml"),
                    inspectionResponse.inspection);

                InvokeRequest(serviceType, service, "UiSetValue",
                    $"{{\"requestId\":\"set-toggle\",\"ref\":\"{FindRef(response.snapshot, "Toggle", "music")}\",\"value\":\"off\"}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                Assert.That(music.value, Is.False);
                Assert.That(musicChanges, Is.EqualTo(1));

                InvokeRequest(serviceType, service, "UiSetValue",
                    $"{{\"requestId\":\"set-int\",\"ref\":\"{FindRef(response.snapshot, "IntegerField", "crew-size")}\",\"value\":\"0x0C\"}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                Assert.That(crewSize.value, Is.EqualTo(12));
                Assert.That(crewChanges, Is.EqualTo(1));

                InvokeRequest(serviceType, service, "UiSetValue",
                    $"{{\"requestId\":\"set-slider\",\"ref\":\"{FindRef(response.snapshot, "Slider", "volume")}\",\"value\":\"25%\"}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                Assert.That(volume.value, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(volumeChanges, Is.EqualTo(1));

                InvokeRequest(serviceType, service, "UiSetValue",
                    $"{{\"requestId\":\"set-range\",\"ref\":\"{FindRef(response.snapshot, "MinMaxSlider", "safe-range")}\",\"value\":[10,90]}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                Assert.That(safeRange.value, Is.EqualTo(new Vector2(10f, 90f)));
                Assert.That(rangeChanges, Is.EqualTo(1));

                InvokeRequest(serviceType, service, "UiSetValue",
                    $"{{\"requestId\":\"set-text\",\"ref\":\"{FindRef(response.snapshot, "TextField", "operator-name")}\",\"value\":\"Bob\\nBuilder\"}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                Assert.That(operatorName.value, Is.EqualTo("Bob\nBuilder"));
                Assert.That(nameChanges, Is.EqualTo(1));

                InvokeRequest(serviceType, service, "UiSetValue",
                    $"{{\"requestId\":\"read-only\",\"ref\":\"{FindRef(response.snapshot, "TextField", "read-only-id")}\",\"value\":\"changed\"}}");
                Assert.That(Responses[^1], Does.Contain("\"code\":\"read_only\""));
                Assert.That(readOnlyId.value, Is.EqualTo("MC-2026-0730"));

                var match = Regex.Match(response.snapshot,
                    @"Button[^\r\n]*\[name=""primary-action""\][^\r\n]*\[ref=(e\d+)\]");
                Assert.That(match.Success, Is.True, Responses[^1]);
                var reference = match.Groups[1].Value;

                InvokeRequest(serviceType, service, "UiHover",
                    $"{{\"requestId\":\"hover\",\"ref\":\"{reference}\"}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                Assert.That(enters, Is.EqualTo(1));

                InvokeRequest(serviceType, service, "UiClick",
                    $"{{\"requestId\":\"click\",\"ref\":\"{reference}\"}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                Assert.That(clicks, Is.EqualTo(1),
                    "A synthetic left button down and up must drive the Button's Clickable exactly once.");
                Assert.That(pointerDowns, Is.EqualTo(1),
                    "Every element and every button takes the same synthetic pointer path.");
                Assert.That(pointerUps, Is.EqualTo(1));
                Assert.That(lastDownButton, Is.EqualTo(0));
                Assert.That(lastUpButton, Is.EqualTo(0));

                InvokeRequest(serviceType, service, "UiClick",
                    $"{{\"requestId\":\"right-click\",\"ref\":\"{reference}\",\"button\":1}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                Assert.That(pointerDowns, Is.EqualTo(2));
                Assert.That(pointerUps, Is.EqualTo(2));
                Assert.That(lastDownButton, Is.EqualTo(1));
                Assert.That(lastUpButton, Is.EqualTo(1));
                Assert.That(clicks, Is.EqualTo(1),
                    "Clickable only activates on the left button, so a right click is not a click.");

                InvokeRequest(serviceType, service, "UiClick",
                    $"{{\"requestId\":\"middle-click\",\"ref\":\"{reference}\",\"button\":2}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));
                Assert.That(pointerDowns, Is.EqualTo(3));
                Assert.That(lastDownButton, Is.EqualTo(2));
                Assert.That(clicks, Is.EqualTo(1));

                InvokeRequest(serviceType, service, "UiClick",
                    $"{{\"requestId\":\"bad-button\",\"ref\":\"{reference}\",\"button\":3}}");
                Assert.That(Responses[^1], Does.Contain("\"code\":\"invalid_request\""));
                InvokeRequest(serviceType, service, "UiClick",
                    $"{{\"requestId\":\"bad-button-type\",\"ref\":\"{reference}\",\"button\":\"right\"}}");
                Assert.That(Responses[^1], Does.Contain("\"code\":\"invalid_request\""));
                Assert.That(pointerDowns, Is.EqualTo(3), "A rejected request must not send events.");

                var responseCount = Responses.Count;
                InvokeRequest(serviceType, service, "GameViewScreenshot",
                    "{\"requestId\":\"screenshot\"}");
                var update = serviceType.GetMethod("Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                for (var frame = 0; frame < 600 && Responses.Count == responseCount; frame++)
                {
                    update.Invoke(service, null);
                    yield return null;
                }

                Assert.That(Responses.Count, Is.GreaterThan(responseCount),
                    "The screenshot request did not complete.");
                var screenshot = JsonUtility.FromJson<ScreenshotResponse>(Responses[^1]);
                Assert.That(screenshot.ok, Is.True, Responses[^1]);
                Assert.That(File.Exists(screenshot.path), Is.True);
                Assert.That(new FileInfo(screenshot.path).Length, Is.GreaterThan(20));
            }
            finally
            {
                if (service != null)
                    serviceType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public)
                        ?.Invoke(service, null);
                UnityEngine.Object.Destroy(gameObject);
                UnityEngine.Object.Destroy(panelSettings);
            }
        }

        /// <summary>
        ///     Pins the Unity behaviour that decides how far right click support can go: a right click
        ///     reaches the element, but no contextual menu can open on a runtime panel.
        /// </summary>
        /// <remarks>
        ///     <c>ContextualMenuManipulator</c> activates on a right button PointerUp, then asks
        ///     <c>panel.contextualMenuManager</c> to display the menu. <c>Panel</c>'s constructor leaves
        ///     that null and only <c>EditorPanel</c> assigns one, so on a runtime panel the manipulator
        ///     runs and silently displays nothing. Sending a <c>ContextClickEvent</c> would not change
        ///     this, nothing on a runtime panel listens for it.
        /// </remarks>
        [UnityTest]
        public IEnumerator RightClick_ReachesTheElementButOpensNoContextualMenu()
        {
            Responses.Clear();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var gameObject = new GameObject("Context Menu Test UIDocument");
            object service = null;
            Type serviceType = null;
            try
            {
                var document = gameObject.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                var target = new VisualElement { name = "context-target" };
                target.style.width = 200;
                target.style.height = 100;
                document.rootVisualElement.Add(target);

                var menuPopulated = 0;
                var rightUps = 0;
                target.AddManipulator(new ContextualMenuManipulator(populate =>
                    menuPopulated++));
                target.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (evt.button == 1)
                        rightUps++;
                });

                yield return null;
                yield return null;
                Assert.That(document.rootVisualElement.panel, Is.Not.Null);

                serviceType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "Hackerzhuli.Code.Editor.GameViewAutomationService", false))
                    .FirstOrDefault(type => type != null);
                Assert.That(serviceType, Is.Not.Null, "The Editor automation service assembly was not loaded.");
                service = CreateService(serviceType);

                InvokeRequest(serviceType, service, "UiSnapshot", "{\"requestId\":\"context-snapshot\"}");
                var response = JsonUtility.FromJson<SnapshotResponse>(Responses[^1]);
                Assert.That(response.ok, Is.True, Responses[^1]);
                var reference = FindRef(response.snapshot, "VisualElement", "context-target");

                InvokeRequest(serviceType, service, "UiClick",
                    $"{{\"requestId\":\"context-click\",\"ref\":\"{reference}\",\"button\":1}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"));

                Assert.That(rightUps, Is.EqualTo(1),
                    "The element must receive the right button release.");
                Assert.That(menuPopulated, Is.EqualTo(0),
                    "A runtime panel has no ContextualMenuManager, so no menu can be built.");
            }
            finally
            {
                if (service != null)
                    serviceType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public)
                        ?.Invoke(service, null);
                UnityEngine.Object.Destroy(gameObject);
                UnityEngine.Object.Destroy(panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator PanelAttachedContent_IsSnapshottedAndAllowedAsHierarchyTarget()
        {
            Responses.Clear();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            var gameObject = new GameObject("Panel Overlay Test UIDocument");
            object service = null;
            Type serviceType = null;
            try
            {
                var document = gameObject.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                document.rootVisualElement.Add(new Button { name = "anchor", text = "Anchor" });

                yield return null;
                yield return null;
                Assert.That(document.rootVisualElement.panel, Is.Not.Null);

                // An open dropdown parents its menu to the panel root rather than to any
                // UIDocument root, which is what this reproduces.
                var popup = new VisualElement { name = "panel-popup" };
                popup.Add(new Label { name = "panel-popup-item", text = "Cargo" });
                document.rootVisualElement.panel.visualTree.Add(popup);

                yield return null;

                serviceType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "Hackerzhuli.Code.Editor.GameViewAutomationService", false))
                    .FirstOrDefault(type => type != null);
                Assert.That(serviceType, Is.Not.Null, "The Editor automation service assembly was not loaded.");
                service = CreateService(serviceType);

                InvokeRequest(serviceType, service, "UiSnapshot", "{\"requestId\":\"overlay-snapshot\"}");
                var response = JsonUtility.FromJson<SnapshotResponse>(Responses[^1]);
                Assert.That(response.ok, Is.True, Responses[^1]);
                Assert.That(response.snapshot, Does.Contain("- PanelOverlay"));
                Assert.That(response.snapshot, Does.Contain("VisualElement [name=\"panel-popup\"]"));
                Assert.That(response.snapshot, Does.Contain("Label [name=\"panel-popup-item\"]"));

                var popupRef = FindRef(response.snapshot, "VisualElement", "panel-popup");
                InvokeRequest(serviceType, service, "UiHierarchy",
                    $"{{\"requestId\":\"overlay-hierarchy\",\"ref\":\"{popupRef}\"}}");
                var hierarchy = JsonUtility.FromJson<HierarchyResponse>(Responses[^1]);
                Assert.That(hierarchy.ok, Is.True, Responses[^1]);
                Assert.That(hierarchy.hierarchy, Does.Contain("panel-popup-item"));

                popup.RemoveFromHierarchy();
                yield return null;

                InvokeRequest(serviceType, service, "UiSnapshot", "{\"requestId\":\"overlay-closed\"}");
                var closed = JsonUtility.FromJson<SnapshotResponse>(Responses[^1]);
                Assert.That(closed.ok, Is.True, Responses[^1]);
                Assert.That(closed.snapshot, Does.Not.Contain("PanelOverlay"));
                Assert.That(closed.snapshot, Does.Contain("Button [name=\"anchor\"]"));
            }
            finally
            {
                if (service != null)
                    serviceType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public)
                        ?.Invoke(service, null);
                UnityEngine.Object.Destroy(gameObject);
                UnityEngine.Object.Destroy(panelSettings);
            }
        }

        private static object CreateService(Type serviceType)
        {
            var constructor = serviceType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single();
            var delegateType = constructor.GetParameters()[0].ParameterType;
            var invoke = delegateType.GetMethod("Invoke");
            var parameters = invoke.GetParameters()
                .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                .ToArray();
            var capture = typeof(GameViewAutomationPlayModeTests).GetMethod(
                nameof(CaptureResponse), BindingFlags.Static | BindingFlags.NonPublic);
            var body = Expression.Call(capture, Expression.Convert(parameters[2], typeof(string)));
            var callback = Expression.Lambda(delegateType, body, parameters).Compile();
            return constructor.Invoke(new object[] { callback });
        }

        private static string FindRef(string snapshot, string typeName, string elementName)
        {
            var match = Regex.Match(snapshot,
                $@"{Regex.Escape(typeName)}[^\r\n]*\[name=""{Regex.Escape(elementName)}""\][^\r\n]*\[ref=(e\d+)\]");
            Assert.That(match.Success, Is.True,
                $"Could not find {typeName} '{elementName}' in snapshot:\n{snapshot}");
            return match.Groups[1].Value;
        }

        private static void InvokeRequest(Type serviceType, object service, string messageTypeName, string json)
        {
            var process = serviceType.GetMethod("Process", BindingFlags.Instance | BindingFlags.NonPublic);
            var messageType = process.GetParameters()[0].ParameterType;
            var message = Activator.CreateInstance(messageType);
            var protocolEnum = messageType.GetProperty("Type").PropertyType;
            messageType.GetProperty("Type").SetValue(message, Enum.Parse(protocolEnum, messageTypeName));
            messageType.GetProperty("Value").SetValue(message, json);
            messageType.GetProperty("Origin").SetValue(message,
                new IPEndPoint(IPAddress.Loopback, 22000));
            process.Invoke(service, new[] { message });
        }

        private static void CaptureResponse(string response)
        {
            Responses.Add(response);
        }

        private static string DumpTree(VisualElement root, int depth = 0)
        {
            var result = new System.Text.StringBuilder();
            result.Append(' ', depth * 2).Append(root.GetType().Name)
                .Append(" name=").Append(root.name).AppendLine();
            foreach (var child in root.Children())
                result.Append(DumpTree(child, depth + 1));
            return result.ToString();
        }

        [Serializable]
        private sealed class SnapshotResponse
        {
            public bool ok;
            public string snapshot;
        }

        [Serializable]
        private sealed class ScreenshotResponse
        {
            public bool ok;
            public string path;
        }

        [Serializable]
        private sealed class HierarchyResponse
        {
            public bool ok;
            public string hierarchy;
        }

        [Serializable]
        private sealed class InspectionResponse
        {
            public bool ok;
            public string inspection;
        }
    }
}
