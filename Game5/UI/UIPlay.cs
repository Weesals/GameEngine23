using System;
using System.Collections.Generic;
using System.Numerics;
using Weesals.Engine;
using Game5.Game;
using Weesals.UI;
using Game5.UI.Interaction;
using Weesals.Utility;
using Weesals.ECS;

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

        private Int2 cameraMove = Int2.Zero;

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
                Resources.LoadTexture("./Assets/T_Hammer.png"),
                Resources.LoadTexture("./Assets/T_Sword.png"),
                Resources.LoadTexture("./Assets/T_Staff.png"),
                Resources.LoadTexture("./Assets/T_Axe.png"),
                Resources.LoadTexture("./Assets/T_Bow.png"),
                Resources.LoadTexture("./Assets/T_Meat.png"),
                Resources.LoadTexture("./Assets/T_Wheat.png"),
                Resources.LoadTexture("./Assets/T_House.png"),
                Resources.LoadTexture("./Assets/T_Castle.png"),
                Resources.LoadTexture("./Assets/T_Tick.png"),
                Resources.LoadTexture("./Assets/T_Spear.png"),
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
                ScaleMode = ListLayout.ScaleModes.StretchOrClamp,
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
        }

        public void Update(float dt) {
            //MarkComposeDirty();
            Play.Camera.Position += (new Vector2(cameraMove.X, cameraMove.Y) * (dt * Play.Camera.Position.Y)).AppendY(0f);
        }

        public Ray ScreenToRay(Vector2 mpos) {
            var layout = GetComputedLayout();
            var m = layout.InverseTransformPosition2D(mpos) / layout.GetSize();
            var mray = Play.Camera.ViewportToRay(m);
            return mray;
        }

        private Int2 GetAxis(KeyCode key) {
            Int2 axis = Int2.Zero;
            if (key == KeyCode.W || key == KeyCode.UpArrow) axis.Y++;
            if (key == KeyCode.A || key == KeyCode.LeftArrow) axis.X--;
            if (key == KeyCode.S || key == KeyCode.DownArrow) axis.Y--;
            if (key == KeyCode.D || key == KeyCode.RightArrow) axis.X++;
            return axis;
        }
        public void OnKeyPress(ref KeyEvent key) {
            cameraMove += GetAxis(key);
            if (key == KeyCode.Delete) {
                using var selected = PooledArray<GenericTarget>.FromEnumerator(Play.SelectionManager.Selected);
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
