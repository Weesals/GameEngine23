using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Landscape;
using Weesals.UI;

namespace Weesals.Editor {
    public class UIGameView : TabbedWindow, IDropTarget, INestedEventSystem {

        public EventSystem? EventSystem;
        EventSystem? INestedEventSystem.EventSystem => EventSystem;
        CanvasLayout INestedEventSystem.GetComputedLayout() { return GetContentsLayout(); }

        // TODO: Remove these, get them dynamically
        public Camera? Camera;
        public ScenePassManager? Scene;

        private ToggleButton realtimeToggle;
        public bool EnableRealtime => realtimeToggle.State;

        public UIGameView(Editor editor) : base(editor, "Game") {
            EnableBackground = false;
            realtimeToggle = new ToggleButton();
            realtimeToggle.Transform = CanvasTransform.MakeAnchored(new(40f, 40f), new(0f, 0f));
            realtimeToggle.State = false;
            realtimeToggle.AppendChild(new TextBlock("Realtime") { FontSize = 10, });
            AppendChild(realtimeToggle);
        }

        public RectI GetGameViewportRect() {
            var gameLayout = GetContentsLayout();
            return new RectI(
                (int)gameLayout.Position.X, (int)gameLayout.Position.Y,
                (int)gameLayout.GetWidth(), (int)gameLayout.GetHeight());
        }

        public bool InitializePotentialDrop(PointerEvent events, CanvasRenderable source) {
            return true;
        }
        public void UninitializePotentialDrop(CanvasRenderable source) {
        }
        public void ReceiveDrop(CanvasRenderable item) {
            Console.WriteLine("Received " + item);
        }

    }
}
