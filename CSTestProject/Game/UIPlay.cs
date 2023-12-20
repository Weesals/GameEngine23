using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Weesals.Engine;
using Weesals.Landscape;
using Weesals.UI;

namespace Weesals.Game {
    public class UXCameraControls : IInteraction, IBeginDragHandler, IDragHandler, IEndDragHandler {
        public readonly UIPlay PlayUI;
        private TimedEvent<Vector2> rubberband;

        public UXCameraControls(UIPlay playUI) {
            PlayUI = playUI;
        }

        public ActivationScore GetActivation(PointerEvent events) {
            if (PlayUI.Play.Camera == null) return ActivationScore.None;
            if (events.HasButton(1) && events.IsDrag) return ActivationScore.Active;
            return ActivationScore.Potential;
        }

        public void OnBeginDrag(PointerEvent events) {
            if (!events.GetIsButtonDown(1)) { events.Yield(); return; }
            rubberband.Clear();
        }
        public void OnDrag(PointerEvent events) {
            var camera = PlayUI.Play.Camera;
            var layout = PlayUI.GetComputedLayout();
            var m0 = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var m1 = layout.InverseTransformPosition2D(events.CurrentPosition) / layout.GetSize();
            if (m0 == m1) return;
            var ray0 = camera.ViewportToRay(m0);
            var ray1 = camera.ViewportToRay(m1);
            // TODO: Project onto the coarse terrain
            var pos0 = ray0.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var pos1 = ray1.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var delta = pos1 - pos0;
            camera.Position -= delta;
        }
        public void OnEndDrag(PointerEvent events) {
        }
    }

    public class UXEntityDrag : IInteraction, IBeginDragHandler, IDragHandler, IEndDragHandler {
        public readonly UIPlay PlayUI;
        public Play Play => PlayUI.Play;

        public struct Instance {
            public WorldObject Target;
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
            return target != null;
        }
        private WorldObject? FindTarget(PointerEvent events) {
            var layout = PlayUI.GetComputedLayout();
            var m = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var mray = PlayUI.Play.Camera.ViewportToRay(m);
            return PlayUI.Play.HitTest(mray);
        }
        public void OnBeginDrag(PointerEvent events) {
            if (!events.GetIsButtonDown(0)) { events.Yield(); return; }
            var entity = FindTarget(events);
            if (entity == null) { events.Yield(); return; }
            SetSelected(entity, true);
            instances.Add(events, new Instance() { Target = entity, });
        }
        public void OnDrag(PointerEvent events) {
            if (!instances.TryGetValue(events, out var instance)) return;
            var camera = PlayUI.Play.Camera;
            var layout = PlayUI.GetComputedLayout();
            var m0 = layout.InverseTransformPosition2D(events.PreviousPosition) / layout.GetSize();
            var m1 = layout.InverseTransformPosition2D(events.CurrentPosition) / layout.GetSize();
            var ray0 = camera.ViewportToRay(m0);
            var ray1 = camera.ViewportToRay(m1);
            var pos0 = ray0.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var pos1 = ray1.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var delta = pos1 - pos0;
            var pos = PlayUI.Play.Scene.GetLocation(instance.Target);
            PlayUI.Play.Scene.SetLocation(instance.Target, pos + delta);
        }
        public void OnEndDrag(PointerEvent events) {
            SetSelected(instances[events].Target, false);
            instances.Remove(events);
        }

        unsafe private void SetSelected(WorldObject entity, bool selected) {
            foreach (var instance in entity.Meshes) {
                float value = selected ? 1.0f : 0.0f;
                Play.Scene.UpdateInstanceData(instance, sizeof(float) * (16 + 16 + 4), &value, sizeof(float));
            }
        }
    }

    public class UIPlay : CanvasRenderable, IUpdatable {

        public readonly Play Play;

        private InputDispatcher inputDispatcher;
        private UXCameraControls cameraControls;
        private UXEntityDrag entityDrag;

        public UIPlay(Play play) {
            Play = play;
            Play.Updatables.RegisterUpdatable(this, true);
        }

        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            AppendChild(inputDispatcher = new());
            inputDispatcher.AddInteraction(cameraControls = new UXCameraControls(this));
            inputDispatcher.AddInteraction(entityDrag = new UXEntityDrag(this));

            var btn = new TextButton();
            btn.SetTransform(CanvasTransform.MakeAnchored(new Vector2(128, 128), new Vector2(0.5f, 1.0f), new Vector2(0.0f, 0.0f)));
            btn.OnClick += () => { Console.WriteLine("Hello"); };
            AppendChild(btn);

            var spriteRenderer = new SpriteRenderer();
            var atlas = spriteRenderer.Generate(new[] {
                Resources.LoadTexture("./assets/T_Hammer.png"),
                Resources.LoadTexture("./assets/T_Sword.png"),
                Resources.LoadTexture("./assets/T_Staff.png"),
                Resources.LoadTexture("./assets/T_Axe.png"),
                Resources.LoadTexture("./assets/T_Bow.png"),
                Resources.LoadTexture("./assets/T_Meat.png"),
                Resources.LoadTexture("./assets/T_Wheat.png"),
                Resources.LoadTexture("./assets/T_House.png"),
                Resources.LoadTexture("./assets/T_Castle.png"),
                Resources.LoadTexture("./assets/T_Tick.png"),
                Resources.LoadTexture("./assets/T_Spear.png"),
            });

#if FALSE
            var vlist = new ListLayout() {
                Transform = CanvasTransform.MakeAnchored(new Vector2(1024, 512), new Vector2(0.0f, 1.0f)),
            };
            for (int i = 0; i < 50; ++i) {
                var hlist = new ListLayout() { Axis = ListLayout.Axes.Horizontal, };
                for (int x = 0; x < 100; x++) {
                    hlist.AppendChild(new ImageButton(atlas.Sprites[i % atlas.Sprites.Length]) {
                        Transform = CanvasTransform.MakeDefault().Inset(0),
                    });
                }
                vlist.AppendChild(hlist);
            }
            AppendChild(vlist);
#endif

            var list = new ListLayout() {
                Transform = CanvasTransform.MakeAnchored(new Vector2(256, 256), new Vector2(0.0f, 1.0f)),
            };
            for (int i = 0; i < 5; ++i) {
                list.AppendChild(new ImageButton(atlas.Sprites[i % atlas.Sprites.Length]) {
                    Transform = CanvasTransform.MakeDefault().Inset(1),
                });
            }
            AppendChild(list);
        }

        public void Update(float dt) {
            //MarkComposeDirty();
        }

    }
}
