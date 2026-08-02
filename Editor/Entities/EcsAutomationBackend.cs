using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Hackerzhuli.Code.Editor.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Rendering;
using Unity.Transforms;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;
using Object = UnityEngine.Object;

namespace Hackerzhuli.Code.Editor.Entities
{
    /// <summary>Read-only Unity Entities 6.5 implementation of the ECS automation protocol.</summary>
    [InitializeOnLoad]
    internal static class EcsAutomationBackend
    {
        private const int SystemLimit = 500;
        private const int DefaultEntityLimit = 100;
        private const int MaxEntityLimit = 500;
        private const int NameScanLimit = 10000;
        private const int MaxValueDepth = 4;
        private const int MaxFields = 64;
        private const int MaxItems = 20;
        private const int VisualEntityLimit = 200;

        static EcsAutomationBackend()
        {
            EcsAutomation.Register(Process);
        }

        internal static EcsAutomation.Result Process(MessageType type, JObject request)
        {
            return type switch
            {
                MessageType.EcsWorldList => WorldList(request),
                MessageType.EcsSystemList => SystemList(request),
                MessageType.EcsSystemInspect => SystemInspect(request),
                MessageType.EcsEntityQuery => EntityQuery(request),
                MessageType.EcsEntityInspect => EntityInspect(request),
                MessageType.EcsVisualSnapshot => VisualSnapshot(request),
                _ => EcsAutomation.Result.Error("invalid_request", "Unknown ECS message type.")
            };
        }

        #region Worlds and ids

        private static EcsAutomation.Result WorldList(JObject request)
        {
            if (!OnlyFields(request, out var fieldError, "requestId"))
                return Invalid(fieldError);

            var worlds = ValidWorlds();
            var yaml = new StringBuilder().Append("count: ").Append(worlds.Count).Append("\nworlds:");
            if (worlds.Count == 0)
                yaml.Append(" []");
            foreach (var world in worlds)
            {
                var manager = world.EntityManager;
                int entityCount = manager.UniversalQuery.CalculateEntityCount();
                int chunkCount;
                using (var chunks = manager.GetAllChunks(Allocator.TempJob))
                    chunkCount = chunks.Length;
                int allSystemCount;
                using (var systems = world.Unmanaged.GetAllSystems(Allocator.Temp))
                    allSystemCount = systems.Length;
                var managedCount = world.Systems.Count;
                var time = world.Time;
                yaml.Append("\n  - id: ").Append(Q(WorldId(world)))
                    .Append("\n    name: ").Append(Q(world.Name))
                    .Append("\n    flags: ").Append(Q(world.Flags.ToString()))
                    .Append("\n    isDefault: ").Append(Bool(ReferenceEquals(world, World.DefaultGameObjectInjectionWorld)))
                    .Append("\n    worldVersion: ").Append(world.Version)
                    .Append("\n    globalSystemVersion: ").Append(manager.GlobalSystemVersion)
                    .Append("\n    elapsedTime: ").Append(Number(time.ElapsedTime))
                    .Append("\n    deltaTime: ").Append(Number(time.DeltaTime))
                    .Append("\n    entityCount: ").Append(entityCount)
                    .Append("\n    chunkCount: ").Append(chunkCount)
                    .Append("\n    managedSystemCount: ").Append(managedCount)
                    .Append("\n    unmanagedSystemCount: ").Append(Math.Max(0, allSystemCount - managedCount));
            }
            return EcsAutomation.Result.Success(yaml.ToString());
        }

        private static bool TryWorld(JObject request, out World world, out EcsAutomation.Result error)
        {
            world = null;
            error = null;
            var token = request["world"];
            if (token == null || token.Type == JTokenType.Null ||
                token.Type == JTokenType.String && string.IsNullOrEmpty(token.Value<string>()))
            {
                world = World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated)
                {
                    error = EcsAutomation.Result.Error("world_not_found",
                        "DefaultGameObjectInjectionWorld is not available.");
                    return false;
                }
                return true;
            }

            if (token.Type != JTokenType.String)
            {
                error = Invalid("world must be an opaque world id or a world name.");
                return false;
            }

            var value = token.Value<string>();
            var valid = ValidWorlds();
            world = valid.FirstOrDefault(item => WorldId(item) == value);
            if (world != null)
                return true;
            var named = valid.Where(item => item.Name == value).ToList();
            if (named.Count == 1)
            {
                world = named[0];
                return true;
            }
            error = named.Count > 1
                ? EcsAutomation.Result.Error("ambiguous_world", $"More than one world is named '{value}'. Use its id.")
                : EcsAutomation.Result.Error("world_not_found", $"No valid world matches '{value}'.");
            return false;
        }

        private static string WorldId(World world) => Encode("w", world.SequenceNumber.ToString(CultureInfo.InvariantCulture));

        private static List<World> ValidWorlds()
        {
            var result = new List<World>();
            for (var index = 0; index < World.All.Count; index++)
            {
                var world = World.All[index];
                if (world != null && world.IsCreated) result.Add(world);
            }
            return result;
        }

        private static World FindWorld(ulong sequence)
        {
            for (var index = 0; index < World.All.Count; index++)
            {
                var world = World.All[index];
                if (world != null && world.IsCreated && world.SequenceNumber == sequence) return world;
            }
            return null;
        }
        private static string EntityId(World world, Entity entity) => Encode("e",
            world.SequenceNumber.ToString(CultureInfo.InvariantCulture), entity.Index.ToString(CultureInfo.InvariantCulture),
            entity.Version.ToString(CultureInfo.InvariantCulture));
        private static string SystemId(World world, Type type, string kind) => Encode("s",
            world.SequenceNumber.ToString(CultureInfo.InvariantCulture), kind, type.AssemblyQualifiedName);

        private static string Encode(params string[] values)
        {
            var text = string.Join("\n", values);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static bool TryDecode(string id, out string[] values)
        {
            values = null;
            if (string.IsNullOrEmpty(id)) return false;
            try
            {
                var base64 = id.Replace('-', '+').Replace('_', '/');
                base64 += new string('=', (4 - base64.Length % 4) % 4);
                values = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('\n');
                return true;
            }
            catch (FormatException) { return false; }
        }

        #endregion

        #region Systems

        private sealed class SystemInfo
        {
            internal World World;
            internal Type Type;
            internal SystemHandle Handle;
            internal ComponentSystemBase Managed;
            internal ComponentSystemGroup Group;
            internal string Kind;
            internal List<SystemInfo> Children = new();
            internal SystemInfo Parent;
            internal string Id => SystemId(World, Type, Kind == "unmanaged" ? "u" : "m");
            internal bool Enabled => Managed != null ? Managed.Enabled : World.Unmanaged.ResolveSystemStateRef(Handle).Enabled;
            internal bool ShouldRun => Managed != null ? Managed.ShouldRunSystem() : World.Unmanaged.ResolveSystemStateRef(Handle).ShouldRunSystem();
            internal uint LastVersion => Managed != null ? Managed.LastSystemVersion : World.Unmanaged.ResolveSystemStateRef(Handle).LastSystemVersion;
            internal int TypeIndex => World.Unmanaged.GetSystemTypeIndex(Handle).Index;
        }

        private static List<SystemInfo> GetSystems(World world)
        {
            var result = new List<SystemInfo>();
            var byHandle = new Dictionary<SystemHandle, SystemInfo>();
            foreach (var managed in world.Systems)
            {
                var info = new SystemInfo
                {
                    World = world, Type = managed.GetType(), Handle = managed.SystemHandle, Managed = managed,
                    Group = managed as ComponentSystemGroup, Kind = managed is ComponentSystemGroup ? "group" : "managed"
                };
                result.Add(info);
                byHandle[info.Handle] = info;
            }

            using (var handles = world.Unmanaged.GetAllUnmanagedSystems(Allocator.Temp))
            {
                var systemTypes = GetUnmanagedTypes();
                foreach (var handle in handles)
                {
                    var type = systemTypes.FirstOrDefault(candidate => world.Unmanaged.GetExistingUnmanagedSystem(candidate) == handle);
                    if (type == null)
                        continue;
                    var info = new SystemInfo { World = world, Type = type, Handle = handle, Kind = "unmanaged" };
                    result.Add(info);
                    byHandle[handle] = info;
                }
            }

            foreach (var group in result.Where(item => item.Group != null))
            using (var children = group.Group.GetAllSystems(Allocator.Temp))
                foreach (var handle in children)
                    if (byHandle.TryGetValue(handle, out var child) && child != group && child.Parent == null)
                    {
                        child.Parent = group;
                        group.Children.Add(child);
                    }
            return result;
        }

        private static List<Type> GetUnmanagedTypes()
        {
            var result = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception) { types = exception.Types; }
                foreach (var type in types)
                    if (type != null && type.IsValueType && typeof(ISystem).IsAssignableFrom(type))
                        result.Add(type);
            }
            return result;
        }

        private static EcsAutomation.Result SystemList(JObject request)
        {
            if (!OnlyFields(request, out var fieldError, "requestId", "world", "name", "match"))
                return Invalid(fieldError);
            if (!TryWorld(request, out var world, out var error)) return error;
            if (!TryOptionalString(request, "name", out var filter, out var textError)) return Invalid(textError);
            if (!TryMatch(request, out var match, out textError)) return Invalid(textError);

            var all = GetSystems(world);
            bool Matches(SystemInfo item) => filter == null || (match == "exact"
                ? item.Type.Name == filter || item.Type.FullName == filter
                : item.Type.Name.Contains(filter) || item.Type.FullName.Contains(filter));
            bool Include(SystemInfo item) => Matches(item) || item.Children.Any(Include);
            var roots = all.Where(item => item.Parent == null && Include(item)).ToList();
            var matchingCount = all.Count(Matches);
            var written = 0;
            var yaml = new StringBuilder().Append("world: ").Append(Q(WorldId(world)))
                .Append("\nname: ").Append(Q(world.Name))
                .Append("\nmatchedCount: ").Append(matchingCount)
                .Append("\nnodeLimit: ").Append(SystemLimit)
                .Append("\nsystems:");
            if (roots.Count == 0) yaml.Append(" []");
            foreach (var root in roots)
                AppendSystemNode(yaml, root, 1, Include, ref written);
            var omitted = Math.Max(0, CountIncluded(roots, Include) - written);
            yaml.Insert(yaml.ToString().IndexOf("\nsystems:", StringComparison.Ordinal),
                "\nreturnedCount: " + written + "\nomittedCount: " + omitted);
            return EcsAutomation.Result.Success(yaml.ToString());
        }

        private static int CountIncluded(IEnumerable<SystemInfo> systems, Func<SystemInfo, bool> include) =>
            systems.Sum(item => include(item) ? 1 + CountIncluded(item.Children, include) : 0);

        private static void AppendSystemNode(StringBuilder yaml, SystemInfo item, int depth,
            Func<SystemInfo, bool> include, ref int written)
        {
            if (written >= SystemLimit || !include(item)) return;
            written++;
            var indent = new string(' ', depth * 2);
            yaml.Append('\n').Append(indent).Append("- id: ").Append(Q(item.Id))
                .Append('\n').Append(indent).Append("  name: ").Append(Q(item.Type.Name))
                .Append('\n').Append(indent).Append("  type: ").Append(Q(item.Type.FullName))
                .Append('\n').Append(indent).Append("  typeIndex: ").Append(item.TypeIndex)
                .Append('\n').Append(indent).Append("  kind: ").Append(item.Kind)
                .Append('\n').Append(indent).Append("  enabled: ").Append(Bool(item.Enabled))
                .Append('\n').Append(indent).Append("  shouldRun: ").Append(Bool(item.ShouldRun))
                .Append('\n').Append(indent).Append("  lastSystemVersion: ").Append(item.LastVersion);
            var children = item.Children.Where(include).ToList();
            if (children.Count > 0 && written < SystemLimit)
            {
                yaml.Append('\n').Append(indent).Append("  children:");
                foreach (var child in children) AppendSystemNode(yaml, child, depth + 2, include, ref written);
            }
        }

        private static EcsAutomation.Result SystemInspect(JObject request)
        {
            if (!OnlyFields(request, out var fieldError, "requestId", "system")) return Invalid(fieldError);
            if (!RequiredString(request, "system", out var id, out var idError)) return Invalid(idError);
            if (!TryDecode(id, out var parts) || parts.Length != 4 || parts[0] != "s" ||
                !ulong.TryParse(parts[1], out var sequence))
                return EcsAutomation.Result.Error("invalid_request", "system must be an opaque system id.");
            var world = FindWorld(sequence);
            if (world == null) return EcsAutomation.Result.Error("world_not_found", "The system's world is no longer valid.");
            var item = GetSystems(world).FirstOrDefault(candidate => candidate.Id == id);
            if (item == null) return Invalid("The system id is no longer valid.");

            var yaml = new StringBuilder().Append("id: ").Append(Q(item.Id))
                .Append("\nworld: ").Append(Q(WorldId(world)))
                .Append("\nname: ").Append(Q(item.Type.Name))
                .Append("\ntype: ").Append(Q(item.Type.FullName))
                .Append("\ntypeIndex: ").Append(item.TypeIndex)
                .Append("\nkind: ").Append(item.Kind)
                .Append("\nenabled: ").Append(Bool(item.Enabled))
                .Append("\nshouldRun: ").Append(Bool(item.ShouldRun))
                .Append("\nlastSystemVersion: ").Append(item.LastVersion)
                .Append("\ngroup: ").Append(item.Parent == null ? "null" : Q(item.Parent.Id));
            if (item.Managed == null)
                yaml.Append("\nqueriesAvailable: false");
            else
            {
                yaml.Append("\nqueriesAvailable: true\nqueries:");
                var queries = item.Managed.EntityQueries;
                if (queries.Length == 0) yaml.Append(" []");
                for (var index = 0; index < queries.Length; index++)
                {
                    var query = queries[index];
                    var descriptions = query.GetEntityQueryDescs();
                    yaml.Append("\n  - index: ").Append(index)
                        .Append("\n    entityCount: ").Append(query.CalculateEntityCount())
                        .Append("\n    chunkCount: ").Append(query.CalculateChunkCount())
                        .Append("\n    descriptions:");
                    foreach (var description in descriptions)
                    {
                        yaml.Append("\n      - all: ").Append(TypeList(description.All))
                            .Append("\n        any: ").Append(TypeList(description.Any))
                            .Append("\n        none: ").Append(TypeList(description.None))
                            .Append("\n        options: ").Append(Q(description.Options.ToString()));
                    }
                }
            }
            return EcsAutomation.Result.Success(yaml.ToString());
        }

        #endregion

        #region Visual snapshot

        private static EcsAutomation.Result VisualSnapshot(JObject request)
        {
            if (!OnlyFields(request, out var fieldError, "requestId", "world", "camera"))
                return Invalid(fieldError);
            if (!TryWorld(request, out var world, out var worldError)) return worldError;
            if (!TryOptionalString(request, "camera", out var requestedCamera, out var cameraFieldError))
                return Invalid(cameraFieldError);
            if (!VisualSnapshotUtility.TryResolveCamera(requestedCamera, out var camera, out var cameraErrorCode,
                    out var cameraError))
                return EcsAutomation.Result.Error(cameraErrorCode, cameraError);

            var visible = CollectVisibleEntities(world, camera);
            var totalCount = visible.Count;
            visible.Sort(CompareVisualProminence);
            if (visible.Count > VisualEntityLimit)
                visible.RemoveRange(VisualEntityLimit, visible.Count - VisualEntityLimit);
            var roots = BuildVisualTree(world, visible);
            return EcsAutomation.Result.Success(BuildVisualSnapshotYaml(world, camera, roots, totalCount,
                Math.Max(0, totalCount - visible.Count)));
        }

        private static List<EcsVisualNode> CollectVisibleEntities(World world, Camera camera)
        {
            var result = new List<EcsVisualNode>();
            var manager = world.EntityManager;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            var pixelsPerUnit = VisualSnapshotUtility.ComputePixelsPerUnit(camera);
            using var query = manager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldRenderBounds>(),
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.ReadOnly<MaterialMeshInfo>(),
                ComponentType.ReadOnly<RenderFilterSettings>());
            using var entities = query.ToEntityArray(Allocator.TempJob);
            foreach (var entity in entities)
            {
                if (!manager.Exists(entity) || !manager.IsEnabled(entity) ||
                    manager.HasComponent<Disabled>(entity) || manager.HasComponent<Prefab>(entity) ||
                    manager.HasComponent<DisableRendering>(entity) ||
                    !manager.IsComponentEnabled<MaterialMeshInfo>(entity))
                    continue;

                var filter = manager.GetSharedComponent<RenderFilterSettings>(entity);
                if (filter.Layer < 0 || filter.Layer > 31 || (camera.cullingMask & (1 << filter.Layer)) == 0 ||
                    filter.ShadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    continue;

                var worldBounds = manager.GetComponentData<WorldRenderBounds>(entity).Value;
                var bounds = new Bounds(
                    new Vector3(worldBounds.Center.x, worldBounds.Center.y, worldBounds.Center.z),
                    new Vector3(worldBounds.Extents.x * 2f, worldBounds.Extents.y * 2f,
                        worldBounds.Extents.z * 2f));
                if (bounds.size == Vector3.zero || !GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                if (!VisualSnapshotUtility.TryProjectBounds(bounds, camera.worldToCameraMatrix,
                        camera.projectionMatrix, camera.pixelRect, screen.height, camera.nearClipPlane,
                        out var rect, out var depthNear, out var depthFar))
                    continue;
                var intersection = VisualSnapshotUtility.Intersect(rect, screen);
                if (intersection.width <= 0f || intersection.height <= 0f) continue;

                result.Add(new EcsVisualNode(entity, manager.GetName(entity), EntityId(world, entity))
                {
                    Visible = true,
                    Rect = VisualSnapshotUtility.ToPixelRect(rect),
                    ZNear = Mathf.RoundToInt(depthNear * pixelsPerUnit),
                    ZFar = Mathf.RoundToInt(depthFar * pixelsPerUnit)
                });
            }
            return result;
        }

        private static int CompareVisualProminence(EcsVisualNode first, EcsVisualNode second)
        {
            var depth = first.ZNear.CompareTo(second.ZNear);
            return depth != 0 ? depth : CompareVisualHierarchy(first, second);
        }

        private static List<EcsVisualNode> BuildVisualTree(World world, IReadOnlyList<EcsVisualNode> visible)
        {
            var manager = world.EntityManager;
            var nodes = visible.ToDictionary(node => node.Entity);
            var parentByChild = new Dictionary<Entity, Entity>();
            foreach (var visibleNode in visible)
            {
                var current = visibleNode.Entity;
                var visited = new HashSet<Entity>();
                while (manager.Exists(current) && visited.Add(current) && manager.HasComponent<Parent>(current))
                {
                    var parent = manager.GetComponentData<Parent>(current).Value;
                    if (parent == Entity.Null || !manager.Exists(parent) || visited.Contains(parent) ||
                        WouldCreateVisualCycle(current, parent, parentByChild))
                        break;
                    if (!nodes.ContainsKey(parent))
                        nodes.Add(parent, new EcsVisualNode(parent, manager.GetName(parent), EntityId(world, parent)));
                    if (!parentByChild.ContainsKey(current)) parentByChild.Add(current, parent);
                    current = parent;
                }
            }

            foreach (var relation in parentByChild)
                if (nodes.TryGetValue(relation.Key, out var child) && nodes.TryGetValue(relation.Value, out var parent))
                    parent.Children.Add(child);
            var roots = nodes.Values.Where(node => !parentByChild.ContainsKey(node.Entity)).ToList();
            SortVisualTree(roots);
            return roots;
        }

        private static bool WouldCreateVisualCycle(Entity child, Entity parent,
            IReadOnlyDictionary<Entity, Entity> parentByChild)
        {
            var visited = new HashSet<Entity>();
            var current = parent;
            while (visited.Add(current) && parentByChild.TryGetValue(current, out var next))
            {
                if (next == child) return true;
                current = next;
            }
            return false;
        }

        private static void SortVisualTree(List<EcsVisualNode> nodes)
        {
            nodes.Sort(CompareVisualHierarchy);
            foreach (var node in nodes) SortVisualTree(node.Children);
        }

        private static int CompareVisualHierarchy(EcsVisualNode first, EcsVisualNode second)
        {
            var name = string.CompareOrdinal(first.Name, second.Name);
            if (name != 0) return name;
            var index = first.Entity.Index.CompareTo(second.Entity.Index);
            return index != 0 ? index : first.Entity.Version.CompareTo(second.Entity.Version);
        }

        private static string BuildVisualSnapshotYaml(World world, Camera camera, IReadOnlyList<EcsVisualNode> roots,
            int totalCount, int omitted)
        {
            var yaml = new StringBuilder().Append("screen: [").Append(Screen.width).Append(',').Append(Screen.height)
                .Append("]\ncamera: ").Append(Q(camera.name))
                .Append(" [id=").Append(Q(UnityObjectId.Get(camera))).Append(']')
                .Append(" [projection=").Append(camera.orthographic ? "Orthographic" : "Perspective").Append(']')
                .Append(" [depth=").Append(camera.depth.ToString("0.##", CultureInfo.InvariantCulture)).Append(']')
                .Append(" [pxPerUnit=").Append(VisualSnapshotUtility.ComputePixelsPerUnit(camera)
                    .ToString("0.##", CultureInfo.InvariantCulture)).Append(']')
                .Append("\nworld: ").Append(Q(world.Name)).Append(" [id=").Append(Q(WorldId(world))).Append(']')
                .Append("\ncount: ").Append(totalCount);
            if (omitted > 0) yaml.Append("\nomitted: ").Append(omitted);
            if (roots.Count == 0) return yaml.Append("\nentities: []").ToString();
            yaml.Append("\nentities:\n");
            foreach (var root in roots) AppendVisualNode(yaml, root, 1);
            return yaml.ToString().TrimEnd();
        }

        private static void AppendVisualNode(StringBuilder yaml, EcsVisualNode node, int indent)
        {
            yaml.Append(' ', indent * 2).Append("- ").Append(Q(node.Name)).Append(" [id=").Append(Q(node.Id))
                .Append(']');
            if (node.Visible)
                yaml.Append(" [rect=[").Append(node.Rect.x).Append(',').Append(node.Rect.y).Append(',')
                    .Append(node.Rect.width).Append(',').Append(node.Rect.height).Append("]] [z=[")
                    .Append(node.ZNear).Append(',').Append(node.ZFar).Append("]]");
            if (node.Children.Count == 0) { yaml.Append('\n'); return; }
            yaml.Append(":\n");
            foreach (var child in node.Children) AppendVisualNode(yaml, child, indent + 1);
        }

        internal sealed class EcsVisualNode
        {
            internal EcsVisualNode(Entity entity, string name, string id)
            {
                Entity = entity; Name = name; Id = id;
            }
            internal Entity Entity { get; }
            internal string Name { get; }
            internal string Id { get; }
            internal bool Visible { get; set; }
            internal RectInt Rect { get; set; }
            internal int ZNear { get; set; }
            internal int ZFar { get; set; }
            internal List<EcsVisualNode> Children { get; } = new();
        }

        #endregion

        #region Entity query and inspection

        private static EcsAutomation.Result EntityQuery(JObject request)
        {
            if (!OnlyFields(request, out var fieldError, "requestId", "world", "all", "any", "none", "name",
                    "match", "includeDisabled", "includePrefabs", "limit")) return Invalid(fieldError);
            if (!TryWorld(request, out var world, out var error)) return error;
            if (!TryTypeArray(request, "all", out var all, out error) ||
                !TryTypeArray(request, "any", out var any, out error) ||
                !TryTypeArray(request, "none", out var none, out error)) return error;
            if (!TryOptionalString(request, "name", out var name, out var textError)) return Invalid(textError);
            if (!TryMatch(request, out var match, out textError)) return Invalid(textError);
            if (!TryBool(request, "includeDisabled", false, out var includeDisabled, out textError) ||
                !TryBool(request, "includePrefabs", false, out var includePrefabs, out textError)) return Invalid(textError);
            if (!TryInt(request, "limit", DefaultEntityLimit, 1, MaxEntityLimit, out var limit, out textError))
                return Invalid(textError);

            var manager = world.EntityManager;
            var disabled = ComponentType.ReadOnly<Disabled>();
            var prefab = ComponentType.ReadOnly<Prefab>();
            var matches = new List<Entity>();
            int candidateCount = 0, scanned = 0;
            bool scanLimitReached = false;
            using (var entities = manager.UniversalQuery.ToEntityArray(Allocator.TempJob))
            {
                foreach (var entity in entities)
                {
                    if (!includeDisabled && manager.HasComponent(entity, disabled) ||
                        !includePrefabs && manager.HasComponent(entity, prefab) ||
                        all.Any(type => !manager.HasComponent(entity, type)) ||
                        any.Count > 0 && !any.Any(type => manager.HasComponent(entity, type)) ||
                        none.Any(type => manager.HasComponent(entity, type))) continue;
                    candidateCount++;
                    if (name != null)
                    {
                        if (scanned >= NameScanLimit) { scanLimitReached = true; break; }
                        scanned++;
                        var entityName = manager.GetName(entity);
                        if (match == "exact" ? entityName != name : !entityName.Contains(name)) continue;
                    }
                    matches.Add(entity);
                }
            }

            var exact = name == null || !scanLimitReached;
            var yaml = new StringBuilder().Append("world: ").Append(Q(WorldId(world)))
                .Append("\nquery:")
                .Append("\n  all: ").Append(TypeList(all))
                .Append("\n  any: ").Append(TypeList(any))
                .Append("\n  none: ").Append(TypeList(none))
                .Append("\n  name: ").Append(name == null ? "null" : Q(name))
                .Append("\n  match: ").Append(match)
                .Append("\n  includeDisabled: ").Append(Bool(includeDisabled))
                .Append("\n  includePrefabs: ").Append(Bool(includePrefabs))
                .Append("\ncandidateCount: ").Append(candidateCount)
                .Append("\nscannedCount: ").Append(name == null ? candidateCount : scanned)
                .Append("\ncount: ").Append(matches.Count)
                .Append("\ncountIsExact: ").Append(Bool(exact))
                .Append("\nscanLimitReached: ").Append(Bool(scanLimitReached))
                .Append("\nreturnedCount: ").Append(Math.Min(limit, matches.Count))
                .Append("\nomittedCount: ").Append(Math.Max(0, matches.Count - limit));
            if (scanLimitReached)
                yaml.Append("\nhint: ").Append(Q("Use component conditions to narrow the candidate set."));
            yaml.Append("\nentities:");
            if (matches.Count == 0) yaml.Append(" []");
            foreach (var entity in matches.Take(limit))
                yaml.Append("\n  - id: ").Append(Q(EntityId(world, entity)))
                    .Append("\n    name: ").Append(Q(manager.GetName(entity)))
                    .Append("\n    index: ").Append(entity.Index)
                    .Append("\n    version: ").Append(entity.Version)
                    .Append("\n    enabled: ").Append(Bool(manager.IsEnabled(entity)))
                    .Append("\n    componentCount: ").Append(manager.GetComponentCount(entity));
            return EcsAutomation.Result.Success(yaml.ToString());
        }

        private static EcsAutomation.Result EntityInspect(JObject request)
        {
            if (!OnlyFields(request, out var fieldError, "requestId", "entity", "components")) return Invalid(fieldError);
            if (!RequiredString(request, "entity", out var id, out var idError)) return Invalid(idError);
            if (!TryDecode(id, out var parts) || parts.Length != 4 || parts[0] != "e" ||
                !ulong.TryParse(parts[1], out var sequence) || !int.TryParse(parts[2], out var index) ||
                !int.TryParse(parts[3], out var version))
                return EcsAutomation.Result.Error("entity_not_found", "entity must be an opaque entity id.");
            var world = FindWorld(sequence);
            if (world == null) return EcsAutomation.Result.Error("stale_entity", "The entity's world is no longer valid.");
            var entity = new Entity { Index = index, Version = version };
            var manager = world.EntityManager;
            if (!manager.Exists(entity)) return EcsAutomation.Result.Error("stale_entity", "The entity no longer exists or its version changed.");
            if (!TryTypeArray(request, "components", out var selected, out var typeError, true)) return typeError;

            using var componentTypes = manager.GetComponentTypes(entity, Allocator.Temp);
            var actual = componentTypes.ToArray().Where(type => TypeManager.GetType(type.TypeIndex) != typeof(Entity)).ToList();
            if (request["components"] != null)
                actual = actual.Where(type => selected.Any(wanted => wanted.TypeIndex == type.TypeIndex)).ToList();
            var yaml = new StringBuilder().Append("world: ").Append(Q(WorldId(world)))
                .Append("\nentity: ").Append(Q(id))
                .Append("\nname: ").Append(Q(manager.GetName(entity)))
                .Append("\nindex: ").Append(entity.Index)
                .Append("\nversion: ").Append(entity.Version)
                .Append("\nenabled: ").Append(Bool(manager.IsEnabled(entity)))
                .Append("\narchetype: ").Append(TypeList(componentTypes.ToArray()))
                .Append("\ncomponents:");
            if (actual.Count == 0) yaml.Append(" []");
            foreach (var component in actual)
            {
                var type = TypeManager.GetType(component.TypeIndex);
                ref readonly var info = ref TypeManager.GetTypeInfo(component.TypeIndex);
                yaml.Append("\n  - type: ").Append(Q(type.FullName))
                    .Append("\n    kind: ").Append(ComponentKind(component, info))
                    .Append("\n    enableable: ").Append(Bool(info.EnableableType));
                if (info.EnableableType)
                    yaml.Append("\n    enabled: ").Append(Bool(manager.IsComponentEnabled(entity, component)));
                if (info.IsZeroSized && info.Category != TypeManager.TypeCategory.BufferData)
                {
                    yaml.Append("\n    value: null");
                    continue;
                }
                try
                {
                    var value = manager.Debug.GetComponentBoxed(entity, component);
                    yaml.Append("\n    value: ").Append(FormatValue(value, world, 0).ToString(Formatting.None));
                }
                catch (Exception exception)
                {
                    yaml.Append("\n    error: ").Append(Q(exception.Message));
                }
            }
            return EcsAutomation.Result.Success(yaml.ToString());
        }

        private static string ComponentKind(ComponentType component, TypeManager.TypeInfo info)
        {
            if (info.Category == TypeManager.TypeCategory.BufferData) return "buffer";
            if (info.Category == TypeManager.TypeCategory.ISharedComponentData) return "shared";
            if (info.Category == TypeManager.TypeCategory.UnityEngineObject) return "unityObject";
            if (component.TypeIndex.IsManagedComponent) return "managed";
            if (info.IsZeroSized) return "tag";
            return "component";
        }

        #endregion

        #region Value formatting and validation

        private static JToken FormatValue(object value, World world, int depth)
        {
            if (value == null) return JValue.CreateNull();
            if (value is Entity entity) return entity == Entity.Null ? JValue.CreateNull() : EntityId(world, entity);
            if (value is Object unityObject) return new JObject
            {
                ["name"] = unityObject.name, ["type"] = unityObject.GetType().FullName
            };
            var type = value.GetType();
            if (type.IsEnum) return value.ToString();
            if (value is string || value is char || value is bool || value is byte || value is sbyte ||
                value is short || value is ushort || value is int || value is uint || value is long || value is ulong ||
                value is float || value is double || value is decimal) return JToken.FromObject(value);
            if (depth >= MaxValueDepth) return new JObject { ["truncated"] = true, ["type"] = type.FullName };
            if (value is IEnumerable enumerable)
            {
                var array = new JArray();
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count++ >= MaxItems) break;
                    array.Add(FormatValue(item, world, depth + 1));
                }
                var result = new JObject { ["items"] = array };
                if (count > MaxItems) result["omitted"] = CountRemaining(enumerable, MaxItems);
                return result;
            }
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var obj = new JObject();
            for (var i = 0; i < Math.Min(fields.Length, MaxFields); i++)
            {
                try { obj[fields[i].Name] = FormatValue(fields[i].GetValue(value), world, depth + 1); }
                catch (Exception exception) { obj[fields[i].Name] = new JObject { ["error"] = exception.Message }; }
            }
            if (fields.Length > MaxFields) obj["omittedFields"] = fields.Length - MaxFields;
            return obj;
        }

        private static int CountRemaining(IEnumerable enumerable, int skipped)
        {
            if (enumerable is ICollection collection) return Math.Max(0, collection.Count - skipped);
            var count = 0;
            foreach (var unused in enumerable) count++;
            return Math.Max(0, count - skipped);
        }

        private static bool TryTypeArray(JObject request, string field, out List<ComponentType> result,
            out EcsAutomation.Result error, bool optionalSelection = false)
        {
            result = new List<ComponentType>();
            error = null;
            var token = request[field];
            if (token == null) return true;
            if (token.Type != JTokenType.Array)
            {
                error = Invalid($"{field} must be an array of component type names.");
                return false;
            }
            foreach (var item in (JArray)token)
            {
                if (item.Type != JTokenType.String || string.IsNullOrWhiteSpace(item.Value<string>()))
                {
                    error = Invalid($"Every {field} entry must be a non-empty string.");
                    return false;
                }
                var name = item.Value<string>();
                var types = TypeManager.AllTypes.Select(info => info.Type).Where(type => type != null &&
                    (type.FullName == name || type.Name == name)).Distinct().ToList();
                if (types.Count == 0)
                {
                    error = EcsAutomation.Result.Error("unknown_component_type", $"Unknown component type '{name}'.");
                    return false;
                }
                if (types.Count > 1 && types.All(type => type.FullName != name))
                {
                    error = EcsAutomation.Result.Error("ambiguous_component_type",
                        $"Component type '{name}' is ambiguous: {string.Join(", ", types.Select(type => type.FullName))}.");
                    return false;
                }
                result.Add(ComponentType.ReadOnly(types.First(type => type.FullName == name || types.Count == 1)));
            }
            return true;
        }

        private static bool OnlyFields(JObject request, out string error, params string[] fields)
        {
            var allowed = new HashSet<string>(fields);
            var unknown = request.Properties().FirstOrDefault(property => !allowed.Contains(property.Name));
            error = unknown == null ? null : $"Unknown request field '{unknown.Name}'.";
            return unknown == null;
        }

        private static bool RequiredString(JObject request, string field, out string value, out string error)
        {
            value = null; error = null;
            var token = request[field];
            if (token?.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
            { error = $"{field} must be a non-empty string."; return false; }
            value = token.Value<string>(); return true;
        }

        private static bool TryOptionalString(JObject request, string field, out string value, out string error)
        {
            value = null; error = null;
            var token = request[field];
            if (token == null || token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.String) { error = $"{field} must be a string."; return false; }
            value = token.Value<string>(); return true;
        }

        private static bool TryMatch(JObject request, out string value, out string error)
        {
            error = null; value = request["match"]?.Value<string>() ?? "contains";
            if (request["match"] != null && request["match"].Type != JTokenType.String || value is not ("exact" or "contains"))
            { error = "match must be 'exact' or 'contains'."; return false; }
            return true;
        }

        private static bool TryBool(JObject request, string field, bool fallback, out bool value, out string error)
        {
            error = null; value = fallback;
            var token = request[field]; if (token == null) return true;
            if (token.Type != JTokenType.Boolean) { error = $"{field} must be a boolean."; return false; }
            value = token.Value<bool>(); return true;
        }

        private static bool TryInt(JObject request, string field, int fallback, int min, int max,
            out int value, out string error)
        {
            error = null; value = fallback;
            var token = request[field]; if (token == null) return true;
            if (token.Type != JTokenType.Integer || (value = token.Value<int>()) < min || value > max)
            { error = $"{field} must be an integer from {min} through {max}."; return false; }
            return true;
        }

        private static EcsAutomation.Result Invalid(string message) =>
            EcsAutomation.Result.Error("invalid_request", message);
        private static string Q(string value) => "\"" + AutomationProtocol.EscapeYamlString(value ?? string.Empty) + "\"";
        private static string Bool(bool value) => value ? "true" : "false";
        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string TypeList(IEnumerable<ComponentType> values) => "[" + string.Join(", ", values.Select(value =>
            Q(TypeManager.GetType(value.TypeIndex)?.FullName ?? value.ToString()))) + "]";

        #endregion
    }
}
