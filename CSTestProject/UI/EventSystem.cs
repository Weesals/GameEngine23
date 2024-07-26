using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Landscape.Editor;
using Weesals.Utility;

namespace Weesals.UI {
    public class PointerEvent : IDisposable {
        public struct EventTargets {
            public object? Hover;
            public object? Press;
            public object? Active;
            public object? EffectiveHover { get => Hover; }
            public object? EffectivePress { get => Active ?? Press; } 
            public object? EffectiveActive { get => Active; }
            public object? EffectiveDefer { get => Active ?? Hover; }
            public EventTargets(EventTargets targets) { this = targets; }
            public override string ToString() { return $"A:{Active}, H:{Press}"; }
        }
        public readonly EventSystem System;
        [Flags]
        public enum States { None = 0, Active = 1, Hover = 2, Press = 4, Drag = 8, ExplicitActive = 16, };
        public uint DeviceId;
        public uint ButtonState;
        public uint PreviousButtonState;
        public float DragDistance;
        public float PreviousDragDistance;
        public Vector2 CurrentPosition;
        public Vector2 PreviousPosition;
        public EventTargets Targets;
        public object? Active => Targets.EffectiveActive;
        public States State = States.Active | States.Hover;
        public Modifiers Modifiers = Modifiers.None;

        public bool IsDrag => DragDistance >= 5f && ButtonState != 0;
        public bool WasDrag => PreviousDragDistance >= 5f && PreviousButtonState != 0;
        public bool IsButtonDown => ButtonState != 0;
        public PointerEvent(EventSystem system, uint deviceId) {
            System = system;
            DeviceId = deviceId;
        }
        public PointerEvent(PointerEvent other) : this(other.System, other.DeviceId) {
            ButtonState = other.ButtonState;
            PreviousButtonState = other.PreviousButtonState;
            CurrentPosition = other.CurrentPosition;
            PreviousPosition = other.PreviousPosition;
            DragDistance = other.DragDistance;
            PreviousDragDistance = other.PreviousDragDistance;
            Modifiers = other.Modifiers;
        }
        public void Dispose() {
            System.SetStates(this, States.None);
            System.SetHover(this, null);
            System.SetPress(this, null);
            System.SetActive(this, null);
        }
        public bool HasState(States flag) { return (State & flag) != 0; }

        internal void Step() {
            PreviousButtonState = ButtonState;
            PreviousPosition = CurrentPosition;
            PreviousDragDistance = DragDistance;
        }
        internal EventSystem.PointerUpdate StepPre(PointerEvent other) {
            Modifiers = other.Modifiers;
            EventSystem.PointerUpdate update = new(this);
            System.UpdatePointerPre(this, other.CurrentPosition, other.ButtonState, update);
            return update;
        }
        internal void StepPost(PointerEvent other, EventSystem.PointerUpdate update) {
            System.UpdatePointerPost(this, other.ButtonState, update);
        }
        public bool GetIsButtonDown(int btnId) {
            return (ButtonState & (1 << btnId)) != 0;
        }
        public bool HasButton(int buttonId) {
            return ((ButtonState | PreviousButtonState) & (1 << buttonId)) != 0;
        }
        public bool HasModifier(Modifiers modifier) {
            return (Modifiers & modifier) != 0;
        }
        public void Yield() {
            System.MarkYielded();
        }
        public void SetHover(object? instance) {
            if (Targets.Hover == instance) return;
            System.SetHover(this, instance);
        }
        public void SetPress(object? instance) {
            if (Targets.Press == instance) return;
            System.SetPress(this, instance);
        }
        public void SetActive(object? instance) {
            if (Targets.Active == instance) return;
            System.SetActive(this, instance);
        }
        public void SetState(States state, bool enable) {
            System.SetState(this, state, enable);
        }
        public bool CanYield() {
            //if(Active != null && Active is IPointerClickHandler)
            //return !HasState(States.Press);
            return (ButtonState | PreviousButtonState) == 0;
        }
        public void Cancel(object? active) {
            var targets = Targets;
            if (targets.Press == active) targets.Press = null;
            if (targets.Active == active) targets.Active = null;
            System.SetTargetStates(this, targets, State);
        }
        public override string ToString() {
            return $"PE<D={DeviceId} {Targets}>";
        }
    }
    public interface ISelectable {
        void OnSelected(ISelectionGroup group, bool selected);
    }
    public interface ISelectionProxy {
        ISelectionGroup SelectionGroup { get; }
    }
    public interface ISelectionGroup {
        IReadOnlyCollection<ItemReference> Selected { get; }
        void ClearSelected();
        void SetSelected(ItemReference selectable);
        void AppendSelected(ItemReference selectable);
        void RemoveSelected(ItemReference selectable);
    }
    public static class SelectableExt {
        public static ISelectionGroup? GetSelectionGroup(this ISelectable selectable) {
            if (selectable is CanvasRenderable renderable) {
                for (object? parent = renderable; parent != null; parent = HierarchyExt.TryGetParent(parent)) {
                    if (parent is ISelectionGroup group) return group;
                    if (parent is ISelectionProxy proxy) return proxy.SelectionGroup;
                }
                return renderable.Canvas?.SelectionGroup;
            }
            return default;
        }
        public static void Select(this ISelectable selectable) {
            GetSelectionGroup(selectable)?.SetSelected(new(selectable));
        }
        public static void SetSelected(this ISelectable selectable, bool selected) {
            var group = GetSelectionGroup(selectable);
            if (group != null) {
                if (selected) group.AppendSelected(new(selectable));
                else group.RemoveSelected(new(selectable));
            }
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
        void ReceiveDrop(PointerEvent events, CanvasRenderable item);
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
                if (Target != null) Target.ReceiveDrop(events, Source);
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
            events.SetPress(instance);
        }
        private void CancelDrag(PointerEvent events) {
            if (instances.TryGetValue(events, out var instance)) {
                instance.RemoveFromParent();
                instances.Remove(events);
            }
        }
    }
    public class SelectionManager : ISelectionGroup, IPointerDownHandler {
        private HashSet<ItemReference> selected = new();

        public IReadOnlyCollection<ItemReference> Selected => selected;
        public EventSystem EventSystem { get; private set; }

        public event Action<ItemReference, bool>? OnEntitySelected;
        public event Action<ICollection<ItemReference>>? OnSelectionChanged;

        private int holdRef;

        public struct Hold : IDisposable {
            public readonly SelectionManager Manager;
            public Hold(SelectionManager manager) {
                Manager = manager;
                ++Manager.holdRef;
            }
            public void Dispose() {
                --Manager.holdRef;
                if (Manager.holdRef == 0x8000) {
                    Manager.NotifySelectionChanged();
                }
            }
        }

        public struct Scope : IDisposable {
            public readonly SelectionManager Manager;
            private PooledList<ItemReference> toDeselect;
            public Scope(SelectionManager manager) {
                Manager = manager;
                toDeselect = new(manager.selected.Count);
                toDeselect.CopyFrom(manager.selected);
                manager.selected.Clear();
            }
            public void Append(ItemReference item) {
                Manager.selected.Add(item);
            }
            public void Set(ICollection<ItemReference> items) {
                Debug.Assert(Manager.selected.Count == 0,
                    "Did you Append() before Set(), items will be lost");
                foreach (var item in items) Manager.selected.Add(item);
            }
            public void Dispose() {
                using var hold = new Hold(Manager);
                // TODO: Consider if something externally adds directly to Manager
                int keepCount = 0;
                // Separate into 'keep' and 'deselect' chunks
                foreach (var item in Manager.selected) {
                    var index = Array.IndexOf(toDeselect.Data, item, keepCount);
                    // Item was found, push it to 'keep' range
                    if (index >= 0) toDeselect.Swap(index, keepCount++);
                }
                // Deselect items that were not added within scope
                foreach (var item in toDeselect.Data.AsSpan(keepCount, toDeselect.Count - keepCount)) {
                    Manager.NotifySelected(item, false);
                }
                toDeselect.Count = keepCount;
                // Select items that were newly added (not in 'keep')
                foreach (var item in Manager.selected) {
                    var index = Array.IndexOf(toDeselect.Data, item, 0, keepCount);
                    if (index < 0) toDeselect.Add(item);
                }
                // Notify selection
                foreach (var item in toDeselect.AsSpan(keepCount)) {
                    Manager.NotifySelected(item, true);
                }
                toDeselect.Dispose();
            }
        }

        public SelectionManager(EventSystem eventSystem) {
            EventSystem = eventSystem;
        }

        public void ClearSelected() {
            using var hold = new Hold(this);
            using var toDeselect = new PooledList<ItemReference>(selected.Count);
            foreach (var item in selected) toDeselect.Add(item);
            selected.Clear();
            foreach (var item in toDeselect) NotifySelected(item, false);
        }
        public void SetSelected(ItemReference newItem) {
            using var hold = new Hold(this);
            using var toDeselect = new PooledList<ItemReference>(selected.Count);
            foreach (var item in selected) if (item != newItem) toDeselect.Add(item);
            if (selected.Count == toDeselect.Count) selected.Clear();
            else foreach (var item in toDeselect) selected.Remove(item);
            foreach (var item in toDeselect) NotifySelected(item, false);
            if (newItem.IsValid) AppendSelected(newItem);
        }
        public void SetSelectedItems(ICollection<ItemReference> newItems) {
            using var hold = new Hold(this);
            using var toDeselect = new PooledList<ItemReference>(selected.Count);
            foreach (var item in selected) if (!newItems.Contains(item)) toDeselect.Add(item);
            if (selected.Count == toDeselect.Count) selected.Clear();
            else foreach (var item in toDeselect) selected.Remove(item);
            foreach (var item in toDeselect) NotifySelected(item, false);
            foreach (var item in newItems) AppendSelected(item);
        }
        public void AppendSelected(ItemReference item) {
            if (selected.Add(item)) NotifySelected(item, true);
        }
        public void RemoveSelected(ItemReference item) {
            if (selected.Remove(item)) NotifySelected(item, false);
        }

        public void OnPointerDown(PointerEvent events) {
            if (!events.GetIsButtonDown(0)) return;
            if (HierarchyExt.TryGetRecursive(events.Targets.EffectivePress, out ISelectable selectable) == true) {
                selectable.Select();
            } else {
                if (HierarchyExt.TryGetRecursive(events.Targets.EffectivePress, out ISelectionGroup? group) != true) {
                    group = events.System.Canvas.SelectionGroup;
                }
                if (group != null) group.SetSelected(default);
            }
        }

        private void NotifySelected(ItemReference item, bool selected) {
            if (item.Owner is ISelectable selectable)
                selectable.OnSelected(this, selected);  //item.Data, 
            if (OnEntitySelected != null) OnEntitySelected(item, selected);
            NotifySelectionChanged();
        }
        private void NotifySelectionChanged() {
            if (holdRef == 0x8000) {
                holdRef = 0;
                if (OnSelectionChanged != null) OnSelectionChanged(this.selected);
            } else holdRef |= 0x8000;
        }
    }

    public class KeyboardFilter {

        public const short NestedEventSystemPriority = 100;
        public const short MaximumPriority = short.MaxValue;

        public struct Entry : IComparable<Entry> {
            public readonly object Filter;
            public readonly short Priority;
            public short RefCount;
            public uint ActiveKeyMask;
            public Entry(short priority, object filter) {
                Priority = priority;
                RefCount = 0;
                Filter = filter;
            }
            public readonly int CompareTo(Entry other) { return Priority.CompareTo(other.Priority); }
            public readonly override string ToString() { return Filter?.ToString() ?? "-none-"; }
        }
        private PooledList<Entry> filters = new();
        private SparseArray<KeyCode> activeKeys = new();
        private int FindIndex(Entry item) {
            for (int i = 0; i < filters.Count; i++) {
                if (filters[i].Filter == item.Filter) return i;
            }
            var index = filters.AsSpan().BinarySearch(item);
            if (index >= 0 && item.Filter != filters[index].Filter) index = ~index;
            return index;
        }
        public void Insert(short priority, object? filter) {
            if (filter == null) return;
            var item = new Entry(priority, filter);
            var index = FindIndex(item);
            if (index >= 0) filters[index].RefCount++;
            else {
                index = ~index;
                item.RefCount++;
                filters.Insert(index, item);
            }
        }
        public bool Remove(short priority, object? filter) {
            if (filter == null) return false;
            var item = new Entry(priority, filter);
            var index = FindIndex(item);
            Debug.Assert(index >= 0);
            if (--filters[index].RefCount > 0) return false;
            var bits = filters[index].ActiveKeyMask;
            while (bits != 0) {
                var id = BitOperations.TrailingZeroCount(bits);
                bits ^= 1u << id;
                if (filters[index].Filter is not IKeyReleaseHandler handler) continue;
                var keyEvent = new KeyEvent(activeKeys[id], Modifiers.None);
                handler.OnKeyRelease(ref keyEvent);
            }
            filters.RemoveAt(index);
            return true;
        }
        public ref struct Enumerator {
            private Span<Entry>.Enumerator enumerator;
            public ref uint KeyMask => ref enumerator.Current.ActiveKeyMask;
            public object Current => enumerator.Current.Filter;
            public Enumerator(Span<Entry>.Enumerator _en) { enumerator = _en; }
            public void Dispose() { }
            public bool MoveNext() { return enumerator.MoveNext(); }
            public void Reset() { }
        }
        public Enumerator GetEnumerator() { return new(filters.GetEnumerator()); }

        private int FindKeyIndex(KeyCode key) {
            for (var it = activeKeys.GetEnumerator(); it.MoveNext();) if (it.Current == key) return it.Index;
            return -1;
        }
        public void OnKeyPress(ref KeyEvent keyEvent) {
            var id = activeKeys.Add(keyEvent.Key);
            Debug.Assert(id <= 32, "Too many concurrent keys!");
            for (var it = GetEnumerator(); it.MoveNext();) {
                if (it.Current is not IKeyPressHandler handler) continue;
                it.KeyMask |= 1u << id;
                handler.OnKeyPress(ref keyEvent);
                if (keyEvent.IsConsumed) break;
            }
        }
        public void OnKeyDown(ref KeyEvent keyEvent) {
            var id = FindKeyIndex(keyEvent.Key);
            if (id == -1) return;
            for (var it = GetEnumerator(); it.MoveNext();) {
                if ((it.KeyMask & (1u << id)) == 0) continue;
                if (it.Current is not IKeyDownHandler handler) continue;
                handler.OnKeyDown(ref keyEvent);
                if (keyEvent.IsConsumed) break;
            }
        }
        public void OnKeyRelease(ref KeyEvent keyEvent) {
            var id = FindKeyIndex(keyEvent.Key);
            if (id == -1) return;
            for (var it = GetEnumerator(); it.MoveNext();) {
                if ((it.KeyMask & (1u << id)) == 0) continue;
                it.KeyMask &= ~(1u << id);
                if (it.Current is not IKeyReleaseHandler handler) continue;
                handler.OnKeyRelease(ref keyEvent);
                Debug.Assert(!keyEvent.IsConsumed, "Cannot consume a key up event");
            }
            activeKeys.Return(id);
        }
        public void OnCharInput(ref CharInputEvent chars) {
            for (var it = GetEnumerator(); it.MoveNext();) {
                if (it.Current is not ICharInputHandler handler) continue;
                handler.OnCharInput(ref chars);
                if (chars.IsConsumed) break;
            }
        }
    }

    public class EventSystem
        : IKeyPressHandler
        , IKeyDownHandler
        , IKeyReleaseHandler
        , ICharInputHandler
        {

        public const bool AlwaysCallMoveHandlers = true;

        public Canvas Canvas { get; private set; }
        public readonly SelectionManager SelectionManager;
        public readonly DoubleClickManager DoubleClick = new();
        public readonly DragDropManager DragDropManager;
        public readonly KeyboardFilter KeyboardFilter = new();

        private CSInput input;
        private Vector2 pointerOffset;
        private int prevTimerUS;
        private int currTimerUS;
        private bool yielded;
        private bool enableDebug;
        public float DeltaTime => (currTimerUS - prevTimerUS) / (1000.0f * 1000.0f);

        private List<IPointerDownHandler> pointerDownHandlers = new();
        private Dictionary<uint, PointerEvent> pointerEvents = new();

        public class DeferredState : List<CSPointer> {
            public ulong ActivePointers;
        }
        private Dictionary<INestedEventSystem, DeferredState> activeDeferred = new();

        public EventSystem(Canvas canvas) {
            input = default;
            Canvas = canvas;
            SelectionManager = new(this);
            Canvas.SelectionGroup = SelectionManager;
            Canvas.KeyboardFilter = KeyboardFilter;
            Canvas.OnRepaint += Canvas_Repaint;
            DragDropManager = new(Canvas);
            pointerDownHandlers.Add(SelectionManager);
            enableDebug = false;
        }

        public void SetPointerOffset(Vector2 pointerOffset) {
            this.pointerOffset = pointerOffset;
        }
        public void SetInput(CSInput input) {
            this.input = input;
        }

        private void Canvas_Repaint(ref CanvasCompositor.Context context) {
            if (!enableDebug) return;
            foreach (var pointer in pointerEvents) {
                var active = pointer.Value.Targets.Hover;
                if (active is CanvasRenderable renderable) {
                    var img = context.CreateTransient<CanvasImage>(Canvas);
                    img.UpdateLayout(Canvas, renderable.GetComputedLayout());
                    img.Append(ref context);
                    var txt = context.CreateTransient<CanvasText>(Canvas);
                    txt.Text = active.ToString();
                    txt.UpdateLayout(Canvas, renderable.GetComputedLayout());
                    txt.Append(ref context);
                    Trace.WriteLine(active.ToString());
                }
            }
        }

        public void Update(float dt) {
            prevTimerUS = currTimerUS;
            currTimerUS += (int)(dt * 1000.0f * 1000.0f + 0.5f);

            if (input.IsValid) {
                var modifiers = Modifiers.None;
                if (input.GetKeyDown((char)KeyCode.LeftShift) || input.GetKeyDown((char)KeyCode.RightShift)) modifiers |= Modifiers.Shift;
                if (input.GetKeyDown((char)KeyCode.LeftControl) || input.GetKeyDown((char)KeyCode.RightControl)) modifiers |= Modifiers.Control;
                if (input.GetKeyDown((char)KeyCode.LeftAlt) || input.GetKeyDown((char)KeyCode.RightAlt)) modifiers |= Modifiers.Alt;

                var inputPointers = input.GetPointers();
                using var pointers = new PooledList<CSPointer>(inputPointers.Length);
                foreach (var ptr in inputPointers) pointers.Add(ptr);
                ProcessPointers(pointers, modifiers);

                foreach (var key in input.GetPressKeys()) {
                    var keyEvent = KeyEvent.CreateEvent(key, modifiers);
                    OnKeyPress(ref keyEvent);
                }
                foreach (var key in input.GetDownKeys()) {
                    var keyEvent = KeyEvent.CreateEvent(key, modifiers);
                    OnKeyDown(ref keyEvent);
                }
                foreach (var key in input.GetReleaseKeys()) {
                    var keyEvent = KeyEvent.CreateEvent(key, modifiers);
                    OnKeyRelease(ref keyEvent);
                }
                var charBuffer = input.GetCharBuffer();
                if (charBuffer.Length > 0) {
                    var builder = new StringBuilder();
                    foreach (var c in charBuffer) builder.Append((char)c);
                    var charEvent = new CharInputEvent(builder.ToString());
                    OnCharInput(ref charEvent);
                }
            }
        }

        public void ProcessPointers(Span<CSPointer> pointers, Modifiers modifiers) {
            using var seenPointers = new PooledList<uint>();
            foreach (var deferredPtrs in activeDeferred.Values) {
                for (int i = deferredPtrs.Count - 1; i >= 0; --i) {
                    if (deferredPtrs[i].mCurrentButtonState != 0) continue;
                    if ((deferredPtrs.ActivePointers & 1ul << i) != 0) continue;
                    deferredPtrs.RemoveAt(i);
                }
                deferredPtrs.ActivePointers = 0;
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
                events.Modifiers = modifiers;
                PointerUpdate update = new(events);
                UpdatePointerPre(events, pointer.mPositionCurrent + pointerOffset, pointer.mCurrentButtonState, update);

                // Find active element (hover is default state so will always happen at least once)
                if (!events.IsButtonDown && events.CanYield() && events.PreviousButtonState == 0) {
                    var hit = Canvas.HitTestGrid.BeginHitTest(events.CurrentPosition).First();
                    SetHover(events, hit);
                }

                UpdatePointerPost(events, pointer.mCurrentButtonState, update);
            }
            foreach (var ptr in pointerEvents) {
                if (seenPointers.Contains(ptr.Key)) continue;
                SetHover(ptr.Value, default);
            }
            using var toRelease = new PooledList<INestedEventSystem>();
            // Send pointers to nested systems
            foreach (var kv in activeDeferred) {
                var nested = kv.Key;
                var es = nested.EventSystem;
                if (es != null)
                    es.ProcessPointers(CollectionsMarshal.AsSpan(kv.Value), modifiers);
                for (int i = 0; i < kv.Value.Count; ++i) {
                    var ptr = kv.Value[i];
                    if (!seenPointers.Contains(ptr.mDeviceId)) kv.Value.RemoveAt(i--);
                }
                if (kv.Value.Count == 0) toRelease.Add(kv.Key);
            }
            // Remove empty deferred systems
            foreach (var es in toRelease) activeDeferred.Remove(es);
        }

        internal struct PointerUpdate {
            public readonly PointerEvent Events;
            public readonly uint PreviousButtonState;
            public PointerUpdate(PointerEvent events) {
                Events = events;
                PreviousButtonState = events.ButtonState;
            }
        }
        internal void UpdatePointerPre(PointerEvent events, Vector2 position, uint buttonState, PointerUpdate update) {
            events.Step();
            events.CurrentPosition = position;
            // Apply button presses
            SetButtonState(events, buttonState);
            if (events.HasState(PointerEvent.States.Press) && events.PreviousPosition != events.CurrentPosition) {
                events.DragDistance += Vector2.Distance(events.PreviousPosition, events.CurrentPosition);
            }
        }
        internal void UpdatePointerPost(PointerEvent events, uint buttonState, PointerUpdate update) {
            if (events.PreviousPosition != events.CurrentPosition || AlwaysCallMoveHandlers) {
                // Drags during click or release are accepted
                if (events.IsDrag && !events.HasState(PointerEvent.States.Drag)) {
                    if (HierarchyExt.TryGetRecursive(events.Targets.EffectivePress, out IBeginDragHandler draggable) == true) {
                        SetPress(events, draggable);
                    }
                    SetState(events, PointerEvent.States.Drag, true);
                }

                // Update drag
                if (events.HasState(PointerEvent.States.Drag) && events.Targets.EffectivePress is IDragHandler dragHandler)
                    dragHandler.OnDrag(events);

                // Update pointer move
                if (HierarchyExt.TryGetRecursive(events.Targets.EffectivePress, out IPointerMoveHandler pmove) == true) {
                    pmove.OnPointerMove(events);
                }
            }

            if (update.PreviousButtonState != 0 && events.ButtonState == 0) {
                SendClickEvent(events);
            }

            // Defer to raw handlers
            if (events.Targets.EffectiveDefer is IPointerEventsRaw raw) {
                raw.ProcessPointer(events);
            }

            // Track nested event systems
            if (events.Targets.EffectiveDefer is INestedEventSystem nested) {
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
                list.ActivePointers |= 1ul << index;
            }
        }

        internal void SetHover(PointerEvent events, object? hit) {
            if (events.Targets.Hover == hit) return;
            if (enableDebug) Canvas.MarkComposeDirty();
            //Debug.WriteLine("Setting hover " + hit);
            SetTargetStates(events, new(events.Targets) { Hover = hit, }, events.State);
        }
        internal void SetPress(PointerEvent events, object? hit) {
            if (events.Targets.Press == hit) return;
            //Debug.WriteLine("Setting Press " + hit);
            SetTargetStates(events, new(events.Targets) { Press = hit, }, events.State);
        }
        internal void SetActive(PointerEvent events, object? hit) {
            if (events.Targets.Active == hit) return;
            //Debug.WriteLine("Setting active " + hit);
            SetTargetStates(events, new(events.Targets) { Active = hit, }, events.State);
        }
        public void SetTargetStates(PointerEvent events, PointerEvent.EventTargets targets, PointerEvent.States state) {
            PointerEvent.States cmn = events.State;
            if (events.Targets.EffectiveHover != targets.EffectiveHover) {
                cmn &= ~(PointerEvent.States.Hover);
            }
            if (events.Targets.EffectivePress != targets.EffectivePress) {
                cmn &= ~(PointerEvent.States.Press | PointerEvent.States.Drag);
            }
            if (events.Targets.EffectiveActive != targets.EffectiveActive) {
                cmn &= ~(PointerEvent.States.Active);
            }
            if (events.Targets.EffectiveDefer != targets.EffectiveDefer) {
                if (events.Targets.EffectiveDefer is INestedEventSystem oldNested)
                    KeyboardFilter.Remove(KeyboardFilter.NestedEventSystemPriority, oldNested.EventSystem);
                if (targets.EffectiveDefer is INestedEventSystem newNested)
                    KeyboardFilter.Insert(KeyboardFilter.NestedEventSystemPriority, newNested.EventSystem);
            }
            SetStates(events, cmn);
            events.Targets = targets;
            SetStates(events, state);
        }
        internal void SetStates(PointerEvent events, PointerEvent.States state) {
            PointerEvent.EventTargets targets = events.Targets;
            TryInvoke(events, state, targets, eDragHandler, false);
            TryInvoke(events, state, targets, pUpHandler, false);
            TryInvoke(events, state, targets, pExitHandler, false);
            TryInvoke(events, state, targets, pEndIntHandler, false);
            TryInvoke(events, state, targets, pBeginIntHandler);
            TryInvoke(events, state, targets, pEnterHandler);
            if (pDownHandler.ShouldInvoke(events, state)) {
                if (!TryInvoke(events, state, targets, pDownHandler)) {
                    foreach (var handler in pointerDownHandlers) handler.OnPointerDown(events);
                }
            }
            TryInvoke(events, state, targets, bDragHandler);
        }
        public abstract class PointerHandler<T> where T : IEventSystemHandler {
            public PointerEvent.States StateMask;
            public PointerEvent.States StateValue;
            public bool IsTransactional;
            public Action<T, PointerEvent> InvokeEvent;
            public PointerHandler(Action<T, PointerEvent> invoker, PointerEvent.States mask, bool sign) { InvokeEvent = invoker; StateMask = mask; StateValue = sign ? mask : default; }
            public bool ShouldInvoke(PointerEvent events, PointerEvent.States states) {
                return ((events.State & StateMask) != StateValue) && ((states & StateMask) == StateValue);
            }
            public PointerEvent.States AssignState(PointerEvent.States state) {
                return (state & ~StateMask) | StateValue;
            }
            public abstract ref object? GetTarget(ref PointerEvent.EventTargets targets);
        }
        public class HoverPointerEvent<T> : PointerHandler<T> where T : IEventSystemHandler {
            public HoverPointerEvent(Action<T, PointerEvent> invoker, PointerEvent.States mask, bool sign) : base(invoker, mask, sign) { }
            public override ref object? GetTarget(ref PointerEvent.EventTargets targets) {
                return ref targets.Hover;
            }
        }
        public class PressPointerEvent<T> : PointerHandler<T> where T : IEventSystemHandler {
            public PressPointerEvent(Action<T, PointerEvent> invoker, PointerEvent.States mask, bool sign) : base(invoker, mask, sign) { }
            public override ref object? GetTarget(ref PointerEvent.EventTargets targets) {
                return ref targets.Active != null ? ref targets.Active : ref targets.Press;
            }
        }
        public class ActivePointerEvent<T> : PointerHandler<T> where T : IEventSystemHandler {
            public ActivePointerEvent(Action<T, PointerEvent> invoker, PointerEvent.States mask, bool sign) : base(invoker, mask, sign) { }
            public override ref object? GetTarget(ref PointerEvent.EventTargets targets) {
                return ref targets.Active != null ? ref targets.Active : ref targets.Press;
            }
        }
        private HoverPointerEvent<IPointerEnterHandler> pEnterHandler = new((item, events) => item.OnPointerEnter(events), PointerEvent.States.Hover, true);
        private HoverPointerEvent<IPointerExitHandler> pExitHandler = new((item, events) => item.OnPointerExit(events), PointerEvent.States.Hover, false);
        private PressPointerEvent<IPointerDownHandler> pDownHandler = new((item, events) => item.OnPointerDown(events), PointerEvent.States.Press, true);
        private PressPointerEvent<IPointerUpHandler> pUpHandler = new((item, events) => item.OnPointerUp(events), PointerEvent.States.Press, false);
        private PressPointerEvent<IBeginDragHandler> bDragHandler = new((item, events) => item.OnBeginDrag(events), PointerEvent.States.Drag, true) { IsTransactional = true, };
        private PressPointerEvent<IEndDragHandler> eDragHandler = new((item, events) => item.OnEndDrag(events), PointerEvent.States.Drag, false);
        private ActivePointerEvent<IBeginInteractionHandler> pBeginIntHandler = new((item, events) => item.OnBeginInteraction(events), PointerEvent.States.Active, true);
        private ActivePointerEvent<IEndInteractionHandler> pEndIntHandler = new((item, events) => item.OnEndInteraction(events), PointerEvent.States.Active, false);
        private bool TryInvoke<T>(PointerEvent events, PointerEvent.States states, object? target, PointerHandler<T> handler, bool allowPropagate = true) where T : IEventSystemHandler {
            if (!handler.ShouldInvoke(events, states)) return true;
            events.State = handler.AssignState(events.State);
            ref var active = ref handler.GetTarget(ref events.Targets);
            var ogactive = active;
            target = ogactive;
            var hitIterator = Canvas.HitTestGrid.BeginHitTest(events.CurrentPosition);
            while (true) {
                if (target == null) {
                    break;
                    //if (!handler.IsTransactional) break;
                    //if (!hitIterator.MoveNext()) break;
                    //target = hitIterator.Current;
                }
                for (; target != null; target = HierarchyExt.TryGetParent(target)) {
                    if (target is T tvalue) {
                        handler.InvokeEvent(tvalue, events);
                        if (!ConsumeYield()) break;
                    }
                    if (!allowPropagate) { target = null; break; }
                }
                if (target != null) break;
            }
            if (active == ogactive && target != ogactive && handler.IsTransactional) {
                events.State &= ~handler.StateValue;
                var newTargets = events.Targets;
                handler.GetTarget(ref newTargets) = target;
                SetTargetStates(events, newTargets, events.State);
                events.State |= handler.StateValue;
            }
            /*if (active != events.Active) {
                target = events.Active;
                if (target is T tvalue)
                    handler.InvokeEvent(tvalue, events);
            }
            if (events.Active != target && target != null && handler.IsTransactional) {
                SetActive(events, target);
            }
            if (target != null || !handler.IsTransactional) {
                events.State = handler.AssignState(events.State);
            }*/
            return target != null;
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
            if (isPress) BeginPress(events);
            SetState(events, PointerEvent.States.Press, isPress);
            if (!isPress) EndPress(events);
        }

        private void BeginPress(PointerEvent events) {
            events.DragDistance = 0;
            SetPress(events, events.Targets.Hover);
        }
        private void EndPress(PointerEvent events) {
            //SendClickEvent();
            SetState(events, PointerEvent.States.Drag, false);
        }
        private void SendClickEvent(PointerEvent events) {
            if (!events.IsDrag && HierarchyExt.TryGetRecursive(events.Targets.EffectivePress, out IPointerClickHandler pclick) == true) {
                pclick.OnPointerClick(events);
            }
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

        public void OnKeyPress(ref KeyEvent keyEvent) {
            KeyboardFilter.OnKeyPress(ref keyEvent);
        }
        public void OnKeyDown(ref KeyEvent keyEvent) {
            KeyboardFilter.OnKeyDown(ref keyEvent);
        }
        public void OnKeyRelease(ref KeyEvent keyEvent) {
            KeyboardFilter.OnKeyRelease(ref keyEvent);
        }
        public void OnCharInput(ref CharInputEvent chars) {
            KeyboardFilter.OnCharInput(ref chars);
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
    public interface IBeginInteractionHandler : IEventSystemHandler {
        void OnBeginInteraction(PointerEvent events);
    }
    public interface IEndInteractionHandler : IEventSystemHandler {
        void OnEndInteraction(PointerEvent events);
    }
    public interface IInteractionHandler : IBeginInteractionHandler, IEndInteractionHandler {
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

    public struct KeyEvent {
        public KeyCode Key;
        private Modifiers modifiers;
        public bool IsConsumed => Key == (KeyCode)0;
        public bool Shift => (modifiers & Modifiers.Shift) != 0;
        public bool Control => (modifiers & Modifiers.Control) != 0;
        public bool Alt => (modifiers & Modifiers.Alt) != 0;
        public KeyEvent(KeyCode key, Modifiers _modifiers) {
            Key = key;
            modifiers = _modifiers;
        }
        public void Consume() { Key = (KeyCode)0; }
        public static implicit operator KeyCode(KeyEvent key) { return key.Key; }
        public static KeyEvent CreateEvent(CSKey key, Modifiers modifiers) {
            return new KeyEvent((KeyCode)key.mKeyId, modifiers);
        }
    }
    public interface IKeyPressHandler : IEventSystemHandler {
        void OnKeyPress(ref KeyEvent keyEvent);
    }
    public interface IKeyDownHandler : IEventSystemHandler {
        void OnKeyDown(ref KeyEvent keyEvent);
    }
    public interface IKeyReleaseHandler : IEventSystemHandler {
        void OnKeyRelease(ref KeyEvent keyEvent);
    }
    public struct CharInputEvent {
        string? buffer;
        public bool IsConsumed => buffer == null;
        public int Length => buffer?.Length ?? 0;
        public CharInputEvent(string _buffer) { buffer = _buffer; }
        public void Consume() { buffer = null; }
        public static implicit operator string(CharInputEvent e) { return e.buffer ?? ""; }
    }
    public interface ICharInputHandler : IEventSystemHandler {
        void OnCharInput(ref CharInputEvent chars);
    }

    public interface INestedEventSystem : IEventSystemHandler {
        EventSystem? EventSystem { get; }
        CanvasLayout GetComputedLayout();
    }
    public interface IPointerEventsRaw : IEventSystemHandler {
        void ProcessPointer(PointerEvent events);
    }
}
