using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
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
        public uint PreviousButtonState;
        public float DragDistance;
        public Vector2 CurrentPosition;
        public Vector2 PreviousPosition;
        public States State = States.Hover;
        public object? Active;
        public bool IsDrag => DragDistance >= 2f && ButtonState != 0;
        public bool IsButtonDown => ButtonState != 0;
        public PointerEvent(EventSystem system, uint deviceId) {
            System = system;
            DeviceId = deviceId;
        }
        public PointerEvent(PointerEvent other) : this(other.System, other.DeviceId) {
            ButtonState = other.ButtonState;
            PreviousButtonState = other.PreviousButtonState;
            DragDistance = other.DragDistance;
            CurrentPosition = other.CurrentPosition;
            PreviousButtonState = other.PreviousButtonState;
        }
        public bool HasState(States flag) { return (State & flag) != 0; }

        internal void Step() {
            PreviousButtonState = ButtonState;
            PreviousPosition = CurrentPosition;
        }
        internal void Step(PointerEvent other) {
            System.UpdatePointerPre(this, other.CurrentPosition, other.ButtonState);
            System.UpdatePointerPost(this, other.ButtonState);
        }
        public bool GetIsButtonDown(int btnId) {
            return (ButtonState & (1 << btnId)) != 0;
        }
        public bool HasButton(int buttonId) {
            return ((ButtonState | PreviousButtonState) & (1 << buttonId)) != 0;
        }
        public void Yield() {
            System.MarkYielded();
        }
        public void SetActive(object? instance) {
            System.SetActive(this, instance);
        }
        public void SetState(States state, bool enable) {
            System.SetState(this, state, enable);
        }
    }
    public interface ISelectable {
        void OnSelected(bool selected);
    }
    public interface ISelectionGroup {
        void SetSelected(ISelectable? selectable);
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
        void UninitializePotentialDrop(CanvasRenderable source);
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
                foreach (var hit in Canvas.HitTestGrid.BeginHitTest(events.CurrentPosition)) {
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
                if (Target != null) Target.UninitializePotentialDrop(Source);
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
    public class SelectionManager : ISelectionGroup, IPointerDownHandler {
        private HashSet<ISelectable> selected = new();

        public IReadOnlyCollection<ISelectable> Selected => selected;

        public void ClearSelected() {
            using var toDeselect = new PooledList<ISelectable>(selected.Count);
            foreach (var item in selected) toDeselect.Add(item);
            selected.Clear();
            foreach (var item in toDeselect) item.OnSelected(false);
        }
        public void SetSelected(ISelectable? item) {
            ClearSelected();
            if (item != null) AppendSelected(item);
        }
        private void AppendSelected(ISelectable item) {
            selected.Add(item);
            item.OnSelected(true);
        }

        public void OnPointerDown(PointerEvent events) {
            if (!events.GetIsButtonDown(0)) return;
            if (HierarchyExt.TryGetRecursive(events.Active, out ISelectable selectable) == true) {
                selectable.Select();
            } else {
                if (HierarchyExt.TryGetRecursive(events.Active, out ISelectionGroup? group) != true) {
                    group = events.System.Canvas.SelectionGroup;
                }
                if (group != null) group.SetSelected(null);
            }
        }
    }

    public class EventSystem {

        public Canvas Canvas { get; private set; }
        public readonly SelectionManager SelectionManager = new();
        public readonly DoubleClickManager DoubleClick = new();
        public readonly DragDropManager DragDropManager;

        private CSInput input;
        private Vector2 pointerOffset;
        private int prevTimerUS;
        private int currTimerUS;
        private bool yielded;
        public float DeltaTime => (currTimerUS - prevTimerUS) / (1000.0f * 1000.0f);

        private List<IPointerDownHandler> pointerDownHandlers = new();
        private Dictionary<uint, PointerEvent> pointerEvents = new();

        private Dictionary<INestedEventSystem, List<CSPointer>> activeDeferred = new();

        public EventSystem(Canvas canvas) {
            input = default;
            Canvas = canvas;
            Canvas.SelectionGroup = SelectionManager;
            DragDropManager = new(Canvas);
            pointerDownHandlers.Add(SelectionManager);
        }

        public void SetPointerOffset(Vector2 pointerOffset) {
            this.pointerOffset = pointerOffset;
        }
        public void SetInput(CSInput input) {
            this.input = input;
        }

        public void Update(float dt) {
            prevTimerUS = currTimerUS;
            currTimerUS += (int)(dt * 1000.0f * 1000.0f + 0.5f);

            if (input.IsValid()) {
                var inputPointers = input.GetPointers();
                using var pointers = new PooledList<CSPointer>(inputPointers.Length);
                foreach (var ptr in inputPointers) pointers.Add(ptr);
                ProcessPointers(pointers);
            }
        }

        public void ProcessPointers(Span<CSPointer> pointers) {
            using var seenPointers = new PooledList<uint>();
            foreach (var deferredPtrs in activeDeferred.Values) {
                for (int i = 0; i < deferredPtrs.Count; ++i) {
                    var ptr = deferredPtrs[i];
                    ptr.mCurrentButtonState = 0;
                    deferredPtrs[i] = ptr;
                }
            }
            foreach (var pointer in pointers) {
                seenPointers.Add(pointer.mDeviceId);
                if (!pointerEvents.TryGetValue(pointer.mDeviceId, out var events)) {
                    events = new PointerEvent(this, pointer.mDeviceId) {
                        CurrentPosition = pointer.mPositionCurrent + pointerOffset,
                    };
                    pointerEvents.Add(pointer.mDeviceId, events);
                }
                UpdatePointerPre(events, pointer.mPositionCurrent + pointerOffset, pointer.mCurrentButtonState);

                // Find active element (hover is default state so will always happen at least once)
                if (!events.IsButtonDown && !events.HasState(PointerEvent.States.Press)) {
                    var hit = Canvas.HitTestGrid.BeginHitTest(events.CurrentPosition).First();
                    SetActive(events, hit);
                }

                UpdatePointerPost(events, pointer.mCurrentButtonState);
            }
            using var toRelease = new PooledList<INestedEventSystem>();
            // Send pointers to nested systems
            foreach (var kv in activeDeferred) {
                var nested = kv.Key;
                var es = nested.EventSystem;
                if (es != null)
                    es.ProcessPointers(CollectionsMarshal.AsSpan(kv.Value));
                for (int i = 0; i < kv.Value.Count; ++i) {
                    var ptr = kv.Value[i];
                    if (seenPointers.IndexOf(ptr.mDeviceId) == -1) kv.Value.RemoveAt(i--);
                }
                if (kv.Value.Count == 0) toRelease.Add(kv.Key);
            }
            foreach (var es in toRelease) {
                activeDeferred.Remove(es);
            }
        }

        internal void UpdatePointerPre(PointerEvent events, Vector2 position, uint buttonState) {
            events.Step();
            events.CurrentPosition = position;
            // Apply button presses
            SetButtonState(events, events.ButtonState | buttonState);

            // Drags during click or release are accepted
            if (events.HasState(PointerEvent.States.Press)) {
                events.DragDistance += Vector2.Distance(events.PreviousPosition, events.CurrentPosition);
                if (events.IsDrag && !events.HasState(PointerEvent.States.Drag)) {
                    if (HierarchyExt.TryGetRecursive(events.Active, out IBeginDragHandler draggable) == true) {
                        SetActive(events, draggable as CanvasRenderable);
                    }
                    SetState(events, PointerEvent.States.Drag, true);
                }
            }
        }
        internal void UpdatePointerPost(PointerEvent events, uint buttonState) {
            // Update drag
            if (events.HasState(PointerEvent.States.Drag) && events.Active is IDragHandler dragHandler)
                dragHandler.OnDrag(events);

            // Update pointer move listeners
            if (events.PreviousPosition != events.CurrentPosition) {
                if (HierarchyExt.TryGetRecursive(events.Active, out IPointerMoveHandler pmove) == true) {
                    pmove.OnPointerMove(events);
                }
            }

            // Apply button releases
            SetButtonState(events, buttonState);

            // Defer to raw handlers
            if (events.Active is IPointerEventsRaw raw) {
                raw.ProcessPointer(events);
            }

            // Track nested event systems
            if (events.Active is INestedEventSystem nested) {
                if (!activeDeferred.TryGetValue(nested, out var list)) {
                    list = new();
                    activeDeferred.Add(nested, list);
                }
                var nestedPtr = new CSPointer();
                var layout = nested.GetComputedLayout();
                nestedPtr.mDeviceId = events.DeviceId;
                nestedPtr.mCurrentButtonState = events.ButtonState;
                nestedPtr.mPositionCurrent = events.CurrentPosition - layout.Position.toxy();
                int index = 0;
                for (; index < list.Count; ++index) if (list[index].mDeviceId == events.DeviceId) break;
                if (index < list.Count) list[index] = nestedPtr;
                else list.Add(nestedPtr);
            }
        }

        internal void SetActive(PointerEvent events, object? hit, PointerEvent.States cmn = PointerEvent.States.None) {
            if (events.Active == hit) return;
            var ostate = events.State;
            SetStates(events, cmn);
            Console.WriteLine("Setting active " + hit);
            events.Active = hit;
            SetStates(events, ostate);
            Debug.Assert(events.HasState(PointerEvent.States.Hover));
        }
        private void SetStates(PointerEvent events, PointerEvent.States state) {
            TryInvoke(events, state, events.Active, eDragHandler);
            TryInvoke(events, state, events.Active, pUpHandler);
            TryInvoke(events, state, events.Active, pExitHandler);
            TryInvoke(events, state, events.Active, pEnterHandler);
            if (pDownHandler.ShouldInvoke(events, state)) {
                if (TryInvoke(events, state, events.Active, pDownHandler) == null) {
                    foreach (var handler in pointerDownHandlers) handler.OnPointerDown(events);
                }
            }
            TryInvoke(events, state, events.Active, bDragHandler);
        }
        public class PointerHandler<T> where T : IEventSystemHandler {
            public PointerEvent.States StateMask;
            public PointerEvent.States StateValue;
            public bool IsTransactional;
            public Action<T, PointerEvent> InvokeEvent;
            public PointerHandler(Action<T, PointerEvent> invoker, PointerEvent.States mask, bool sign) { InvokeEvent = invoker; StateMask = mask; StateValue = sign ? mask : default; }
            public bool ShouldInvoke(PointerEvent events, PointerEvent.States states) {
                return ((events.State & StateMask) != StateValue) && ((states & StateMask) == StateValue);
            }
        }
        private PointerHandler<IPointerDownHandler> pDownHandler = new((item, events) => item.OnPointerDown(events), PointerEvent.States.Press, true);
        private PointerHandler<IPointerUpHandler> pUpHandler = new((item, events) => item.OnPointerUp(events), PointerEvent.States.Press, false);
        private PointerHandler<IPointerEnterHandler> pEnterHandler = new((item, events) => item.OnPointerEnter(events), PointerEvent.States.Hover, true);
        private PointerHandler<IPointerExitHandler> pExitHandler = new((item, events) => item.OnPointerExit(events), PointerEvent.States.Hover, false);
        private PointerHandler<IBeginDragHandler> bDragHandler = new((item, events) => item.OnBeginDrag(events), PointerEvent.States.Drag, true) { IsTransactional = true, };
        private PointerHandler<IEndDragHandler> eDragHandler = new((item, events) => item.OnEndDrag(events), PointerEvent.States.Drag, false);
        private object? TryInvoke<T>(PointerEvent events, PointerEvent.States states, object? target, PointerHandler<T> handler) where T : IEventSystemHandler {
            if (!handler.ShouldInvoke(events, states)) return events.Active;
            var active = events.Active;
            var hitIterator = Canvas.HitTestGrid.BeginHitTest(events.CurrentPosition);
            while (true) {
                if (target == null) {
                    break;
                    //if (!handler.IsTransactional) break;
                    //if (!hitIterator.MoveNext()) break;
                    //target = hitIterator.Current;
                }
                for (; target != null; target = HierarchyExt.TryGetParent(target)) {
                    if (target is not T tvalue) continue;
                    handler.InvokeEvent(tvalue, events);
                    if (!ConsumeYield()) break;
                }
                if (target != null) break;
            }
            if (active != events.Active) {
                target = events.Active;
                if (target is T tvalue)
                    handler.InvokeEvent(tvalue, events);
            }
            if (events.Active != target && target != null && handler.IsTransactional) {
                SetActive(events, target);
            }
            if (target != null || !handler.IsTransactional) {
                events.State = (events.State & ~handler.StateMask) | handler.StateValue;
            }
            return target;
        }

        private bool ConsumeYield() {
            if (!yielded) return false;
            yielded = false;
            return true;
        }

        internal void SetButtonState(PointerEvent events, uint buttons) {
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
            if (!events.IsDrag && HierarchyExt.TryGetRecursive(events.Active, out IPointerClickHandler pclick) == true) {
                pclick.OnPointerClick(events);
            }
            SetState(events, PointerEvent.States.Drag, false);
        }

        internal bool SetState(PointerEvent events, PointerEvent.States state, bool enable) {
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

    public interface INestedEventSystem : IEventSystemHandler {
        EventSystem? EventSystem { get; }
        CanvasLayout GetComputedLayout();
    }
    public interface IPointerEventsRaw : IEventSystemHandler {
        void ProcessPointer(PointerEvent events);
    }
}
