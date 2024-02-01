using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Game;
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

        public UILandscapeTools() {
            var list = new ListLayout() { Axis = ListLayout.Axes.Vertical, };

            HeightButton.OnClick += () => { SetActiveTool(activeTool == CliffTool ? null : CliffTool); };
            list.AppendChild(HeightButton);

            WaterButton.OnClick += () => { SetActiveTool(activeTool == WaterTool ? null : WaterTool); };
            list.AppendChild(WaterButton);

            AppendChild(list);
        }

        public void Initialize(UIGameView gameView, LandscapeRenderer landscape) {
            scenePassManager = gameView.Scene;
            var toolContext = new ToolContext(landscape, InputDispatcher, gameView.Camera, this);
            CliffTool.InitializeTool(toolContext);
            WaterTool.InitializeTool(toolContext);
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

    public class UIEntityInspector : CanvasRenderable {
        public ListLayout List;
        public UIEntityInspector() {
            List = new ListLayout() { Axis = ListLayout.Axes.Vertical, };
            AppendChild(List);
        }
        public void Initialise(GenericTarget entity) {
            List.ClearChildren();
            if (entity.Owner is IEntityRedirect redirect)
                entity = redirect.GetOwner(entity.Data);
            if (entity.Owner is World world) {
                foreach (var component in world.GetEntityComponents(GenericTarget.UnpackEntity(entity.Data))) {
                    var type = component.GetComponentType();
                    var value = component.GetValue();
                    List.AppendChild(new TextBlock(type.Type.Name) { FontSize = 14, Color = component.TypeId.IsSparse ? Color.Yellow : Color.White });
                    foreach (var field in type.Type.GetFields()) {
                        var row = new ListLayout() { Axis = ListLayout.Axes.Horizontal, };
                        List.AppendChild(row);
                        var label = new TextBlock(field.Name) { FontSize = 14, Color = Color.Black, DisplayParameters = TextDisplayParameters.Flat, };
                        label.SetTransform(CanvasTransform.MakeDefault().WithOffsets(-10f, 0f, 10f, 0f));
                        row.AppendChild(label);
                        if (field.FieldType == typeof(bool)) {
                            var toggle = new ToggleButton() { };
                            if (value != null) {
                                var propertyPath = CreatePropertyPath(component.Column.Items, component.Row, field);
                                toggle.BindValue(propertyPath);
                            }
                            row.AppendChild(toggle);
                        } else {
                            var fieldValue = field.GetValue(value);
                            row.AppendChild(new TextBlock("" + fieldValue) { FontSize = 14, Color = Color.Black, DisplayParameters = TextDisplayParameters.Flat, });
                        }
                    }
                    var spacer = new CanvasRenderable();
                    spacer.SetTransform(CanvasTransform.MakeDefault().WithOffsets(0f, -10f, 0f, 10f));
                    List.AppendChild(spacer);
                }
            }
        }
        private PropertyPath CreatePropertyPath(Array owner, int index, FieldInfo field) {
            var propPath = new PropertyPath(owner);
            propPath.DefrenceArray(index);
            propPath.DefrenceField(field);
            return propPath;
        }
    }

    public class UIInspector : TabbedWindow {
        public UILandscapeTools LandscapeTools = new();
        public UIEntityInspector EntityInspector = new();
        private CanvasRenderable? activeInspector;
        public UIInspector(Editor editor) : base(editor, "Inspector") {
            //editor.SelectedItems
        }
        public void SetInspector(CanvasRenderable? inspector) {
            if (activeInspector != null) RemoveChild(activeInspector);
            activeInspector = inspector;
            if (activeInspector != null) AppendChild(activeInspector);
        }
    }
}
