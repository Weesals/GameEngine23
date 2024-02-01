using Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Landscape;
using Weesals.Utility;

namespace Weesals.Game {

    public struct CModel {
        public Model Model;
        public override string ToString() { return Model.Name; }
    }
    public struct CPosition {
        public Vector3 Value;
        public override string ToString() { return Value.ToString(); }
    }
    public struct CTargetPosition {
        public Vector3 Value;
        public bool TestBoolean;
        public override string ToString() { return Value.ToString(); }
    }
    [SparseComponent]
    public struct CSelectable {
        public bool Selected;
        public override string ToString() { return $"Selected {Selected}"; }
    }

    public class EntityProxy : IEntityPosition, IEntitySelectable, IEntityRedirect {

        public readonly World World;

        public EntityProxy(World world) { World = world; }

        public Vector3 GetPosition(ulong id = ulong.MaxValue) {
            return World.GetComponent<CPosition>(GenericTarget.UnpackEntity(id)).Value;
        }
        public Quaternion GetRotation(ulong id = ulong.MaxValue) {
            return Quaternion.Identity;
        }
        public void SetPosition(Vector3 pos, ulong id = ulong.MaxValue) {
            ref var tform = ref World.GetComponentRef<ECTransform>(GenericTarget.UnpackEntity(id));
            tform.Position = SimulationWorld.WorldToSimulation(pos).XZ;
            World.GetComponentRef<CPosition>(GenericTarget.UnpackEntity(id)).Value = pos;
        }
        public void SetRotation(Quaternion rot, ulong id = ulong.MaxValue) {
        }

        public void NotifySelected(ulong id, bool selected) {
            var value = World.TryGetComponentRef<CSelectable>(GenericTarget.UnpackEntity(id));
            if (value.HasValue)
                value.Value.Selected = selected;
        }
        public GenericTarget GetOwner(ulong id) {
            return GenericTarget.FromEntity(World, GenericTarget.UnpackEntity(id));
        }
    }

    public class Simulation {

        public World World { get; private set; }
        public EntityProxy EntityProxy { get; private set; }

        private EntityMapSystem entityMapSystem;
        private NavigationSystem navigationSystem;
        private OrderQueueSystem actionQueueSystem;
        private OrderDispatchSystem actionDispatchSystem;

        public NavMesh NavMesh => navigationSystem.NavMesh;
        public NavMesh2Baker NavBaker => navigationSystem.NavMeshBaker;

        public Simulation(LandscapeData landscape) {
            World = new World();
            EntityProxy = new(World);

            World.GetOrCreateSystem<TimeSystem>();
            World.GetOrCreateSystem<LifeSystem>();
            World.GetOrCreateSystem<ProtoSystem>();
            actionDispatchSystem = World.GetOrCreateSystem<OrderDispatchSystem>();
            actionQueueSystem = World.GetOrCreateSystem<OrderQueueSystem>();
            entityMapSystem = World.GetOrCreateSystem<EntityMapSystem>();
            navigationSystem = World.GetOrCreateSystem<NavigationSystem>();
            World.GetOrCreateSystem<OrderMoveSystem>();

            entityMapSystem.SetLandscape(landscape);
            navigationSystem.SetLandscape(landscape);

            /*NavBaker.InsertRectangle(new RectI(0, 0, 2048, 2048), new TriangleType() { TypeId = 0, });
            var mutator = new NavMesh2Baker.Mutator(NavBaker);
            var vertMutator = mutator.CreateVertexMutator();
            var v1 = vertMutator.RequireVertexId(new Coordinate(64, 32));
            var v2 = vertMutator.RequireVertexId(new Coordinate(128, 73));
            var v3 = vertMutator.RequireVertexId(new Coordinate(23, 50));
            mutator.PinEdge(v1, v2);
            mutator.PinEdge(v2, v3);
            mutator.PinEdge(v3, v1);
            mutator.SetTriangleTypeByEdge(v1, v2, new TriangleType() { TypeId = 1, }, true);*/
        }

        public void GenerateWorld() {
            var rand = new Random();
            using var tmpEntities = new PooledList<Entity>();
            /*for (int i = 0; i < 10; i++) {
                while (tmpEntities.Count > 0 && rand.NextSingle() < 0.6f) {
                    World.DeleteEntity(tmpEntities[0]);
                    tmpEntities.RemoveAt(0);
                }
                var entity1 = World.CreateEntity();
                var barracksModel = Resources.LoadModel("./assets/SM_Barracks.fbx");
                World.AddComponent<CPosition>(entity1) = new() { Value = new Vector3(5f * tmpEntities.Count, 0f, 1f), };
                World.AddComponent<CModel>(entity1) = new() { Model = barracksModel, };
                World.AddComponent<CSelectable>(entity1);
                tmpEntities.Add(entity1);
            }*/

            var houseModel = Resources.LoadModel("./assets/SM_House.fbx");
            var command = new EntityCommandBuffer(World.Stage);
            const int Count = 500;
            var SqrtCount = (int)MathF.Sqrt(Count);
            for (int i = 0; i < Count; i++) {
                var newEntity = command.CreateDeferredEntity();
                var pos = new Vector3(i / SqrtCount, 0f, i % SqrtCount) * 10.0f;
                command.AddComponent<CPosition>(newEntity) = new CPosition() { Value = pos, };
                command.AddComponent<CModel>(newEntity) = new() { Model = houseModel, };
                command.AddComponent<CTargetPosition>(newEntity);
                command.AddComponent<CSelectable>(newEntity);
                command.AddComponent<ECTransform>(newEntity) = new() { Position = new Int2(i / SqrtCount, i % SqrtCount) * 2000 };
                command.AddComponent<ECMobile>(newEntity) = new() { MovementSpeed = 10000, TurnSpeed = 500, NavMask = 1, };
                //command.AddComponent<ECActionMove>(newEntity) = new() { Location = 5000, };
            }
            command.Commit();

            /*testObject = new();
            var model = Resources.LoadModel("./assets/SM_Barracks.fbx");
            foreach (var mesh in model.Meshes) {
                var instance = Scene.CreateInstance();
                testObject.Meshes.Add(instance);
                //scenePasses.AddInstance(instance, mesh);
            };
            Scene.SetTransform(testObject, Matrix4x4.CreateRotationY(MathF.PI));*/

            /*World.AddSystem((ref Entity entity, ref Position pos) => {
                World.AddComponent<TargetPosition>(entity);
            }, World.BeginQuery().With<Position>().Without<TargetPosition>().Build());//*/
        }

        public void Simulate(int dtMS) {

            var timeSystem = World.GetSystem<TimeSystem>();
            timeSystem.Step(dtMS);
            World.Step();

#if false
            var newEntities = World.BeginQuery().With<Position>().Without<SceneRenderable>().Build();
            foreach (var entity in World.GetEntities(newEntities)) {
                var mutator = World.CreateMutator(entity);
                mutator.AddComponent<SceneRenderable>();
                mutator.Commit();
                /*var model = Resources.LoadModel("./assets/SM_Barracks.fbx");
                foreach (var mesh in model.Meshes) {
                    var instance = scene.CreateInstance();
                    testObject.Meshes.Add(instance);
                    scenePasses.AddInstance(instance, mesh);
                };*/
            }
#endif

            /*foreach (var entity in World.GetEntities(World.BeginQuery().With<Position, TargetPosition>().Build())) {
                ref var pos = ref World.GetComponentRef<Position>(entity);
                var targetPos = World.GetComponent<TargetPosition>(entity);
                var delta = targetPos.Value - pos.Value;
                var deltaLen = delta.Length();
                if (deltaLen != 0f) {
                    pos.Value = targetPos.Value - delta * Easing.MoveTowards(1f, 0f, 4f * dt / deltaLen);
                } else {
                    var rand = new Random(time.GetHashCode());
                    World.GetComponentRef<TargetPosition>(entity).Value = pos.Value
                        + new Vector3(rand.NextSingle() - 0.5f, 0f, rand.NextSingle() - 0.5f) * 20f;
                }
            }*/
            /*var rand = new Random(time.GetHashCode());
            foreach (var accessor in World.QueryAll<CPosition, CTargetPosition>()) {
                var etargetPos = (CTargetPosition)accessor;
                if (etargetPos.Value == default) {
                    etargetPos.Value.Y = rand.NextSingle();
                    accessor.Set(etargetPos);
                    continue;
                }
                var epos = (CPosition)accessor;
                var timer = time - etargetPos.Value.Y * 20.0f;
                timer -= MathF.Floor(timer / 10.0f) * 10.0f;
                var newPos = epos.Value;
                newPos.Y = MathF.Abs(MathF.Sin(timer * 10.0f))
                    * Easing.InverseLerp(1f, 0f, Easing.Clamp01(timer));
                if (newPos != epos.Value) {
                    epos.Value = newPos;
                    accessor.Set(epos);
                }
            }*/
        }
        public GenericTarget HitTest(Ray ray) {
            float nearestDst2 = float.MaxValue;
            GenericTarget nearest = GenericTarget.None;
            foreach (var accessor in World.QueryAll<ECTransform, CModel>()) {
                var epos = (ECTransform)accessor;
                var emodel = (CModel)accessor;
                foreach (var mesh in emodel.Model.Meshes) {
                    var lray = ray;
                    lray.Origin -= SimulationWorld.SimulationToWorld(epos.GetPosition3());
                    var dst = mesh.BoundingBox.RayCast(lray);
                    if (dst >= 0f && dst < nearestDst2) {
                        nearest = new GenericTarget(EntityProxy, GenericTarget.PackEntity(accessor));
                        nearestDst2 = dst;
                    }
                }
            }
            return nearest;
        }

        public void EnqueueAction(Entity entity, ActionRequest request, bool append = false) {
            var action = actionQueueSystem.CreateActionInstance(request);
            if (!append) ClearActionQueue(entity);
            actionQueueSystem.EnqueueAction(entity, action);
        }
        private void ClearActionQueue(Entity entity) {
            actionDispatchSystem.CancelAction(entity, RequestId.All);
            actionQueueSystem.CancelAction(entity, RequestId.All);
        }
    }
}
