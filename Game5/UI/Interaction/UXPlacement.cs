using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Game5.Game;
using Weesals.UI;
using Weesals.Utility;

namespace Game5.UI.Interaction {
    public struct Placement {
        public ItemReference Container;
        public Int2 Position;
        public short Orientation;
        public static readonly Placement Default = new() { Orientation = short.MinValue, };
    }
    public class UXPlacement : IInteraction, IBeginInteractionHandler, IPointerMoveHandler, IPointerClickHandler {
        public readonly UIPlay PlayUI;
        public Play Play => PlayUI.Play;

        public class Renderable : IDisposable {
            public PooledList<CSInstance> Instances = new();
            public void Dispose() { Instances.Dispose(); }
        }
        public class Instance {
            public PrototypeData Prototype;
            public Placement Placement = Placement.Default;
            public Renderable Visuals;
        }

        private Instance? instance;

        public UXPlacement(UIPlay playUI) {
            PlayUI = playUI;
        }

        public void BeginPlacement(PrototypeData proto) {
            CancelPlacement();
            var renderable = new Renderable();
            instance = new() {
                Prototype = proto,
                Visuals = renderable,
            };
            var emodel = Play.Simulation.PrefabRegistry.GetComponent<CModel>(proto.Prefab);
            if (emodel.Model != null) {
                foreach (var mesh in emodel.Model.Meshes) {
                    var meshInstance = Play.Scene.CreateInstance(mesh.BoundingBox);
                    renderable.Instances.Add(meshInstance);
                    Play.ScenePasses.AddInstance(meshInstance, mesh);
                }
            }
        }
        public Entity PerformPlacement() {
            if (instance != null && instance.Placement.Container.IsValid) {
                var mover = Play.Simulation.PrefabRegistry.BeginInstantiate(Play.Simulation.World, instance.Prototype.Prefab);
                var target = Play.Simulation.EntityProxy.MakeHandle(mover.Entity);
                var tform = Play.World.TryGetComponentRef<ECTransform>(mover.Entity);
                if (tform.HasValue) {
                    tform.Value.Position = instance.Placement.Position;
                    tform.Value.Orientation = instance.Placement.Orientation;
                } else {
                    var rot = MathF.PI * instance.Placement.Orientation / (float)short.MinValue;
                    target.SetWorldPosition(SimulationWorld.SimulationToWorld(instance.Placement.Position));
                    target.SetWorldRotation(Quaternion.CreateFromAxisAngle(Vector3.UnitY, rot));
                }
                mover.Commit();
                return mover.Entity;
            }
            DestroyInstance();
            return default;
        }
        private void CancelPlacement() {
            if (instance == null) return;
            DestroyInstance();
        }
        private void DestroyInstance() {
            if (instance == null) return;
            foreach (var meshInstance in instance.Visuals.Instances) {
                Play.ScenePasses.RemoveInstance(meshInstance);
                Play.Scene.RemoveInstance(meshInstance);
            }
            instance = null;
        }

        public ActivationScore GetActivation(PointerEvent events) {
            if (instance != null) return ActivationScore.Active;
            return ActivationScore.None;
        }

        public void OnBeginInteraction(PointerEvent events) {

        }
        public void OnPointerMove(PointerEvent events) {
            if (instance == null) { events.Yield(); return; }

            var layout = PlayUI.GetComputedLayout();
            var m = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var mray = PlayUI.Play.Camera.ViewportToRay(m);
            instance.Placement.Position =
                SimulationWorld.WorldToSimulation(mray.ProjectTo(new Plane(Vector3.UnitY, 0f))).XZ;
            var rot = MathF.PI * instance.Placement.Orientation / (float)short.MinValue;
            var tform =
                Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, rot))*
                Matrix4x4.CreateTranslation(SimulationWorld.SimulationToWorld(instance.Placement.Position));
            foreach (var meshInstance in instance.Visuals.Instances) {
                Play.Scene.SetTransform(meshInstance, tform);
                Play.Scene.SetHighlight(meshInstance, new Color(64, 64, 64, 64));
            }
            instance.Placement.Container = new ItemReference(Play.World);
        }
        public void OnPointerClick(PointerEvent events) {
            if (events.HasButton(1)) {
                CancelPlacement();
                return;
            }
            PerformPlacement();
        }

    }
}
