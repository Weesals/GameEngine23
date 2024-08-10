using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Game5.Game;
using Weesals.Engine;
using Weesals.UI;

namespace Game5.UI.Interaction {
    public class UXEntityDrag : IInteraction, IBeginDragHandler, IDragHandler, IEndDragHandler {
        public readonly UIPlay PlayUI;
        public Play Play => PlayUI.Play;

        public struct Instance {
            public ItemReference Target;
            public Vector3 DragOffset;
            public Plane DragPlane;
        }
        private Dictionary<PointerEvent, Instance> instances = new();

        public UXEntityDrag(UIPlay playUI) {
            PlayUI = playUI;
        }

        public ActivationScore GetActivation(PointerEvent events) {
            if (!events.HasButton(0) || !CanInteract(events)) return ActivationScore.None;
            if (events.IsDrag && !events.WasDrag) return ActivationScore.Active;
            return ActivationScore.Potential;
        }
        public bool CanInteract(PointerEvent events) {
            var target = FindTarget(events);
            if (!target.IsValid) return false;
            if (target.Owner is not IItemPosition) return false;
            return true;
        }
        private ItemReference FindTarget(PointerEvent events) {
            var mray = PlayUI.ScreenToRay(events.PreviousPosition);
            return PlayUI.Play.HitTest(mray);
        }
        public void OnBeginDrag(PointerEvent events) {
            if (!events.GetIsButtonDown(0)) { events.Yield(); return; }
            var entity = FindTarget(events);
            if (!entity.IsValid) { events.Yield(); return; }
            var pos = PlayUI.ScreenToRay(events.PreviousPosition)
                .ProjectTo(new Plane(Vector3.UnitY, 0f));
            instances.Add(events, new Instance() {
                Target = entity,
                DragPlane = new Plane(Vector3.UnitY, pos.Y),
                DragOffset = entity.GetWorldPosition() - pos,
            });
        }
        public void OnDrag(PointerEvent events) {
            if (!instances.TryGetValue(events, out var instance)) return;
            /*var camera = PlayUI.Play.Camera;
            var ray0 = PlayUI.ScreenToRay(events.PreviousPosition);
            var ray1 = PlayUI.ScreenToRay(events.CurrentPosition);
            var pos0 = ray0.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var pos1 = ray1.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var delta = pos1 - pos0;
            var position = instance.Target.GetWorldPosition();
            position += delta;
            instance.Target.SetWorldPosition(position);*/

            instance.DragOffset = Vector3.Lerp(
                instance.DragOffset,
                Vector3.Zero,
                Math.Clamp(Vector2.Distance(events.CurrentPosition, events.PreviousPosition) / 100.0f, 0, 1)
            );
            instances[events] = instance;

            var ray = PlayUI.ScreenToRay(events.CurrentPosition);
            var pos = ray.ProjectTo(instance.DragPlane) + instance.DragOffset;
            instance.Target.SetWorldPosition(pos);
        }
        public void OnEndDrag(PointerEvent events) {
            instances.Remove(events);
        }

        unsafe private void SetSelected(ItemReference entity, bool selected) {
            //if (entity.Owner is IEntitySelectable selectable)
                //selectable.NotifySelected(entity.Data, selected);
            /*foreach (var instance in entity.Meshes) {
                float value = selected ? 1.0f : 0.0f;
                Play.Scene.UpdateInstanceData(instance, sizeof(float) * (16 + 16 + 4), &value, sizeof(float));
            }*/
        }
    }
}
