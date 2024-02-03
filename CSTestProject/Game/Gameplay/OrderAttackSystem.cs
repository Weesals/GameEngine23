using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine.Jobs;
using Weesals.Utility;

namespace Weesals.Game {
    public partial class OrderAttackSystem : OrderSystemBase
        , NavigationSystem.INavigationListener
        , ActionInteractSystem.ICompletionListener
        , ActionInteractSystem.IStrikeListener
        , IRollbackSystem {

        public const int ActionId = 2;
        public override int Id => ActionId;

        protected LifeSystem.DamageApplier damageApplier;

        public LifeSystem LifeSystem { get; private set; }
        public NavigationSystem NavigationSystem { get; private set; }
        public ActionInteractSystem ActionInteractSystem { get; private set; }
        //public CombatRangedSystem CombatRangedSystem { get; private set; }
        public PlayerSystem PlayerSystem { get; private set; }

        protected override void OnCreate() {
            base.OnCreate();
            LifeSystem = World.GetOrCreateSystem<LifeSystem>();
            NavigationSystem = World.GetOrCreateSystem<NavigationSystem>();
            ActionInteractSystem = World.GetOrCreateSystem<ActionInteractSystem>();
            //CombatRangedSystem = World.GetOrCreateSystem<CombatRangedSystem>();
            PlayerSystem = World.GetOrCreateSystem<PlayerSystem>();
            damageApplier.Allocate();
            RegisterActivation(true);
        }
        protected override void OnDestroy() {
            RegisterActivation(false);
            damageApplier.Dispose();
            base.OnDestroy();
        }

        protected override void RegisterActivation(bool enable) {
            NavigationSystem.RegisterCompleteListener(this, enable);
            ActionInteractSystem.RegisterCompletionListener(this, enable);
            //CombatRangedSystem.RegisterCompletionListener(this, enable);
            ActionInteractSystem.RegisterStrikeListener(this, enable);
        }

        // If the request was for movement, then signal that we are able to handle it
        public override float ScoreRequest(Entity entity, OrderDispatchSystem.OrderInstance order) {
            if (order.Request.HasType(ActionTypes.Attack) && order.Request.HasValidTarget) {
                if (order.Request.Type != ActionTypes.Attack && order.Request.TargetEntity.Equals(entity)) return 0f;
                if (entity == Entity.Null) return 0f;
                var allegiance = PlayerSystem.GetAllegiance(entity, order.Request.TargetEntity);
                if (allegiance.CanAttack) return 2f;
            }
            return base.ScoreRequest(entity, order);
        }
        public override void GetTrackStates(Entity entity, in ActionRequest request, ref TrackStates trackStates) {
            trackStates.SetTrackState(OrderQueueSystem.Track_Move, 1);
            trackStates.SetTrackState(OrderQueueSystem.Track_Interact, 1);
        }

        // Mutate the entity so that it begins performing the movement
        public override bool Begin(Entity entity, in OrderDispatchSystem.ActionActivation action) {
            base.Begin(entity, in action);
            return UpdateActionRequest(entity, action);
        }
        public override void Cancel(Entity entity, RequestId requestId) {
            NavigationSystem.Cancel(entity, requestId);
            ActionInteractSystem.Cancel(entity, requestId);
            //CombatRangedSystem.Cancel(entity, requestId);
            base.Cancel(entity, requestId);
        }

        // The action state has been cleared, must be reprocessed
        private bool UpdateActionRequest(Entity entity, OrderDispatchSystem.ActionActivation action) {
            var target = action.Request.TargetEntity;
            if (!target.IsValid) return false;

            var rangedLookup = GetComponentLookup<ECAbilityAttackRanged>();
            bool isRanged = rangedLookup.TryGetComponent(entity, out var ranged);
            int range = isRanged ? ranged.Range : 1000;

            var result = NavigationSystem.BeginMoveToTarget(entity, action, range);
            switch (result) {
                case NavigationSystem.MoveResults.Walking: return true;
                case NavigationSystem.MoveResults.Arrived: {
                    if (isRanged) {
                        //return CombatRangedSystem.Begin(entity, action);
                    } else {
                        var meleeLookup = GetComponentLookup<ECAbilityAttackMelee>(false);
                        if (!meleeLookup.HasComponent(entity)) return false;
                        return ActionInteractSystem.Begin(entity, action, meleeLookup[entity].Interval);
                    }
                } break;
            }
            return false;
        }
        // Same as above, but we need to find the Action data
        private bool UpdateActionRequest(Entity entity, RequestId requestId) {
            var action = OrderDispatchSystem.GetAction(entity, requestId);
            if (!action.IsValid) return false;
            action.RequestId = action.RequestId.WithActionId(Id);

            if (UpdateActionRequest(entity, action)) return true;
            NotifyActionComplete(requestId, entity);
            return true;
        }

        // When movement is complete
        public void NotifyNavigationCompleted(HashSet<CompletionInstance> completions) {
            foreach (var completion in completions) {
                if (completion.RequestId.ActionId != Id) return;
                UpdateActionRequest(completion.Entity, completion.RequestId);
            }
        }
        // When combat can no longer continue (unit dead or out of range)
        public void NotifyCombatCompleted(HashSet<CompletionInstance> completions) {
            foreach (var completion in completions) {
                if (completion.RequestId.ActionId != Id) return;
                UpdateActionRequest(completion.Entity, completion.RequestId);
            }
        }
        // When the animation passes the Strike trigger
        public JobHandle NotifyStrikes(JobHandle dependency, Span<ActionInteractSystem.StrikeEvent> strikeEvents) {
            dependency.Complete();
            var actionId = Id;
            var meleeLookup = GetComponentLookup<ECAbilityAttackMelee>(true);
            var dmgApplier = this.damageApplier;
            var toRemove = new PooledHashSet<CompletionInstance>(4);
            dmgApplier.Begin(LifeSystem);
            //Job.WithCode(() => {
            foreach (var strikeEvent in strikeEvents) {
                // This is not a strike from us, ignore it.
                if (strikeEvent.RequestId.ActionId != actionId) continue;

                int damage = 0;
                if (meleeLookup.HasComponent(strikeEvent.Source))
                    damage = meleeLookup[strikeEvent.Source].Damage;
                damage *= strikeEvent.Ticks;
                if (damage == 0) continue;
                var dmgResult = dmgApplier.ApplyDamage(strikeEvent.Target, damage);
                if (dmgResult.IsKilled) {
                    toRemove.Add(new CompletionInstance() {
                        Entity = strikeEvent.Source,
                        RequestId = strikeEvent.RequestId,
                    });
                }
            }
            //}).Run();
            dmgApplier.Flush(LifeSystem);
            if (!toRemove.IsEmpty) {
                foreach (var completion in toRemove) {
                    Cancel(completion.Entity, completion.RequestId);
                    //NotifyActionComplete(completion.RequestId, completion.Entity);
                }
            }
            return default;
        }

        public void CopyStateFrom(World other) {
            Debug.Assert(damageApplier.IsEmpty);
        }
    }
}
