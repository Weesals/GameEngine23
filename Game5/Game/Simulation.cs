using Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Engine.Importers;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Landscape;
using Weesals.Rendering;
using Weesals.Utility;

namespace Game5.Game {

    public struct CModel {
        public string PrefabName;
        public int Variant;
        public ulong ModelVisibility;
        public ulong ParticleVisibility;
        public Model Model;
        public override string ToString() { return PrefabName ?? ""; }
    }
    public struct CAnimation {
        public AnimationHandle Animation;
        public AnimationHandle WalkAnim;
        public AnimationHandle IdleAnim;
        public override string? ToString() { return Animation.ToString(); }
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

    public class EntityProxy : IItemPosition, IEntitySelectable, IItemRedirect, IItemStringifier {

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
            return World.GetComponent<ECTransform>(EntityProxyExt.UnpackEntity(id)).GetWorldPosition();
        }
        public Quaternion GetRotation(ulong id = ulong.MaxValue) {
            return Quaternion.Identity;
        }
        public void SetPosition(Vector3 pos, ulong id = ulong.MaxValue) {
            ref var tform = ref World.GetComponentRef<ECTransform>(EntityProxyExt.UnpackEntity(id));
            moveContract.MoveEntity(EntityProxyExt.UnpackEntity(id), ref tform, SimulationWorld.WorldToSimulation(pos).XZ);
            EntityMapSystem.CommitContract(moveContract);
            moveContract.Clear();
        }
        public void SetRotation(Quaternion rot, ulong id = ulong.MaxValue) {
        }

        public void NotifySelected(ulong id, bool selected) {
            var entity = EntityProxyExt.UnpackEntity(id);
            if (World.IsValid(entity))
                World.AddComponent<CSelectable>(entity).Selected = selected;
        }
        public ItemReference GetOwner(ulong id) {
            return new ItemReference(World, id);
        }

        public string ToString(ulong id) {
            return EntityProxyExt.UnpackEntity(id).ToString();
        }

        public ItemReference MakeHandle(Entity entity) {
            return new ItemReference(this, EntityProxyExt.PackEntity(entity));
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

        public class ProfiledBootstrap : SystemBootstrap {
            public override T CreateSystem<T>(EntityContext context) {
                using var marker = new ProfilerMarker("NewSys " + typeof(T).Name).Auto();
                return base.CreateSystem<T>(context);
            }
        }

        public Simulation() {
            using (new ProfilerMarker("Create World").Auto()) {
                World = new World();
                World.Context.SystemBootstrap = new ProfiledBootstrap();
            }
            using (new ProfilerMarker("Create Proxy").Auto()) {
                EntityProxy = new(World);
            }

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

        Entity tcInstance;
        public void GenerateWorld() {
            var rand = new Random(0);
            /*using var tmpEntities = new PooledList<Entity>();
            for (int i = 0; i < 10; i++) {
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
            var loadMarker = new ProfilerMarker("Loading meshes").Auto();
            var chickenModel = Resources.LoadModel("./Assets/Models/Ignore/chickenV2.fbx", out var chickenHandle);
            var archerModel = Resources.LoadModel("./Assets/Characters/Character_Archer.fbx", out var archerHandle);
            var idleAnim = Resources.LoadModel("./Assets/Characters/Animation_Idle.fbx", out var idleAnimHandle);
            var runAnim = Resources.LoadModel("./Assets/Characters/Animation_Run.fbx", out var runAnimHandle);

            archerHandle = archerHandle.Then(() => {
                // Character FBX references incorrect texture
                foreach (var mesh in archerModel.Meshes) {
                    mesh.Material.SetTexture("Texture", Resources.LoadTexture("./Assets/Characters/T_CharactersAtlas.png"));
                }
            });

            var animHandles = JobHandle.CombineDependencies(idleAnimHandle, runAnimHandle);
            var modelLoadHandle = JobHandle.CombineDependencies(archerHandle, chickenHandle, animHandles);
            modelLoadHandle.Complete();
            loadMarker.Dispose();

            var prefabMarker = new ProfilerMarker("Creating Prefabs").Auto();

            var archer = ProtoSystem.CreatePrototype("Archer")
                .AddComponent<CModel>(new() { PrefabName = "Archer", })
                .AddComponent<CAnimation>(new() {
                    Animation = runAnim.Animations[0],
                    IdleAnim = idleAnim.Animations[0],
                    WalkAnim = runAnim.Animations[0],
                })
                .AddComponent<CHitPoints>(new() { Current = 10, })
                .AddComponent<ECTransform>(new() { Position = default, Orientation = short.MinValue })
                .AddComponent<ECMobile>(new() { MovementSpeed = 6000, TurnSpeed = 500, NavMask = 1, })
                .AddComponent<ECTeam>(new() { SlotId = 0 })
                .AddComponent<ECAbilityAttackMelee>(new() { Damage = 1, Interval = 1000, })
                .Build();

            var chicken = ProtoSystem.CreatePrototype("Chicken")
                .AddComponent<CModel>(new() { PrefabName = "Chicken", })
                .AddComponent<CAnimation>(new() {
                    Animation = chickenModel.Animations[3],
                    IdleAnim = chickenModel.Animations[2],
                    WalkAnim = chickenModel.Animations[3],
                })
                .AddComponent<CHitPoints>(new() { Current = 10, })
                .AddComponent<ECTransform>(new() { Position = default, Orientation = short.MinValue, Altitude = -50, })
                .AddComponent<ECMobile>(new() { MovementSpeed = 4000, TurnSpeed = 500, NavMask = 1, })
                .AddComponent<ECTeam>(new() { SlotId = 0 })
                .AddComponent<ECAbilityAttackMelee>(new() { Damage = 1, Interval = 1000, })
                .Build();

            var house = ProtoSystem.CreatePrototype("House",
                new PrototypeData() {
                    Footprint = new EntityFootprint() { Size = 4000, Height = 200, Shape = EntityFootprint.Shapes.Box, },
                })
                .AddComponent<CModel>(new() { PrefabName = "House", })
                .AddComponent<CHitPoints>(new() { Current = 10, })
                .AddComponent<ECTransform>(new() { Position = default, Orientation = short.MinValue })
                .AddComponent<ECTeam>(new() { SlotId = 0 })
                .AddComponent<ECObstruction>(new() { })
                .Build();

            var townCentre = ProtoSystem.CreatePrototype("TownCentre",
                new PrototypeData() {
                    Footprint = new EntityFootprint() { Size = 6000, Height = 200, Shape = EntityFootprint.Shapes.Box, },
                })
                .AddComponent<CModel>(new() { PrefabName = "TownCentre", })
                .AddComponent<CHitPoints>(new() { Current = 1000, })
                .AddComponent<ECTransform>(new() { Position = default, Orientation = short.MinValue })
                .AddComponent<ECTeam>(new() { SlotId = 0 })
                .AddComponent<ECObstruction>(new() { })
                .Build();

            var tree = ProtoSystem.CreatePrototype("Tree",
                new PrototypeData() {
                    Footprint = new EntityFootprint() { Size = 3000, Height = 200, Shape = EntityFootprint.Shapes.Box, },
                })
                .AddComponent<CModel>(new() { PrefabName = "Tree", })
                .AddComponent<CHitPoints>(new() { Current = 100, })
                .AddComponent<ECTransform>(new() { Position = default, Orientation = short.MinValue })
                .AddComponent<ECTeam>(new() { SlotId = 0 })
                .AddComponent<ECObstruction>(new() { })
                .Build();

            prefabMarker.Dispose();

            using (new ProfilerMarker("Creating Test Entities").Auto()) {
                tcInstance = PrefabRegistry.Instantiate(World, townCentre.Prefab);
                World.GetComponentRef<ECTransform>(tcInstance).Position = new Int2(50000, 50000);

                var archerInstance = PrefabRegistry.Instantiate(World, archer.Prefab);
                World.GetComponentRef<ECTransform>(archerInstance).Position = new Int2(40000, 28000);

                var chickenInstance = PrefabRegistry.Instantiate(World, chicken.Prefab);
                World.GetComponentRef<ECTransform>(chickenInstance).Position = new Int2(50000, 28000);

                World.GetComponentRef<ECTransform>(World.CreateEntity(tcInstance)).Position += new Int2(6000, 1000);
            }

            using (new ProfilerMarker("Creating Houses").Auto()) {
                var command = new EntityCommandBuffer(World.Manager);
                const int Count = 25;
                var sqrtCount = (int)MathF.Sqrt(Count);
                for (int i = 0; i < Count; i++) {
                    var pos = 2000 + new Int2(i / sqrtCount, i % sqrtCount) * 6000;
                    if (Math.Abs(Landscape.GetHeightMap().GetHeightAtF(SimulationWorld.SimulationToWorld(pos).toxz())) > 0.01f) continue;
                    var newEntity = PrefabRegistry.Instantiate(command, house.Prefab);
                    command.AddComponent<ECTransform>(newEntity) = new() {
                        Position = pos,
                        Orientation = (short)(rand.Next(4) * (short.MinValue / 2))
                    };
                    //command.RemoveComponent<ECObstruction>(newEntity);
                }
                using (new ProfilerMarker("Committing").Auto()) {
                    command.Commit();
                }
            }

            using (new ProfilerMarker("Creating Trees").Auto()) {
                var command = new EntityCommandBuffer(World.Manager);
                for (int i = 0; i < 10; i++) {
                    Int2 groupMin = 4000;
                    Int2 groupMax = Landscape.Sizing.SimulationSize - 4000;
                    var groupPos = new Int2(
                        rand.Next(groupMin.X, groupMax.X),
                        rand.Next(groupMin.Y, groupMax.Y)
                    );
                    int spread = 10000;
                    for (int z = 0; z < 5; z++) {
                        var pos = groupPos + new Int2(rand.Next(-spread, spread), rand.Next(-spread, spread));
                        if (Math.Abs(Landscape.GetHeightMap().GetHeightAtF(SimulationWorld.SimulationToWorld(pos).toxz())) > 0.01f) continue;
                        var entity = PrefabRegistry.Instantiate(command, tree.Prefab);
                        command.SetComponent<ECTransform>(entity, new() {
                            Position = pos,
                            Orientation = (short)rand.Next(short.MinValue, short.MaxValue),
                        });
                    }
                }
                command.Commit();
            }

            /*using (new ProfilerMarker("Nav Test").Auto()) {
                //navigationSystem.Update();
                //World.GetComponentRef<ECTransform>(tcInstance).Position = new Int2(30000, 30000);
                World.GetComponentRef<ECTransform>(tcInstance).Position.X -= 6000;
                World.GetComponentRef<ECTransform>(tcInstance).Position.Y += 2000;
                navigationSystem.Update();
                //World.GetComponentRef<ECTransform>(tcInstance).Position.X += 6000;
                //navigationSystem.Update();
            }*/
        }

        public void Step(long dtMS) {
            if (Input.GetKeyPressed(KeyCode.Space)) {
                //World.GetComponentRef<ECTransform>(tcInstance).Position += new Int2(1000, 1000);
                World.GetComponentRef<ECTransform>(tcInstance).Position += new Int2(500, 0);
                World.GetComponentRef<CModel>(tcInstance);
                navigationSystem.Update();
            }
            World.GetOrCreateSystem<TimeSystem>().Step(dtMS);
            World.Step();
            World.GetOrCreateSystem<LifeSystem>().PurgeDead();
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
