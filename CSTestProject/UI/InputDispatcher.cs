using System;

namespace Weesals.UI {
    // Represents the "score" for an interaction which could be activated
    public struct ActivationScore {
        public float Score;
        public bool IsPotential => Score >= 1f;     // May activate in the future, but currently not ready
        public bool IsSatisfied => Score >= 2f;     // Ready to activate (will activate if input ends)
        public bool IsReady => Score >= 5f;         // Activate immediately if no contest (Satisfied or higher)
        public bool IsActive => Score >= 10f;       // Force activate regardless of contest

        public ActivationScore(float score) { Score = score; }
        public static implicit operator ActivationScore(float score) { return new ActivationScore(score); }
        public static bool operator <(ActivationScore s1, ActivationScore s2) { return s1.Score < s2.Score; }
        public static bool operator >(ActivationScore s1, ActivationScore s2) { return s1.Score > s2.Score; }
        public static bool operator ==(ActivationScore s1, ActivationScore s2) { return s1.Score == s2.Score; }
        public static bool operator !=(ActivationScore s1, ActivationScore s2) { return s1.Score != s2.Score; }
        // We are not ready
        public static readonly ActivationScore None = default;
        // We might be ready in the future
        public static readonly ActivationScore Potential = new ActivationScore(1f);
        // We are able to be activated, but not requesting
        public static readonly ActivationScore Satisfied = new ActivationScore(2f);
        // Activate us if no one else is also ready
        public static readonly ActivationScore SatisfiedAndReady = new ActivationScore(5f);
        // We must be activated, even if a conflict exist
        public static readonly ActivationScore Active = new ActivationScore(100f);

        public override bool Equals(object? obj) { return obj is ActivationScore o && this == o; }
        public override int GetHashCode() { return Score.GetHashCode(); }
        public override string ToString() { return $"Score={Score}"; }
    }

    public interface IInteraction {
        // Called by the PlayDispatcher when determining which interaction is most appropriate
        public abstract ActivationScore GetActivation(PointerEvent events);
    }

    public class InputDispatcher : CanvasRenderable, IPointerEnterHandler, IPointerExitHandler, IPointerEventsRaw {

        private List<IInteraction> interactions = new();

        private Dictionary<PointerEvent, PointerEvent> deferredPointers = new();

        struct ActivationState {
            public ActivationScore Score;
            public object Interaction;
            public int Contest;
            public int PotentialCount;
            public int SatisfiedCount;
            public void CombineWith(ActivationState other) {
                if (other.Score > Score) {
                    this = other;
                } else if (other.Score == Score) {
                    Contest += other.Contest;
                }
                if (other.Score.IsPotential) PotentialCount += other.PotentialCount;
                if (other.Score.IsSatisfied) SatisfiedCount += other.SatisfiedCount;
            }
        }
        // Find the best interaction for a specific performance
        private ActivationState GetBestInteraction(PointerEvent events) {
            ActivationState state = default;
            for (int i = 0; i < interactions.Count; i++) {
                var score = interactions[i].GetActivation(events);
                if (score > state.Score) {
                    state.Contest = 1;
                    state.Score = score;
                    state.Interaction = interactions[i];
                    if (state.Score.IsActive) break;
                } else if (score == state.Score) {
                    state.Contest++;
                }
                if (score.IsPotential) state.PotentialCount++;
                if (score.IsSatisfied) state.SatisfiedCount++;
            }
            return state;
        }

        private bool CheckInvokeInteraction(PointerEvent events, ActivationState state) {
            if (state.Interaction == null) return false;
            bool forceResolve = state.Score.IsActive
                || (state.Score.IsReady && state.SatisfiedCount == 1)
                //|| (state.Score.IsSatisfied && state.Contest == 1)
                // Force activate something when clicking
                || (state.Score.IsSatisfied && events.ButtonState == 0 && events.PreviousButtonState != 0)
                //|| (state.Score.IsSatisfied && performance.IsDrag)
                ;
            return forceResolve;
        }
        public PointerEvent? RequireDeferred(PointerEvent events) {
            if (!deferredPointers.TryGetValue(events, out var deferred)) {
                deferred = new(events);
                deferredPointers.Add(events, deferred);
            }
            return deferred;
        }
        private struct Target {
            public object? Active;
            public ActivationState State;
        }
        private Target FindTarget(PointerEvent deferred, ref HittestGrid.HitEnumerator hitIterator) {
            Target target = default;
            ref var state = ref target.State;
            state = GetBestInteraction(deferred);
            if (CheckInvokeInteraction(deferred, state)) { target.Active = state.Interaction; return target; }
            // Skip to find self
            while (hitIterator.MoveNext() && hitIterator.Current != this) ;
            // Iterate any other InputDispatchers immediately following
            while (hitIterator.MoveNext() && hitIterator.Current is InputDispatcher odispatcher) {
                var ostate = odispatcher.GetBestInteraction(deferred);
                if (CheckInvokeInteraction(deferred, ostate)) { target.Active = ostate.Interaction; return target; }
                state.CombineWith(ostate);
            }
            return target;
        }
        public PointerEvent? IntlProcessPointer(PointerEvent events) {
            if (!deferredPointers.TryGetValue(events, out var deferred)) return null;
            var update = deferred.StepPre(events);
            // Might need to tidy up this code
            bool deferredStepped = false;
            if (deferred.Active != null) {
                deferredStepped = true;
                deferred.StepPost(events, update);
                if (deferred.Active != null) return deferred;
            }
            var hitIterator = Canvas.HitTestGrid.BeginHitTest(deferred.CurrentPosition);
            var target = FindTarget(deferred, ref hitIterator);
            if (target.Active != null) deferred.SetActive(target.Active);
            if (!deferredStepped) deferred.StepPost(events, update);
            //else deferred.SetPress(target.State.Interaction);
            if (deferred.Active != null) return deferred;
            for (int i = 0; i < interactions.Count; i++) {
                if (interactions[i] is IPointerEventsRaw oraw) {
                    oraw.ProcessPointer(deferred);
                }
            }
            if (!target.State.Score.IsPotential && hitIterator.Current is IPointerEventsRaw raw) {
                raw.ProcessPointer(deferred);
                return deferred;
            }
            // If nothing is even potential, defer to the next control
            if (!target.State.Score.IsPotential) {
                deferred.SetHover(hitIterator.Current);
            }
            return null;
        }
        public void OnPointerEnter(PointerEvent events) {
            var deferred = new PointerEvent(events);
            deferredPointers.Add(events, deferred);
        }
        public void OnPointerExit(PointerEvent events) {
            if (deferredPointers.TryGetValue(events, out var deferred)) {
                deferred.Dispose();
                deferredPointers.Remove(events);
            }
        }
        public void ProcessPointer(PointerEvent events) {
            var deferred = IntlProcessPointer(events);
            //if(deferred == null) OnPointerExit(events);
            // On mouse-up, clear the active action
            // (implicit hover/press may remain)
            if (deferred != null && deferred.Active != null) {
                if (events.PreviousButtonState != 0 && events.ButtonState == 0) {
                    deferred.SetActive(null);
                }
            }
        }

        public void AddInteraction(IInteraction interaction) {
            interactions.Add(interaction);
        }
        public bool RemoveInteraction(IInteraction interaction) {
            return interactions.Remove(interaction);
        }

        public override string ToString() {
            return $"Dispatcher: Count={interactions.Count} Active={deferredPointers.Count}";
        }

    }
}
