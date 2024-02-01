using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;

namespace Weesals.Game {
    using CompletionInstance = OrderSystemBase.CompletionInstance;

    public abstract partial class ActionSystemBase : SystemBase
        , ILateUpdateSystem
        , LifeSystem.IDestroyListener {

        public TimeSystem TimeSystem { get; private set; }
        public LifeSystem LifeSystem { get; private set; }

        public interface ICompletionListener {
            void NotifyCombatCompleted(HashSet<CompletionInstance> entities);
        }

        protected HashSet<Entity> deadEntities;

        protected HashSet<CompletionInstance> completions;
        protected List<ICompletionListener> completionListeners = new();

        protected override void OnCreate() {
            base.OnCreate();
            TimeSystem = World.GetOrCreateSystem<TimeSystem>();
            LifeSystem = World.GetOrCreateSystem<LifeSystem>();
            completions = new(32);
            deadEntities = new(32);
            LifeSystem.RegisterDestroyListener(this, true);
        }
        protected override void OnDestroy() {
            LifeSystem.RegisterDestroyListener(this, false);
            //deadEntities.Dispose();
            //completions.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate() { }

        public abstract bool Begin(Entity entity, OrderDispatchSystem.ActionActivation action);
        public abstract void Cancel(Entity entity, RequestId requestId);

        public void OnLateUpdate() {
            //Dependency.Complete();
            if (completions.Count > 0) {
                foreach (var listener in completionListeners) listener.NotifyCombatCompleted(completions);
                completions.Clear();
            }
            if (deadEntities.Count > 0) {
                LifeSystem.MarkDeadEntities(deadEntities);
                deadEntities.Clear();
            }
        }

        public void RegisterCompletionListener(ICompletionListener listener, bool enable) {
            if (enable) completionListeners.Add(listener);
            else completionListeners.Remove(listener);
            //completionListeners.AddRemove(listener, enable);
        }

        public virtual void NotifyDestroyedEntities(HashSet<Entity> entities) {

        }
    }
}
