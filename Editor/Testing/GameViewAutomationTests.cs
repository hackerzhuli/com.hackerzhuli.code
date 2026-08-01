using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor.Testing
{
    [TestFixture]
    internal class GameViewAutomationTests
    {
        private const string DemoStyleSheetPath =
            "Packages/com.hackerzhuli.code/Tests/Runtime/Resources/GameViewAutomationDemo.uss";

        private const string RuntimeThemePath =
            "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        private GameViewAutomationService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new GameViewAutomationService((_, _, _) => { });
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        [Test]
        public void MessageTypes_HaveStableProtocolValues()
        {
            Assert.That((int)MessageType.UiSnapshot, Is.EqualTo(107));
            Assert.That((int)MessageType.UiClick, Is.EqualTo(108));
            Assert.That((int)MessageType.UiHover, Is.EqualTo(109));
            Assert.That((int)MessageType.GameViewScreenshot, Is.EqualTo(110));
            Assert.That((int)MessageType.UiHierarchy, Is.EqualTo(111));
            Assert.That((int)MessageType.UiInspect, Is.EqualTo(112));
            Assert.That((int)MessageType.UiSetValue, Is.EqualTo(113));
        }

        [Test]
        public void Snapshot_UsesRuntimeTypeNativePropertiesEscapingAndStableRefs()
        {
            var root = new VisualElement();
            var layout = new VisualElement();
            var button = new DerivedButton
            {
                name = "primary",
                tooltip = "Say \"hello\"",
                text = "Play\nNow"
            };
            layout.Add(button);
            root.Add(layout);

            var first = _service.BuildSnapshotForTests(root);
            var second = _service.BuildSnapshotForTests(root);

            const string expected =
                "- UIDocument:\n" +
                "  - VisualElement [ref=e1]:\n" +
                "    - VisualElement [ref=e2]:\n" +
                "      - DerivedButton [name=\"primary\"] [tooltip=\"Say \\\"hello\\\"\"] " +
                "[text=\"Play\\nNow\"] [ref=e3]";
            Assert.That(first, Is.EqualTo(expected));
            Assert.That(second, Is.EqualTo(expected));
            Assert.That(first, Does.Not.Contain("role"));
            Assert.That(first, Does.Not.Contain("disabled"));
        }

        [Test]
        public void Snapshot_UsesNativeToggleNamesAndFixedPropertyOrder()
        {
            var root = new VisualElement();
            root.Add(new Toggle("Music") { value = true });

            var snapshot = _service.BuildSnapshotForTests(root);

            Assert.That(snapshot,
                Does.Contain("    - Toggle [label=\"Music\"] [value=true]"));
            Assert.That(snapshot, Does.Not.Contain("childrenOmitted"));
            Assert.That(snapshot, Does.Not.Contain("checkbox"));
            Assert.That(snapshot, Does.Not.Contain("checked"));
        }

        [Test]
        public void Snapshot_OmitsCommonBuiltInImplementationHierarchy()
        {
            var root = new VisualElement();
            root.Add(new Toggle("Music") { value = true });

            var snapshot = _service.BuildSnapshotForTests(root);

            Assert.That(snapshot, Does.Contain("- Toggle [label=\"Music\"] [value=true]"));
            Assert.That(snapshot, Does.Not.Contain("- Label [text=\"Music\"]"));
            Assert.That(snapshot, Does.Not.Contain("unity-checkmark"));
            Assert.That(snapshot, Does.Not.Contain("omittedChildCount"));
        }

        [Test]
        public void Snapshot_TruncatesOnlyTwentyOrMoreSameTypeSiblings()
        {
            var root = new VisualElement();
            var repeated = new VisualElement { name = "repeated" };
            for (var index = 0; index < 22; index++)
                repeated.Add(new Label($"Item {index + 1:00}"));
            root.Add(repeated);

            var snapshot = _service.BuildSnapshotForTests(root);

            Assert.That(snapshot, Does.Contain("Label [text=\"Item 10\"]"));
            Assert.That(snapshot, Does.Not.Contain("Label [text=\"Item 11\"]"));
            Assert.That(snapshot, Does.Not.Contain("children omitted"));
            Assert.That(snapshot, Does.Not.Contain("omissionReason"));
        }

        [Test]
        public void Snapshot_HiddenElementIsRetainedButItsChildrenAreOmitted()
        {
            var root = new VisualElement();
            var displayNone = new VisualElement { name = "display-none" };
            displayNone.style.display = DisplayStyle.None;
            displayNone.Add(new Label("Must not appear"));
            var visibilityHidden = new VisualElement { name = "visibility-hidden" };
            visibilityHidden.style.visibility = Visibility.Hidden;
            visibilityHidden.Add(new Label("Also must not appear"));
            root.Add(displayNone);
            root.Add(visibilityHidden);

            var snapshot = _service.BuildSnapshotForTests(root);

            Assert.That(snapshot,
                Does.Contain("VisualElement [name=\"display-none\"] [display=None]"));
            Assert.That(snapshot,
                Does.Contain("VisualElement [name=\"visibility-hidden\"] [visibility=Hidden]"));
            Assert.That(snapshot, Does.Not.Contain("Must not appear"));
            Assert.That(snapshot, Does.Not.Contain("Also must not appear"));
            Assert.That(snapshot, Does.Not.Contain("childrenOmitted"));
        }

        [Test]
        public void Snapshot_ZeroOpacityDoesNotOmitChildren()
        {
            var root = new VisualElement();
            var transparent = new VisualElement { name = "transparent" };
            transparent.style.opacity = 0;
            transparent.Add(new Label("Transparent child remains"));
            root.Add(transparent);

            var snapshot = _service.BuildSnapshotForTests(root);

            Assert.That(snapshot, Does.Contain("Transparent child remains"));
        }

        [Test]
        public void Snapshot_ReportsOnlyTheElementsOwnEnabledState()
        {
            var root = new VisualElement();
            var disabledParent = new VisualElement { name = "disabled-parent" };
            disabledParent.SetEnabled(false);
            disabledParent.Add(new VisualElement { name = "enabled-child" });
            root.Add(disabledParent);

            var snapshot = _service.BuildSnapshotForTests(root);

            Assert.That(snapshot,
                Does.Contain("VisualElement [name=\"disabled-parent\"] [enabledSelf=false]"));
            Assert.That(snapshot, Does.Not.Contain("enabledInHierarchy"));
            Assert.That(snapshot,
                Does.Not.Contain("VisualElement [name=\"enabled-child\"] [enabledSelf=false]"));
        }

        [Test]
        public void Hierarchy_ExpandsChildrenWithOnlyNamesUssClassesAndProtocolMetadata()
        {
            var hidden = new HierarchyElement
            {
                name = "hidden",
                focusable = true,
                tabIndex = 7,
                CustomValue = "created",
                OrdinaryValue = "must-not-be-output"
            };
            hidden.AddToClassList("demo-card");
            hidden.style.display = DisplayStyle.None;
            hidden.Add(new Toggle("Music") { value = true });

            var snapshot = _service.BuildHierarchyForTests(hidden);

            Assert.That(snapshot, Does.Contain("[ussClasses=[\"demo-card\"]]"));
            Assert.That(snapshot, Does.Contain("[name=\"hidden\"]"));
            Assert.That(snapshot, Does.Not.Contain("[display=None]"));
            Assert.That(snapshot, Does.Not.Contain("[value=true]"));
            Assert.That(snapshot, Does.Not.Contain("[label=\"Music\"]"));
            Assert.That(snapshot, Does.Not.Contain("CustomValue"));
            Assert.That(snapshot, Does.Not.Contain("OrdinaryValue"));
            Assert.That(snapshot, Does.Not.Contain("[panel="));
            Assert.That(snapshot, Does.Not.Contain("[resolvedStyle="));
            Assert.That(snapshot, Does.Not.Contain("[enabledInHierarchy="));
            Assert.That(snapshot, Does.Contain("- Toggle"));
            Assert.That(snapshot, Does.Contain("unity-checkmark"));
        }

        [Test]
        public void Hierarchy_DynamicallyLimitsDepthButAlwaysIncludesDirectChildren()
        {
            var root = new VisualElement { name = "root" };
            for (var first = 0; first < 8; first++)
            {
                var child = new VisualElement { name = $"child-{first}" };
                root.Add(child);
                for (var second = 0; second < 8; second++)
                {
                    var grandchild = new VisualElement { name = $"grandchild-{first}-{second}" };
                    child.Add(grandchild);
                    for (var third = 0; third < 4; third++)
                        grandchild.Add(new VisualElement { name = $"leaf-{first}-{second}-{third}" });
                }
            }

            var snapshot = _service.BuildHierarchyForTests(root);

            for (var index = 0; index < 8; index++)
                Assert.That(snapshot, Does.Contain($"[name=\"child-{index}\"]"));
            Assert.That(snapshot.Split('\n').Count(line => line.TrimStart().StartsWith("- ")),
                Is.LessThanOrEqualTo(200));
            Assert.That(snapshot, Does.StartWith("# maxDepth "));
            Assert.That(snapshot, Does.Contain("(dynamic, 200 element budget)"));
            // Elements at the depth limit carry only the count, never a reason.
            Assert.That(snapshot, Does.Contain("[omittedChildCount=4]"));
            Assert.That(snapshot, Does.Not.Contain("omissionReason"));
            Assert.That(snapshot, Does.Not.Contain("childrenOmitted"));
            Assert.That(snapshot, Does.Not.Contain("[name=\"leaf-0-0-0\"]"));
            // The depth limit is stated once in the header, never repeated per element.
            Assert.That(snapshot.Split('\n').Count(line => line.TrimStart().StartsWith("#")), Is.EqualTo(1));
        }

        [Test]
        public void Hierarchy_RequestedDepthZeroIncludesOnlyTheTarget()
        {
            var root = new VisualElement { name = "root" };
            root.Add(new VisualElement { name = "child" });

            var snapshot = _service.BuildHierarchyForTests(root, 0);

            Assert.That(snapshot, Does.Contain("[name=\"root\"]"));
            Assert.That(snapshot, Does.Not.Contain("[name=\"child\"]"));
            Assert.That(snapshot, Does.Contain("[omittedChildCount=1]"));
            Assert.That(snapshot, Does.Not.Contain("omissionReason"));
            Assert.That(snapshot, Does.StartWith("# maxDepth 0 (requested)"));
        }

        [Test]
        public void Hierarchy_RequestedDepthOneIncludesDirectChildrenOnly()
        {
            var root = new VisualElement { name = "root" };
            var child = new VisualElement { name = "child" };
            child.Add(new VisualElement { name = "grandchild" });
            root.Add(child);

            var snapshot = _service.BuildHierarchyForTests(root, 1);

            Assert.That(snapshot, Does.Contain("[name=\"child\"]"));
            Assert.That(snapshot, Does.Not.Contain("[name=\"grandchild\"]"));
            Assert.That(snapshot, Does.Contain("[omittedChildCount=1]"));
            Assert.That(snapshot, Does.Not.Contain("omissionReason"));
        }

        [Test]
        public void Hierarchy_SimilarChildOmissionIsMarkedOnTheParent()
        {
            var root = new VisualElement { name = "root" };
            for (var index = 0; index < 22; index++)
                root.Add(new Label($"Item {index + 1:00}"));

            var snapshot = _service.BuildHierarchyForTests(root);

            Assert.That(snapshot, Does.Contain("[omittedChildCount=12]"));
            Assert.That(snapshot, Does.Contain("[omissionReason=\"similar_children\"]"));
            Assert.That(snapshot, Does.Not.Contain("childrenOmitted"));
            // The properties above say it all, so nothing repeats them as a comment.
            Assert.That(snapshot, Does.Not.Contain("#"));
        }

        [Test]
        public void Hierarchy_ExpandsTheBuiltInStructureOfCompositeControls()
        {
            var scrollView = new ScrollView { name = "log" };
            scrollView.Add(new Label("Entry") { name = "entry" });

            var snapshot = _service.BuildHierarchyForTests(scrollView);

            Assert.That(snapshot, Does.Contain("[name=\"log\"]"));
            Assert.That(snapshot, Does.Contain("unity-scroll-view__content-viewport"));
            Assert.That(snapshot, Does.Contain("unity-scroll-view__content-container"));
            Assert.That(snapshot, Does.Contain("- Scroller"));
            Assert.That(snapshot, Does.Contain("[name=\"entry\"]"));
        }

        [Test]
        public void Snapshot_KeepsCompositeControlContentWithoutItsBuiltInStructure()
        {
            var scrollView = new ScrollView { name = "log" };
            scrollView.Add(new Label("Entry") { name = "entry" });

            var snapshot = _service.BuildSnapshotForTests(scrollView);

            Assert.That(snapshot, Does.Contain("[name=\"log\"]"));
            Assert.That(snapshot, Does.Contain("[name=\"entry\"]"));
            Assert.That(snapshot, Does.Not.Contain("- Scroller"));
        }

        [Test]
        public void Inspect_OutputsAllCreatePropertiesForOnlyTheTarget()
        {
            var target = new HierarchyElement
            {
                name = "target",
                CustomValue = "created",
                OrdinaryValue = "must-not-be-output"
            };
            target.AddToClassList("inspect-me");
            target.Add(new Label("Child must not appear"));

            var inspection = _service.BuildInspectionForTests(target);

            Assert.That(inspection, Does.StartWith("type: HierarchyElement\nref: e1\n"));
            Assert.That(inspection, Does.Contain("layout:\n"));
            Assert.That(inspection, Does.Contain("geometry:\n"));
            Assert.That(inspection, Does.Contain("element:\n"));
            Assert.That(inspection, Does.Contain("uss:\n  classes: [\"inspect-me\"]"));
            Assert.That(inspection, Does.Contain("  inlineStyles:"));
            Assert.That(inspection, Does.Contain("  resolvedStyles:\n"));
            Assert.That(inspection, Does.Contain("properties:\n"));
            Assert.That(inspection, Does.Contain("  CustomValue: \"created\""));
            Assert.That(inspection, Does.Contain("  childCount: 1"));
            Assert.That(inspection, Does.Not.Contain("\n  panel: "));
            Assert.That(inspection, Does.Not.Contain("\n  localBound: "));
            Assert.That(inspection, Does.Not.Contain("\n  userData: "));
            Assert.That(inspection, Does.Not.Contain("\n  disablePlayModeTint: "));
            Assert.That(inspection.Split('\n').Count(line => line == "  focusable: false"),
                Is.EqualTo(1));
            Assert.That(inspection.Split('\n').Count(line => line == "  tabIndex: 0"),
                Is.EqualTo(1));
            Assert.That(inspection, Does.Not.Contain("OrdinaryValue"));
            Assert.That(inspection, Does.Not.Contain("Child must not appear"));
            Assert.That(inspection.Split('\n').Count(line => line.StartsWith("type: ")),
                Is.EqualTo(1));
        }

        [Test]
        public void Inspect_ResolvedStylesOmitOnlyObviousOrConditionallyMeaninglessValues()
        {
            var target = new VisualElement { name = "style-target" };
            target.style.backgroundColor = (Color)new Color32(0x33, 0x4D, 0x66, 0xFF);
            target.style.color = (Color)new Color32(0x12, 0x34, 0x56, 0x78);
            target.style.borderLeftColor = Color.red;
            target.style.borderLeftWidth = 0f;
            target.style.flexDirection = FlexDirection.Row;
            target.style.position = Position.Relative;

            var inspection = _service.BuildInspectionForTests(target);
            var resolvedStart = inspection.IndexOf("  resolvedStyles:\n", StringComparison.Ordinal);
            var propertiesStart = inspection.IndexOf("properties:\n", resolvedStart,
                StringComparison.Ordinal);
            var resolvedStyles = inspection.Substring(resolvedStart,
                propertiesStart - resolvedStart);

            Assert.That(resolvedStyles, Does.Contain("    background-color: \"#334D66\""));
            Assert.That(resolvedStyles, Does.Contain("    color: \"#12345678\""));
            Assert.That(resolvedStyles, Does.Contain("    flex-direction: Row"));
            Assert.That(resolvedStyles, Does.Contain("    position: Relative"));
            Assert.That(resolvedStyles, Does.Not.Contain("    background-image:"));
            Assert.That(resolvedStyles, Does.Not.Contain("    background-position-"));
            Assert.That(resolvedStyles, Does.Not.Contain("    border-left-color:"));
            Assert.That(resolvedStyles, Does.Not.Contain("    left:"));
            Assert.That(resolvedStyles, Does.Not.Contain("    transition-"));
            Assert.That(resolvedStyles, Does.Not.Contain("    transform-origin:"));
        }

        [Test]
        public void Inspect_ReportsMatchedSelectorsWithSourcesAndCascadeWinners()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(DemoStyleSheetPath);
            Assert.That(sheet, Is.Not.Null, $"{DemoStyleSheetPath} must be importable.");

            var toolbar = new VisualElement();
            toolbar.AddToClassList("demo-toolbar");
            toolbar.styleSheets.Add(sheet);
            var target = new VisualElement();
            target.AddToClassList("demo-column");
            target.AddToClassList("event-log");
            // Beats the height of .event-log, so the cascade winner is the inline style.
            target.style.height = 12f;
            toolbar.Add(target);

            var inspection = _service.BuildInspectionForTests(target);
            var matched = Section(inspection, "  matchedSelectors:\n", "  inlineStyles:");

            // Ordered the way Unity applies them, so the last one wins a conflict.
            Assert.That(matched, Does.Contain($"    - selector: \".demo-toolbar > *\"\n" +
                                              $"      source: \"{DemoStyleSheetPath}:36\"\n" +
                                              "      specificity: 11\n"));
            Assert.That(matched.IndexOf("\".demo-column\"", StringComparison.Ordinal),
                Is.GreaterThan(matched.IndexOf("\".demo-toolbar > *\"", StringComparison.Ordinal)));
            Assert.That(matched.IndexOf("\".event-log\"", StringComparison.Ordinal),
                Is.GreaterThan(matched.IndexOf("\".demo-column\"", StringComparison.Ordinal)));

            Assert.That(matched, Does.Contain(
                "        - {property: \"margin-left\", value: \"10px\", line: 37}"));
            // .event-log declares padding and background-color later than .demo-column does.
            Assert.That(matched, Does.Contain(
                $"        - {{property: \"padding\", value: \"14px\", line: 44, overriddenBy: \"{DemoStyleSheetPath}:64\"}}"));
            Assert.That(matched, Does.Contain(
                "        - {property: \"padding\", value: \"8px\", line: 66}"));
            Assert.That(matched, Does.Contain(
                "        - {property: \"height\", value: \"180px\", line: 65, overriddenBy: \"inline\"}"));
            // Every rule reports both lists, so nothing about a matched rule is hidden.
            Assert.That(matched, Does.Contain("      appliedDeclarations:\n"));
            Assert.That(matched, Does.Contain("      overriddenDeclarations:\n"));
        }

        [Test]
        public void Inspect_ReportsMatchedSelectorsFromImportedStyleSheets()
        {
            // A theme declares nothing itself and only imports the sheets that carry the built in
            // control styles, so its rules are only reachable when imports are matched too.
            var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(RuntimeThemePath);
            Assert.That(theme, Is.Not.Null, $"{RuntimeThemePath} must be importable.");

            var root = new VisualElement();
            root.styleSheets.Add(theme);
            var target = new Button();
            root.Add(target);

            var inspection = _service.BuildInspectionForTests(target);

            Assert.That(inspection, Does.Contain("    - selector: \".unity-button\"\n"));
            Assert.That(inspection, Does.Contain($"      source: \"{RuntimeThemePath}:"));
        }

        [Test]
        public void Inspect_ReportsNoMatchedSelectorsWithoutStyleSheets()
        {
            var target = new VisualElement { name = "unstyled" };
            target.AddToClassList("demo-column");

            var inspection = _service.BuildInspectionForTests(target);

            Assert.That(inspection, Does.Contain("  matchedSelectors: []\n"));
        }

        private static string Section(string inspection, string start, string end)
        {
            var startIndex = inspection.IndexOf(start, StringComparison.Ordinal);
            Assert.That(startIndex, Is.GreaterThanOrEqualTo(0), $"'{start}' must be present.");
            var endIndex = inspection.IndexOf(end, startIndex, StringComparison.Ordinal);
            Assert.That(endIndex, Is.GreaterThan(startIndex), $"'{end}' must follow '{start}'.");
            return inspection.Substring(startIndex, endIndex - startIndex);
        }

        [Test]
        public void Snapshot_FormatsNumbersUsingInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var root = new VisualElement();
                root.Add(new Slider("Volume", 0f, 1f) { value = 0.8f });

                var snapshot = _service.BuildSnapshotForTests(root);

                Assert.That(snapshot, Does.Contain("[value=0.8] [lowValue=0] [highValue=1]"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void JsonResponses_PreserveOpaqueNumericRequestIdAndErrorShape()
        {
            var success = JObject.Parse(AutomationProtocol.Success(new JValue(42), "snapshot", "- Button"));
            var failure = JObject.Parse(AutomationProtocol.Error(new JValue("abc"), "unknown_ref", "Missing"));

            Assert.That(success["requestId"]?.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(success.Value<bool>("ok"), Is.True);
            Assert.That(failure.Value<bool>("ok"), Is.False);
            Assert.That(failure["error"]?["code"]?.Value<string>(), Is.EqualTo("unknown_ref"));
        }

        [Test]
        public void CompletePng_RequiresSignatureAndTerminalIendChunk()
        {
            var path = Path.Combine(Path.GetTempPath(), $"game-view-test-{Guid.NewGuid():N}.png");
            try
            {
                File.WriteAllBytes(path, new byte[]
                {
                    137, 80, 78, 71, 13, 10, 26, 10,
                    0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D',
                    174, 66, 96, 130
                });
                Assert.That(GameViewAutomationService.IsCompletePng(path), Is.True);

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
                    stream.SetLength(19);
                Assert.That(GameViewAutomationService.IsCompletePng(path), Is.False);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private sealed class DerivedButton : Button
        {
        }

        private sealed class HierarchyElement : VisualElement
        {
            [CreateProperty]
            public string CustomValue { get; set; }

            public string OrdinaryValue { get; set; }
        }
    }
}
