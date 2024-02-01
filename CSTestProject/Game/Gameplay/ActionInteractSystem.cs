using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Utility;

namespace Weesals.Game {
    using CompletionInstance = OrderSystemBase.CompletionInstance;

    [UpdateAfter(typeof(NavigationSystem))]
    public partial class ActionInteractSystem : ActionSystemBase
        , ILateUpdateSystem {

        public interface IStrikeListener {
            JobHandle NotifyStrikes(JobHandle dependency, Span<StrikeEvent> strikeEvents);
        }

        public struct StrikeEvent {
            public Entity Source;
            public Entity Target;
            public RequestId RequestId;
            public int InteractionId;
            public int Ticks;
        }

        public ProtoSystem ProtoSystem { get; private set; }
        public JobHandle Dependency { get; private set; }

        // TODO: A NativeMultiHashSet might work better, separating by InteractionId
        protected PooledList<StrikeEvent> strikeEvents;
        private List<IStrikeListener> strikeListeners = new();
        private QueryId query = default;

        protected override void OnCreate() {
            base.OnCreate();
            ProtoSystem = World.GetOrCreateSystem<ProtoSystem>();
            strikeEvents = new(64);
        }
        protected override void OnDestroy() {
            strikeEvents.Dispose();
            base.OnDestroy();
        }

        public override bool Begin(Entity entity, OrderDispatchSystem.ActionActivation action) {
            return Begin(entity, action, 1000);
        }
        public bool Begin(Entity entity, OrderDispatchSystem.ActionActivation action, int strikeInterval) {
            var target = action.Request.TargetEntity;
            // In range, try to attack
            World.AddComponent(entity, new ECActionInteractMelee() {
                RequestId = action.RequestId,
                StartTime = (int)TimeSystem.TimeCurrentMS,
                Target = target,
                InteractionId = 0,
                StrikeInterval = strikeInterval,
            });
            return true;
        }
        public override void Cancel(Entity entity, RequestId requestId) {
            World.RemoveComponent<ECActionInteractMelee>(entity);
        }

        protected override void OnUpdate() {
            var time = TimeSystem.GetInterval();
            var tformLookup = GetComponentLookup<ECTransform>(false);
            var completions = this.completions;
            var strikeEvents = this.strikeEvents;
            var mobileLookup = GetComponentLookup<ECMobile>(true);
            strikeEvents.Clear();
            EntityCommandBuffer cmdBuffer = new(Stage);
            foreach (var accessor in World.QueryAll<ECActionInteractMelee>()) {
                Entity entity = accessor;
                ECActionInteractMelee interact = accessor;
                if (interact.Target != Entity.Null) {
                    var tform = tformLookup[entity];
                    var otform = tformLookup[interact.Target];

                    var oprotoData = ProtoSystem.GetPrototypeData(interact.Target);
                    var targetPos = Int2.Clamp(
                        tform.Position,
                        otform.Position - oprotoData.Footprint.Size / 2,
                        otform.Position + oprotoData.Footprint.Size / 2
                    );

                    if (Int2.DistanceSquared(targetPos, tform.Position) <= 1000 * 1000) {
                        var delta = otform.Position - tform.Position;
                        var mobile = mobileLookup.GetRefOptional(entity, false);
                        NavigationSystem.RotateTowardFacing(ref tform, delta, mobile.HasValue ? mobile.Value : default, time.DeltaTimeMS);
                        //tform.ValueRW.SetFacing(delta);
                        // In range, perform attacks at specified interval
                        var ticks = time.GetIntervalTicks(interact.StrikeInterval, interact.StartTime + 250);
                        if (ticks > 0) {
                            strikeEvents.Add(new StrikeEvent() {
                                Source = entity,
                                Target = interact.Target,
                                InteractionId = interact.InteractionId,
                                RequestId = interact.RequestId,
                                Ticks = ticks,
                            });
                        }
                        return;
                    }
                }
                // Failed to attack, remove action
                cmdBuffer.RemoveComponent<ECActionInteractMelee>(entity);
                completions.Add(new CompletionInstance(entity, interact.RequestId));
            }
            if (!Stage.QueryMightHaveMatches(query)) {
                JobHandle strikeResponse = default;
                foreach (var listener in strikeListeners) {
                    strikeResponse = JobHandle.CombineDependencies(
                        strikeResponse,
                        listener.NotifyStrikes(Dependency, strikeEvents)
                    );
                }
                Dependency = JobHandle.CombineDependencies(strikeResponse, Dependency);
            }
        }

        public new void OnLateUpdate() {
            base.OnLateUpdate();
            strikeEvents.Clear();
        }

        public override void NotifyDestroyedEntities(HashSet<Entity> entities) {
            foreach (var accessor in World.QueryAll<ECActionInteractMelee>()) {
                ref ECActionInteractMelee attack = ref accessor.Component1Ref;
                if (entities.Contains(attack.Target)) attack.Target = Entity.Null;
            }
        }

        public void RegisterStrikeListener(IStrikeListener listener, bool enable) {
            if (enable) strikeListeners.Add(listener);
            else strikeListeners.Remove(listener);
        }

    }
}
