using Game5.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;
using Weesals.Utility;

/// <summary>
/// Allows all entities to be selected within a screen-space rectangle
/// (ie. click and drag to make box and select stuff)
/// </summary>
namespace Game5.UI.Interaction {
    using EntityCollection = PooledHashSet<ItemReference>;

    public class UXBoxSelect : IInteraction, IBeginDragHandler, IEndDragHandler, IDragHandler {
        // Selection mode can be modified by pressing
        // Control or Shift
        public enum SelectionModes {
            Overwrite = Flag_New | Flag_Intersect,
            Union = Flag_New | Flag_Intersect | Flag_Prior,
            Exclude = Flag_Prior,
            Flag_New = 0x01,        // The newly selected items
            Flag_Intersect = 0x02,  // Items in both new and prior
            Flag_Prior = 0x04,      // The items previously selected
        };

        public readonly UIPlay PlayUI;
        public Play Play => PlayUI.Play;
        public Image SelectionBox = new() { Color = new Color(new Vector4(1f, 1f, 1f, 0.3f)) };
        public SelectionModes Mode { get; private set; }

        public class Instance {
            public Play Play;
            public EntityCollection CurItems;
            public EntityCollection PriorItems;
            public Vector2 DownPosition;
            public SelectionManager Selection => Play.SelectionManager;
        }
        private Instance? instance;

        public UXBoxSelect(UIPlay play) {
            PlayUI = play;
        }

        void IBeginDragHandler.OnBeginDrag(PointerEvent events) {
            instance = new Instance() {
                Play = Play,
                CurItems = new(16),
                PriorItems = new(16),
                DownPosition = events.PreviousPosition,
            };
            instance.PriorItems.AddRange(instance.Selection.Selected);
            Mode = SelectionModes.Union;
            UpdateSelectMode();
            PlayUI.Canvas.AppendChild(SelectionBox);
        }
        void IDragHandler.OnDrag(PointerEvent events) {
            // Cancel when the mouse is released
            if (instance == null || !events.IsButtonDown) { events.Cancel(this); return; }

            // TODO: This modifies an enumerator? Need to not do that.
            // Stick them in a temp list first
            foreach (var item in instance.Selection.Selected) {
                // We selected it ourselves
                if (instance.CurItems.Contains(item)) continue;
                // We already know about it
                if (instance.PriorItems.Contains(item)) continue;
                instance.PriorItems.Add(item);
                if (!GetIncludeForMode(instance, Mode, item))
                    instance.Selection.RemoveSelected(item);
            }
            UpdateSelectMode();
            var p0 = instance.DownPosition;
            var p1 = events.CurrentPosition;
            var pMin = Vector2.Min(p0, p1) - new Vector2(5f, 5f);
            var pMax = Vector2.Max(p0, p1) + new Vector2(5f, 5f);
            SelectionBox.Transform = CanvasTransform.MakeDefault()
                .WithAnchors(default, default)
                .WithOffsets(pMin, pMax);
            /*SelectionBox.enabled = true;
            var rt = SelectionBox.rectTransform;
            var parent = (RectTransform)rt.parent;
            var canvas = GetComponentInParent<Canvas>();
            canvas.ScreenPointToLocalPointInRectangle(
                parent, pMin, out Vector2 rmin);
            canvas.ScreenPointToLocalPointInRectangle(
                parent, pMax, out Vector2 rmax);

            // Update the selection box visuals
            var prect = parent.rect;
            rt.anchorMin = math.unlerp(prect.min, prect.max, rmin);
            rt.anchorMax = math.unlerp(prect.min, prect.max, rmax);
            */
            // Find the relevant entities (within the box area)
            var titems = new EntityCollection(16);
            Play.GetEntitiesInScreenRect(
                RectF.FromMinMax(pMin.X, pMin.Y, pMax.X, pMax.Y),
                ref titems
            );
            var temp = new EntityCollection(16);
            temp.Clear();

            // Filter based on selection priority
            int maxPriority = 0;
            foreach (var item in titems) {
                // If we previously selected this unit (and selection mode is not Overwrite)
                // filter it out from this check
                if ((Mode & SelectionModes.Flag_Prior) != 0 && instance.PriorItems.Contains(item)) continue;
                maxPriority = Math.Max(maxPriority, GetEntitySelectPriority(item));
            }
            if (maxPriority > 0) {
                for (var en = titems.GetEnumerator(); en.MoveNext();) {
                    if (GetEntitySelectPriority(en.Current) < maxPriority) en.RemoveSelf(ref titems);
                }
            }
            using (var hold = new SelectionManager.Hold(instance.Selection)) {
                // Intersect with the existing items
                foreach (var item in instance.CurItems) temp.Add(item);
                foreach (var item in titems) temp.Remove(item);
                // Add/remove items if they dont/do exist in the current active set
                foreach (var item in temp) {
                    RemoveEntity(item);
                }
                foreach (var item in titems) {
                    AddEntity(item);
                }
            }
            titems.Dispose();
            temp.Dispose();
        }
        void IEndDragHandler.OnEndDrag(PointerEvent events) {
            //PlayInteractions.SetSelection(curItems, Append);
            //SelectionBox.enabled = false;
            if (instance != null) {
                PlayUI.Canvas.RemoveChild(SelectionBox);
                if (!instance.CurItems.IsEmpty) {
                    foreach (var item in instance.CurItems) RegisterEntity(item, false);
                }
                instance.CurItems.Dispose();
                instance.PriorItems.Dispose();
                instance = null;
            }
        }

        private int GetEntitySelectPriority(ItemReference target) {
            var play = Play;
            var isLocalEntity = target.TryGetOwnerId() == 1;// play.GetLocalPlayer().SlotId;
            if (!isLocalEntity) return 0;
            var entity = target.GetEntity();
            if (entity.IsValid && play.World.HasComponent<ECMobile>(entity)) return 1;
            return 2;
        }

        // This interaction applies for left-mouse click-drag performances
        public ActivationScore GetActivation(PointerEvent events) {
            if (!events.HasButton(0)) return ActivationScore.None;
            if (events.IsDrag) return ActivationScore.Active;
            return ActivationScore.Satisfied;
        }

        // Should an entity be included based on the current selection mode
        private bool GetIncludeForMode(Instance instance, SelectionModes mode, ItemReference entity) {
            bool cur = instance.CurItems.Contains(entity);
            bool prev = instance.PriorItems.Contains(entity);
            var flags = cur && prev ? SelectionModes.Flag_Intersect :
                cur ? SelectionModes.Flag_New :
                prev ? SelectionModes.Flag_Prior :
                (SelectionModes)0;
            return ((mode & flags) != 0);
        }

        // Determine current selection mode based on key states
        private void UpdateSelectMode() {
            SetSelectMode(
                Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) ? SelectionModes.Union :
                Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl) ? SelectionModes.Exclude :
                SelectionModes.Overwrite
            );
        }
        private int FlagChanged(SelectionModes mode1, SelectionModes mode2, SelectionModes flag) {
            int delta = (mode2 & flag) - (mode1 & flag);
            return delta < 0 ? -1 : delta > 0 ? 1 : 0;
        }
        // Repair the currently selected objects based on the active
        // object set when changing modes
        // (ie. changing from Subtract to Unison means all
        // entities should be re-added to the active selection)
        private void SetSelectMode(SelectionModes mode) {
            if (Mode == mode) return;
            var temp = new EntityCollection(16);
            int intersectD = FlagChanged(Mode, mode, SelectionModes.Flag_Intersect);
            if (intersectD != 0) {
                if (FlagChanged(Mode, mode, SelectionModes.Flag_New) == intersectD) {
                    ApplyModeDelta(instance, ref instance.CurItems, intersectD, SelectionModes.Flag_New | SelectionModes.Flag_Intersect);
                } else if (FlagChanged(Mode, mode, SelectionModes.Flag_Prior) == intersectD) {
                    ApplyModeDelta(instance, ref instance.PriorItems, intersectD, SelectionModes.Flag_Prior | SelectionModes.Flag_Intersect);
                } else {
                    GetIntersect(ref instance.CurItems, ref instance.PriorItems, ref temp);
                    ApplyModeDelta(instance, ref temp, intersectD, SelectionModes.Flag_Intersect);
                }
            }
            var newD = FlagChanged(Mode, mode, SelectionModes.Flag_New);
            if (newD != 0) {
                GetExclude(ref instance.CurItems, ref instance.PriorItems, ref temp);
                ApplyModeDelta(instance, ref temp, newD, SelectionModes.Flag_New);
            }
            var oldD = FlagChanged(Mode, mode, SelectionModes.Flag_Prior);
            if (oldD != 0) {
                GetExclude(ref instance.PriorItems, ref instance.CurItems, ref temp);
                ApplyModeDelta(instance, ref temp, oldD, SelectionModes.Flag_Prior);
            }
            Mode = mode;
            temp.Dispose();
        }
        // Modify the actual current selection based on mode changes
        private void ApplyModeDelta(Instance instance, ref EntityCollection items, int delta, SelectionModes modeFlags) {
            using var hold = new SelectionManager.Hold(instance.Selection);
            if (delta > 0) {
                foreach (var item in items) instance.Selection.AppendSelected(item);
                Mode |= modeFlags;
            } else {
                foreach (var item in items) instance.Selection.RemoveSelected(item);
                Mode &= ~modeFlags;
            }
        }
        private void GetIntersect(ref EntityCollection items1, ref EntityCollection items2, ref PooledHashSet<ItemReference> temp) {
            temp.Clear();
            foreach (var item in items2) if (items1.Contains(item)) temp.Add(item);
        }
        private void GetExclude(ref EntityCollection items, ref EntityCollection exclude, ref PooledHashSet<ItemReference> temp) {
            temp.Clear();
            foreach (var item in items) temp.Add(item);
            foreach (var item in exclude) temp.Remove(item);
        }

        // Add or remove an entity from the active set
        private void AddEntity(ItemReference entity) {
            if (instance.CurItems.Contains(entity)) return;
            AddRemoveEntity(entity, true);
        }
        private void RemoveEntity(ItemReference entity) {
            if (!instance.CurItems.Contains(entity)) return;
            AddRemoveEntity(entity, false);
        }
        private void AddRemoveEntity(ItemReference entity, bool add) {
            var selection = instance.Selection;
            bool wasIncluded = GetIncludeForMode(instance, Mode, entity);
            if (add) instance.CurItems.Add(entity);
            else instance.CurItems.Remove(entity);
            RegisterEntity(entity, add);
            bool isIncluded = GetIncludeForMode(instance, Mode, entity);
            if (isIncluded != wasIncluded) {
                if (isIncluded) selection.AppendSelected(entity); else selection.RemoveSelected(entity);
            }
        }
        private void RegisterEntity(ItemReference entity, bool enable) {
            //entity.SetHighlight(enable ? Color.white * 0.3f : Color.clear);
        }

    }
}

