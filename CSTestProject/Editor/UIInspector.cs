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

    public class UILandscapeTools : CanvasRenderable
        , IToolServiceProvider<BrushConfiguration>
        , IToolServiceProvider<ScenePassManager> {
        public TextButton HeightButton = new("Edit Height");
        public TextButton WaterButton = new("Edit Water");
        public InputDispatcher InputDispatcher = new();

        public UXLandscapeCliffTool CliffTool = new();
        public BrushWaterTool WaterTool = new();

        private BrushConfiguration brushConfig = new();

        ScenePassManager scenePassManager;
        ScenePassManager IToolServiceProvider<ScenePassManager>.Service => scenePassManager;
        BrushConfiguration IToolServiceProvider<BrushConfiguration>.Service => brushConfig;

        private UXBrushTool? activeTool;

        public void Initialize(UIGameView gameView) {
            scenePassManager = gameView.Scene;
            var toolContext = new ToolContext(gameView.Landscape, InputDispatcher, gameView.Camera, this);
            CliffTool.InitializeTool(toolContext);
            WaterTool.InitializeTool(toolContext);

            var list = new ListLayout() { Axis = ListLayout.Axes.Vertical, };

            HeightButton.OnClick += () => { SetActiveTool(activeTool == CliffTool ? null : CliffTool); };
            list.AppendChild(HeightButton);

            WaterButton.OnClick += () => { SetActiveTool(activeTool == WaterTool ? null : WaterTool); };
            list.AppendChild(WaterButton);

            AppendChild(list);
        }

        private void SetActiveTool(UXBrushTool? tool) {
            if (activeTool != null) activeTool.SetActive(false);
            if (activeTool is IInteraction otool) InputDispatcher.RemoveInteraction(otool);
            activeTool = tool;
            if (activeTool is IInteraction ntool) InputDispatcher.AddInteraction(ntool);
            if (activeTool != null) activeTool.SetActive(true);
            HeightButton.Text.Text = activeTool == CliffTool ? "End" : "Edit Height";
            WaterButton.Text.Text = activeTool == WaterTool ? "End" : "Edit Water";
        }
    }
    public class UIInspector : TabbedWindow {
        public UILandscapeTools LandscapeTools = new();
        public UIInspector(Editor editor) : base(editor, "Inspector") {
        }
    }
}
