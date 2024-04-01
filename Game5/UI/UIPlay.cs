using System;
using System.Collections.Generic;
using System.Numerics;
using Weesals.Engine;
using Game5.Game;
using Weesals.UI;
using Game5.UI.Interaction;
using Weesals.Utility;
using Weesals.ECS;
using Weesals.Engine.Importers;

namespace Game5.UI {

    public class UIPlay : CanvasRenderable
        , IUpdatable
        , IKeyPressHandler
        , IKeyReleaseHandler
        {

        public readonly Play Play;

        private InputDispatcher inputDispatcher;
        private UXCameraControls cameraControls;
        private UXEntityDrag entityDrag;
        private UXEntityTap entityTap;
        private UXEntityOrder entityOrder;
        private UXPlacement placement;

        private Int3 cameraMove = Int3.Zero;

        public UIPlay(Play play) {
            Play = play;
            Play.Updatables.RegisterUpdatable(this, true);
        }

        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            AppendChild(inputDispatcher = new());
            inputDispatcher.AddInteraction(cameraControls = new(this));
            inputDispatcher.AddInteraction(entityDrag = new(this));
            inputDispatcher.AddInteraction(entityTap = new(this));
            inputDispatcher.AddInteraction(entityOrder = new(this));
            inputDispatcher.AddInteraction(placement = new(this));

            var btn = new TextButton();
            btn.SetTransform(CanvasTransform.MakeAnchored(new Vector2(128, 128), new Vector2(0.5f, 1.0f), new Vector2(0.0f, 0.0f)));
            btn.OnClick += () => { Console.WriteLine("Hello"); };
            //AppendChild(btn);

            var spriteRenderer = new SpriteRenderer();
            var atlas = spriteRenderer.Generate(new[] {
                Resources.LoadTexture("./Assets/ui/T_Bow.png"),
                Resources.LoadTexture("./Assets/ui/T_House.png"),
                Resources.LoadTexture("./Assets/ui/T_Castle.png"),
                Resources.LoadTexture("./Assets/ui/T_Hammer.png"),
                Resources.LoadTexture("./Assets/ui/T_Sword.png"),
                Resources.LoadTexture("./Assets/ui/T_Staff.png"),
                Resources.LoadTexture("./Assets/ui/T_Axe.png"),
                Resources.LoadTexture("./Assets/ui/T_Meat.png"),
                Resources.LoadTexture("./Assets/ui/T_Wheat.png"),
                Resources.LoadTexture("./Assets/ui/T_Tick.png"),
                Resources.LoadTexture("./Assets/ui/T_Spear.png"),
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
                Transform = CanvasTransform.MakeAnchored(new Vector2(300, 64), new Vector2(0.0f, 1.0f)),
                ScaleMode = ListLayout.ScaleModes.StretchOrClamp,
                Axis = ListLayout.Axes.Horizontal,
            };
            for (int i = 0; i < 5; ++i) {
                var imgBtn = new ImageButton(atlas.Sprites[i % atlas.Sprites.Length]) {
                    Transform = CanvasTransform.MakeDefault().Inset(1),
                };
                int id = i;
                imgBtn.OnClick += () => {
                    var protoData = Play.Simulation.ProtoSystem.GetPrototypeData(new EntityPrefab(id));
                    placement.BeginPlacement(protoData);
                };
                list.AppendChild(imgBtn);
            }
            AppendChild(list);
            Canvas.KeyboardFilter.Insert(0, this);
            //AppendChild(new Image(testTexture.Texture));
        }

        public void Update(float dt) {
            //MarkComposeDirty();
            var camRight = Vector3.Normalize(Play.Camera.Right * new Vector3(1f, 0f, 1f));
            var camFwd = Vector3.Normalize(Vector3.Cross(camRight, Vector3.UnitY));
            var ray = Play.Camera.ViewportToRay(new Vector2(0.5f, 0.5f));

            var camera = Play.Camera;
            Vector3 pivot = camera.Position;
            if (Play.Landscape.Raycast(ray, out var hit)) pivot = hit.HitPosition;
            camera.Position +=
                (cameraMove.X * camRight + cameraMove.Z * camFwd + cameraMove.Y * Vector3.UnitY)
                * (dt * camera.Position.Y);
            var delta = (Input.GetKeyDown(KeyCode.Minus) ? -1 : 0)
                + (Input.GetKeyDown(KeyCode.Plus) ? 1 : 0);
            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, delta * dt);
            camera.Orientation = rot * camera.Orientation;
            camera.Position += (pivot - camera.Position)
                - Vector3.Transform(pivot - camera.Position, rot);
        }

        public Ray ScreenToRay(Vector2 mpos) {
            var layout = GetComputedLayout();
            var m = layout.InverseTransformPosition2DN(mpos);
            var mray = Play.Camera.ViewportToRay(m);
            return mray;
        }

        private Int3 GetAxis(KeyCode key) {
            Int3 axis = 0;
            if (key == KeyCode.W || key == KeyCode.UpArrow) axis.Z++;
            if (key == KeyCode.A || key == KeyCode.LeftArrow) axis.X--;
            if (key == KeyCode.S || key == KeyCode.DownArrow) axis.Z--;
            if (key == KeyCode.D || key == KeyCode.RightArrow) axis.X++;
            if (key == KeyCode.Q) axis.Y--;
            if (key == KeyCode.E) axis.Y++;
            return axis;
        }
        public void OnKeyPress(ref KeyEvent key) {
            cameraMove += GetAxis(key);
            if (key == KeyCode.Delete) {
                using var selected = PooledArray<ItemReference>.FromEnumerator(Play.SelectionManager.Selected);
                Play.SelectionManager.ClearSelected();
                foreach (var entity in selected) Play.World.DeleteEntity(entity.GetEntity());
            }
            if (key == KeyCode.Escape) {
                Play.SelectionManager.ClearSelected();
            }
        }
        public void OnKeyRelease(ref KeyEvent key) {
            cameraMove -= GetAxis(key.Key);
        }

    }
}
