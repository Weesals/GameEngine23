using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals;

namespace Game5.Game {
    using CompletionInstance = OrderSystemBase.CompletionInstance;
    /// <summary>
    /// Allows external systems to register a Request that requires an
    /// auto-filling buildpoint/trainpoint value, and be called back
    /// when the target value is reached.
    /// </summary>
    public partial class AccrualSystem : SystemBase {

        public interface ICompleteListener {
            void NotifyAccumulationCompleted(HashSet<CompletionInstance> completions);
        }

        public struct Instance {
            public RequestId RequestId;
            public int Required;
            public int Accrued;
            public Entity Entity;
        }
        private SparseArray<Instance> instances;

        public TimeSystem TimeSystem { get; private set; }

        private HashSet<CompletionInstance> completions;
        private List<ICompleteListener> completionListeners = new();

        protected override void OnCreate() {
            base.OnCreate();
            instances = new(128);
            completions = new(32);
            TimeSystem = World.GetOrCreateSystem<TimeSystem>();
        }
        protected override void OnDestroy() {
            //completions.Dispose();
            //instances.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate() {
            var dt = TimeSystem.TimeDeltaMS;
            for (var it = instances.GetEnumerator(); it.MoveNext();) {
                var instance = it.Current;
                instance.Accrued += (int)dt;
                it.Current = instance;
                if (instance.Accrued >= instance.Required) {
                    completions.Add(new CompletionInstance() {
                        RequestId = instance.RequestId,
                        Entity = instance.Entity,
                    });
                    instances.Return(it.Index);
                }
            }
            if (completions.Count > 0) {
                for (int i = completionListeners.Count - 1; i >= 0; i--) {
                    completionListeners[i].NotifyAccumulationCompleted(completions);
                }
                completions.Clear();
            }
        }

        public void CreateInstance(Entity entity, OrderDispatchSystem.ActionActivation action, int required) {
            var index = instances.Allocate();
            instances[index] = new Instance() {
                Entity = entity,
                Accrued = 0,
                Required = required,
                RequestId = action.RequestId,
            };
        }

        public void RegisterCompleteListener(ICompleteListener listener, bool enable) {
            if (enable) completionListeners.Add(listener);
            else completionListeners.Remove(listener);
        }

        public Int2 GetProgress(Entity entity, RequestId requestId) {
            foreach (var instance in instances) {
                if (instance.Entity == entity && instance.RequestId == requestId) {
                    return new Int2(instance.Accrued, instance.Required);
                }
            }
            return 0;
        }

        public void CopyStateFrom(AccrualSystem other) {
            other.instances.CopyTo(instances);
            Debug.Assert(completions.Count == 0);
        }
    }
}
