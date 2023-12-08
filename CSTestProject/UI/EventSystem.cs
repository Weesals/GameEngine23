using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.UI {
    public class PointerEvent {
        public readonly EventSystem System;
        [Flags]
        public enum States { None = 0, Hover = 1, Press = 2, Drag = 4, };
        public uint DeviceId;
        public uint ButtonState;
        public float DragDistance;
        public Vector2 CurrentPosition;
        public Vector2 PreviousPosition;
        public States State = States.Hover;
        public CanvasRenderable? Active;
        public bool IsDrag => DragDistance >= 2f;
        public bool IsButtonDown => ButtonState != 0;
        public PointerEvent(EventSystem system, uint deviceId) {
            System = system;
            DeviceId = deviceId;
        }
        public bool HasState(States flag) { return (State & flag) != 0; }

        public bool GetIsButtonDown(int btnId) {
            return (ButtonState & (1 << btnId)) != 0;
        }
        public void Yield() {
            System.MarkYielded();
        }
        public void SetActive(CanvasRenderable instance) {
            System.SetActive(this, instance);
        }
    }
    public interface ISelectable {
        void OnSelected(bool selected);
    }
    public interface ISelectionGroup {
        void SetSelected(ISelectable selectable);
    }
    public static class SelectableExt {
        public static void Select(this ISelectable selectable) {
            ISelectionGroup? group = null;
            if (selectable is CanvasRenderable renderable) {
                if (!renderable.TryGetRecursive(out group)) {
                    group = renderable.Canvas?.SelectionGroup;
                }
            }
            group?.SetSelected(selectable);
        }
    }

    public class DoubleClickManager {
        private struct Instance {
            public DateTime LastClick;
            public int ClickCount;
        }
        private Dictionary<CanvasRenderable, Instance> instances = new();
        private bool IsExpired(Instance instance) {
            return (DateTime.UtcNow - instance.LastClick) > TimeSpan.FromMilliseconds(300);
        }
        public int NotifyClick(CanvasRenderable renderable) {
            PruneOldInstances();
            if (!instances.TryGetValue(renderable, out var instance)) {
                instance = new Instance();
            }
            if (IsExpired(instance)) instance = default;
            instance.LastClick = DateTime.UtcNow;
            instance.ClickCount++;
            instances[renderable] = instance;
            return instance.ClickCount;
        }
        private void PruneOldInstances() {
            using var toRemove = new PooledList<CanvasRenderable>();
            foreach (var item in instances) {
                if (IsExpired(item.Value)) toRemove.Add(item.Key);
            }
            foreach (var item in toRemove) instances.Remove(item);
        }
    }
    public interface IDropTarget {
        bool InitializePotentialDrop(PointerEvent events, CanvasRenderable source);
        void UninitialzePotentialDrop(CanvasRenderable source);
        void ReceiveDrop(CanvasRenderable item);
    }
    public interface IDropTargetCustom {
        bool UpdatePotentialDrop(PointerEvent events, CanvasRenderable source);
    }
    public class DragDropManager {
        public class Instance : CloneView, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler {
            public readonly DragDropManager Manager;
            public CanvasRenderable Source;
            public IDropTarget? Target;
            public Vector2 DragOffset;

            public Instance(DragDropManager manager, CanvasRenderable source) {
                Manager = manager;
                Source = source;
                AppendChild(source);
                SetHitTestEnabled(false);
            }
            public void OnPointerDown(PointerEvent events) {
                DragOffset = events.CurrentPosition - Source.GetComputedLayout().Position.toxy();
            }
            public void OnBeginDrag(PointerEvent events) {
            }
            public void OnDrag(PointerEvent events) {
                var delta = (events.CurrentPosition - events.PreviousPosition);
                if (delta != Vector2.Zero) {
                    CloneTransform.Translation += delta.AppendZ(0f);
                    MarkComposeDirty();
                }
                IDropTarget? target = null;
                foreach (var hit in Canvas.HittestGrid.BeginHitTest(events.CurrentPosition)) {
                    if (hit is IDropTarget dropTarget && (hit == Target || dropTarget.InitializePotentialDrop(events, Source))) {
                        target = dropTarget;
                        break;
                    }
                }
                SetTarget(events, target);
                if (target is IDropTargetCustom dropCustom) {
                    if (!dropCustom.UpdatePotentialDrop(events, Source)) SetTarget(events, null);
                }
            }
            private void SetTarget(PointerEvent events, IDropTarget? dropTarget) {
                if (Target == dropTarget) return;
                if (Target != null) Target.UninitialzePotentialDrop(Source);
                Target = dropTarget;
            }
            public void OnEndDrag(PointerEvent events) {
                Manager.CancelDrag(events);
                if (Target != null) Target.ReceiveDrop(Source);
            }
        }
        private CanvasRenderable root;
        private Dictionary<PointerEvent, Instance> instances = new();
        public DragDropManager(CanvasRenderable _root) {
            root = _root;
        }
        public void BeginDrag(PointerEvent events, CanvasRenderable source) {
            CancelDrag(events);
            var instance = new Instance(this, source);
            if (root != null) root.AppendChild(instance);
            CanvasRenderable.CopyTransform(instance, source);
            instances.Add(events, instance);
            events.SetActive(instance);
        }
        private void CancelDrag(PointerEvent events) {
            if (instances.TryGetValue(events, out var instance)) {
                instance.RemoveFromParent();
                instances.Remove(events);
            }
        }
    }

    public class EventSystem : ISelectionGroup {

        public Canvas Canvas { get; private set; }
        public readonly DoubleClickManager DoubleClick = new();
        public readonly DragDropManager DragDropManager;

        private CSInput input;
        private Vector2 pointerOffset;
        private int prevTimerUS;
        private int currTimerUS;
        private bool yielded;
        public float DeltaTime => (currTimerUS - prevTimerUS) / (1000.0f * 1000.0f);

        private Dictionary<uint, PointerEvent> pointerEvents = new();

        private HashSet<ISelectable> selected = new();

        public EventSystem(Canvas canvas) {
            input = Core.ActiveInstance?.GetInput() ?? default;
            Canvas = canvas;
            Canvas.SelectionGroup = this;
            DragDropManager = new(Canvas);
        }

        private void ClearSelected() {
            foreach(var item in selected) item.OnSelected(false);
            selected.Clear();
        }
        public void SetSelected(ISelectable item) {
            ClearSelected();
            if (item != null) AppendSelected(item);
        }
        private void AppendSelected(ISelectable item) {
            selected.Add(item);
            item.OnSelected(true);
        }

        public void SetPointerOffset(Vector2 pointerOffset) {
            this.pointerOffset = pointerOffset;
        }

        public void Update(float dt) {
            prevTimerUS = currTimerUS;
            currTimerUS += (int)(dt * 1000.0f * 1000.0f + 0.5f);

            using var seenPointers = new PooledList<uint>();
            foreach (var pointer in input.GetPointers()) {
                seenPointers.Add(pointer.mDeviceId);
                if (!pointerEvents.TryGetValue(pointer.mDeviceId, out var events)) {
                    events = new PointerEvent(this, pointer.mDeviceId) {
                        PreviousPosition = pointer.mPositionPrevious + pointerOffset,
                        CurrentPosition = pointer.mPositionCurrent + pointerOffset,
                    };
                    pointerEvents.Add(pointer.mDeviceId, events);
                } else {
                    events.PreviousPosition = events.CurrentPosition;
                    events.CurrentPosition = pointer.mPositionCurrent + pointerOffset;
                }
                // Apply button presses
                SetButtonState(events, events.ButtonState | pointer.mCurrentButtonState);

                // Drags during click or release are accepted
                if (events.HasState(PointerEvent.States.Press)) {
                    events.DragDistance += Vector2.Distance(events.PreviousPosition, events.CurrentPosition);
                    if (events.IsDrag && !events.HasState(PointerEvent.States.Drag)) {
                        if (events.Active?.TryGetRecursive(out IBeginDragHandler draggable) == true) {
                            SetActive(events, draggable as CanvasRenderable);
                        }
                        SetState(events, PointerEvent.States.Drag, true);
                    }
                }
                if (events.PreviousPosition != events.CurrentPosition) {
                    if (events.Active?.TryGetRecursive(out IPointerMoveHandler pmove) == true) {
                        pmove.OnPointerMove(events);
                    }
                }

                // Find active element (hover is default state so will always happen at least once)
                if (!events.IsButtonDown) {
                    var hit = Canvas.HittestGrid.BeginHitTest(events.CurrentPosition).First();
                    SetActive(events, hit);
                }

                // Update drag
                if (events.HasState(PointerEvent.States.Drag) && events.Active is IDragHandler dragHandler)
                    dragHandler.OnDrag(events);

                // Apply button releases
                SetButtonState(events, pointer.mCurrentButtonState);
            }
        }

        internal void SetActive(PointerEvent events, CanvasRenderable? hit) {
            if (events.Active == hit) return;
            var ostate = events.State;
            SetStates(events, PointerEvent.States.None);
            events.Active = hit;
            SetStates(events, ostate);
            Debug.Assert(events.HasState(PointerEvent.States.Hover));
        }
        private void SetStates(PointerEvent events, PointerEvent.States state) {
            TryInvoke(events, state, events.Active, eDragHandler);
            TryInvoke(events, state, events.Active, pUpHandler);
            TryInvoke(events, state, events.Active, pExitHandler);
            TryInvoke(events, state, events.Active, pEnterHandler);
            TryInvoke(events, state, events.Active, pDownHandler);
            TryInvoke(events, state, events.Active, bDragHandler);
        }
        public class PointerHandler<T> where T : IEventSystemHandler {
            public PointerEvent.States StateMask;
            public PointerEvent.States StateValue;
            public bool IsTransactional;
            public Action<T, PointerEvent> InvokeEvent;
            public PointerHandler(Action<T, PointerEvent> invoker, PointerEvent.States mask, bool sign) { InvokeEvent = invoker; StateMask = mask; StateValue = sign ? mask : default; }
        }
        private PointerHandler<IPointerDownHandler> pDownHandler = new((item, events) => item.OnPointerDown(events), PointerEvent.States.Press, true);
        private PointerHandler<IPointerUpHandler> pUpHandler = new((item, events) => item.OnPointerUp(events), PointerEvent.States.Press, false);
        private PointerHandler<IPointerEnterHandler> pEnterHandler = new((item, events) => item.OnPointerEnter(events), PointerEvent.States.Hover, true);
        private PointerHandler<IPointerExitHandler> pExitHandler = new((item, events) => item.OnPointerExit(events), PointerEvent.States.Hover, false);
        private PointerHandler<IBeginDragHandler> bDragHandler = new((item, events) => item.OnBeginDrag(events), PointerEvent.States.Drag, true) { IsTransactional = true, };
        private PointerHandler<IEndDragHandler> eDragHandler = new((item, events) => item.OnEndDrag(events), PointerEvent.States.Drag, false);
        private CanvasRenderable? TryInvoke<T>(PointerEvent events, PointerEvent.States states, CanvasRenderable? target, PointerHandler<T> handler) where T : IEventSystemHandler {
            if (((events.State & handler.StateMask) == handler.StateValue) || ((states & handler.StateMask) != handler.StateValue)) return default;
            var ostate = events.State & handler.StateMask;
            events.State = (events.State & ~handler.StateMask) | handler.StateValue;
            for (; target != null; target = target.Parent) {
                if (target is not T tvalue) continue;
                handler.InvokeEvent(tvalue, events);
                if (!ConsumeYield()) break;
            }
            if (handler.IsTransactional && target == null) {
                events.State = (events.State & ~handler.StateMask) | ostate;
            }
            return target;
        }

        private bool ConsumeYield() {
            if (!yielded) return false;
            yielded = false;
            return true;
        }

        private void SetButtonState(PointerEvent events, uint buttons) {
            if (events.ButtonState == buttons) return;
            events.ButtonState = buttons;
            var isPress = buttons != 0;
            if (SetState(events, PointerEvent.States.Press, isPress)) {
                if (isPress) BeginPress(events); else EndPress(events);
            }
        }

        private void BeginPress(PointerEvent events) {
            events.DragDistance = 0;
        }
        private void EndPress(PointerEvent events) {
            if (!events.IsDrag && events.Active?.TryGetRecursive(out IPointerClickHandler pclick) == true) {
                pclick.OnPointerClick(events);
            }
            SetState(events, PointerEvent.States.Drag, false);
        }

        private bool SetState(PointerEvent events, PointerEvent.States state, bool enable) {
            var nstate = events.State;
            if (enable) nstate |= state; else nstate &= ~state;
            if (nstate == events.State) return false;
            SetStates(events, nstate);
            return true;
        }

        internal void MarkYielded() {
            yielded = true;
        }
    }

    public interface IEventSystemHandler {
    }
    public interface IPointerMoveHandler : IEventSystemHandler {
        void OnPointerMove(PointerEvent events);
    }
    public interface IPointerEnterHandler : IEventSystemHandler {
        void OnPointerEnter(PointerEvent events);
    }
    public interface IPointerExitHandler : IEventSystemHandler {
        void OnPointerExit(PointerEvent events);
    }
    public interface IPointerDownHandler : IEventSystemHandler {
        void OnPointerDown(PointerEvent events);
    }
    public interface IPointerUpHandler : IEventSystemHandler {
        void OnPointerUp(PointerEvent events);
    }
    public interface IPointerClickHandler : IEventSystemHandler {
        void OnPointerClick(PointerEvent events);
    }
    public interface IBeginDragHandler : IEventSystemHandler {
        void OnBeginDrag(PointerEvent events);
    }
    public interface IDragHandler : IEventSystemHandler {
        void OnDrag(PointerEvent events);
    }
    public interface IEndDragHandler : IEventSystemHandler {
        void OnEndDrag(PointerEvent events);
    }
}
