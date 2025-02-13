﻿using Navigation;
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

    public class EntityProxy : IItemPosition, IEntitySelectable, IEntityTeam, IEntityFootprint, IItemRedirect, IItemStringifier {

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

        public int GetTeam(ulong id) {
            var entity = EntityProxyExt.UnpackEntity(id);
            if (World.IsValid(entity))
                return World.GetComponent<ECTeam>(entity).SlotId;
            return -1;
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

        public EntityFootprint GetEntityFootprint(ulong id = 0) {
            var entity = EntityProxyExt.UnpackEntity(id);
            if (entity.IsValid) {
                var protoData = World.GetComponent<PrototypeData>(entity);
                return protoData.Footprint;
            }
            return default;
        }
    }

    public class LandscapeProxy : LandscapeRenderer, IEntityFootprint, IItemPositionRO {
        public EntityFootprint GetEntityFootprint(ulong id = 0) {
            return new EntityFootprint() {
                Size = Bounds.Size.toxz() * SimulationWorld.InvWorldScale,
                Shape = EntityFootprint.Shapes.Box,
            };
        }

        Vector3 IItemPositionRO.GetPosition(ulong id) => new(Bounds.Centre.X, 0f, Bounds.Centre.Z);
        Quaternion IItemPositionRO.GetRotation(ulong id) => Quaternion.Identity;
    }

    public class Simulation {

        public World World { get; private set; }
        public LandscapeData Landscape { get; private set; }

        public PrefabLoader PrefabLoader { get; private set; }
        public EntityProxy EntityProxy { get; private set; }
        public TimeSystem TimeSystem { get; private set; }
        public ProtoSystem ProtoSystem { get; private set; }
        public OrderQueueSystem ActionQueueSystem { get; private set; }
        public OrderDispatchSystem ActionDispatchSystem { get; private set; }
        public EntityMapSystem EntityMapSystem { get; private set; }
        public LifeSystem LifeSystem { get; private set; }
        public PrefabRegistry PrefabRegistry { get; private set; }

        public NavigationSystem NavigationSystem { get; private set; }

        public class ProfiledBootstrap : SystemBootstrap {
            public override T CreateSystem<T>(EntityContext context) {
                using var marker = new ProfilerMarker("NewSys " + typeof(T).Name).Auto();
                return base.CreateSystem<T>(context);
            }
            public override void UpdateSystem<T>(T system) {
                using (new ProfilerMarker(system.Name).Auto()) {
                    base.UpdateSystem(system);
                }
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
            NavigationSystem = World.GetOrCreateSystem<NavigationSystem>();
            World.GetOrCreateSystem<OrderMoveSystem>();
            World.GetOrCreateSystem<OrderAttackSystem>();
            World.GetOrCreateSystem<OrderTrainSystem>();

            PrefabRegistry = ProtoSystem.PrefabRegistry;
        }
        public void SetLandscape(LandscapeData landscape) {
            Landscape = landscape;
            EntityMapSystem.SetLandscape(landscape);
            NavigationSystem.SetLandscape(landscape);
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
            var archerModel = Resources.LoadModel("./Assets/Characters/Character_Archer.fbx", out var archerHandle);

            archerHandle.Then(() => {
                // Character FBX references incorrect texture
                foreach (var mesh in archerModel.Meshes) {
                    mesh.Material.SetTexture("Texture", Resources.LoadTexture("./Assets/Characters/T_CharactersAtlas.png"));
                }
            });

            archerHandle.Complete();
            loadMarker.Dispose();

            var prefabMarker = new ProfilerMarker("Creating Prefabs").Auto();

            PrefabLoader = new PrefabLoader(ProtoSystem);
            PrefabLoader.RegisterSerializer((ref AnimationHandle handle, SJson serializer) => {
                foreach (var field in serializer.GetFields()) {
                    var model = Resources.LoadModel(field.Key.ToString(), out var loadHandle);
                    loadHandle.Complete();
                    handle = model.Animations[field.Value.ToString()];
                }
            });
            var archer = PrefabLoader.LoadPrototype("./Assets/Prefabs/Archer.json");
            var chicken = PrefabLoader.LoadPrototype("./Assets/Prefabs/Chicken.json");
            var house = PrefabLoader.LoadPrototype("./Assets/Prefabs/House.json");
            var townCentre = PrefabLoader.LoadPrototype("./Assets/Prefabs/TownCentre.json");
            var tree = PrefabLoader.LoadPrototype("./Assets/Prefabs/Tree.json");

            prefabMarker.Dispose();

            var housesCmdBuffer = new EntityCommandBuffer(World.Manager);
            var housesJob = JobHandle.Schedule(() => {
                using (new ProfilerMarker("Creating Houses").Auto()) {
                    var command = housesCmdBuffer;
#if DEBUG
                    const int Count = 115 * 4;
#else
                    const int Count = 2000000 * 4;
#endif
                    var sqrtCount = (int)MathF.Sqrt(Count);
                    int totCount = 0;
                    for (int i = 0; i < Count; i++) {
                        var pos = 2000 + new Int2(i / sqrtCount, i % sqrtCount) * 6000;
                        if (rand.Next(0, 4) != 0) continue;
                        if (Math.Abs(Landscape.GetHeightMap().GetHeightAtF(SimulationWorld.SimulationToWorld(pos).toxz())) > 0.01f) continue;
                        var newEntity = PrefabRegistry.Instantiate(command, house.Prefab);
                        command.AddComponent<ECTransform>(newEntity) = new() {
                            Position = pos,
                            Orientation = (short)(rand.Next(4) * (short.MinValue / 2))
                        };
                        command.AddComponent<ECTeam>(newEntity) = new() {
                            SlotId = 1,
                        };
                        command.MutateComponent<CModel>(newEntity).Variant = i;
                        //command.RemoveComponent<ECObstruction>(newEntity);
                        ++totCount;
                    }
                    Trace.WriteLine($"Spawned {totCount} houses");
                }
            });

            var treesCmdBuffer = new EntityCommandBuffer(World.Manager);
            var treesJob = JobHandle.Schedule(() => {
                using (new ProfilerMarker("Creating Trees").Auto()) {
                    var command = treesCmdBuffer;
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
                }
            });

            if (true) {
                using (new ProfilerMarker("Creating Test Entities").Auto()) {
                    tcInstance = PrefabRegistry.Instantiate(World, townCentre.Prefab);
                    World.GetComponentRef<ECTransform>(tcInstance).Position = new Int2(50000, 50000);

                    var archerInstance = PrefabRegistry.Instantiate(World, archer.Prefab);
                    World.GetComponentRef<ECTransform>(archerInstance).Position = new Int2(40000, 28000);

                    //var chickenInstance = PrefabRegistry.Instantiate(World, chicken.Prefab);
                    //World.GetComponentRef<ECTransform>(chickenInstance).Position = new Int2(50000, 28000);

                    //World.GetComponentRef<ECTransform>(World.CreateEntity(tcInstance)).Position += new Int2(6000, 1000);
                    World.GetComponentRef<ECTransform>(World.CreateEntity(archerInstance)).Position += new Int2(1000, 1000);
                    World.GetComponentRef<ECTransform>(World.CreateEntity(archerInstance)).Position += new Int2(2000, 1000);
                    World.GetComponentRef<ECTransform>(World.CreateEntity(archerInstance)).Position += new Int2(3000, 1000);
                    World.Manager.ColumnStorage.Validate?.Invoke();
                    World.GetComponentRef<ECTransform>(World.CreateEntity(archerInstance)).Position += new Int2(0000, 2000);
                    World.GetComponentRef<ECTransform>(World.CreateEntity(archerInstance)).Position += new Int2(1000, 2000);
                    World.GetComponentRef<ECTransform>(World.CreateEntity(archerInstance)).Position += new Int2(2000, 2000);
                    World.GetComponentRef<ECTransform>(World.CreateEntity(archerInstance)).Position += new Int2(3000, 2000);
                }
            }
            using (new ProfilerMarker("Committing").Auto()) {
                treesJob.Complete();
                treesCmdBuffer.Commit();
                housesJob.Complete();
                housesCmdBuffer.Commit();
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
                NavigationSystem.Update();
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
