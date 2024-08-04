using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Navigation;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Game5.Game.Gameplay;
using Weesals.Landscape;
using Weesals.Utility;

namespace Game5.Game {
    using CompletionInstance = OrderSystemBase.CompletionInstance;
    //[UpdateOrderId(10)]
    public partial class NavigationSystem : SystemBase
        , ILateUpdateSystem
        , IPostUpdateVerificationSystem
        {

        public interface INavigationListener {
            void NotifyNavigationCompleted(HashSet<CompletionInstance> completions);
        }

        public ProtoSystem ProtoSystem { get; private set; }
        public EntityMapSystem EntityMapSystem { get; private set; }
        public LifeSystem LifeSystem { get; private set; }
        public TimeSystem TimeSystem { get; private set; }

        public NavGrid NavGrid { get; private set; }
        public Navigation.NavMesh NavMesh { get; private set; }
        public NavMesh2Baker NavMeshBaker { get; private set; }
        public LandscapeData Landscape { get; private set; }

        private HashSet<CompletionInstance> completions;
        private List<INavigationListener> completionListeners = new();

        public struct PathCache {
            public int ExpiryTime;
            public bool CanReachTarget;
            public Int2 StopPos;
            public RangeInt Points;
        }
        private Dictionary<Entity, PathCache> pathCache = new(256);
        public struct ObstructionCache {
            public ECTransform Transform;
            public EntityFootprint Footprint;
        }
        private Dictionary<Entity, ObstructionCache> obstructionCache = new(128);
        private SparseArray<Int2> pathCachePoints;

        private EntityMapSystem.MoveContract moveContract;
        private NavQuery navQuery;

        private bool disableMovements;

        private ComponentMutateListener mutations;

        protected override void OnCreate() {
            base.OnCreate();
            ProtoSystem = World.GetOrCreateSystem<ProtoSystem>();
            EntityMapSystem = World.GetOrCreateSystem<EntityMapSystem>();
            LifeSystem = World.GetOrCreateSystem<LifeSystem>();
            TimeSystem = World.GetOrCreateSystem<TimeSystem>();
            completions = new(32);
            pathCachePoints = new(512);
            moveContract = EntityMapSystem.AllocateContract();
            NavGrid = new NavGrid();
            NavMesh = new Navigation.NavMesh();
            NavMeshBaker = new NavMesh2Baker(NavMesh);
            NavMeshBaker.Allocate();
            var query = World.BeginQuery().With<ECObstruction>().Build();
            World.Manager.AddListener(query, new ArchetypeListener() {
                OnCreate = RegisterEntity,
                OnDelete = UnregisterEntity,
            });
            mutations = new ComponentMutateListener(World.Manager, query, World.Context.RequireComponentTypeId<ECTransform>());
        }
        protected override void OnDestroy() {
            // TODO: Remove listener
            NavMeshBaker.Dispose();
            NavGrid.Dispose();
            NavMesh.Dispose();
            moveContract.Dispose();
            //completions.Dispose();
            base.OnDestroy();
        }

        public bool BeginNavigation(Entity entity, RequestId requestId, Int2 targetLocation, int range = 0) {
            //Debug.WriteLine($"ADD {requestId} {new StackTrace().ToString()}");
            World.AddComponent(entity, new ECActionMove() {
                RequestId = requestId,
                Location = targetLocation,
                Range = range
            });
            return true;
        }
        public void Cancel(Entity entity, RequestId requestId) {
            //Debug.WriteLine($"REM {requestId} {new StackTrace().ToString()}");
            World.TryRemoveComponent<ECActionMove>(entity);
            RemoveCache(entity);
        }

        private void RemoveCache(Entity entity) {
            if (pathCache.TryGetValue(entity, out var cache)) {
                pathCachePoints.Return(ref cache.Points);
                pathCache.Remove(entity);
            }
        }

        public unsafe void SetLandscape(LandscapeData landscapeData) {
            if (Landscape != null) Landscape.OnLandscapeChanged -= Landscape_OnLandscapeChanged;
            Landscape = landscapeData;
            NavGrid.Allocate(NavGrid.SimulationToGrid(Landscape.Sizing.SimulationSize));
            NavMesh.Allocate(NavigationSystem.SimulationToNavMesh(Landscape.Sizing.SimulationSize));
            if (Landscape != null) Landscape.OnLandscapeChanged += Landscape_OnLandscapeChanged;
            UpdateGridRepresentation(new(Int2.Zero, Landscape.Size));
        }

        private void Landscape_OnLandscapeChanged(LandscapeData landscape, LandscapeChangeEvent change) {
            UpdateGridRepresentation(change.Range);
        }
        private void UpdateGridRepresentation(RectI range) {
            var controlMap = Landscape.GetControlMap();
            var heightMap = Landscape.GetHeightMap();
            var waterMap = Landscape.GetWaterMap();
            var accessor = NavGrid.GetAccessor();
            ulong layerIsPassable = 0;
            for (int i = 0; i < Landscape.Layers.LayerCount; i++) {
                var layer = Landscape.Layers[i];
                if ((layer.Flags & LandscapeLayer.TerrainFlags.FlagImpassable) != 0)
                    layerIsPassable |= 1ul << i;
            }
            var rangeMin = NavGrid.SimulationToGrid(Landscape.Sizing.LandscapeToSimulation(range.Min));
            var rangeMax = NavGrid.SimulationToGrid(Landscape.Sizing.LandscapeToSimulation(range.Max));
            for (int y = rangeMin.Y; y < rangeMax.Y; y++) {
                for (int x = rangeMin.X; x < rangeMax.X; x++) {
                    var pnt = new Int2(x, y);
                    var mode = NavGrid.LandscapeModes.None;
                    var pos = NavGrid.GridToSimulation(pnt);
                    var lpos = SimulationWorld.SimulationToLandscape(pos);
                    if (controlMap.Sizing.IsInBounds(lpos)) {
                        int i = controlMap.Sizing.ToIndex(lpos);
                        if (waterMap.IsValid && waterMap[i].Height > heightMap[i].Height) {
                            mode = NavGrid.LandscapeModes.Water;
                        } else if ((layerIsPassable & (1ul << controlMap.GetTypeAt(lpos))) != 0) {
                            mode = NavGrid.LandscapeModes.Impassable;
                        }
                    }
                    accessor.SetLandscapePassable(pnt, mode);
                }
            }
        }

        protected override void OnUpdate() {
            foreach (var mutation in mutations) {
                UpdateEntity(mutation.EntityAddress);
            }
            mutations.Clear();
            if (NavGrid.HasChanges) {
                NavGrid.PushToNavMesh(NavMeshBaker);
            }
            if (Input.IsInitialized && Input.GetKeyDown(KeyCode.Z)) this.disableMovements = !this.disableMovements;
            var time = TimeSystem.GetInterval();
            var entityMap = EntityMapSystem.AllEntities;
            var tformLookup = GetComponentLookup<ECTransform>(false);
            var completions = this.completions;
            ref var moveContract = ref this.moveContract;
            var navMesh = NavMesh.GetReadOnly();
            ref var navQuery = ref this.navQuery;
            navQuery.Initialise(NavMesh);
            using var portals = new PooledList<TriangleEdge>(4);
            bool noPath = Input.IsInitialized && Input.GetKeyDown(KeyCode.LeftShift);
            var pathCache = this.pathCache;
            var pathCachePoints = this.pathCachePoints;
            var heightMap = EntityMapSystem.LandscapeData.GetHeightMap();
            var waterMap = EntityMapSystem.LandscapeData.GetWaterMap();
            var disableMovements = this.disableMovements;
            EntityCommandBuffer cmdBuffer = Stage.SharedCommandBuffer;
            foreach (var accessor in World.QueryAll<ECMobile, ECActionMove, ECTransform>()) {
                Entity entity = accessor;
                ECMobile mobile = accessor;
                ECActionMove move = accessor;
                ref ECTransform tform = ref accessor.Component3Ref;
                var dt = time.DeltaTimeMS;
                var stopPos = move.Location;
                if (move.Range > 0) stopPos = FixedMath.MoveToward(stopPos, tform.Position, move.Range);
                var nextPos = stopPos;

                if (!noPath) {
                    bool recompute = false;
                    if (!pathCache.TryGetValue(entity, out var cache)) {
                        cache = new PathCache();
                        recompute = true;
                    } else {
                        var navNext = pathCachePoints[cache.Points.Start + 0];
                        if (navNext.Equals(default)) navNext = stopPos;
                        else navNext = NavMeshToSimulation(navNext);
                        if ((int)time.TimeCurrentMS - cache.ExpiryTime >= 0 || tform.Position.Equals(navNext))
                            recompute = true;
                    }
                    if (recompute) {
                        var navFrom = SimulationToNavMesh(tform.Position);
                        var navTo = SimulationToNavMesh(stopPos);
                        portals.Clear();
                        bool valid = false;
                        using (new ProfilerMarker("Generating Path").Auto()) {
                            valid = navQuery.ComputePath(navFrom, navTo, mobile.NavMask, ref portals.AsMutable());
                        }
                        if (!valid) {
                            if (cache.Points.Length != 0)
                                pathCachePoints.Reallocate(ref cache.Points, 0);
                            cache.CanReachTarget = false;
                            cache.ExpiryTime = (int)time.TimeCurrentMS + 10000;
                        } else {
                            if (cache.Points.Length != 2)
                                pathCachePoints.Reallocate(ref cache.Points, 2);
                            using (new ProfilerMarker("Funneling Path").Auto()) {
                                var portalFunnel = new NavPortalFunnel(portals, navMesh);
                                var points = pathCachePoints.Slice(cache.Points);
                                int i = 0;
                                if (!navQuery.CanReachTarget) {
                                    navTo = navQuery.ReadOnly.GetNearestPointInTriangle(navQuery.NearestTri, navTo);
                                }
                                for (; i < 2; i++) {
                                    var node = portalFunnel.FindNextNode(navFrom, navTo);
                                    if (portalFunnel.IsEnded && !navQuery.CanReachTarget) { node = default; }
                                    points[i] = NavMeshToSimulation(node);
                                    if (portalFunnel.IsEnded) break;
                                }
                                /*var navNext = portalFunnel.FindNextNode(navFrom, navTo);
                                if (navNext.Equals(default)) navNext = stopPos;
                                else navNext = NavMeshToSimulation(navNext);*/
                                var dst = FixedMath.Distance(tform.Position, NavMeshToSimulation(points[0]));
                                cache.ExpiryTime = (int)time.TimeCurrentMS + dst / mobile.MovementSpeed + 10;
                                cache.CanReachTarget = navQuery.CanReachTarget;
                                if (!cache.CanReachTarget) {
                                    //stopPos = NavMeshToSimulation(navMesh.GetCentreInt2(portals[^1]));
                                    //stopPos = NavMeshToSimulation(navQuery.NearestHop);
                                    stopPos = NavMeshToSimulation(navTo);
                                    cache.StopPos = stopPos;
                                }
                            }
                        }
                        pathCache[entity] = cache;
                    }
                    if (cache.Points.Length <= 0) {
                        return;
                    }
                    if (!cache.CanReachTarget) {
                        stopPos = cache.StopPos;
                    }
                    using (new ProfilerMarker("Evaluating Path").Auto()) {
                        int pointI = 0;
                        var navNext = pathCachePoints[cache.Points.Start + pointI];
                        if (navNext.Equals(default)) {
                            navNext = stopPos;
                        }
                        nextPos = FixedMath.MoveToward(tform.Position, navNext, (int)(mobile.MovementSpeed * dt / 1000));
                    }
                }

                var delta = nextPos - tform.Position;
                var newPos = FixedMath.MoveToward(tform.Position, nextPos, (int)(mobile.MovementSpeed * dt / 1000));

                for (var it = entityMap.CreateSpiralIterator(newPos); it.MoveNext() && it.GetDistanceSq(newPos) < 1000 * 1000;) {
                    foreach (var other in entityMap.GetChunk(it.Current)) {
                        if (other == entity) continue;
                        var otform = tformLookup[other];
                        var odelta = newPos - otform.Position;
                        var len2 = (int)Int2.Dot(odelta, odelta);
                        if (len2 == 0) continue;
                        const int AvoidRange = 700;
                        if (len2 < AvoidRange * AvoidRange) {
                            len2 = (int)FixedMath.SqrtFastI((uint)len2);
                            newPos = otform.Position + odelta * AvoidRange / len2;
                        }
                    }
                }
                if (!disableMovements)
                    moveContract.MoveEntity(entity, ref tform, newPos);
                if (mobile.NavMask == 0x40) {
                    tform.Altitude = (short)waterMap.GetInterpolatedHeightAt(tform.Position);
                } else {
                    tform.Altitude = (short)heightMap.GetInterpolatedHeightAt(tform.Position);
                }
                if (Int2.Dot(delta, delta) > 1) {
                    RotateTowardFacing(ref tform, delta, mobile, dt);
                }
                if (stopPos.Equals(newPos)) {
                    cmdBuffer.RemoveComponent<ECActionMove>(entity);
                    completions.Add(new CompletionInstance(entity, move.RequestId));
                }
            }
            cmdBuffer.Commit();
            foreach (var completion in completions) {
                RemoveCache(completion.Entity);
            }
            //Dependency.Complete();
            Debug.Assert(moveContract.IsValid);
            EntityMapSystem.CommitContract(moveContract);
            moveContract.Clear();
            for (int i = 1; i < portals.Count; i++) {
                var p0 = portals[i - 1];
                var p1 = portals[i + 0];
                var ctr0 = navMesh.GetCentreInt2(p0);
                var ctr1 = navMesh.GetCentreInt2(p1);
                Handles.DrawLine(
                    SimulationWorld.SimulationToWorld(NavMeshToSimulation(ctr0)),
                    SimulationWorld.SimulationToWorld(NavMeshToSimulation(ctr1))
                );
            }
            foreach (var item in pathCache) {
                if (!World.IsValid(item.Key)) {
                    Debug.Fail("Bad entity");
                    continue;
                }
                var stopPos = item.Value.StopPos;
                var tform = World.GetComponent<ECTransform>(item.Key);
                if (item.Value.Points.Length > 0) {
                    var pnt = pathCachePoints[item.Value.Points.Start];
                    Handles.DrawLine(
                        SimulationWorld.SimulationToWorld(tform.Position),
                        SimulationWorld.SimulationToWorld(pnt),
                        Color.Green
                    );
                }
                if (!stopPos.Equals(default)) {
                    Handles.DrawLine(
                        SimulationWorld.SimulationToWorld(tform.Position),
                        SimulationWorld.SimulationToWorld(stopPos),
                        Color.Blue
                    );
                }
            }
            NavGrid.DrawGizmos();
        }

        private static Int2 NavMeshToSimulation(Int2 position) {
            return (position * 1024 + Coordinate.Granularity / 2) / Coordinate.Granularity;
        }
        private static Int2 SimulationToNavMesh(Int2 position) {
            return ((position * Coordinate.Granularity + 512) / 1024);
        }

        public void OnLateUpdate() {
            if (completions.Count > 0) {
                foreach (var listener in completionListeners) listener.NotifyNavigationCompleted(completions);
                completions.Clear();
            }
        }

        public void RegisterCompleteListener(INavigationListener listener, bool enable) {
            if (enable) completionListeners.Add(listener);
            else completionListeners.Remove(listener);
        }

        public void DebugOnPostUpdate() {
        }

        public static void RotateTowardFacing(ref ECTransform tform, Int2 facing, in ECMobile mobile, long dt) {
            var orientation = ECTransform.OrientationFromFacing(facing);
            var oriDelta = (short)(orientation - tform.Orientation);
            var turnAmount = (int)(mobile.TurnSpeed * dt * short.MaxValue / (180 * 1000));
            tform.Orientation += (short)Math.Clamp(oriDelta, -turnAmount, turnAmount);
        }

        public enum MoveResults { Failed, Walking, Arrived, }
        public MoveResults BeginMoveToTarget(Entity entity, OrderDispatchSystem.ActionActivation action, int range) {
            var target = action.Request.TargetEntity;
            if (!target.IsValid) return MoveResults.Failed;

            var eTform = World.GetComponent<ECTransform>(entity);
            var tTform = World.GetComponent<ECTransform>(target);
            if (!World.TryGetComponent<PrototypeData>(target, out var protoData)) protoData = PrototypeData.Default;
            var targetLocation = Int2.Clamp(
                eTform.Position,
                tTform.Position - protoData.Footprint.Size / 2,
                tTform.Position + protoData.Footprint.Size / 2
            );

            var dst2 = Int2.DistanceSquared(eTform.Position, targetLocation);
            if (dst2 <= range * range) {
                return MoveResults.Arrived;
            } else {
                // Out of range, walk to target
                return BeginNavigation(entity, action.RequestId, targetLocation, range - 1) ? MoveResults.Walking : MoveResults.Failed;
            }

        }

        private void RegisterEntity(EntityAddress entityAddr) {
            if (World.Manager.HasComponent<ECMobile>(entityAddr)) return;
            var entity = World.Manager.GetEntity(entityAddr);
            var tform = World.Manager.GetComponent<ECTransform>(entityAddr);
            var proto = ProtoSystem.GetPrototypeData(entityAddr);
            var obstruction = new ObstructionCache() { Transform = tform, Footprint = proto.Footprint, };
            obstructionCache.Add(entity, obstruction);
            RegisterObstruction(entity, obstruction, true);
        }
        private void UnregisterEntity(EntityAddress entityAddr) {
            if (World.Manager.HasComponent<ECMobile>(entityAddr)) return;
            var entity = World.Manager.GetEntity(entityAddr);
            var obstruction = obstructionCache[entity];
            obstructionCache.Remove(entity);
            RegisterObstruction(entity, obstruction, false);
        }
        private void UpdateEntity(EntityAddress entityAddr) {
            var entity = World.Manager.GetEntity(entityAddr);
            var tform = World.Manager.GetComponent<ECTransform>(entityAddr);
            var proto = ProtoSystem.GetPrototypeData(entityAddr);
            var newObstruction = new ObstructionCache() { Transform = tform, Footprint = proto.Footprint, };
            obstructionCache.TryGetValue(entity, out var oldObstruction);
            if (newObstruction.Transform.Equals(oldObstruction.Transform) &&
                newObstruction.Footprint.Equals(oldObstruction.Footprint)) return;
            RegisterObstruction(entity, oldObstruction, false);
            RegisterObstruction(entity, newObstruction, true);
            obstructionCache[entity] = newObstruction;
        }
        private void ComputeObstruction(Span<Int2> corners, ObstructionCache obstruction) {
            var fwd = obstruction.Transform.GetFacing();
            var rgt = new Int2(fwd.Y, -fwd.X);
            for (int i = 0; i < 4; i++) {
                var delta = obstruction.Footprint.Size / 2;
                delta *= new Int2(((i + 0) & 2) - 1, ((i + 1) & 2) - 1);
                delta = (fwd * delta.Y + rgt * delta.X) / 1024;
                corners[i] = (obstruction.Transform.Position + delta);
            }
        }
        private void RegisterObstruction(Entity entity, ObstructionCache obstruction, bool enable) {
            Span<Int2> corners = stackalloc Int2[4];
            ComputeObstruction(corners, obstruction);
            NavGrid.AppendGeometry(corners, enable ? 1 : -1);
        }

        public Int2 FindNearestObstruction(Int2 from) {
            var nfrom = Coordinate.FromInt2(SimulationToNavMesh(from));
            var triI = NavMeshBaker.GetTriangleAt(nfrom);
            var tri = NavMesh.GetTriangle(triI);
            int nearestDst2 = int.MaxValue;
            Int2 nearest = default;
            for (int i = 0; i < 3; i++) {
                var corner = NavMesh.GetCorner(tri.GetCorner(i));
                var dst2 = (int)Int2.DistanceSquared(corner, nfrom);
                if (dst2 < nearestDst2) {
                    nearestDst2 = dst2;
                    nearest = (Int2)corner;
                }
            }
            return NavMeshToSimulation(nearest);
        }
        public Int2 FindNearestPathable(byte navMask, Int2 from) {
            var nfrom = Coordinate.FromInt2(SimulationToNavMesh(from));
            var ro = NavMesh.GetReadOnly();
            var aj = NavMesh.GetAdjacency();
            var triI = aj.FindNearestPathable(ro, navMask, nfrom);
            if (triI == NavMesh.InvalidTriId) return from;
            from = ro.GetNearestPointInTriangle(triI, nfrom);
            return NavMeshToSimulation(from);
        }

    }
}
