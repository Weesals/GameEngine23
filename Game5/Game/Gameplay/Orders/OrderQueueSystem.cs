using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Utility;

namespace Game5.Game {
    /// <summary>
    /// Allow arbitrary actions to be pushed onto a list to be executed
    /// when the appropriate track(s) are available.
    /// Tracks can be:
    ///     - Forced: Action cannot be cancelled
    ///     - Active: Action is active, should wait but can cancel
    ///     - Idle: Action is not important, cancel if something else wants it
    ///     - Unoccupied: No action is present
    /// For example:
    /// - Daimyo cannot walk while training: DaimyoTrain{Move+Train+Interact}
    /// - Buildings can shoot while training: Train{Move+Train}
    /// - Snapfire can shoot while moving: Move{Move} + AutoFire{Interact}
    /// Actions can have target location or entity (covered by ActionRequest)
    /// Example Actions:
    /// - Move, MeleeAttack, RangedAttack, Train, DaimyoTrain, AutoFire, Build, Die
    /// Actions should defer to other systems for fulfilment
    /// ActionSystemBase? GetTrackState(), BeginAction(), CancelAction(), Dev#ForceCompleteAction()
    /// </summary>
    public partial class OrderQueueSystem : SystemBase
        , LifeSystem.IDestroyListener
        , IActionContainer {

        public static readonly int Track_Move = HashInt.Compute("Move");
        public static readonly int Track_Interact = HashInt.Compute("Interact");
        public static readonly int Track_Train = HashInt.Compute("Train");

        // A queue for a specific entity
        [SparseComponent]
        public struct EntityQueue {
            // This references a block within the `actions` array
            public RangeInt Queue;
        }

        // Where we attempt to begin actions
        public OrderDispatchSystem ActionDispatchSystem { get; private set; }
        public LifeSystem LifeSystem { get; private set; }
        public TimeSystem TimeSystem { get; private set; }

        // Each entity can have its own queue of action requests
        //private Dictionary<Entity, EntityQueue> entityQueue = new();
        // The data store for action request instances
        private SparseArray<OrderInstance> actions = new();

        protected override void OnCreate() {
            base.OnCreate();
            ActionDispatchSystem = World.GetOrCreateSystem<OrderDispatchSystem>();
            LifeSystem = World.GetOrCreateSystem<LifeSystem>();
            TimeSystem = World.GetOrCreateSystem<TimeSystem>();
            LifeSystem.RegisterDestroyListener(this, true);
        }
        protected override void OnDestroy() {
            LifeSystem.RegisterDestroyListener(this, false);
            base.OnDestroy();
        }

        private void VerifyQueue(EntityQueue queue) {
            actions.Slice(queue.Queue);
        }

        public OrderInstance CreateActionInstance(ActionRequest request) {
            request.Time = (uint)TimeSystem.TimeCurrentMS;
            return new OrderInstance() {
                Request = request,
                RequestId = ActionDispatchSystem.AllocateRequestId(request.ActionId),
            };
        }
        // Add an action request to be fulfilled at a later date
        public void EnqueueAction(Entity entity, OrderInstance action) {
            ref var queue = ref Stage.RequireComponent<EntityQueue>(entity, false);
            if (action.Request.TargetEntity != Entity.Null) {
                if (!Stage.IsValid(action.Request.TargetEntity)) {
                    Debug.Fail("Invalid entity detected");
                    action.Request.TargetEntity = Entity.Null;
                }
            }
            actions.Reallocate(ref queue.Queue, queue.Queue.Length + 1);
            actions[queue.Queue.End - 1] = action;
            VerifyQueue(queue);
        }
        public Span<OrderInstance> GetQueueForEntity(Entity entity) {
            ref var queue = ref Stage.RequireComponent<EntityQueue>(entity, false);
            if (queue.Queue.Length <= 0) return default;
            return actions.Slice(queue.Queue);
        }

        protected override void OnUpdate() {
            // Attempt to begin actions if all required tracks are free
            var trackStates = new OrderSystemBase.TrackStates();
            foreach (var accessor in World.QueryAll<EntityQueue>()) {
                var entity = accessor.Entity;
                ref var queue = ref accessor.Component1Ref;
                if (queue.Queue.Length == 0) {
                    Stage.RemoveComponent<EntityQueue>(entity);
                    continue;
                }
                trackStates.Clear();
                ActionDispatchSystem.GetTrackState(entity, ref trackStates);
                for (int q = 0; q < queue.Queue.Length; q++) {
                    var action = actions[queue.Queue.Start + q];
                    action = ActionDispatchSystem.GetActivation(entity, action, trackStates);
                    if (!action.IsValid) continue;
                    VerifyQueue(queue);
                    ActionDispatchSystem.BeginOrder(entity, action);
                    VerifyQueue(queue);

                    actions.Splice(ref queue.Queue, q, 1, 0);
                    VerifyQueue(queue);
                    break;
                }
            }
        }

        public void NotifyDestroyedEntities(HashSet<Entity> entities) {
            for (var it = actions.GetEnumerator(); it.MoveNext();) {
                var request = it.Current;
                var entity = request.Request.TargetEntity;
                if (!entities.Contains(entity)) continue;
                request.Request.TargetEntity = default;
                actions[it.Index] = request;
            }
        }

        public RequestEnumerator GetActionEnumerator(RequestId requestId) {
            return new RequestEnumerator(this, requestId);
        }

        private int GetActionIndex(Entity entity, RequestId requestId) {
            ref var queue = ref Stage.RequireComponent<EntityQueue>(entity, false);
            for (int i = queue.Queue.Start; i < queue.Queue.End; i++) {
                if (actions[i].RequestId == requestId) return i;
            }
            return -1;
        }
        public ActionRequest GetRequest(Entity entity, RequestId requestId) {
            var id = GetActionIndex(entity, requestId);
            if (id < 0) return default;
            return actions[id].Request;
        }
        public void CancelAction(Entity entity, RequestId requestId) {
            ref var queue = ref Stage.RequireComponent<EntityQueue>(entity, true);
            if (requestId.IsAll) {
                actions.Return(ref queue.Queue);
            } else {
                int index;
                for (index = queue.Queue.Start; index < queue.Queue.End; index++) {
                    if (actions[index].RequestId == requestId) break;
                }
                if (index >= queue.Queue.Length) return;
                for (int i = index; i < queue.Queue.Length; i++) {
                    actions[i] = actions[i + 1];
                }
                actions.Return(queue.Queue.End - 1);
                queue.Queue.Length--;
            }
            VerifyQueue(queue);
        }

        public struct RequestEnumerator : IEnumerator<Entity> {
            private TypedQueryIterator<EntityQueue> entityQueue;
            private SparseArray<OrderInstance> actions;
            public Entity Current => entityQueue.Current;
            object IEnumerator.Current => Current;
            public RequestId RequestId { get; private set; }
            public RequestEnumerator(OrderQueueSystem queueSystem, RequestId requestId) {
                entityQueue = queueSystem.World.QueryAll<EntityQueue>();
                actions = queueSystem.actions;
                RequestId = requestId;
            }
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                while (true) {
                    if (!entityQueue.MoveNext()) break;
                    var queue = actions.Slice(entityQueue.Current.Component1Ref.Queue);
                    for (int i = 0; i < queue.Count; i++) {
                        if (queue[i].RequestId.MatchesPattern(RequestId)) return true;
                    }
                }
                return false;
            }
        }
    }

}
