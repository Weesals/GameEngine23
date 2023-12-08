using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.Game {
    public class UXCameraControls : CanvasRenderable, IBeginDragHandler, IDragHandler, IEndDragHandler {
        public readonly UIPlay PlayUI;
        private TimedEvent<Vector2> rubberband;

        public UXCameraControls(UIPlay playUI) {
            PlayUI = playUI;
        }

        public void OnBeginDrag(PointerEvent events) {
            rubberband.Clear();
        }
        public void OnDrag(PointerEvent events) {
            var camera = PlayUI.Play.Camera;
            var m0 = mLayoutCache.InverseTransformPosition2D(events.PreviousPosition) / mLayoutCache.GetSize();
            var m1 = mLayoutCache.InverseTransformPosition2D(events.CurrentPosition) / mLayoutCache.GetSize();
            if (m0 == m1) return;
            var ray0 = camera.ViewportToRay(m0);
            var ray1 = camera.ViewportToRay(m1);
            // TODO: Project onto the coarse terrain
            var pos0 = ray0.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var pos1 = ray1.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var delta = pos0 - pos1;
            camera.Position += delta;
        }
        public void OnEndDrag(PointerEvent events) {
        }
    }

    public class UIPlay : CanvasRenderable, IPointerMoveHandler, IUpdatable {

        public readonly Play Play;

        public UIPlay(Play play) {
            Play = play;
            Play.Updatables.RegisterUpdatable(this, true);
        }

        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            AppendChild(new UXCameraControls(this));

            var btn = new TextButton();
            btn.SetTransform(CanvasTransform.MakeAnchored(new Vector2(128, 128), new Vector2(0.5f, 1.0f), new Vector2(0.0f, 0.0f)));
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

            if (false) {
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
            }

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

        unsafe public void OnPointerMove(PointerEvent events) {
            // Control position with mouse cursor
            var mpos = Input.GetMousePosition();
            var mray = Play.Camera.ViewportToRay(((RectF)Play.GameViewport).Unlerp(mpos));
            var pos = mray.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var mat = Matrix4x4.CreateRotationY(3.14f) * Matrix4x4.CreateTranslation(pos);
            foreach (var instance in Play.instances) {
                Play.Scene.CSScene.UpdateInstanceData(instance, &mat, sizeof(Matrix4x4));
            }
        }

    }
}
