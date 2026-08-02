using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;
using Object = UnityEngine.Object;

namespace Hackerzhuli.Code.Editor.Testing
{
    [TestFixture]
    internal class GameObjectAutomationTests
    {
        private static readonly IPEndPoint Loopback = new(IPAddress.Loopback, 12345);
        private static readonly IPEndPoint Remote = new(IPAddress.Parse("203.0.113.7"), 12345);

        /// <summary>
        ///     The objects created by a test, destroyed again so the open scene is left as it was found.
        /// </summary>
        private readonly List<GameObject> _created = new();

        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (var gameObject in _created)
                if (gameObject != null)
                    Object.DestroyImmediate(gameObject);
            _created.Clear();
        }

        [Test]
        public void MessageTypes_HaveStableProtocolValues()
        {
            Assert.That((int)MessageType.SceneHierarchy, Is.EqualTo(118));
            Assert.That((int)MessageType.GameObjectHierarchy, Is.EqualTo(119));
            Assert.That((int)MessageType.GameObjectFind, Is.EqualTo(120));
            Assert.That((int)MessageType.GameObjectInspect, Is.EqualTo(121));
        }

        #region Hierarchy formatting

        [Test]
        public void BuildHierarchyYaml_UsesFixedFieldOrderAndEscaping()
        {
            var header = new GameObjectAutomation.HierarchyHeader("Assets/Scenes/Main.unity", null, false,
                2, null, 2, 0);
            var roots = new List<GameObjectAutomation.GameObjectNode>
            {
                Leaf("Main \"Camera\"", "a1", true),
                new("Canvas", "a2", true, 1,
                    new List<GameObjectAutomation.GameObjectNode> { Leaf("Panel", "a3", false) }, false)
            };

            var yaml = GameObjectAutomation.BuildHierarchyYaml(header, roots);

            Assert.That(yaml, Is.EqualTo(
                "scene: \"Assets/Scenes/Main.unity\"\n" +
                "mode: Edit\n" +
                "maxDepth: 2\n" +
                "rootCount: 2\n" +
                "gameObjects:\n" +
                "  - \"Main \\\"Camera\\\"\" [id=\"a1\"]\n" +
                "  - \"Canvas\" [id=\"a2\"]:\n" +
                "    - \"Panel\" [id=\"a3\"] [active=false]"));
        }

        [Test]
        public void BuildHierarchyYaml_StatesTheDepthLimitOnceAndNeverAsAComment()
        {
            var header = new GameObjectAutomation.HierarchyHeader("Assets/Scenes/Main.unity", null, false,
                1, "dynamic", 2, 0);
            var roots = new List<GameObjectAutomation.GameObjectNode>
            {
                new("A", "1", true, 1, new List<GameObjectAutomation.GameObjectNode>
                {
                    new("A1", "2", true, 3, null, true)
                }, false),
                new("B", "3", true, 1, new List<GameObjectAutomation.GameObjectNode>
                {
                    new("B1", "4", true, 2, null, true)
                }, false)
            };

            var yaml = GameObjectAutomation.BuildHierarchyYaml(header, roots);

            Assert.That(yaml, Does.Contain("depthLimit: \"dynamic\"\n"));
            Assert.That(yaml.Split('\n').Count(line => line.Contains("depthLimit")), Is.EqualTo(1));
            // A node at the depth limit carries nothing but the fact that it has children.
            Assert.That(yaml, Does.Contain("    - \"A1\" [id=\"2\"] [omittedChildCount=3]\n"));
            Assert.That(yaml, Does.EndWith("    - \"B1\" [id=\"4\"] [omittedChildCount=2]"));
            Assert.That(yaml, Does.Not.Contain("omissionReason"));
            Assert.That(yaml, Does.Not.Contain("childrenOmitted"));
            Assert.That(yaml, Does.Not.Contain("#"));
        }

        [Test]
        public void BuildHierarchyYaml_ReportsTheChildLimitSeparatelyFromTheDepthLimit()
        {
            var header = new GameObjectAutomation.HierarchyHeader("Assets/Scenes/Main.unity", null, false,
                1, null, 1, 0);
            var children = new List<GameObjectAutomation.GameObjectNode>();
            for (var index = 0; index < 20; index++)
                children.Add(Leaf($"Item {index:00}", (100 + index).ToString("x"), true));
            var roots = new List<GameObjectAutomation.GameObjectNode>
            {
                new("List", "1", true, 63, children, false)
            };

            var yaml = GameObjectAutomation.BuildHierarchyYaml(header, roots);

            // A node cut off by the child limit lists the first twenty and counts the rest.
            Assert.That(yaml, Does.Contain("  - \"List\" [id=\"1\"] [omittedChildCount=43]:"));
            Assert.That(yaml, Does.Not.Contain("depthLimit"));
            Assert.That(yaml.Split('\n').Count(line => line.TrimStart().StartsWith("- ")), Is.EqualTo(21));
        }

        [Test]
        public void BuildHierarchyYaml_ReportsOmittedRootsAndTheRequestedObjectPath()
        {
            var header = new GameObjectAutomation.HierarchyHeader("Assets/Scenes/Main.unity",
                "Canvas/Panel", true, 0, "requested", 87, 37);

            var yaml = GameObjectAutomation.BuildHierarchyYaml(header,
                new List<GameObjectAutomation.GameObjectNode> { Leaf("Panel", "f", true) });

            Assert.That(yaml, Does.Contain("path: \"Canvas/Panel\"\n"));
            Assert.That(yaml, Does.Contain("mode: Play\n"));
            Assert.That(yaml, Does.Contain("depthLimit: \"requested\"\n"));
            Assert.That(yaml, Does.Contain("rootCount: 87\n"));
            Assert.That(yaml, Does.Contain("rootsOmitted: 37\n"));
        }

        [Test]
        public void BuildHierarchyYaml_ReportsAnEmptyScene()
        {
            var header = new GameObjectAutomation.HierarchyHeader("Assets/Scenes/Empty.unity", null, false,
                0, null, 0, 0);

            var yaml = GameObjectAutomation.BuildHierarchyYaml(header,
                new List<GameObjectAutomation.GameObjectNode>());

            Assert.That(yaml, Does.EndWith("gameObjects: []"));
        }

        #endregion

        #region Find formatting

        [Test]
        public void BuildFindYaml_UsesFixedFieldOrder()
        {
            var entries = new List<GameObjectAutomation.FindEntry>
            {
                new("Button", "a1", "Assets/Scenes/Main.unity", "Canvas/Panel/Button", true, false, 4)
            };

            var yaml = GameObjectAutomation.BuildFindYaml("Button", "name", "exact", entries, 1);

            Assert.That(yaml, Is.EqualTo(
                "query: \"Button\"\n" +
                "queryKind: \"name\"\n" +
                "match: \"exact\"\n" +
                "count: 1\n" +
                "gameObjects:\n" +
                "  - name: \"Button\"\n" +
                "    id: \"a1\"\n" +
                "    scene: \"Assets/Scenes/Main.unity\"\n" +
                "    path: \"Canvas/Panel/Button\"\n" +
                "    active: true\n" +
                "    activeInHierarchy: false\n" +
                "    componentCount: 4"));
        }

        [Test]
        public void BuildFindYaml_ReportsOmittedMatchesAndEmptyResults()
        {
            var entries = new List<GameObjectAutomation.FindEntry>
            {
                new("Button", "1", "Assets/Scenes/Main.unity", "Button", true, true, 2)
            };

            Assert.That(GameObjectAutomation.BuildFindYaml("Button", "name", "contains", entries, 130),
                Does.Contain("matchesOmitted: 129\n"));
            Assert.That(
                GameObjectAutomation.BuildFindYaml("Nothing", "name", "exact",
                    new List<GameObjectAutomation.FindEntry>(), 0),
                Does.EndWith("count: 0\ngameObjects: []"));
        }

        #endregion

        #region Path parsing

        [Test]
        public void TryParsePath_SplitsSegmentsAndToleratesALeadingSlash()
        {
            Assert.That(GameObjectAutomation.TryParsePath("Canvas/Panel/Button", out var segments, out _),
                Is.True);
            Assert.That(segments, Is.EqualTo(new[] { "Canvas", "Panel", "Button" }));

            Assert.That(GameObjectAutomation.TryParsePath("/Canvas/Panel", out var rooted, out _), Is.True);
            Assert.That(rooted, Is.EqualTo(new[] { "Canvas", "Panel" }));
        }

        [Test]
        public void TryParsePath_RejectsEmptyPathsAndEmptySegments()
        {
            Assert.That(GameObjectAutomation.TryParsePath("  ", out _, out var emptyError), Is.False);
            Assert.That(emptyError, Does.Contain("non-empty"));

            Assert.That(GameObjectAutomation.TryParsePath("Canvas//Button", out _, out var gapError),
                Is.False);
            Assert.That(gapError, Does.Contain("empty name segment"));

            Assert.That(GameObjectAutomation.TryParsePath("Canvas/", out _, out var trailingError),
                Is.False);
            Assert.That(trailingError, Does.Contain("empty name segment"));
        }

        #endregion

        #region Request handling

        [Test]
        public void Requests_FromNonLoopbackClientsAreForbidden()
        {
            var response = Send(MessageType.SceneHierarchy, "{\"requestId\":\"go-1\"}", Remote);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("forbidden"));
        }

        [Test]
        public void Requests_WithoutRequestIdAreRejected()
        {
            var response = Send(MessageType.SceneHierarchy, "{}", Loopback);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
        }

        [Test]
        public void SceneHierarchy_ListsTheActiveSceneAndItsRoots()
        {
            Create("UnityCodeTestRoot");

            var response = Send(MessageType.SceneHierarchy, "{\"requestId\":\"go-2\"}", Loopback);

            Assert.That(response.Value<bool>("ok"), Is.True);
            var hierarchy = response.Value<string>("hierarchy");
            Assert.That(hierarchy, Does.StartWith("scene: "));
            Assert.That(hierarchy, Does.Contain("mode: Edit\n"));
            Assert.That(hierarchy, Does.Contain("\"UnityCodeTestRoot\" [id="));
        }

        [Test]
        public void SceneHierarchy_ReportsAnUnknownScene()
        {
            var response = Send(MessageType.SceneHierarchy,
                "{\"requestId\":\"go-3\",\"scene\":\"Assets/Scenes/NoSuchScene.unity\"}", Loopback);

            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("not_found"));
        }

        [Test]
        public void SceneHierarchy_RejectsANegativeDepth()
        {
            var response = Send(MessageType.SceneHierarchy, "{\"requestId\":\"go-4\",\"depth\":-1}",
                Loopback);

            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
            Assert.That(response["error"]?["message"]?.Value<string>(), Does.Contain("non-negative"));
        }

        [Test]
        public void GameObjectHierarchy_ResolvesByOpaqueIdAndHonoursDepthZero()
        {
            var parent = Create("UnityCodeParent");
            var child = new GameObject("UnityCodeChild");
            child.transform.SetParent(parent.transform);

            var response = Send(MessageType.GameObjectHierarchy,
                $"{{\"requestId\":\"go-5\",\"id\":\"{UnityObjectId.Get(parent)}\",\"depth\":0}}",
                Loopback);

            var hierarchy = response.Value<string>("hierarchy");
            Assert.That(hierarchy, Does.Contain("path: \"UnityCodeParent\"\n"));
            Assert.That(hierarchy, Does.Contain("depthLimit: \"requested\"\n"));
            Assert.That(hierarchy, Does.Contain("[omittedChildCount=1]"));
            Assert.That(hierarchy, Does.Not.Contain("omissionReason"));
            Assert.That(hierarchy, Does.Not.Contain("UnityCodeChild"));
        }

        [Test]
        public void GameObjectHierarchy_ResolvesByPath()
        {
            var parent = Create("UnityCodeParent");
            var child = new GameObject("UnityCodeChild");
            child.transform.SetParent(parent.transform);

            var response = Send(MessageType.GameObjectHierarchy,
                "{\"requestId\":\"go-6\",\"path\":\"UnityCodeParent\"}", Loopback);

            var hierarchy = response.Value<string>("hierarchy");
            Assert.That(hierarchy,
                Does.Contain($"\"UnityCodeChild\" [id=\"{UnityObjectId.Get(child)}\"]"));
        }

        [Test]
        public void GameObjectHierarchy_ReportsAnAmbiguousPathWithCandidateIds()
        {
            var first = Create("UnityCodeTwin");
            var second = Create("UnityCodeTwin");

            var response = Send(MessageType.GameObjectHierarchy,
                "{\"requestId\":\"go-7\",\"path\":\"UnityCodeTwin\"}", Loopback);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("ambiguous_path"));

            var text = response["error"]?["message"]?.Value<string>();
            Assert.That(text, Does.Contain("2 GameObjects match path 'UnityCodeTwin'"));
            Assert.That(text, Does.Contain($"id=\"{UnityObjectId.Get(first)}\""));
            Assert.That(text, Does.Contain($"id=\"{UnityObjectId.Get(second)}\""));
        }

        [Test]
        public void GameObjectHierarchy_RejectsAmbiguousAndMissingTargetSelectors()
        {
            var neither = Send(MessageType.GameObjectHierarchy, "{\"requestId\":\"go-8\"}", Loopback);
            Assert.That(neither["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));

            var both = Send(MessageType.GameObjectHierarchy,
                "{\"requestId\":\"go-9\",\"id\":\"1\",\"path\":\"Whatever\"}", Loopback);
            Assert.That(both["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
        }

        [Test]
        public void GameObjectHierarchy_ReportsAnUnknownObjectId()
        {
            var response = Send(MessageType.GameObjectHierarchy,
                "{\"requestId\":\"go-10\",\"id\":\"7ffffffe\"}", Loopback);

            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("not_found"));
            Assert.That(response["error"]?["message"]?.Value<string>(), Does.Contain("domain reload"));
        }

        [Test]
        public void GameObjectHierarchy_RequiresTheOpaqueIdToBeAString()
        {
            var response = Send(MessageType.GameObjectHierarchy,
                "{\"requestId\":\"go-10b\",\"id\":1234}", Loopback);

            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
            Assert.That(response["error"]?["message"]?.Value<string>(), Does.Contain("string"));
        }

        [Test]
        public void GameObjectFind_MatchesByExactNameAndBySubstring()
        {
            Create("UnityCodeFindTarget");

            var exact = Send(MessageType.GameObjectFind,
                "{\"requestId\":\"go-11\",\"name\":\"UnityCodeFindTarget\"}", Loopback);
            Assert.That(exact.Value<string>("gameObjects"), Does.Contain("name: \"UnityCodeFindTarget\""));
            Assert.That(exact.Value<string>("gameObjects"), Does.Contain("queryKind: \"name\""));

            var partial = Send(MessageType.GameObjectFind,
                "{\"requestId\":\"go-12\",\"name\":\"CodeFindTar\",\"match\":\"contains\"}", Loopback);
            Assert.That(partial.Value<string>("gameObjects"), Does.Contain("name: \"UnityCodeFindTarget\""));

            var caseMismatch = Send(MessageType.GameObjectFind,
                "{\"requestId\":\"go-13\",\"name\":\"unitycodefindtarget\"}", Loopback);
            Assert.That(caseMismatch.Value<string>("gameObjects"), Does.Contain("count: 0"));
        }

        [Test]
        public void GameObjectFind_RefusesToMatchAPathLoosely()
        {
            var response = Send(MessageType.GameObjectFind,
                "{\"requestId\":\"go-14\",\"path\":\"Canvas\",\"match\":\"contains\"}", Loopback);

            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
            Assert.That(response["error"]?["message"]?.Value<string>(), Does.Contain("matched in full"));
        }

        [Test]
        public void GameObjectFind_RequiresExactlyOneQuery()
        {
            var neither = Send(MessageType.GameObjectFind, "{\"requestId\":\"go-15\"}", Loopback);
            Assert.That(neither["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));

            var both = Send(MessageType.GameObjectFind,
                "{\"requestId\":\"go-16\",\"name\":\"A\",\"path\":\"A\"}", Loopback);
            Assert.That(both["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
        }

        [Test]
        public void GameObjectInspect_ReportsTheObjectTransformAndComponents()
        {
            var target = Create("UnityCodeInspectTarget");
            target.transform.localPosition = new Vector3(1, 2, 3);
            target.AddComponent<Camera>();

            var response = Send(MessageType.GameObjectInspect,
                $"{{\"requestId\":\"go-17\",\"id\":\"{UnityObjectId.Get(target)}\"}}", Loopback);

            var inspection = response.Value<string>("gameObject");
            Assert.That(inspection, Does.StartWith("name: \"UnityCodeInspectTarget\"\n"));
            Assert.That(inspection, Does.Contain($"id: \"{UnityObjectId.Get(target)}\"\n"));
            Assert.That(inspection, Does.Contain("activeInHierarchy: true\n"));
            // The transform is a component like any other, and Unity always lists it first.
            Assert.That(inspection, Does.Contain("components:\n  - type: \"Transform\"\n"));
            Assert.That(inspection, Does.Contain("      localPosition: [1,2,3]\n"));
            Assert.That(inspection, Does.Contain("  - type: \"Camera\"\n"));
            Assert.That(inspection, Does.Contain("    enabled: true\n"));
            Assert.That(inspection, Does.Contain("      fieldOfView: "));
        }

        [Test]
        public void GameObjectInspect_LimitsCommonComponentsToTheirCuratedMembers()
        {
            var target = Create("UnityCodeCuratedTarget");
            target.AddComponent<Camera>();

            var response = Send(MessageType.GameObjectInspect,
                $"{{\"requestId\":\"go-18\",\"id\":\"{UnityObjectId.Get(target)}\"}}", Loopback);

            var inspection = response.Value<string>("gameObject");
            Assert.That(inspection, Does.Contain("    detail: \"common\"\n"));
            Assert.That(inspection, Does.Contain("      fieldOfView: "));
            Assert.That(inspection, Does.Contain("      nearClipPlane: "));
            // Reflecting over Camera yields around sixty members, several of them matrices.
            Assert.That(inspection, Does.Not.Contain("      cameraToWorldMatrix: "));
            Assert.That(inspection, Does.Not.Contain("      cullingMatrix: "));
        }

        [Test]
        public void GameObjectInspect_ReportsAllPublicMembersOfAComponentWithoutACuratedList()
        {
            var target = Create("UnityCodeUncuratedTarget");
            target.AddComponent<AudioListener>();

            var response = Send(MessageType.GameObjectInspect,
                $"{{\"requestId\":\"go-19\",\"id\":\"{UnityObjectId.Get(target)}\"}}", Loopback);

            var inspection = response.Value<string>("gameObject");
            var listener = inspection.Substring(inspection.IndexOf("- type: \"AudioListener\"",
                System.StringComparison.Ordinal));
            Assert.That(listener, Does.Not.Contain("detail: "));
        }

        [Test]
        public void GameObjectInspect_IncludesEverythingOnlyForNamedComponents()
        {
            var target = Create("UnityCodeDetailTarget");
            target.AddComponent<Camera>();

            var summary = Send(MessageType.GameObjectInspect,
                $"{{\"requestId\":\"go-20\",\"id\":\"{UnityObjectId.Get(target)}\"}}", Loopback);

            var full = Send(MessageType.GameObjectInspect,
                $"{{\"requestId\":\"go-21\",\"id\":\"{UnityObjectId.Get(target)}\"," +
                "\"fullDetailComponents\":[\"Camera\"]}", Loopback);
            var inspection = full.Value<string>("gameObject");
            Assert.That(inspection, Does.Contain("    detail: \"full\"\n"));
            Assert.That(inspection, Does.Contain("      cameraToWorldMatrix: "));
            Assert.That(CountMembers(inspection), Is.GreaterThan(CountMembers(
                summary.Value<string>("gameObject"))));
            // Naming one component does not expand the others.
            Assert.That(inspection, Does.Contain("  - type: \"Transform\"\n    id: \""));
            Assert.That(inspection, Does.Contain("    detail: \"common\"\n"));
        }

        [Test]
        public void GameObjectInspect_NeverReadsPropertiesThatInstantiateAssets()
        {
            var target = Create("UnityCodeRendererTarget");
            var renderer = target.AddComponent<MeshRenderer>();

            var response = Send(MessageType.GameObjectInspect,
                $"{{\"requestId\":\"go-22\",\"id\":\"{UnityObjectId.Get(target)}\"," +
                "\"fullDetailComponents\":[\"MeshRenderer\"]}", Loopback);

            var inspection = response.Value<string>("gameObject");
            Assert.That(inspection, Does.Contain("  - type: \"MeshRenderer\"\n"));
            Assert.That(inspection, Does.Not.Contain("      material: "));
            Assert.That(inspection, Does.Not.Contain("      materials: "));
            // Reading Renderer.material would have instantiated a copy and assigned it back.
            Assert.That(renderer.sharedMaterial, Is.Null);
        }

        [Test]
        public void GameObjectInspect_WritesObjectReferencesAsReferencesNotAsCollections()
        {
            var target = Create("UnityCodeReferenceTarget");
            var child = new GameObject("UnityCodeReferenceChild");
            child.transform.SetParent(target.transform);

            var skin = target.AddComponent<SkinnedMeshRenderer>();
            skin.rootBone = child.transform;
            skin.sharedMaterials = new[] { new Material(Shader.Find("Unlit/Color")) { name = "Probe" } };

            var body = target.AddComponent<Rigidbody>();
            body.excludeLayers = 5;

            var response = Send(MessageType.GameObjectInspect,
                $"{{\"requestId\":\"go-24\",\"id\":\"{UnityObjectId.Get(target)}\"," +
                "\"fullDetailComponents\":[\"Rigidbody\"]}", Loopback);

            var inspection = response.Value<string>("gameObject");
            // Transform enumerates its children, so testing for a collection first would turn a
            // reference to one into a list of something else entirely.
            Assert.That(inspection,
                Does.Contain($"      rootBone: \"Transform(name=UnityCodeReferenceChild,id={UnityObjectId.Get(child.transform)})\""));
            // Items of a collection get the same treatment as a value in its own right.
            Assert.That(inspection, Does.Contain("      sharedMaterials: [\"Material(name=Probe,"));
            // LayerMask has no useful ToString, and must be reported as its numeric value.
            Assert.That(inspection, Does.Contain("      excludeLayers: 5\n"));
        }

        [Test]
        public void GameObjectInspect_SkipsCompilerGeneratedMembers()
        {
            var target = Create("UnityCodeBackingFieldTarget");
            target.AddComponent<Camera>();

            var response = Send(MessageType.GameObjectInspect,
                $"{{\"requestId\":\"go-25\",\"id\":\"{UnityObjectId.Get(target)}\"," +
                "\"fullDetailComponents\":[\"Camera\"]}", Loopback);

            Assert.That(response.Value<string>("gameObject"), Does.Not.Contain("k__BackingField"));
        }

        [Test]
        public void GameObjectInspect_RejectsAMalformedFullDetailList()
        {
            var response = Send(MessageType.GameObjectInspect,
                "{\"requestId\":\"go-23\",\"id\":\"1\",\"fullDetailComponents\":\"Camera\"}",
                Loopback);

            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
        }

        #endregion

        #region Helpers

        private static GameObjectAutomation.GameObjectNode Leaf(string name, string id, bool isActive)
        {
            return new GameObjectAutomation.GameObjectNode(name, id, isActive, 0, null, false);
        }

        /// <summary>
        ///     Counts the member lines of an inspection, which are indented deeper than anything else.
        /// </summary>
        private static int CountMembers(string inspection)
        {
            return inspection.Split('\n').Count(line => line.StartsWith("      "));
        }

        /// <summary>
        ///     Creates a root object in the open scene, registered for cleanup.
        /// </summary>
        private GameObject Create(string name)
        {
            var gameObject = new GameObject(name);
            _created.Add(gameObject);
            return gameObject;
        }

        /// <summary>
        ///     Sends a message through the real handler and returns the parsed response.
        /// </summary>
        private static JObject Send(MessageType type, string value, IPEndPoint origin)
        {
            string payload = null;
            var message = new Message { Type = type, Value = value, Origin = origin };

            GameObjectAutomation.Process(message, (endPoint, responseType, responseValue) =>
            {
                Assert.That(endPoint, Is.EqualTo(origin));
                Assert.That(responseType, Is.EqualTo(type));
                payload = responseValue;
            });

            Assert.That(payload, Is.Not.Null, "The handler did not answer the request.");
            return JObject.Parse(payload);
        }

        #endregion
    }
}
