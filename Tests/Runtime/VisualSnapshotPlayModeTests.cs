using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Hackerzhuli.Code.PlayModeTests
{
    /// <summary>
    ///     Drives the visual snapshot handler against a scene built at runtime, which is the only way to
    ///     exercise the parts of it that need a real camera and a real Game View.
    /// </summary>
    [TestFixture]
    public class VisualSnapshotPlayModeTests
    {
        private const string CameraName = "Visual Snapshot Camera";

        /// <summary>
        ///     The layer the camera is told not to draw, taken from the user range so no project setting
        ///     can make it mean something else.
        /// </summary>
        private const int MaskedLayer = 31;

        private static readonly List<string> Responses = new();

        [UnityTest]
        public IEnumerator VisualSnapshot_ReportsVisibleObjectsWithTheirAncestors()
        {
            Responses.Clear();
            var created = new List<Object>();
            try
            {
                var camera = NewObject(created, CameraName).AddComponent<Camera>();
                camera.transform.position = Vector3.zero;
                camera.transform.rotation = Quaternion.identity;
                camera.orthographic = false;
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 1000f;
                camera.cullingMask = ~(1 << MaskedLayer);

                // A visible object two levels down, so the answer has to carry an ancestor that is not
                // itself visible.
                var parent = NewObject(created, "Snapshot Parent");
                Cube("Snapshot Child", new Vector3(0f, 0f, 10f)).transform.SetParent(parent.transform, true);

                // Straight above the first cube, which is what pins down the direction of the y axis.
                created.Add(Cube("Snapshot Above", new Vector3(0f, 2f, 10f)));

                var sprite = NewObject(created, "Snapshot Sprite");
                sprite.transform.position = new Vector3(2f, 0f, 10f);
                var texture = new Texture2D(4, 4);
                created.Add(texture);
                var spriteAsset = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
                created.Add(spriteAsset);
                var spriteRenderer = sprite.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = spriteAsset;
                spriteRenderer.sortingOrder = 5;

                created.Add(Cube("Snapshot Behind", new Vector3(0f, 0f, -10f)));
                var masked = Cube("Snapshot Masked", new Vector3(0f, 0f, 10f));
                masked.layer = MaskedLayer;
                created.Add(masked);

                // A LineRenderer stands in for everything that is decoration rather than an object. Its
                // two points differ in both x and y so the line covers real screen area.
                var line = NewObject(created, "Snapshot Decoration").AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.SetPosition(0, new Vector3(-2f, -1f, 10f));
                line.SetPosition(1, new Vector3(-1f, 1f, 10f));

                // A real Renderer parented under a Canvas belongs to the UI, not to the scene. A world
                // space canvas keeps the cube in front of the camera, so only the guard can drop it.
                var canvas = NewObject(created, "Snapshot Canvas").AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.transform.position = new Vector3(0f, 0f, 10f);
                Cube("Snapshot Canvas Child", new Vector3(-1f, 0f, 10f))
                    .transform.SetParent(canvas.transform, true);

                yield return null;
                yield return null;

                var automationType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "Hackerzhuli.Code.Editor.VisualSnapshotAutomation", false))
                    .FirstOrDefault(type => type != null);
                Assert.That(automationType, Is.Not.Null, "The Editor automation assembly was not loaded.");

                InvokeRequest(automationType, $"{{\"requestId\":\"core\",\"camera\":\"{CameraName}\"}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"), Responses[^1]);
                var snapshot = JsonUtility.FromJson<VisualSnapshotResponse>(Responses[^1]).visualSnapshot;
                Assert.That(snapshot, Is.Not.Null.And.Not.Empty);

                Assert.That(snapshot, Does.StartWith("screen: ["));
                Assert.That(snapshot, Does.Contain($"camera: \"{CameraName}\""));
                Assert.That(snapshot, Does.Contain("[projection=Perspective]"));

                // The ancestor is structure: it is in the tree, above its child, and carries no bounds.
                var lines = snapshot.Split('\n');
                var parentLine = Line(lines, "\"Snapshot Parent\" [id=", snapshot);
                var childLine = Line(lines, "\"Snapshot Child\" [id=", snapshot);
                Assert.That(lines[parentLine], Does.Not.Contain("[rect="));
                Assert.That(lines[parentLine].TrimEnd(), Does.EndWith(":"));
                Assert.That(childLine, Is.GreaterThan(parentLine));
                Assert.That(lines[childLine], Does.Contain("[rect="));
                Assert.That(lines[childLine], Does.Contain("[renderer=MeshRenderer]"));
                Assert.That(Indent(lines[childLine]), Is.GreaterThan(Indent(lines[parentLine])));

                // The coordinate convention, checked against a real camera rather than a matrix: the cube
                // on the optical axis lands in the middle of the screen, the one above it lands higher up,
                // which in a top left origin means a smaller y, and the sprite at +x lands to the right.
                var child = ParseRect(lines[childLine]);
                var above = ParseRect(lines[Line(lines, "\"Snapshot Above\" [id=", snapshot)]);
                Assert.That(child.center.x, Is.EqualTo(Screen.width * 0.5f).Within(2f));
                Assert.That(child.center.y, Is.EqualTo(Screen.height * 0.5f).Within(2f));
                Assert.That(above.center.y, Is.LessThan(child.center.y),
                    "An object above the camera axis must have a smaller y in a top left origin.");
                Assert.That(above.center.x, Is.EqualTo(child.center.x).Within(2f));

                // A sprite always says how it is sorted, because that, not depth, decides what it hides.
                var spriteLine = lines[Line(lines, "\"Snapshot Sprite\" [id=", snapshot)];
                Assert.That(ParseRect(spriteLine).center.x, Is.GreaterThan(child.center.x),
                    "An object at +x must land to the right of one on the axis.");
                Assert.That(spriteLine, Does.Contain("[renderer=SpriteRenderer]"));
                Assert.That(spriteLine, Does.Contain("[sortingLayer=\"Default\"]"));
                Assert.That(spriteLine, Does.Contain("[sortingOrder=5]"));

                Assert.That(snapshot, Does.Not.Contain("\"Snapshot Behind\""));
                Assert.That(snapshot, Does.Not.Contain("\"Snapshot Masked\""));
                Assert.That(snapshot, Does.Not.Contain("\"Snapshot Decoration\""));
                Assert.That(snapshot, Does.Not.Contain("\"Snapshot Canvas Child\""));

                // The same scene, asked for without the filter, gives the decoration back.
                InvokeRequest(automationType,
                    $"{{\"requestId\":\"all\",\"camera\":\"{CameraName}\",\"renderers\":\"all\"}}");
                Assert.That(Responses[^1], Does.Contain("\"ok\":true"), Responses[^1]);
                var everything = JsonUtility.FromJson<VisualSnapshotResponse>(Responses[^1]).visualSnapshot;
                Assert.That(everything, Does.Contain("\"Snapshot Decoration\""));
                Assert.That(everything, Does.Contain("[renderer=LineRenderer]"));

                // uGUI stays out either way, because the Canvas guard is not part of the whitelist.
                Assert.That(everything, Does.Not.Contain("\"Snapshot Canvas Child\""));

                InvokeRequest(automationType,
                    $"{{\"requestId\":\"bogus\",\"camera\":\"{CameraName}\",\"renderers\":\"bogus\"}}");
                Assert.That(Responses[^1], Does.Contain("\"code\":\"invalid_request\""));

                InvokeRequest(automationType, "{\"requestId\":\"missing\",\"camera\":\"No Such Camera\"}");
                Assert.That(Responses[^1], Does.Contain("\"code\":\"not_found\""));
            }
            finally
            {
                foreach (var item in created)
                    if (item != null)
                        Object.Destroy(item);
            }
        }

        #region Helpers

        /// <summary>
        ///     Creates a GameObject that the test will destroy again.
        /// </summary>
        private static GameObject NewObject(ICollection<Object> created, string name)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            return gameObject;
        }

        /// <summary>
        ///     Creates a cube at a world position. Destroying its root destroys it too.
        /// </summary>
        private static GameObject Cube(string name, Vector3 position)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            gameObject.transform.position = position;
            return gameObject;
        }

        /// <summary>
        ///     Finds the line describing one object, failing with the whole document when it is not there,
        ///     because that is the only useful thing to look at when this breaks.
        /// </summary>
        private static int Line(IReadOnlyList<string> lines, string needle, string document)
        {
            for (var index = 0; index < lines.Count; index++)
                if (lines[index].Contains(needle, StringComparison.Ordinal))
                    return index;

            Assert.Fail($"No line contains '{needle}' in:\n{document}");
            return -1;
        }

        private static int Indent(string line)
        {
            return line.Length - line.TrimStart(' ').Length;
        }

        /// <summary>
        ///     Reads the screen rectangle back out of the line describing an object.
        /// </summary>
        private static Rect ParseRect(string line)
        {
            var match = Regex.Match(line, @"\[rect=\[(-?\d+),(-?\d+),(-?\d+),(-?\d+)\]\]");
            Assert.That(match.Success, Is.True, $"No rect in '{line}'.");
            return new Rect(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture));
        }

        private static void InvokeRequest(Type automationType, string json)
        {
            var process = automationType.GetMethod("Process", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(process, Is.Not.Null, "VisualSnapshotAutomation.Process was not found.");

            var parameters = process.GetParameters();
            var messageType = parameters[0].ParameterType;
            var message = Activator.CreateInstance(messageType);
            var protocolEnum = messageType.GetProperty("Type").PropertyType;
            messageType.GetProperty("Type")
                .SetValue(message, Enum.Parse(protocolEnum, "GameObjectVisualSnapshot"));
            messageType.GetProperty("Value").SetValue(message, json);
            messageType.GetProperty("Origin").SetValue(message, new IPEndPoint(IPAddress.Loopback, 22000));

            var delegateType = parameters[1].ParameterType;
            var invoke = delegateType.GetMethod("Invoke");
            var lambdaParameters = invoke.GetParameters()
                .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                .ToArray();
            var capture = typeof(VisualSnapshotPlayModeTests).GetMethod(
                nameof(CaptureResponse), BindingFlags.Static | BindingFlags.NonPublic);
            var body = Expression.Call(capture, Expression.Convert(lambdaParameters[2], typeof(string)));
            var callback = Expression.Lambda(delegateType, body, lambdaParameters).Compile();

            process.Invoke(null, new[] { message, callback });
        }

        private static void CaptureResponse(string response)
        {
            Responses.Add(response);
        }

        [Serializable]
        private sealed class VisualSnapshotResponse
        {
            public bool ok;
            public string visualSnapshot;
        }

        #endregion
    }
}
