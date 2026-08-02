using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Entities;
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
