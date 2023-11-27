using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.Game {
    public class UIPlay : CanvasRenderable {
        public CanvasImage Background = new();
        public CanvasText Text = new();
        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            Background.Initialize(Canvas);
            Text.Initialize(Canvas);
            Text.SetFont(Resources.LoadFont("./assets/Roboto-Regular.ttf"));
            Text.Text = "Hello";
            Text.SetColor(new Color(0xff00ffff));


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

            var grid = new ListLayout() {
                Transform = CanvasTransform.MakeAnchored(new Vector2(256, 256), new Vector2(0.0f, 1.0f)),
            };
            for (int i = 0; i < 5; ++i) {
                var bg = new Image() { Transform = CanvasTransform.MakeDefault().Inset(2), };
                bg.AppendChild(new Image(atlas.Sprites[i]));
                grid.AppendChild(bg);
            }
            AppendChild(grid);
        }
        public override void Uninitialise(CanvasBinding binding) {
            Background.Dispose(Canvas);
            Text.Dispose(Canvas);
            base.Uninitialise(binding);
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            var tform = CanvasTransform.MakeAnchored(new Vector2(128, 128), new Vector2(0.5f, 1.0f), new Vector2(0.0f, 0.0f));
            tform.Apply(mLayoutCache, out var layout);
            Background.UpdateLayout(Canvas, layout);
            Text.UpdateLayout(Canvas, layout);
            Background.Append(ref composer);
            Text.Append(ref composer);
            base.Compose(ref composer);
        }
    }
}
