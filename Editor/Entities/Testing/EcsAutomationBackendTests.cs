using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;

namespace Hackerzhuli.Code.Editor.Entities.Testing
{
    internal struct TestPosition : IComponentData { public int X; public Nested Value; }
    internal struct Nested { public int Number; }
    internal struct TestTag : IComponentData { }
    internal struct TestEnableable : IComponentData, IEnableableComponent { public int Value; }
    internal struct TestShared : ISharedComponentData { public int Value; }
    internal struct TestBuffer : IBufferElementData { public int Value; }
    internal struct TestReference : IComponentData { public Entity Target; }
    internal sealed class TestManaged : IComponentData { public string Text; }

    [DisableAutoCreation]
    internal sealed partial class TestGroup : ComponentSystemGroup { }

    [DisableAutoCreation]
    internal sealed partial class TestManagedSystem : SystemBase
    {
        protected override void OnCreate() { GetEntityQuery(ComponentType.ReadOnly<TestPosition>()); }
        protected override void OnUpdate() { }
    }

    [DisableAutoCreation]
    internal partial struct TestUnmanagedSystem : ISystem
    {
        public void OnCreate(ref SystemState state) { }
        public void OnUpdate(ref SystemState state) { }
        public void OnDestroy(ref SystemState state) { }
    }

    internal class EcsAutomationBackendTests
    {
        private World _world;
        private Entity _entity;
        private Entity _target;

        [SetUp]
        public void SetUp()
        {
            _world = new World("EcsAutomationTests", WorldFlags.Game);
            var manager = _world.EntityManager;
            _target = manager.CreateEntity(typeof(TestTag));
            manager.SetName(_target, "Target");
            _entity = manager.CreateEntity(typeof(TestPosition), typeof(TestTag), typeof(TestEnableable),
                typeof(TestShared), typeof(TestReference));
            manager.SetName(_entity, "Hero Entity");
            manager.SetComponentData(_entity, new TestPosition { X = 7, Value = new Nested { Number = 8 } });
            manager.SetComponentData(_entity, new TestEnableable { Value = 9 });
            manager.SetSharedComponent(_entity, new TestShared { Value = 10 });
            manager.SetComponentData(_entity, new TestReference { Target = _target });
            manager.AddComponentData(_entity, new TestManaged { Text = "managed" });
            var buffer = manager.AddBuffer<TestBuffer>(_entity);
            for (var i = 0; i < 25; i++) buffer.Add(new TestBuffer { Value = i });

            var group = _world.CreateSystemManaged<TestGroup>();
            var system = _world.CreateSystemManaged<TestManagedSystem>();
            group.AddSystemToUpdateList(system);
            _world.CreateSystem<TestUnmanagedSystem>();
        }

        [TearDown]
        public void TearDown() => _world?.Dispose();

        [Test]
        public void WorldAndSystems_ReportTemporaryWorldAndQueries()
        {
            var worlds = Run(MessageType.EcsWorldList, "{\"requestId\":1}");
            Assert.That(worlds.Value, Does.Contain("name: \"EcsAutomationTests\""));
            Assert.That(worlds.Value, Does.Contain("entityCount: 2"));

            var systems = Run(MessageType.EcsSystemList,
                "{\"requestId\":1,\"world\":\"EcsAutomationTests\"}");
            Assert.That(systems.Value, Does.Contain("name: \"TestGroup\""));
            Assert.That(systems.Value, Does.Contain("name: \"TestManagedSystem\""));
            Assert.That(systems.Value, Does.Contain("kind: unmanaged"));

            var systemId = ReadIdBeforeName(systems.Value, "TestManagedSystem");
            var inspect = Run(MessageType.EcsSystemInspect,
                new JObject { ["requestId"] = 2, ["system"] = systemId }.ToString());
            Assert.That(inspect.Ok, Is.True);
            Assert.That(inspect.Value, Does.Contain("queriesAvailable: true"));
            Assert.That(inspect.Value, Does.Contain(typeof(TestPosition).FullName));
        }

        [Test]
        public void QueryAndInspect_ReadAllSupportedComponentShapesWithoutMutation()
        {
            var manager = _world.EntityManager;
            var orderVersion = manager.EntityOrderVersion;
            var before = manager.GetComponentData<TestPosition>(_entity);
            var query = Run(MessageType.EcsEntityQuery,
                "{\"requestId\":1,\"world\":\"EcsAutomationTests\",\"all\":[\"TestPosition\",\"TestTag\"],\"none\":[\"Disabled\"],\"name\":\"Hero\",\"match\":\"contains\"}");
            Assert.That(query.Ok, Is.True);
            Assert.That(query.Value, Does.Contain("name: \"Hero Entity\""));
            var id = ReadFirstId(query.Value);
            var inspect = Run(MessageType.EcsEntityInspect,
                new JObject { ["requestId"] = 2, ["entity"] = id }.ToString());
            Assert.That(inspect.Value, Does.Contain(typeof(TestPosition).FullName));
            Assert.That(inspect.Value, Does.Contain("kind: buffer"));
            Assert.That(inspect.Value, Does.Contain("\"omitted\":5"));
            Assert.That(inspect.Value, Does.Contain("managed"));
            Assert.That(manager.EntityOrderVersion, Is.EqualTo(orderVersion));
            Assert.That(manager.GetComponentData<TestPosition>(_entity).X, Is.EqualTo(before.X));
        }

        [Test]
        public void StaleEntityId_IsRejectedAfterEntityIsDestroyed()
        {
            var query = Run(MessageType.EcsEntityQuery,
                "{\"requestId\":1,\"world\":\"EcsAutomationTests\",\"name\":\"Target\",\"match\":\"exact\"}");
            var id = ReadFirstId(query.Value);
            _world.EntityManager.DestroyEntity(_target);
            var inspect = Run(MessageType.EcsEntityInspect,
                new JObject { ["requestId"] = 2, ["entity"] = id }.ToString());
            Assert.That(inspect.Ok, Is.False);
            Assert.That(inspect.ErrorCode, Is.EqualTo("stale_entity"));
        }

        [Test]
        public void DisabledAndPrefabEntities_AreOptIn()
        {
            var manager = _world.EntityManager;
            var disabled = manager.CreateEntity(typeof(TestPosition), typeof(Disabled));
            var prefab = manager.CreateEntity(typeof(TestPosition), typeof(Prefab));
            manager.SetName(disabled, "Disabled Position");
            manager.SetName(prefab, "Prefab Position");

            var normal = Run(MessageType.EcsEntityQuery,
                "{\"requestId\":1,\"world\":\"EcsAutomationTests\",\"all\":[\"TestPosition\"]}");
            Assert.That(normal.Value, Does.Contain("count: 1"));
            var included = Run(MessageType.EcsEntityQuery,
                "{\"requestId\":2,\"world\":\"EcsAutomationTests\",\"all\":[\"TestPosition\"],\"includeDisabled\":true,\"includePrefabs\":true}");
            Assert.That(included.Value, Does.Contain("count: 3"));
        }

        [Test]
        public void VisualSnapshot_ReportsVisibleChildAndStructuralParent()
        {
            var cameraObject = new GameObject("ECS Test Camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.pixelRect = new Rect(0, 0, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
                camera.cullingMask = 1;
                var manager = _world.EntityManager;
                var root = manager.CreateEntity();
                manager.SetName(root, "Visual Root");
                var child = CreateRenderEntity(manager, "Visual Child", new float3(0, 0, 10), 0);
                manager.AddComponentData(child, new Parent { Value = root });
                CreateRenderEntity(manager, "Wrong Layer", new float3(0, 0, 10), 2);
                CreateRenderEntity(manager, "Off Screen", new float3(10000, 0, 10), 0);
                var zeroBounds = CreateRenderEntity(manager, "Zero Bounds", new float3(0, 0, 10), 0);
                manager.SetComponentData(zeroBounds, new WorldRenderBounds { Value = default });
                var disabled = CreateRenderEntity(manager, "Disabled Render", new float3(0, 0, 10), 0);
                manager.AddComponent<Disabled>(disabled);
                var prefab = CreateRenderEntity(manager, "Prefab Render", new float3(0, 0, 10), 0);
                manager.AddComponent<Prefab>(prefab);
                var hidden = CreateRenderEntity(manager, "Rendering Disabled", new float3(0, 0, 10), 0);
                manager.AddComponent<DisableRendering>(hidden);
                var meshDisabled = CreateRenderEntity(manager, "Mesh Disabled", new float3(0, 0, 10), 0);
                manager.SetComponentEnabled<MaterialMeshInfo>(meshDisabled, false);
                var shadowsOnly = CreateRenderEntity(manager, "Shadows Only", new float3(0, 0, 10), 0);
                var shadowSettings = RenderFilterSettings.Default;
                shadowSettings.ShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                manager.SetSharedComponent(shadowsOnly, shadowSettings);

                var result = Run(MessageType.EcsVisualSnapshot,
                    "{\"requestId\":1,\"world\":\"EcsAutomationTests\",\"camera\":\"ECS Test Camera\"}");
                Assert.That(result.Ok, Is.True, result.ErrorMessage);
                Assert.That(result.Value, Does.Contain("world: \"EcsAutomationTests\""));
                Assert.That(result.Value, Does.Contain("- \"Visual Root\" [id="));
                Assert.That(result.Value, Does.Contain("- \"Visual Child\" [id="));
                Assert.That(result.Value, Does.Contain("[rect=["));
                Assert.That(result.Value, Does.Not.Match("\\\"Visual Root\\\"[^\\n]*\\[rect="));
                Assert.That(result.Value, Does.Match("\\\"Visual Child\\\"[^\\n]*\\[rect="));
                Assert.That(result.Value, Does.Not.Contain("Wrong Layer"));
                Assert.That(result.Value, Does.Not.Contain("Off Screen"));
                Assert.That(result.Value, Does.Not.Contain("Zero Bounds"));
                Assert.That(result.Value, Does.Not.Contain("Disabled Render"));
                Assert.That(result.Value, Does.Not.Contain("Prefab Render"));
                Assert.That(result.Value, Does.Not.Contain("Rendering Disabled"));
                Assert.That(result.Value, Does.Not.Contain("Mesh Disabled"));
                Assert.That(result.Value, Does.Not.Contain("Shadows Only"));
            }
            finally { Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void VisualSnapshot_LimitsVisibleEntitiesAndReportsOmitted()
        {
            var cameraObject = new GameObject("ECS Limit Camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.pixelRect = new Rect(0, 0, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
                var manager = _world.EntityManager;
                for (var index = 0; index < 201; index++)
                    CreateRenderEntity(manager, "Limited " + index, new float3(0, 0, 10 + index * 0.001f), 0);
                var result = Run(MessageType.EcsVisualSnapshot,
                    "{\"requestId\":1,\"world\":\"EcsAutomationTests\",\"camera\":\"ECS Limit Camera\"}");
                Assert.That(result.Ok, Is.True, result.ErrorMessage);
                Assert.That(result.Value, Does.Contain("count: 201"));
                Assert.That(result.Value, Does.Contain("omitted: 1"));
            }
            finally { Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void VisualSnapshot_ValidatesCameraAndRequestFields()
        {
            var unknownCamera = Run(MessageType.EcsVisualSnapshot,
                "{\"requestId\":1,\"world\":\"EcsAutomationTests\",\"camera\":\"Missing ECS Camera\"}");
            Assert.That(unknownCamera.Ok, Is.False);
            Assert.That(unknownCamera.ErrorCode, Is.EqualTo("no_camera").Or.EqualTo("not_found"));
            var unknownField = Run(MessageType.EcsVisualSnapshot,
                "{\"requestId\":1,\"world\":\"EcsAutomationTests\",\"limit\":1}");
            Assert.That(unknownField.Ok, Is.False);
            Assert.That(unknownField.ErrorCode, Is.EqualTo("invalid_request"));
        }

        [Test]
        public void VisualSnapshot_BrokenAndCyclicParentsBecomeFiniteRoots()
        {
            var cameraObject = new GameObject("ECS Parent Camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.pixelRect = new Rect(0, 0, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
                var manager = _world.EntityManager;
                var expiredParent = manager.CreateEntity();
                var broken = CreateRenderEntity(manager, "Broken Parent Child", new float3(-1, 0, 10), 0);
                manager.AddComponentData(broken, new Parent { Value = expiredParent });
                manager.DestroyEntity(expiredParent);

                var cycleVisible = CreateRenderEntity(manager, "Cycle Visible", new float3(1, 0, 10), 0);
                var cycleParent = manager.CreateEntity(typeof(Parent));
                manager.SetName(cycleParent, "Cycle Parent");
                manager.SetComponentData(cycleParent, new Parent { Value = cycleVisible });
                manager.AddComponentData(cycleVisible, new Parent { Value = cycleParent });

                var result = Run(MessageType.EcsVisualSnapshot,
                    "{\"requestId\":1,\"world\":\"EcsAutomationTests\",\"camera\":\"ECS Parent Camera\"}");
                Assert.That(result.Ok, Is.True, result.ErrorMessage);
                Assert.That(result.Value, Does.Contain("Broken Parent Child"));
                Assert.That(result.Value, Does.Contain("Cycle Visible"));
                Assert.That(result.Value, Does.Contain("Cycle Parent"));
                Assert.That(result.Value.Length, Is.LessThan(5000));
            }
            finally { Object.DestroyImmediate(cameraObject); }
        }

        private static Entity CreateRenderEntity(EntityManager manager, string name, float3 center, int layer)
        {
            var entity = manager.CreateEntity(typeof(WorldRenderBounds), typeof(LocalToWorld),
                typeof(MaterialMeshInfo), typeof(RenderFilterSettings));
            manager.SetName(entity, name);
            manager.SetComponentData(entity, new WorldRenderBounds
            {
                Value = new AABB { Center = center, Extents = new float3(0.5f) }
            });
            manager.SetComponentData(entity, new LocalToWorld { Value = float4x4.Translate(center) });
            var settings = RenderFilterSettings.Default;
            settings.Layer = layer;
            manager.SetSharedComponent(entity, settings);
            return entity;
        }

        private static EcsAutomation.Result Run(MessageType type, string json) =>
            EcsAutomationBackend.Process(type, JObject.Parse(json));

        private static string ReadFirstId(string yaml)
        {
            const string marker = "  - id: \"";
            var start = yaml.IndexOf(marker) + marker.Length;
            return yaml.Substring(start, yaml.IndexOf('"', start) - start);
        }

        private static string ReadIdBeforeName(string yaml, string name)
        {
            var nameIndex = yaml.IndexOf("name: \"" + name + "\"");
            var idIndex = yaml.LastIndexOf("id: \"", nameIndex) + 5;
            return yaml.Substring(idIndex, yaml.IndexOf('"', idIndex) - idIndex);
        }
    }
}
