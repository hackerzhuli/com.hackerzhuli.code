using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor.Testing
{
    [TestFixture]
    internal class VisualSnapshotAutomationTests
    {
        private static readonly IPEndPoint Loopback = new(IPAddress.Loopback, 12345);
        private static readonly IPEndPoint Remote = new(IPAddress.Parse("203.0.113.7"), 12345);

        /// <summary>
        ///     A camera looking down world +Z, which is what an orthographic or perspective projection
        ///     matrix below expects to be fed.
        /// </summary>
        private static readonly Matrix4x4 LookingForward = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));

        private static readonly Rect Viewport = new(0f, 0f, 800f, 400f);

        [Test]
        public void MessageTypes_HaveStableProtocolValues()
        {
            Assert.That((int)MessageType.GameObjectVisualSnapshot, Is.EqualTo(122));
        }

        #region Request handling

        [Test]
        public void Process_RejectsRequestsThatDidNotComeFromThisMachine()
        {
            var response = Send("{\"requestId\":\"1\"}", Remote);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?.Value<string>("code"), Is.EqualTo("forbidden"));
        }

        [Test]
        public void Process_RejectsARequestThatIsNotAJsonObjectWithARequestId()
        {
            var response = Send("not json", Loopback);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?.Value<string>("code"), Is.EqualTo("invalid_request"));
        }

        [Test]
        public void Process_RefusesToDescribeAScreenThatDoesNotExistYet()
        {
            // Edit Mode tests run outside Play Mode, which is the very state being asserted here.
            var response = Send("{\"requestId\":\"1\"}", Loopback);

            Assert.That(response.Value<bool>("ok"), Is.False);
            Assert.That(response["error"]?.Value<string>("code"), Is.EqualTo("not_playing"));
        }

        #endregion

        #region Projection

        [Test]
        public void TryProjectBounds_MapsAnOrthographicBoxToExactPixels()
        {
            // Half height 5 over 400 pixels is 40 pixels per world unit, so the 2 unit cube is 80 wide.
            var projected = VisualSnapshotAutomation.TryProjectBounds(
                new Bounds(new Vector3(0f, 0f, 10f), Vector3.one * 2f), LookingForward,
                Matrix4x4.Ortho(-10f, 10f, -5f, 5f, 0.3f, 1000f), Viewport, Viewport.height, 0.3f,
                out var rect, out var eyeDepthNear, out var eyeDepthFar);

            Assert.That(projected, Is.True);
            Assert.That(rect.xMin, Is.EqualTo(360f).Within(0.01f));
            Assert.That(rect.yMin, Is.EqualTo(160f).Within(0.01f));
            Assert.That(rect.width, Is.EqualTo(80f).Within(0.01f));
            Assert.That(rect.height, Is.EqualTo(80f).Within(0.01f));
            Assert.That(eyeDepthNear, Is.EqualTo(9f).Within(0.001f));
            Assert.That(eyeDepthFar, Is.EqualTo(11f).Within(0.001f));
        }

        [Test]
        public void TryProjectBounds_PutsTheOriginInTheTopLeftCorner()
        {
            // The box sits above the camera, so in a top left origin it must land above the middle.
            var projected = VisualSnapshotAutomation.TryProjectBounds(
                new Bounds(new Vector3(0f, 3f, 10f), Vector3.one * 2f), LookingForward,
                Matrix4x4.Ortho(-10f, 10f, -5f, 5f, 0.3f, 1000f), Viewport, Viewport.height, 0.3f,
                out var rect, out _, out _);

            Assert.That(projected, Is.True);
            Assert.That(rect.yMin, Is.EqualTo(40f).Within(0.01f));
            Assert.That(rect.yMax, Is.EqualTo(120f).Within(0.01f));
            Assert.That(rect.yMax, Is.LessThan(Viewport.height * 0.5f));
        }

        [Test]
        public void TryProjectBounds_CentersAPerspectiveBoxThatSitsOnTheOpticalAxis()
        {
            var projected = VisualSnapshotAutomation.TryProjectBounds(
                new Bounds(new Vector3(0f, 0f, 10f), Vector3.one * 2f), LookingForward,
                Matrix4x4.Perspective(60f, 2f, 0.3f, 1000f), Viewport, Viewport.height, 0.3f,
                out var rect, out var eyeDepthNear, out var eyeDepthFar);

            Assert.That(projected, Is.True);
            Assert.That(rect.center.x, Is.EqualTo(400f).Within(0.01f));
            Assert.That(rect.center.y, Is.EqualTo(200f).Within(0.01f));

            // The nearest face decides the size of the box on screen, and a cube stays square when the
            // aspect of the projection matches the aspect of the viewport.
            Assert.That(rect.width, Is.EqualTo(76.98f).Within(0.01f));
            Assert.That(rect.height, Is.EqualTo(76.98f).Within(0.01f));
            Assert.That(eyeDepthNear, Is.EqualTo(9f).Within(0.001f));
            Assert.That(eyeDepthFar, Is.EqualTo(11f).Within(0.001f));
        }

        [Test]
        public void TryProjectBounds_ClipsABoxThatStraddlesTheNearPlane()
        {
            // Half of this box is behind the camera, where a perspective divide would mirror it onto
            // the screen instead of dropping it.
            var projected = VisualSnapshotAutomation.TryProjectBounds(
                new Bounds(Vector3.zero, Vector3.one * 2f), LookingForward,
                Matrix4x4.Perspective(60f, 2f, 0.3f, 1000f), Viewport, Viewport.height, 0.3f,
                out var rect, out var eyeDepthNear, out var eyeDepthFar);

            Assert.That(projected, Is.True);
            Assert.That(eyeDepthNear, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(eyeDepthFar, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(float.IsNaN(rect.width) || float.IsInfinity(rect.width), Is.False);
            Assert.That(rect.center.x, Is.EqualTo(400f).Within(0.01f));
        }

        [Test]
        public void TryProjectBounds_ReportsNothingForABoxBehindTheCamera()
        {
            var projected = VisualSnapshotAutomation.TryProjectBounds(
                new Bounds(new Vector3(0f, 0f, -10f), Vector3.one * 2f), LookingForward,
                Matrix4x4.Perspective(60f, 2f, 0.3f, 1000f), Viewport, Viewport.height, 0.3f,
                out _, out _, out _);

            Assert.That(projected, Is.False);
        }

        #endregion

        #region Yaml building

        [Test]
        public void BuildSnapshotYaml_GivesBoundsToVisibleNodesAndNoneToTheirAncestors()
        {
            var ground = Visible("Ground", 101, new RectInt(-40, 700, 2000, 400), 648, 702, "MeshRenderer");
            ground.Clipped = true;
            var level = Ancestor("Level", 100);
            level.Children.Add(ground);

            var yaml = VisualSnapshotAutomation.BuildSnapshotYaml(
                Header(1920, 1080, "Main \"Camera\"", -14562, false, 0f, 1039.2305f, 1, 0, false),
                new List<VisualSnapshotAutomation.VisualNode> { level });

            Assert.That(yaml, Is.EqualTo(
                "screen: [1920,1080]\n" +
                "camera: \"Main \\\"Camera\\\"\" [id=\"ffffc71e\"] [projection=Perspective] [depth=0] " +
                "[pxPerUnit=1039.23]\n" +
                "count: 1\n" +
                "gameObjects:\n" +
                "  - \"Level\" [id=\"64\"]:\n" +
                "    - \"Ground\" [id=\"65\"] [rect=[-40,700,2000,400]] [z=[648,702]] [renderer=MeshRenderer]" +
                " [clipped=true]"));
        }

        [Test]
        public void BuildSnapshotYaml_WritesTheChildrenOfANodeThatIsItselfVisible()
        {
            var weapon = Visible("Weapon", 1235, new RectInt(960, 470, 60, 40), 538, 545, "SpriteRenderer");
            weapon.HasSorting = true;
            var player = Visible("Player", 1234, new RectInt(820, 410, 180, 240), 540, 594, "SpriteRenderer");
            player.HasSorting = true;
            player.Children.Add(weapon);

            var yaml = VisualSnapshotAutomation.BuildSnapshotYaml(
                Header(1920, 1080, "Main Camera", 7, true, 0f, 108f, 2, 0, false),
                new List<VisualSnapshotAutomation.VisualNode> { player });

            Assert.That(yaml, Is.EqualTo(
                "screen: [1920,1080]\n" +
                "camera: \"Main Camera\" [id=\"7\"] [projection=Orthographic] [depth=0] [pxPerUnit=108]\n" +
                "count: 2\n" +
                "gameObjects:\n" +
                "  - \"Player\" [id=\"4d2\"] [rect=[820,410,180,240]] [z=[540,594]] [renderer=SpriteRenderer]" +
                " [sortingLayer=\"Characters\"] [sortingOrder=3]:\n" +
                "    - \"Weapon\" [id=\"4d3\"] [rect=[960,470,60,40]] [z=[538,545]] [renderer=SpriteRenderer]" +
                " [sortingLayer=\"Characters\"] [sortingOrder=3]"));
        }

        [Test]
        public void BuildSnapshotYaml_CountsWhatTheLimitLeftOutAndSaysWhenNothingIsVisible()
        {
            var yaml = VisualSnapshotAutomation.BuildSnapshotYaml(
                Header(800, 600, "Cam", 1, true, -1f, 60f, 250, 50, false),
                new List<VisualSnapshotAutomation.VisualNode>());

            Assert.That(yaml, Is.EqualTo(
                "screen: [800,600]\n" +
                "camera: \"Cam\" [id=\"1\"] [projection=Orthographic] [depth=-1] [pxPerUnit=60]\n" +
                "count: 250\n" +
                "omitted: 50\n" +
                "gameObjects: []"));
        }

        [Test]
        public void BuildSnapshotYaml_NamesTheSceneOnRootsOnlyWhenSeveralAreOpen()
        {
            var main = Visible("Prop", 1, new RectInt(0, 0, 10, 10), 5, 6, "MeshRenderer");
            main.Scene = "Assets/Scenes/Main.unity";
            var additive = Visible("Extra", 2, new RectInt(20, 20, 10, 10), 7, 8, "MeshRenderer");
            additive.Scene = "Assets/Scenes/Additive.unity";
            var child = Visible("Detail", 3, new RectInt(21, 21, 4, 4), 7, 8, "MeshRenderer");
            child.Scene = "Assets/Scenes/Additive.unity";
            additive.Children.Add(child);

            var yaml = VisualSnapshotAutomation.BuildSnapshotYaml(
                Header(800, 600, "Cam", 1, true, 0f, 60f, 3, 0, true),
                new List<VisualSnapshotAutomation.VisualNode> { main, additive });

            Assert.That(yaml, Is.EqualTo(
                "screen: [800,600]\n" +
                "camera: \"Cam\" [id=\"1\"] [projection=Orthographic] [depth=0] [pxPerUnit=60]\n" +
                "count: 3\n" +
                "gameObjects:\n" +
                "  - \"Prop\" [id=\"1\"] [scene=\"Assets/Scenes/Main.unity\"] [rect=[0,0,10,10]] [z=[5,6]]" +
                " [renderer=MeshRenderer]\n" +
                "  - \"Extra\" [id=\"2\"] [scene=\"Assets/Scenes/Additive.unity\"] [rect=[20,20,10,10]]" +
                " [z=[7,8]] [renderer=MeshRenderer]:\n" +
                "    - \"Detail\" [id=\"3\"] [rect=[21,21,4,4]] [z=[7,8]] [renderer=MeshRenderer]"));
        }

        [Test]
        public void BuildSnapshotYaml_ReportsHowManyRenderersAnObjectHasOnlyWhenItHasSeveral()
        {
            var node = Visible("Combo", 9, new RectInt(1, 2, 3, 4), 10, 11, "MeshRenderer");
            node.RendererCount = 3;

            var yaml = VisualSnapshotAutomation.BuildSnapshotYaml(
                Header(800, 600, "Cam", 1, true, 0f, 60f, 1, 0, false),
                new List<VisualSnapshotAutomation.VisualNode> { node });

            Assert.That(yaml, Does.EndWith(
                "  - \"Combo\" [id=\"9\"] [rect=[1,2,3,4]] [z=[10,11]] [renderer=MeshRenderer]" +
                " [rendererCount=3]"));
        }

        #endregion

        #region Helpers

        private static VisualSnapshotAutomation.VisualSnapshotHeader Header(int screenWidth, int screenHeight,
            string cameraName, int cameraId, bool isOrthographic, float cameraDepth, float pixelsPerUnit,
            int totalCount, int omitted, bool multipleScenes)
        {
            return new VisualSnapshotAutomation.VisualSnapshotHeader(screenWidth, screenHeight, cameraName,
                unchecked((uint)cameraId).ToString("x", CultureInfo.InvariantCulture), isOrthographic,
                cameraDepth, pixelsPerUnit, totalCount, omitted, multipleScenes);
        }

        private static VisualSnapshotAutomation.VisualNode Visible(string name, int id, RectInt rect,
            int zNear, int zFar, string rendererType)
        {
            return new VisualSnapshotAutomation.VisualNode(name,
                unchecked((uint)id).ToString("x", CultureInfo.InvariantCulture))
            {
                IsVisible = true,
                Rect = rect,
                ZNear = zNear,
                ZFar = zFar,
                RendererType = rendererType,
                RendererCount = 1,
                SortingLayerName = "Characters",
                SortingOrder = 3
            };
        }

        private static VisualSnapshotAutomation.VisualNode Ancestor(string name, int id)
        {
            return new VisualSnapshotAutomation.VisualNode(name,
                unchecked((uint)id).ToString("x", CultureInfo.InvariantCulture));
        }

        private static JObject Send(string value, IPEndPoint origin)
        {
            string payload = null;
            var message = new Message
            {
                Type = MessageType.GameObjectVisualSnapshot, Value = value, Origin = origin
            };

            VisualSnapshotAutomation.Process(message, (endPoint, responseType, responseValue) =>
            {
                Assert.That(endPoint, Is.EqualTo(origin));
                Assert.That(responseType, Is.EqualTo(MessageType.GameObjectVisualSnapshot));
                payload = responseValue;
            });

            Assert.That(payload, Is.Not.Null, "The handler did not answer the request.");
            return JObject.Parse(payload);
        }

        #endregion
    }
}
