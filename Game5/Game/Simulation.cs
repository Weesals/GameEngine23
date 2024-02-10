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

namespace Game5.Game {

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

    public class EntityProxy : IEntityPosition, IEntitySelectable, IEntityRedirect, IEntityStringifier {

        public readonly World World;
        public EntityMapSystem EntityMapSystem;
        private EntityMapSystem.MoveContract moveContract;

        public EntityProxy(World world) {
            World = world;
            EntityMapSystem = World.GetOrCreateSystem<EntityMapSystem>();
            moveContract = EntityMapSystem.AllocateContract();
        }

        public Vector3 GetPosition(ulong id = ulong.MaxValue) {
            //return World.GetComponent<CPosition>(GenericTarget.UnpackEntity(id)).Value;
            return World.GetComponent<ECTransform>(GenericTarget.UnpackEntity(id)).GetWorldPosition();
        }
        public Quaternion GetRotation(ulong id = ulong.MaxValue) {
            return Quaternion.Identity;
        }
        public void SetPosition(Vector3 pos, ulong id = ulong.MaxValue) {
            ref var tform = ref World.GetComponentRef<ECTransform>(GenericTarget.UnpackEntity(id));
            moveContract.MoveEntity(GenericTarget.UnpackEntity(id), ref tform, SimulationWorld.WorldToSimulation(pos).XZ);
            EntityMapSystem.CommitContract(moveContract);
            moveContract.Clear();
        }
        public void SetRotation(Quaternion rot, ulong id = ulong.MaxValue) {
        }

        public void NotifySelected(ulong id, bool selected) {
            var entity = GenericTarget.UnpackEntity(id);
            if (World.IsValid(entity))
                World.AddComponent<CSelectable>(entity).Selected = selected;
        }
        public GenericTarget GetOwner(ulong id) {
            return GenericTarget.FromEntity(World, GenericTarget.UnpackEntity(id));
        }

        public string ToString(ulong id) {
            return GenericTarget.UnpackEntity(id).ToString();
        }
    }

    public class Simulation {

        public World World { get; private set; }
        public LandscapeData Landscape { get; private set; }

        public EntityProxy EntityProxy { get; private set; }
        public TimeSystem TimeSystem { get; private set; }
        public ProtoSystem ProtoSystem { get; private set; }
        public OrderQueueSystem ActionQueueSystem { get; private set; }
        public OrderDispatchSystem ActionDispatchSystem { get; private set; }
        public EntityMapSystem EntityMapSystem { get; private set; }
        public LifeSystem LifeSystem { get; private set; }
        public PrefabRegistry PrefabRegistry { get; private set; }

        private NavigationSystem navigationSystem;

        public NavMesh NavMesh => navigationSystem.NavMesh;
        public NavMesh2Baker NavBaker => navigationSystem.NavMeshBaker;

        public Simulation() {
            World = new World();
            EntityProxy = new(World);

            TimeSystem = World.GetOrCreateSystem<TimeSystem>();
            LifeSystem = World.GetOrCreateSystem<LifeSystem>();
            ProtoSystem = World.GetOrCreateSystem<ProtoSystem>();
            ActionDispatchSystem = World.GetOrCreateSystem<OrderDispatchSystem>();
            ActionQueueSystem = World.GetOrCreateSystem<OrderQueueSystem>();
            EntityMapSystem = World.GetOrCreateSystem<EntityMapSystem>();
            navigationSystem = World.GetOrCreateSystem<NavigationSystem>();
            World.GetOrCreateSystem<OrderMoveSystem>();
            World.GetOrCreateSystem<OrderAttackSystem>();
            World.GetOrCreateSystem<OrderTrainSystem>();

            PrefabRegistry = ProtoSystem.PrefabRegistry;
        }
        public void SetLandscape(LandscapeData landscape) {
            Landscape = landscape;
            EntityMapSystem.SetLandscape(landscape);
            navigationSystem.SetLandscape(landscape);
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
                var barracksModel = Resources.LoadModel("./Assets/SM_Barracks.fbx");
                World.AddComponent<CPosition>(entity1) = new() { Value = new Vector3(5f * tmpEntities.Count, 0f, 1f), };
                World.AddComponent<CModel>(entity1) = new() { Model = barracksModel, };
                World.AddComponent<CSelectable>(entity1);
                tmpEntities.Add(entity1);
            }*/

            var houseModel = Resources.LoadModel("./Assets/SM_House.fbx");

            var house = ProtoSystem.CreatePrototype("House")
                .AddComponent<CModel>(new() { Model = houseModel, })
                .AddComponent<CHitPoints>(new() { Current = 10, })
                .AddComponent<ECTransform>(new() { Position = default, Orientation = short.MinValue })
                .AddComponent<ECMobile>(new() { MovementSpeed = 10000, TurnSpeed = 500, NavMask = 1, })
                .AddComponent<ECTeam>(new() { SlotId = 0 })
                .AddComponent<ECAbilityAttackMelee>(new() { Damage = 1, Interval = 1000, })
                .Build();
            var townCentre = ProtoSystem.CreatePrototype("TownCentre",
                new PrototypeData() {
                    Footprint = new EntityFootprint() { Size = 6000, Height = 200, Shape = EntityFootprint.Shapes.Box, },
                })
                .AddComponent<CModel>(new() { Model = Resources.LoadModel("./Assets/SM_TownCentre.fbx"), })
                .AddComponent<CHitPoints>(new() { Current = 1000, })
                .AddComponent<ECTransform>(new() { Position = default, Orientation = short.MinValue })
                .AddComponent<ECTeam>(new() { SlotId = 0 })
                .AddComponent<ECObstruction>(new() { })
                .Build();

            var houseInstance = PrefabRegistry.Instantiate(World, house.Prefab);
            World.GetComponentRef<ECTransform>(houseInstance).Position = new Int2(-2000, 2000);

            var command = new EntityCommandBuffer(World.Stage);
            const int Count = 10;
            var SqrtCount = (int)MathF.Sqrt(Count);
            for (int i = 0; i < Count; i++) {
                var newEntity = command.CreateDeferredEntity();
                var pos = new Vector3(i / SqrtCount, 0f, i % SqrtCount) * 40.0f;
                command.AddComponent<CModel>(newEntity) = new() { Model = houseModel, };
                command.AddComponent<CHitPoints>(newEntity) = new() { Current = 10, };
                command.AddComponent<ECTransform>(newEntity) = new() { Position = new Int2(i / SqrtCount, i % SqrtCount) * 6000, Orientation = short.MinValue };
                command.AddComponent<ECMobile>(newEntity) = new() { MovementSpeed = 10000, TurnSpeed = 500, NavMask = 1, };
                command.AddComponent<ECTeam>(newEntity) = new() { SlotId = (byte)i };
                command.AddComponent<ECAbilityAttackMelee>(newEntity) = new() { Damage = 1, Interval = 1000, };
                //command.AddComponent<ECActionMove>(newEntity) = new() { Location = 5000, };
            }
            command.Commit();

            /*testObject = new();
            var model = Resources.LoadModel("./Assets/SM_Barracks.fbx");
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

        public void Step(long dtMS) {

            var timeSystem = World.GetSystem<TimeSystem>();
            timeSystem.Step(dtMS);
            World.Step();

            World.GetOrCreateSystem<LifeSystem>().PurgeDead();

#if false
            var newEntities = World.BeginQuery().With<Position>().Without<SceneRenderable>().Build();
            foreach (var entity in World.GetEntities(newEntities)) {
                var mutator = World.CreateMutator(entity);
                mutator.AddComponent<SceneRenderable>();
                mutator.Commit();
                /*var model = Resources.LoadModel("./Assets/SM_Barracks.fbx");
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
            var action = ActionQueueSystem.CreateActionInstance(request);
            if (!append) ClearActionQueue(entity);
            ActionQueueSystem.EnqueueAction(entity, action);
        }
        private void ClearActionQueue(Entity entity) {
            ActionDispatchSystem.CancelAction(entity, RequestId.All);
            ActionQueueSystem.CancelAction(entity, RequestId.All);
        }

        public Entity SpawnEntity(int protoId, Int2 location, int playerId) {
            var heightmap = Landscape.GetHeightMap();
            var height = (short)heightmap.GetInterpolatedHeightAt1024(location);

            var entity = PrefabRegistry.Instantiate(World, new(protoId));
            ref var tform = ref World.AddComponent<ECTransform>(entity);
            tform.Position = location;
            tform.Altitude = height;
            World.AddComponent<ECTeam>(entity).SlotId = (byte)playerId;
            return entity;
        }

        public void CopyStateFrom(Simulation simulation) {
            throw new NotImplementedException();
        }
    }
}
