using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Game5.Game;
using Game5.Game.Gameplay;
using Weesals.UI;

namespace Game5.UI.Interaction {
    public class UXEntityOrder : IInteraction, IInteractionHandler {

        public readonly UIPlay PlayUI;
        public Play Play => PlayUI.Play;

        public struct Instance {
            public ActionRequest Request;
        }

        private Dictionary<PointerEvent, Instance> instances = new();

        public UXEntityOrder(UIPlay playUI) {
            PlayUI = playUI;
        }

        public ActivationScore GetActivation(PointerEvent events) {
            if (events.HasButton(1)) return ActivationScore.Satisfied;
            return ActivationScore.None;
        }
        public void OnBeginInteraction(PointerEvent events) {
            var layout = PlayUI.GetComputedLayout();
            var m = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var mray = PlayUI.Play.Camera.ViewportToRay(m);

            Vector3 pos;
            if (Play.Landscape.Raycast(mray, out var hit, float.MaxValue)) {
                pos = hit.HitPosition;
            } else {
                new Plane(Vector3.UnitY, 0f).Raycast(mray, out var dst);
                pos = mray.GetPoint(dst);
            }
            // Find the entity this ray hit
            var entity = Play.HitTest(mray);
            ActionRequest request;
            if (entity.IsValid) {
                // If an entity was hit, it becomes the focus of the request
                request = new ActionRequest(entity.GetEntity()) {
                    TargetLocation = SimulationWorld.WorldToSimulation(pos).XZ,
                };
            } else {
                // If no entity was hit, use the ground location
                request = new ActionRequest(SimulationWorld.WorldToSimulation(pos).XZ);
            }
            instances[events] = new Instance() { Request = request };
        }
        public void OnEndInteraction(PointerEvent events) {
            ActionRequest request = default;
            if (instances.TryGetValue(events, out var instance)) {
                request = instance.Request;
                instances.Remove(events);
            }
            var entity = request.TargetEntity;
            //if (!entity.IsValid) { events.Yield(); return; }
            Play.PushLocalCommand(request);
            if (entity.IsValid) {
                Play.EntityHighlighting.Flashing.BeginFlashing(
                    Play.Simulation.EntityProxy.MakeHandle(entity),
                    EntityHighlighting.OrderEffect
                );
            }
        }

        public bool CanInteract(PointerEvent events) {
            var target = FindTarget(events);
            return target.IsValid;
        }
        private ItemReference FindTarget(PointerEvent events) {
            var layout = PlayUI.GetComputedLayout();
            var m = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var mray = PlayUI.Play.Camera.ViewportToRay(m);
            return PlayUI.Play.HitTest(mray);
        }

    }
}
