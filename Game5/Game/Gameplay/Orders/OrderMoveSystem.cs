using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Utility;

namespace Game5.Game {
    public partial class OrderMoveSystem : OrderSystemBase
        , NavigationSystem.INavigationListener {

        public override int Id => 1;

        public NavigationSystem NavigationSystem { get; private set; }

        private Dictionary<RequestId, int> requestIdRefs = new();

        protected override void OnCreate() {
            base.OnCreate();
            NavigationSystem = World.GetOrCreateSystem<NavigationSystem>();
            requestIdRefs = new(128);
            RegisterActivation(true);
        }
        protected override void OnDestroy() {
            //requestIdRefs.Dispose();
            RegisterActivation(false);
            base.OnDestroy();
        }
        protected override void RegisterActivation(bool enable) {
            NavigationSystem.RegisterCompleteListener(this, enable);
        }

        // If the request was for movement, then signal that we are able to handle it
        public override float ScoreRequest(Entity entity, OrderInstance action) {
            if (action.Request.HasType(ActionTypes.Move)) {
                if (Stage.HasComponent<ECMobile>(entity)) return 1f;
            }
            return base.ScoreRequest(entity, action);
        }
        public override void GetTrackStates(Entity entity, in ActionRequest request, ref TrackStates trackStates) {
            trackStates.SetTrackState(OrderQueueSystem.Track_Move, 1);
        }

        // Mutate the entity so that it begins performing the movement
        // TODO: Track movement state and notify when the action completes
        public override bool Begin(Entity entity, in OrderDispatchSystem.ActionActivation action) {
            return Begin(entity, action, 0);
        }
        public bool Begin(Entity entity, OrderDispatchSystem.ActionActivation action, int range) {
            base.Begin(entity, action);
            var group = OrderDispatchSystem.GetActionGroup(entity, action.RequestId);
            var location = action.Request.TargetLocation;
            if (group.Count > 1) {
                var theta = (2 * Fixed16_16.PI / Fixed16_16.Phi) * group.Index;
                var radius = 600 * FixedMath.Sqrt(group.Index + Fixed16_16.Half);
                location.X += (FixedMath.Sin(theta) * radius).ToInt();
                location.Y += (FixedMath.Cos(theta) * radius).ToInt();
            }
            NavigationSystem.BeginNavigation(entity, action.RequestId, location, range);
            return true;
        }
        public override void Cancel(Entity entity, RequestId requestId) {
            NavigationSystem.Cancel(entity, requestId);
            base.Cancel(entity, requestId);
        }
        // Called by the NavigationSystem
        public void NotifyNavigationCompleted(HashSet<CompletionInstance> completions) {
            foreach (var completion in completions) {
                NotifyActionComplete(completion.RequestId, completion.Entity);
            }
        }

    }
}
