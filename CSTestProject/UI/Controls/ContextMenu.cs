using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.UI.Controls {
    public class ContextMenu : ApplicationWindow {

        public Canvas Canvas = new();
        public EventSystem EventSystem;

        private ListLayout itemsList;

        public ContextMenu() {
            EventSystem = new(Canvas);
            EventSystem.SetInput(Input);

            Canvas.AppendChild(itemsList = new() {
                Transform = CanvasTransform.MakeDefault(),
                Separation = 1f,
            });
        }

        public void AppendItem(string label, Action onSelect) {
            var button = new TextButton(label);
            button.OnClick += onSelect;
            button.OnClick += Button_Clicked;
            itemsList.AppendChild(button);
        }

        public void Show() {
            if (!Window.IsValid) {
                var window = Core.ActiveInstance.CreateWindow("Context");
                window.SetStyle("borderless");
                window.SetSize((Vector2)Canvas.GetDesiredSize(SizingParameters.Default));
                RegisterRootWindow(window);
            }
            Window.SetVisible(true);
        }

        public override void Update(float dt) {
            base.Update(dt);
            EventSystem.Update(dt);
            if (!Window.IsValid) return;
            Canvas.SetSize(Size);
            Canvas.Update(dt);
            Canvas.RequireComposed();
        }

        private void Button_Clicked() {
            Window.Dispose();
            Dispose();
        }

        public override float AdjustRenderDT(float dt, int renderHash, float targetDT) {
            renderHash += Canvas.Revision;
            return base.AdjustRenderDT(dt, renderHash, targetDT);
        }

        public override void Render(float dt, CSGraphics graphics) {
            graphics.Reset();
            graphics.SetSurface(Surface);
            graphics.SetRenderTargets(Surface.GetBackBuffer(), default);
            graphics.Clear(new(Color.Black, 1f));
            Canvas.Render(graphics);
            graphics.Execute();
            Surface.Present();
        }
    }
}
