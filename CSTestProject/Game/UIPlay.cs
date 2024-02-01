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
using Weesals.UI.Interaction;

namespace Weesals.Game {

    public class UIPlay : CanvasRenderable, IUpdatable {

        public readonly Play Play;

        private InputDispatcher inputDispatcher;
        private UXCameraControls cameraControls;
        private UXEntityDrag entityDrag;
        private UXEntityTap entityTap;
        private UXEntityOrder entityOrder;
        private UXPlacement placement;

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
                ScaleMode = ListLayout.ScaleModes.StretchOrClamp,
            };
            for (int i = 0; i < 5; ++i) {
                var imgBtn = new ImageButton(atlas.Sprites[i % atlas.Sprites.Length]) {
                    Transform = CanvasTransform.MakeDefault().Inset(1),
                };
                imgBtn.OnClick += () => {
                    var entities = Play.World.GetEntities();
                    if (entities.MoveNext())
                        placement.BeginPlacement(entities.Current);
                };
                list.AppendChild(imgBtn);
            }
            AppendChild(list);
        }

        public void Update(float dt) {
            //MarkComposeDirty();
        }

    }
}
