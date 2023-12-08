using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.UI;

namespace Weesals.Editor {
    public class UITerrainTools : CanvasRenderable {
        public TextButton HeightButton = new("Height");
    }
    public class UIInspector : TabbedWindow {
        public UIInspector(Editor editor) : base(editor, "Inspector") {
        }
    }
}
