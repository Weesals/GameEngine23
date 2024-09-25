using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Landscape;
using Weesals.Landscape.Editor;
using Weesals.UI;

namespace Weesals.Editor {
    public class UILandscapeTools : CanvasRenderable
        , IToolServiceProvider<BrushConfiguration>
        , IToolServiceProvider<ScenePassManager>
        , IInspectorGameOverlay {

        public TextButton HeightButton = new("Cliff") { Name = "Edit Height Btn" };
        public TextButton PaintButton = new("Paint") { Name = "Terrain Paint Btn" };
        public TextButton WaterButton = new("Water") { Name = "Edit Water Btn" };
        public InputDispatcher InputDispatcher = new() { Name = "Landscape Dispatcher" };

        public UXLandscapeCliffTool CliffTool = new();
        public UXLandscapePaintTool PaintTool = new ();
        public BrushWaterTool WaterTool = new();

        private BrushConfiguration brushConfig = new();

        ToolContext toolContext;
        ScenePassManager scenePassManager;
        ScenePassManager IToolServiceProvider<ScenePassManager>.Service => scenePassManager;
        BrushConfiguration IToolServiceProvider<BrushConfiguration>.Service => brushConfig;
        CanvasRenderable IInspectorGameOverlay.GameViewOverlay => InputDispatcher;

        private UXBrushTool? activeTool;

        private UIPropertiesList landscapeProps;
        private UIPropertiesList toolProps;

        public UILandscapeTools() {
            var list = new ListLayout() { Axis = CanvasAxes.Vertical, };

            var modes = new ListLayout() { Axis = CanvasAxes.Horizontal, ScaleMode = ListLayout.ScaleModes.StretchOrClamp, ItemSize = 1.0f, };
            list.AppendChild(modes);

            HeightButton.OnClick += () => { SetActiveTool(activeTool == CliffTool ? null : CliffTool); };
            modes.AppendChild(HeightButton);

            PaintButton.OnClick += () => { SetActiveTool(activeTool == PaintTool ? null : PaintTool); };
            modes.AppendChild(PaintButton);

            WaterButton.OnClick += () => { SetActiveTool(activeTool == WaterTool ? null : WaterTool); };
            modes.AppendChild(WaterButton);

            toolProps = new();
            list.AppendChild(toolProps);

            landscapeProps = new();
            list.AppendChild(landscapeProps);

            AppendChild(list);
        }
        public override void Uninitialise(CanvasBinding binding) {
            base.Uninitialise(binding);
            SetActiveTool(null);
        }

        public void Initialize(UIGameView gameView, LandscapeRenderer landscape) {
            scenePassManager = gameView.Scene;
            toolContext = new ToolContext(landscape, InputDispatcher, gameView.Camera, this);
            CliffTool.InitializeTool(toolContext);
            PaintTool.InitializeTool(toolContext);
            WaterTool.InitializeTool(toolContext);
            landscapeProps.ClearChildren();
            landscapeProps.AppendPropertiesFrom(landscape);
        }

        private void SetActiveTool(UXBrushTool? tool) {
            if (activeTool != null) activeTool.SetActive(false);
            if (activeTool is IInteraction otool) InputDispatcher.RemoveInteraction(otool);
            activeTool = tool;
            if (activeTool is IInteraction ntool) InputDispatcher.AddInteraction(ntool);
            if (activeTool != null) activeTool.SetActive(true);
            HeightButton.TextColor = activeTool == CliffTool ? Color.Black : Color.White;
            PaintButton.TextColor = activeTool == PaintTool ? Color.Black : Color.White;
            WaterButton.TextColor = activeTool == WaterTool ? Color.Black : Color.White;
            toolProps.ClearChildren();
            if (activeTool != null) toolProps.AppendPropertiesFrom(activeTool);
        }
    }
}
