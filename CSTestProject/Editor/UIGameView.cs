using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.Editor {
    public class UIGameView : TabbedWindow, IDropTarget {
        public UIGameView(Editor editor) : base(editor, "Game") {
            EnableBackground = false;
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
        public void UninitialzePotentialDrop(CanvasRenderable source) {
        }
        public void ReceiveDrop(CanvasRenderable item) {
            Console.WriteLine("Received " + item);
        }
    }
}
