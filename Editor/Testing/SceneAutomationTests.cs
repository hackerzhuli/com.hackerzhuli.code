using System.Collections.Generic;
using System.Net;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor.Testing
{
    [TestFixture]
    internal class SceneAutomationTests
    {
        private static readonly IPEndPoint Loopback = new(IPAddress.Loopback, 12345);
        private static readonly IPEndPoint Remote = new(IPAddress.Parse("203.0.113.7"), 12345);

        [Test]
        public void MessageTypes_HaveStableProtocolValues()
        {
            Assert.That((int)MessageType.SceneList, Is.EqualTo(114));
            Assert.That((int)MessageType.SceneOpen, Is.EqualTo(115));
        }

        [Test]
        public void BuildScenesYaml_UsesFixedFieldOrderAndEscaping()
        {
            var scenes = new List<SceneAutomation.SceneInfo>
            {
                new("Main \"Menu\"", "Assets/Scenes/Main.unity", true, true, false, false, 0, 12),
                new("Extra", "Assets/Scenes/Extra.unity", false, false, true, true, -1, -1)
            };

            var yaml = SceneAutomation.BuildScenesYaml(scenes, true);

            Assert.That(yaml, Is.EqualTo(
                "mode: Play\n" +
                "activeScene: \"Assets/Scenes/Main.unity\"\n" +
                "scenes:\n" +
                "  - name: \"Main \\\"Menu\\\"\"\n" +
                "    path: \"Assets/Scenes/Main.unity\"\n" +
                "    isActive: true\n" +
                "    isLoaded: true\n" +
                "    isDirty: false\n" +
                "    isSubScene: false\n" +
                "    buildIndex: 0\n" +
                "    rootCount: 12\n" +
                "  - name: \"Extra\"\n" +
                "    path: \"Assets/Scenes/Extra.unity\"\n" +
                "    isActive: false\n" +
                "    isLoaded: false\n" +
                "    isDirty: true\n" +
                "    isSubScene: true\n" +
                "    buildIndex: -1"));
        }

        [Test]
        public void BuildScenesYaml_ReportsEditModeAndEmptyState()
        {
            var yaml = SceneAutomation.BuildScenesYaml(new List<SceneAutomation.SceneInfo>(), false);

            Assert.That(yaml, Is.EqualTo("mode: Edit\nactiveScene: null\nscenes: []"));
        }

        [Test]
        public void BuildScenesYaml_ReportsUnsavedSceneWithEmptyPath()
        {
            var scenes = new List<SceneAutomation.SceneInfo>
            {
                new("Untitled", "", true, true, true, false, -1, 0)
            };

            var yaml = SceneAutomation.BuildScenesYaml(scenes, false);

            Assert.That(yaml, Does.Contain("activeScene: \"\""));
            Assert.That(yaml, Does.Contain("    path: \"\""));
            Assert.That(yaml, Does.Contain("    rootCount: 0"));
        }

        [Test]
        public void TryResolveScenePath_AcceptsProjectRelativeAndAbsolutePaths()
        {
            Assert.That(SceneAutomation.TryResolveScenePath("Assets/Scenes/Main.unity", out var relative,
                out _), Is.True);
            Assert.That(relative, Is.EqualTo("Assets/Scenes/Main.unity"));

            var absolute = FileUtility.GetAssetFullPath("Assets/Scenes/Main.unity");
            Assert.That(SceneAutomation.TryResolveScenePath(absolute, out var fromAbsolute, out _), Is.True);
            Assert.That(fromAbsolute, Is.EqualTo("Assets/Scenes/Main.unity"));
        }

        [Test]
        public void TryResolveScenePath_RejectsEmptyNonSceneAndOutsidePaths()
        {
            Assert.That(SceneAutomation.TryResolveScenePath("  ", out _, out var emptyError), Is.False);
            Assert.That(emptyError, Does.Contain("non-empty"));

            Assert.That(SceneAutomation.TryResolveScenePath("Assets/Readme.txt", out _, out var typeError),
                Is.False);
            Assert.That(typeError, Does.Contain(".unity"));

            Assert.That(SceneAutomation.TryResolveScenePath("Z:/elsewhere/Other.unity", out _,
                out var outsideError), Is.False);
            Assert.That(outsideError, Does.Contain("outside"));
        }

        [Test]
        public void SceneList_ReportsOpenScenesToLoopbackClient()
        {
            var response = Send(MessageType.SceneList, "{\"requestId\":\"scene-1\"}", Loopback);

            Assert.That(response.Value<bool>("ok"), Is.True);
            Assert.That(response["requestId"]?.Value<string>(), Is.EqualTo("scene-1"));

            var scenes = response.Value<string>("scenes");
            Assert.That(scenes, Does.StartWith("mode: Edit\n"));
            Assert.That(scenes, Does.Contain("activeScene: "));
        }

        [Test]
        public void Requests_FromNonLoopbackClientsAreForbidden()
        {
            var response = Send(MessageType.SceneList, "{\"requestId\":\"scene-2\"}", Remote);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("forbidden"));
        }

        [Test]
        public void Requests_WithoutRequestIdAreRejected()
        {
            var response = Send(MessageType.SceneList, "{}", Loopback);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
        }

        [Test]
        public void SceneOpen_ReportsMissingSceneAsset()
        {
            var response = Send(MessageType.SceneOpen,
                "{\"requestId\":\"scene-3\",\"path\":\"Assets/NoSuchSceneHere.unity\"}", Loopback);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?["code"]?.Value<string>(), Is.EqualTo("not_found"));
        }

        [Test]
        public void SceneOpen_RejectsNonSceneAndMissingPaths()
        {
            var wrongExtension = Send(MessageType.SceneOpen,
                "{\"requestId\":\"scene-4\",\"path\":\"Assets/Readme.txt\"}", Loopback);
            Assert.That(wrongExtension["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));

            var missingPath = Send(MessageType.SceneOpen, "{\"requestId\":\"scene-5\"}", Loopback);
            Assert.That(missingPath["error"]?["code"]?.Value<string>(), Is.EqualTo("invalid_request"));
        }

        /// <summary>
        ///     Sends a message through the real handler and returns the parsed response.
        /// </summary>
        private static JObject Send(MessageType type, string value, IPEndPoint origin)
        {
            string payload = null;
            var message = new Message { Type = type, Value = value, Origin = origin };

            SceneAutomation.Process(message, (endPoint, responseType, responseValue) =>
            {
                Assert.That(endPoint, Is.EqualTo(origin));
                Assert.That(responseType, Is.EqualTo(type));
                payload = responseValue;
            });

            Assert.That(payload, Is.Not.Null, "The handler did not answer the request.");
            return JObject.Parse(payload);
        }
    }
}
