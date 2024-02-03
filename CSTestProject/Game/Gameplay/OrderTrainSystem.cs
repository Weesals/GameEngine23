using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Game {
    /// <summary>
    /// Allows an entity to spawn other entities after generating the required TrainPoints
    /// </summary>
    public partial class OrderTrainSystem : OrderSystemBase
        , AccrualSystem.ICompleteListener {

        public const int Identifier = 3;
        public override int Id => Identifier;

        public ProtoSystem ProtoSystem { get; private set; }
        public AccrualSystem AccumulationSystem { get; private set; }
        public OrderQueueSystem ActionQueueSystem { get; private set; }
        public NavigationSystem NavigationSystem { get; private set; }

        protected override void OnCreate() {
            base.OnCreate();
            ProtoSystem = World.GetOrCreateSystem<ProtoSystem>();
            AccumulationSystem = World.GetOrCreateSystem<AccrualSystem>();
            ActionQueueSystem = World.GetOrCreateSystem<OrderQueueSystem>();
            NavigationSystem = World.GetOrCreateSystem<NavigationSystem>();
            RegisterActivation(true);
        }
        protected override void OnDestroy() {
            RegisterActivation(false);
            base.OnDestroy();
        }
        protected override void RegisterActivation(bool enable) {
            AccumulationSystem.RegisterCompleteListener(this, enable);
        }

        public override float ScoreRequest(Entity entity, OrderDispatchSystem.OrderInstance action) {
            return 0f;
        }
        public override bool Begin(Entity entity, in OrderDispatchSystem.ActionActivation action) {
            base.Begin(entity, action);
            AccumulationSystem.CreateInstance(entity, action, 1000);
            Debug.WriteLine($"Training {action.RequestId}");
            return true;
        }
        public override void GetTrackStates(Entity entity, in ActionRequest request, ref TrackStates trackStates) {
            trackStates.SetTrackState(OrderQueueSystem.Track_Train, 1);
        }

        public struct SpawnedEntity {
            public Entity Owner;
            public Entity Spawn;
        }
        public void NotifyAccumulationCompleted(HashSet<CompletionInstance> completions) {
            var tformLookup = GetComponentLookup<ECTransform>();
            var ownerLookup = GetComponentLookup<ECTeam>();
            var cmdBuffer = new EntityCommandBuffer(Stage);
            var spawnedEntities = new PooledList<SpawnedEntity>(32);
            foreach (var completion in completions) {
                var action = OrderDispatchSystem.GetAction(completion.Entity, completion.RequestId);
                if (!action.IsValid) continue;
                var spawnerTform = tformLookup[completion.Entity];
                var ownerId = ownerLookup[completion.Entity].SlotId;
                var pos = spawnerTform.Position;
                //pos += new int2(0, -2000);
                var protoData = ProtoSystem.GetPrototypeData(action.Request.Data1);
                var prefabMobile = Stage.GetComponent<ECMobile>(protoData.Prefab);
                pos -= spawnerTform.GetFacing(64);
                pos = NavigationSystem.FindNearestPathable(prefabMobile.NavMask, pos);
                var entity = World.CreateEntity(protoData.Prefab);
                cmdBuffer.SetComponent(entity, new ECTeam() { SlotId = ownerId, });
                cmdBuffer.SetComponent(entity, new ECTransform() {
                    Position = pos,
                    Orientation = (short)(spawnerTform.Orientation + short.MinValue),
                });
                NotifyActionComplete(completion.RequestId, completion.Entity);
                spawnedEntities.Add(new SpawnedEntity() {
                    Owner = completion.Entity,
                    Spawn = entity,
                });
            }
            cmdBuffer.Commit();
            if (!spawnedEntities.IsEmpty) {
                var queueCopy = new PooledList<OrderDispatchSystem.OrderInstance>(8);
                foreach (var item in spawnedEntities) {
                    var queue = ActionQueueSystem.GetQueueForEntity(item.Owner);
                    queueCopy.Clear();
                    for (int i = 0; i < queue.Length; i++) {
                        var action = queue[i];
                        // TODO: Fix this. HACK: Dont copy Train actions
                        if (action.Request.ActionId != -1) continue;
                        queueCopy.Add(action);
                    }
                    var spawn = item.Spawn;
                    if (queueCopy.Count == 0) {
                        var tform = Stage.GetComponent<ECTransform>(spawn);
                        var request = new ActionRequest(ActionTypes.Move) {
                            TargetLocation = tform.Position + tform.GetFacing(1024),
                        };
                        ActionQueueSystem.EnqueueAction(spawn, ActionQueueSystem.CreateActionInstance(request));
                    } else {
                        foreach (var action in queueCopy) {
                            ActionQueueSystem.EnqueueAction(spawn, action);
                        }
                    }
                }
                queueCopy.Dispose();
            }
        }

        public Int2 GetProgress(GenericTarget entity, RequestId requestId) {
            return AccumulationSystem.GetProgress(entity.GetEntity(), requestId);
        }
    }
}
