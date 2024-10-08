﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals;
using Weesals.ECS;
using Weesals.Utility;

namespace Game5.Game {
    public interface IActionContainer {
        ActionRequest GetRequest(Entity entity, RequestId requestId);
        void CancelAction(Entity entity, RequestId requestId);
    }

    // An action request for a specific Entity
    public struct OrderInstance {
        // The location/target/type of the request
        public ActionRequest Request;
        public RequestId RequestId;
        public bool IsValid => RequestId.IsValid;
        public bool IsReady => RequestId.ActionId != 0;
        public override string ToString() { return RequestId.ToString(); }
        public static readonly OrderInstance Invalid = new OrderInstance() { RequestId = RequestId.Invalid, };
    }

    public partial class OrderDispatchSystem : SystemBase
        , OrderSystemBase.ICompletionListener
        , LifeSystem.IDestroyListener
        , IActionContainer {

        public enum OrderTypes { Train, Upgrade, Interact, }

        public OrderQueueSystem ActionQueueSystem { get; private set; }
        public LifeSystem LifeSystem { get; private set; }

        public struct ActionActivation : IEquatable<ActionActivation> {
            public ActionRequest Request;
            public RequestId RequestId;
            public ActionActivation(in ActionRequest request, RequestId requestId) {
                Request = request;
                RequestId = requestId;
                Debug.Assert(RequestId.ActionId != 0);
            }
            public int ActionIndex => RequestId.ActionId;
            public bool IsValid => RequestId.IsValid;
            public static readonly ActionActivation Invalid = new ActionActivation() { RequestId = RequestId.Invalid, };
            public bool Equals(ActionActivation other) { return RequestId == other.RequestId && Request.Equals(other.Request); }
        }

        private List<OrderSystemBase> registeredOrders = new();
        private MultiHashMap<Entity, ActionActivation> activeActionsByEntityId;

        private int requestCounter;

        protected override void OnCreate() {
            base.OnCreate();
            activeActionsByEntityId = new(512);
            ActionQueueSystem = World.GetOrCreateSystem<OrderQueueSystem>();
            LifeSystem = World.GetOrCreateSystem<LifeSystem>();
            LifeSystem.RegisterDestroyListener(this, true);
        }
        protected override void OnDestroy() {
            LifeSystem.RegisterDestroyListener(this, false);
            activeActionsByEntityId.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate() {
        }

        public OrderInstance GetActivation(Entity entity, OrderInstance order, OrderSystemBase.TrackStates trackStates) {
            var actionSystem = GetOrderForRequest(entity, order);
            if (actionSystem == null) return default;
            order.RequestId = order.RequestId.WithActionId(actionSystem.Id);
            trackStates.SetIsFlagging(true);
            actionSystem.GetTrackStates(entity, order.Request, ref trackStates);
            if (trackStates.Flagging == 2) return default;
            return order;
        }
        // Find the relevant ActionSystem and attempt to begin executing the action
        public bool BeginOrder(Entity entity, OrderInstance order) {
            if (!order.IsValid) return false;
            var actionSystem = registeredOrders[order.RequestId.ActionId];
            var activation = new ActionActivation(order.Request, order.RequestId);
            if (!actionSystem.Begin(entity, activation)) return false;
            ForceActivatedAction(entity, activation);
            return true;
        }
        public void ForceActivatedAction(Entity entity, ActionActivation activation) {
            activeActionsByEntityId.Add(entity, activation);
        }

        // Register an ActionSystem capable of handling action reuests
        public void RegisterOrder(OrderSystemBase order, bool enable) {
            order.RegisterCompletionListener(this, enable);
            if (enable) {
                while (order.Id >= registeredOrders.Count) registeredOrders.Add(default);
                Debug.Assert(registeredOrders[order.Id] == null, "Invalid action!");
            }
            registeredOrders[order.Id] = enable ? order : default;
        }

        internal RequestId AllocateRequestId(int id) {
            return new RequestId((id << 24) | ((++requestCounter) & 0x00ffffff));
        }
        // Find the most ideal ActionSystem for a given request
        public OrderSystemBase GetOrderForRequest(Entity entity, OrderInstance request) {
            if (request.Request.ActionId != -1) return registeredOrders[request.Request.ActionId];
            var bestScore = 0f;
            OrderSystemBase bestSystem = default;
            for (int i = 0; i < registeredOrders.Count; i++) {
                var action = registeredOrders[i];
                if (action == null) continue;
                var score = action.ScoreRequest(entity, request);
                if (score > bestScore) {
                    bestScore = score;
                    bestSystem = registeredOrders[i];
                }
            }
            return bestSystem;
        }

        // TODO: Actions will occupy certain tracks (ie. Movement)
        // this should return > 0 if a track is busy
        // Actions will not attempt to be begun if any of their
        // required tracks are busy
        public void GetTrackState(Entity entity, ref OrderSystemBase.TrackStates trackStates) {
            foreach (var activation in activeActionsByEntityId.GetValuesForKey(entity)) {
                var action = registeredOrders[activation.ActionIndex];
                action.GetTrackStates(entity, activation.Request, ref trackStates);
            }
        }

        // TODO: This makes testing easier, it will not be used for users
        public void ForceComplete(Entity entity) {

        }

        public MultiHashMap<Entity, ActionActivation>.Enumerator GetActionsForEntity(Entity entity) {
            return activeActionsByEntityId.GetValuesForKey(entity);
        }

        public struct ActionGroup {
            public int Index;
            public int Count;
        }
        // Gets our index within the group of entities that were issued the same action request
        public ActionGroup GetActionGroup(Entity entity, RequestId requestId) {
            ActionGroup group = default;
            foreach (var action in activeActionsByEntityId) {
                if (!action.Value.RequestId.MatchesPattern(requestId)) continue;
                if (action.Key.Index < entity.Index) ++group.Index;
                ++group.Count;
            }
            for (var it = ActionQueueSystem.GetActionEnumerator(requestId); it.MoveNext();) {
                if (it.Current.Index < entity.Index) ++group.Index;
                ++group.Count;
            }
            return group;
        }
        public ActionActivation GetAction(Entity entity, RequestId requestId) {
            foreach (var item in GetActionsForEntity(entity)) {
                if (item.RequestId.Pattern == requestId.Pattern) return item;
            }
            return ActionActivation.Invalid;
        }

        public ActionRequest GetRequest(Entity entity, RequestId requestId) {
            foreach (var action in activeActionsByEntityId.GetValuesForKey(entity)) {
                if (action.RequestId == requestId) return action.Request;
            }
            return default;
        }

        // An action has been completed. It should be removed from the active list
        public void NotifyCompletion(int actionId, OrderSystemBase.CompletionInstance completion) {
            for (var it = activeActionsByEntityId.GetValuesForKey(completion.Entity); it.MoveNext();) {
                var item = it.Current;
                if (item.ActionIndex != actionId) continue;
                if (completion.RequestId.IsValid && !item.RequestId.Equals(completion.RequestId)) continue;

                registeredOrders[item.ActionIndex].Cancel(completion.Entity, completion.RequestId);
                it.RemoveSelf();
                break;
            }
        }

        // Entities are dead, make sure we are not targeting them
        public void NotifyDestroyedEntities(HashSet<Entity> entities) {
            foreach (var entity in entities) {
                using var toDelete = new PooledList<RequestId>(4);
                foreach (var action in activeActionsByEntityId.GetValuesForKey(entity)) {
                    toDelete.Add(action.RequestId);
                }
                foreach (var requestId in toDelete) {
                    CancelAction(entity, requestId);
                }
            }
            var keys = PooledArray<Entity>.FromEnumerator(activeActionsByEntityId.Keys);
            foreach (var key in keys) {
                for (var it = activeActionsByEntityId.GetValuesForKey(key); it.MoveNext();) {
                    var item = it.Current;
                    var entity = item.Request.TargetEntity;
                    if (entities.Contains(entity)) it.RemoveSelf();
                }
            }
        }

        public void CancelAction(Entity entity, RequestId requestId) {
            using var items = new PooledList<ActionActivation>(32);
            for (var it = activeActionsByEntityId.GetValuesForKey(entity); it.MoveNext();) {
                var activation = it.Current;
                if (!requestId.IsAll && activation.RequestId != requestId) continue;

                var actionSystem = registeredOrders[activation.ActionIndex];
                actionSystem.Cancel(entity, activation.RequestId);
                it.RemoveSelf();
            }
        }

    }
}
