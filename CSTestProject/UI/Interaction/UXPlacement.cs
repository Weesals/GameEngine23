﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Game;
using Weesals.Utility;

namespace Weesals.UI.Interaction {
    public struct Placement {
        public GenericTarget Container;
        public Int2 Position;
        public short Orientation;
    }
    public class UXPlacement : IInteraction, IBeginInteractionHandler, IPointerMoveHandler, IPointerClickHandler {
        public readonly UIPlay PlayUI;
        public Play Play => PlayUI.Play;

        public class Renderable : IDisposable {
            public PooledList<CSInstance> Instances = new();
            public void Dispose() { Instances.Dispose(); }
        }
        public class Instance {
            public Entity Prototype;
            public Placement Placement;
            public Renderable Visuals;
        }

        private Instance? instance;

        public UXPlacement(UIPlay playUI) {
            PlayUI = playUI;
        }

        public void BeginPlacement(Entity entity) {
            CancelPlacement();
            var renderable = new Renderable();
            instance = new() {
                Prototype = entity,
                Visuals = renderable,
            };
            var emodel = Play.World.GetComponent<CModel>(entity);
            foreach (var mesh in emodel.Model.Meshes) {
                var meshInstance = Play.Scene.CreateInstance();
                renderable.Instances.Add(meshInstance);
                Play.ScenePasses.AddInstance(meshInstance, mesh);
            }
        }
        public Entity PerformPlacement() {
            if (instance != null && instance.Placement.Container.IsValid) {
                var entity = Play.World.CreateEntity(instance.Prototype);
                var target = new GenericTarget(Play.Simulation.EntityProxy, GenericTarget.PackEntity(entity));
                var tform = Play.World.TryGetComponentRef<ECTransform>(entity);
                if (tform.HasValue) {
                    tform.Value.Position = instance.Placement.Position;
                    tform.Value.Orientation = instance.Placement.Orientation;
                } else {
                    target.SetWorldPosition(SimulationWorld.SimulationToWorld(instance.Placement.Position));
                }
                return entity;
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
            var tform = Matrix4x4.CreateTranslation(
                SimulationWorld.SimulationToWorld(instance.Placement.Position));
            foreach (var meshInstance in instance.Visuals.Instances) {
                Play.Scene.SetTransform(meshInstance, tform);
            }
            instance.Placement.Container = new GenericTarget(Play.World);
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
