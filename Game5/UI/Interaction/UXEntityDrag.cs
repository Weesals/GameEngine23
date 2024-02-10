using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Game5.Game;
using Weesals.UI;

namespace Game5.UI.Interaction {
    public class UXEntityDrag : IInteraction, IBeginDragHandler, IDragHandler, IEndDragHandler {
        public readonly UIPlay PlayUI;
        public Play Play => PlayUI.Play;

        public struct Instance {
            public GenericTarget Target;
        }
        private Dictionary<PointerEvent, Instance> instances = new();

        public UXEntityDrag(UIPlay playUI) {
            PlayUI = playUI;
        }

        public ActivationScore GetActivation(PointerEvent events) {
            if (!CanInteract(events)) return ActivationScore.None;
            if (events.IsDrag && events.HasButton(0)) return ActivationScore.Active;
            return ActivationScore.Potential;
        }
        public bool CanInteract(PointerEvent events) {
            var target = FindTarget(events);
            if (!target.IsValid) return false;
            if (target.Owner is not IEntityPosition) return false;
            return true;
        }
        private GenericTarget FindTarget(PointerEvent events) {
            var mray = PlayUI.ScreenToRay(events.PreviousPosition);
            return PlayUI.Play.HitTest(mray);
        }
        public void OnBeginDrag(PointerEvent events) {
            if (!events.GetIsButtonDown(0)) { events.Yield(); return; }
            var entity = FindTarget(events);
            if (!entity.IsValid) { events.Yield(); return; }
            instances.Add(events, new Instance() { Target = entity, });
        }
        public void OnDrag(PointerEvent events) {
            if (!instances.TryGetValue(events, out var instance)) return;
            var camera = PlayUI.Play.Camera;
            var ray0 = PlayUI.ScreenToRay(events.PreviousPosition);
            var ray1 = PlayUI.ScreenToRay(events.CurrentPosition);
            var pos0 = ray0.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var pos1 = ray1.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var delta = pos1 - pos0;
            var position = instance.Target.GetWorldPosition();
            position += delta;
            instance.Target.SetWorldPosition(position);
        }
        public void OnEndDrag(PointerEvent events) {
            instances.Remove(events);
        }

        unsafe private void SetSelected(GenericTarget entity, bool selected) {
            //if (entity.Owner is IEntitySelectable selectable)
                //selectable.NotifySelected(entity.Data, selected);
            /*foreach (var instance in entity.Meshes) {
                float value = selected ? 1.0f : 0.0f;
                Play.Scene.UpdateInstanceData(instance, sizeof(float) * (16 + 16 + 4), &value, sizeof(float));
            }*/
        }
    }
}
