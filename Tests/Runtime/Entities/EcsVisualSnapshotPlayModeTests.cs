using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Hackerzhuli.Code.Entities.PlayModeTests
{
    public class EcsVisualSnapshotPlayModeTests
    {
        [UnityTest]
        public IEnumerator Snapshot_ReportsARealRenderMeshUtilityEntity()
        {
            var previousDefault = World.DefaultGameObjectInjectionWorld;
            var world = new World("ECS Visual PlayMode World", WorldFlags.Game);
            var cameraObject = new GameObject("ECS Visual PlayMode Camera");
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material material = null;
            try
            {
                World.DefaultGameObjectInjectionWorld = world;
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 100f;

                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                var mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
                var manager = world.EntityManager;
                var parent = manager.CreateEntity();
                manager.SetName(parent, "ECS Render Parent");
                var entity = manager.CreateEntity();
                manager.SetName(entity, "ECS Render Child");
                var description = new RenderMeshDescription(ShadowCastingMode.On);
                var meshes = new RenderMeshArray(new[] { material }, new[] { mesh });
                RenderMeshUtility.AddComponents(entity, manager, description, meshes,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                manager.SetComponentData(entity, new LocalToWorld
                    { Value = float4x4.Translate(new float3(0, 0, 10)) });
                manager.SetComponentData(entity, new WorldRenderBounds
                {
                    Value = new AABB { Center = new float3(0, 0, 10), Extents = new float3(0.5f) }
                });
                manager.AddComponentData(entity, new Parent { Value = parent });

                yield return null;
                yield return null;

                var backend = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "Hackerzhuli.Code.Editor.Entities.EcsAutomationBackend", false))
                    .FirstOrDefault(type => type != null);
                Assert.That(backend, Is.Not.Null, "The optional Entities backend was not loaded.");
                var protocolType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("Hackerzhuli.Code.Editor.Messaging.MessageType", false))
                    .First(type => type != null);
                var process = backend.GetMethod("Process", BindingFlags.Static | BindingFlags.NonPublic);
                var request = JObject.Parse(
                    "{\"requestId\":\"play\",\"world\":\"ECS Visual PlayMode World\",\"camera\":\"ECS Visual PlayMode Camera\"}");
                var result = process.Invoke(null, new[] { Enum.Parse(protocolType, "EcsVisualSnapshot"), request });
                var resultType = result.GetType();
                Assert.That((bool)resultType.GetField("Ok", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(result), Is.True);
                var yaml = (string)resultType.GetField("Value", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(result);
                Assert.That(yaml, Does.Contain("\"ECS Render Parent\""));
                var parentLine = yaml.Split('\n').Single(line => line.Contains("\"ECS Render Parent\""));
                Assert.That(parentLine, Does.Not.Contain("[rect="));
                Assert.That(yaml, Does.Match("\\\"ECS Render Child\\\"[^\\n]*\\[rect="));
            }
            finally
            {
                if (ReferenceEquals(World.DefaultGameObjectInjectionWorld, world))
                    World.DefaultGameObjectInjectionWorld = previousDefault;
                if (world.IsCreated) world.Dispose();
                Object.Destroy(cameraObject);
                Object.Destroy(primitive);
                Object.Destroy(material);
            }
        }
    }
}
