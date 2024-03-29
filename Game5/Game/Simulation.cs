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
        public VisualsCollection EntityVisuals { get; private set; }

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
        public void SetVisuals(VisualsCollection entityVisuals) {
            EntityVisuals = entityVisuals;
        }

        public void GenerateWorld() {
            var rand = new Random();
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

            var chickenModel = Resources.LoadModel("./Assets/Models/Ignore/chickenV2.fbx", out var chickenHandle);
            var archerModel = Resources.LoadModel("./Assets/Characters/Character_Archer.fbx", out var archerHandle);
            var idleAnim = Resources.LoadModel("./Assets/Characters/Animation_Idle.fbx", out var idleAnimHandle);
            var runAnim = Resources.LoadModel("./Assets/Characters/Animation_Run.fbx", out var runAnimHandle);
            var animHandles = JobHandle.CombineDependencies(idleAnimHandle, runAnimHandle);
            var houseModels = new[] {
                Resources.LoadModel("./Assets/SM_House.fbx", out var house1Handle),
                Resources.LoadModel("./Assets/B_House2.fbx", out var house2Handle),
                Resources.LoadModel("./Assets/B_House3.fbx", out var house3Handle),
                Resources.LoadModel("./Assets/B_Granary1.fbx", out var granary1Handle),
                Resources.LoadModel("./Assets/B_Granary2.fbx", out var granary2Handle),
                Resources.LoadModel("./Assets/B_Granary3.fbx", out var granary3Handle),
            };
            var houseHandles = JobHandle.CombineDependencies(house1Handle, house2Handle, house3Handle);
            var granaryHandles = JobHandle.CombineDependencies(granary1Handle, granary2Handle, granary3Handle);
            var modelLoadHandle = JobHandle.CombineDependencies(archerHandle, chickenHandle, animHandles, houseHandles, granaryHandles);

            modelLoadHandle.Complete();

            foreach (var mesh in archerModel.Meshes) {
                mesh.Material.SetTexture("Texture", Resources.LoadTexture("./Assets/T_CharactersAtlas.png"));
            }
            foreach (var houseModel in houseModels) {
                foreach (var mesh in houseModel.Meshes) {
                    mesh.Material.SetTexture("Texture", Resources.LoadTexture("./Assets/T_ToonBuildingsAtlas.png"));
                }
            }
            //var houseModel = (AnimatedModel)FBXImporter.Import("./Assets/Characters/TestAnim.fbx");

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
                .AddComponent<ECTransform>(new() { Position = default, Orientation = short.MinValue })
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

            var tcInstance = PrefabRegistry.Instantiate(World, townCentre.Prefab);
            World.GetComponentRef<ECTransform>(tcInstance).Position = new Int2(50000, 50000);

            var archerInstance = PrefabRegistry.Instantiate(World, archer.Prefab);
            World.GetComponentRef<ECTransform>(archerInstance).Position = new Int2(40000, 28000);

            var chickenInstance = PrefabRegistry.Instantiate(World, chicken.Prefab);
            World.GetComponentRef<ECTransform>(chickenInstance).Position = new Int2(50000, 28000);

            var houseProto = new PrototypeData() {
                Footprint = new EntityFootprint() { Size = 4000, Height = 200, Shape = EntityFootprint.Shapes.Box, },
            };

            var command = new EntityCommandBuffer(World.Stage);
            const int Count = 25;
            var SqrtCount = (int)MathF.Sqrt(Count);
            for (int i = 0; i < Count; i++) {
                var newEntity = command.CreateDeferredEntity();
                var pos = 2000 + new Int2(i / SqrtCount, i % SqrtCount) * 6000;
                if (Math.Abs(Landscape.GetHeightMap().GetHeightAtF(SimulationWorld.SimulationToWorld(pos).toxz())) > 0.01f) continue;
                var houseId = rand.Next(houseModels.Length);
                var orientation = rand.Next(4) * (short.MinValue / 2);
                command.AddComponent<CModel>(newEntity) = new() { PrefabName = "House", };
                command.AddComponent<CHitPoints>(newEntity) = new() { Current = 10, };
                command.AddComponent<ECTransform>(newEntity) = new() { Position = pos, Orientation = (short)orientation };
                //command.AddComponent<ECMobile>(newEntity) = new() { MovementSpeed = 10000, TurnSpeed = 500, NavMask = 1, };
                command.AddComponent<ECTeam>(newEntity) = new() { SlotId = (byte)i };
                command.AddComponent<ECAbilityAttackMelee>(newEntity) = new() { Damage = 1, Interval = 1000, };
                command.AddComponent<PrototypeData>(newEntity) = houseProto;
                //command.AddComponent<ECObstruction>(newEntity);
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
        public ItemReference HitTest(Ray ray) {
            float nearestDst2 = float.MaxValue;
            ItemReference nearest = ItemReference.None;
            foreach (var accessor in World.QueryAll<ECTransform, CModel>()) {
                var epos = (ECTransform)accessor;
                var emodel = (CModel)accessor;
                var prefab = EntityVisuals.GetVisuals(emodel.PrefabName);
                if (prefab == null) continue;
                foreach (var model in prefab.Models) {
                    foreach (var mesh in model.Meshes) {
                        var lray = ray;
                        lray.Origin -= SimulationWorld.SimulationToWorld(epos.GetPosition3());
                        var dst = mesh.BoundingBox.RayCast(lray);
                        if (dst >= 0f && dst < nearestDst2) {
                            nearest = EntityProxy.MakeHandle(accessor);
                            nearestDst2 = dst;
                        }
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
