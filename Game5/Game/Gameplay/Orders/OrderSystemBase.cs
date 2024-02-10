using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Utility;

namespace Game5.Game {
    /// <summary>
    /// Any system capable of handling an action request should extend this class
    /// For example: Melee Combat, Movement, Ability Casting
    /// </summary>
    public abstract partial class OrderSystemBase : SystemBase {

        public struct CompletionInstance : IEquatable<CompletionInstance> {
            public Entity Entity;
            public RequestId RequestId;
            public CompletionInstance(Entity entity, RequestId requestId) {
                Entity = entity;
                RequestId = requestId;
            }
            public bool Equals(CompletionInstance o) {
                return Entity == o.Entity && RequestId.Equals(o.RequestId);
            }
            public override int GetHashCode() { return Entity.GetHashCode() ^ RequestId.GetHashCode(); }
        }
        public interface ICompletionListener {
            void NotifyCompletion(int actionId, CompletionInstance completion);
        }

        public struct TrackStates : IDisposable {
            public PooledHashMap<int, int> States;
            public int Flagging;
            public TrackStates() {
                States = new PooledHashMap<int, int>(8);
                Flagging = 0;
            }
            public void Clear() {
                States.Clear();
            }
            public void SetIsFlagging(bool flagging) { Flagging = flagging ? 1 : 0; }
            public void SetTrackState(int trackId, int state) {
                if (States.TryGetValue(trackId, out var curState) && curState >= state) { Flagging = 2; return; }
                if (Flagging > 0) return;
                States[trackId] = state;
            }
            public void Dispose() { States.Dispose(); }
        }

        public abstract int Id { get; }
        public OrderDispatchSystem OrderDispatchSystem { get; private set; }
        private List<ICompletionListener> completionListeners = new();

        protected override void OnCreate() {
            base.OnCreate();
            OrderDispatchSystem = World.GetOrCreateSystem<OrderDispatchSystem>();
            OrderDispatchSystem.RegisterOrder(this, true);
        }
        protected override void OnDestroy() {
            OrderDispatchSystem.RegisterOrder(this, false);
            base.OnDestroy();
        }
        protected override void OnUpdate() { }

        // Return 1 or higher if we are capable of fulfilling this request
        // The system with the highest (>0) response will be selected
        public virtual float ScoreRequest(Entity entity, OrderInstance action) {
            return 0f;
        }
        public virtual void GetTrackStates(Entity entity, in ActionRequest request, ref TrackStates trackStates) {
        }

        // Begin evaluating a request, mutating the entity as required
        public virtual bool Begin(Entity entity, in OrderDispatchSystem.ActionActivation action) {
            return false;
        }
        public virtual void Cancel(Entity entity, RequestId requestId) {
            //NotifyActionComplete(requestId, entity);
        }

        protected void NotifyActionComplete(in PooledHashSet<CompletionInstance> completions) {
            foreach (var item in completions) {
                foreach (var listener in completionListeners) {
                    listener.NotifyCompletion(Id, item);
                }
            }
        }
        protected void NotifyActionComplete(RequestId requestId, Entity entity) {
            foreach (var listener in completionListeners) {
                listener.NotifyCompletion(Id, new CompletionInstance() { RequestId = requestId, Entity = entity, });
            }
        }
        protected virtual void RegisterActivation(bool enable) {
        }

        public void RegisterCompletionListener(ICompletionListener listener, bool enable) {
            if (enable) completionListeners.Add(listener);
            else completionListeners.Remove(listener);
        }

    }
}
