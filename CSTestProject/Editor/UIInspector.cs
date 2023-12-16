using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Weesals.Engine;
using Weesals.Landscape;
using Weesals.Landscape.Editor;
using Weesals.UI;

namespace Weesals.Editor {

    public class UILandscapeTools : CanvasRenderable, IToolServiceProvider<BrushConfiguration> {
        public TextButton HeightButton = new("Edit Height");
        public TextButton WaterButton = new("Edit Water");
        public InputDispatcher InputDispatcher = new();

        public UXLandscapeCliffTool CliffTool = new();
        public BrushWaterTool WaterTool = new();

        private BrushConfiguration brushConfig = new();
        BrushConfiguration IToolServiceProvider<BrushConfiguration>.Service => brushConfig;

        public void Initialize(UIGameView gameView) {
            var toolContext = new ToolContext(gameView.Landscape, InputDispatcher, gameView.Camera, this);
            CliffTool.InitializeTool(toolContext);
            WaterTool.InitializeTool(toolContext);

            var list = new ListLayout() { Axis = ListLayout.Axes.Vertical, };

            HeightButton.OnClick += () => {
                bool isAdded = !InputDispatcher.RemoveInteraction(CliffTool);
                if (isAdded) InputDispatcher.AddInteraction(CliffTool);
                HeightButton.Text.Text = isAdded ? "End" : "Edit Height";
            };
            list.AppendChild(HeightButton);

            WaterButton.OnClick += () => {
                bool isAdded = !InputDispatcher.RemoveInteraction(WaterTool);
                if (isAdded) InputDispatcher.AddInteraction(WaterTool);
                WaterButton.Text.Text = isAdded ? "End" : "Edit Water";
            };
            list.AppendChild(WaterButton);

            AppendChild(list);
        }


    }
    public class UIInspector : TabbedWindow {
        public UILandscapeTools LandscapeTools = new();
        public UIInspector(Editor editor) : base(editor, "Inspector") {
        }
    }
}
