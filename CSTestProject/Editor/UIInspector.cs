using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Engine.Converters;
using Weesals.Landscape;
using Weesals.Landscape.Editor;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Editor
{

    public class UILandscapeTools : CanvasRenderable
        , IToolServiceProvider<BrushConfiguration>
        , IToolServiceProvider<ScenePassManager> {

        public TextButton HeightButton = new("Edit Height") { Name = "Edit Height Btn" };
        public TextButton WaterButton = new("Edit Water") { Name = "Edit Water Btn" };
        public TextButton SaveButton = new("Save") { Name = "Save Btn" };
        public InputDispatcher InputDispatcher = new();

        public UXLandscapeCliffTool CliffTool = new();
        public BrushWaterTool WaterTool = new();

        private BrushConfiguration brushConfig = new();

        ToolContext toolContext;
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

            SaveButton.OnClick += () => { toolContext.LandscapeData.Save(); };
            list.AppendChild(SaveButton);

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
            WaterTool.InitializeTool(toolContext);
        }

        private void SetActiveTool(UXBrushTool? tool) {
            if (activeTool != null) activeTool.SetActive(false);
            if (activeTool is IInteraction otool) InputDispatcher.RemoveInteraction(otool);
            activeTool = tool;
            if (activeTool is IInteraction ntool) InputDispatcher.AddInteraction(ntool);
            if (activeTool != null) activeTool.SetActive(true);
            HeightButton.TextElement.Text = activeTool == CliffTool ? "End" : "Edit Height";
            WaterButton.TextElement.Text = activeTool == WaterTool ? "End" : "Edit Water";
        }
    }

    public class UIPropertiesList : ListLayout, IUpdatable {
        public class TextBlockBound : TextBlock, IBindableValue {
            public void BindValue(PropertyPath path) {
                var type = path.GetPropertyType();
                if (type == typeof(float))
                    Text = path.GetValueAs<float>().ToString("0.00") ?? "";
                else
                    Text = path.GetValueAs<object>()?.ToString() ?? "";
            }
        }
        public class TextFieldBound : TextField, IBindableValue {
            private static Regex FloatRegex = new Regex("^[+-]?([0-9]*[.])?[0-9]+$");
            private static Regex IntRegex = new Regex("^[+-]?\\d+$");
            private static Regex UIntRegex = new Regex("^[+]?\\d+$");
            private PropertyPath binding;
            public TextFieldBound() {
                OnValidateInput += Text_Validate;
                OnTextChanged += Text_Changed;
            }
            private string? Text_Validate(string value) {
                if (string.IsNullOrEmpty(value)) return value;
                var propertyType = binding.GetPropertyType();
                if (propertyType.IsValueType && false) {
                    if (propertyType == typeof(int) || propertyType == typeof(short) || propertyType == typeof(sbyte)) {
                        if (!IntRegex.IsMatch(value)) return default;
                    }
                    if (propertyType == typeof(uint) || propertyType == typeof(ushort) || propertyType == typeof(byte)) {
                        if (!UIntRegex.IsMatch(value)) return default;
                    }
                    if (propertyType == typeof(float) || propertyType == typeof(double)) {
                        if (!FloatRegex.IsMatch(value)) return default;
                    }
                }
                return value;
            }
            private void Text_Changed(string value) {
                if (binding == null) return;
                var propertyType = binding.GetPropertyType();
                try {
                    if (value != "") {
                        var converter = TypeDescriptor.GetConverter(propertyType);
                        if (converter.CanConvertFrom(typeof(string))) {
                            binding.SetValueAs(converter.ConvertFrom(value));
                        } else {
                            binding.SetValueAs(Convert.ChangeType(value, binding.GetPropertyType()));
                        }
                        return;
                    }
                } catch (Exception e) { Debug.WriteLine(e.Message); }
                binding.SetValueAs(propertyType.IsValueType ? Activator.CreateInstance(propertyType) : default);
            }
            public void BindValue(PropertyPath path) {
                if (IsEditing) return;
                binding = path;
                var type = path.GetPropertyType();
                if (type == typeof(float))
                    Text = path.GetValueAs<float>().ToString("0.#######") ?? "";
                else if (type.IsArray) {
                    var arr = path.GetValueAs<Array>();
                    if (arr == null) {
                        Text = "null";
                    } else {
                        var builder = new StringBuilder();
                        builder.Append($"Array[{arr.Length}] {{");
                        for (int i = 0; i < arr.Length; i++) {
                            var item = arr.GetValue(i)?.ToString() ?? "null";
                            if (i > 0) builder.Append(",");
                            if (Text.Length + item.Length >= 32) {
                                builder.Append("...");
                                break;
                            }
                            builder.Append(item);
                        }
                        builder.Append($"}}");
                        Text = builder.ToString();
                    }
                } else
                    Text = path.GetValueAs<object>()?.ToString() ?? "null";
            }
            public override Vector2 GetDesiredSize(SizingParameters sizing) {
                var size = base.GetDesiredSize(sizing);
                size.X = Math.Max(size.X, 200.0f);
                return size;
            }
        }
        public class PropertyLabel : TextBlock, IDragHandler, IBeginDragHandler {
            public readonly PropertyPath Binding;
            private double dragOver = 0;
            public Action OnValueChanged;
            public PropertyLabel(PropertyPath path) : base(path.GetPropertyName()) {
                Binding = path;
                TextElement.Alignment = TextAlignment.Left;
            }
            public void OnBeginDrag(PointerEvent events) {
                if (!events.GetIsButtonDown(0)) { events.Yield(); return; }
                dragOver = 0;
            }
            private double ToLinear(double value) => Math.Sign(value) * Math.Pow(Math.Abs(value), 1.0f / 3.0f);
            private double ToExponent(double value) => value * value * value;
            public void OnDrag(PointerEvent events) {
                try {
                    var oldValue = Binding.GetValueAs<object>();
                    var value = ToLinear((double)Convert.ChangeType(oldValue, typeof(double))!);
                    var drag = events.CurrentPosition - events.PreviousPosition;
                    dragOver += drag.X * 0.1f;
                    value += dragOver;
                    var newValue = NumberConverter.ConvertTo(ToExponent(value), Binding.GetPropertyType());
                    var newValueDbl = ToLinear((double)Convert.ChangeType(newValue, typeof(double))!);
                    dragOver = value - newValueDbl;
                    if (oldValue.Equals(newValue)) return;
                    Binding.SetValueAs<object>(newValue);
                    OnValueChanged?.Invoke();
                } catch { }
            }
            public override Vector2 GetDesiredSize(SizingParameters sizing) {
                var size = base.GetDesiredSize(sizing);
                size.X = Math.Max(size.X, 100.0f);
                return size;
            }
        }
        public struct Bindables {
            public PropertyPath Path;
            public IBindableValue Bindable;
            public Bindables(PropertyPath path, IBindableValue bindable) {
                Path = path;
                Bindable = bindable;
            }
        }
        private List<Bindables> bindables = new();
        public UIPropertiesList() {
            Axis = ListLayout.Axes.Vertical;
        }
        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            if (Canvas != null) Canvas.Updatables.RegisterUpdatable(this, true);
        }
        public override void Uninitialise(CanvasBinding binding) {
            if (Canvas != null) Canvas.Updatables.RegisterUpdatable(this, false);
            base.Uninitialise(binding);
        }
        public void AppendProperty(PropertyPath path, Action onChanged = null) {
            var row = new ListLayout() { Axis = ListLayout.Axes.Horizontal, ScaleMode = ScaleModes.StretchOrClamp, };
            var label = new PropertyLabel(path) { FontSize = 14, TextColor = Color.Black, DisplayParameters = TextDisplayParameters.Flat, };
            if (onChanged != null) label.OnValueChanged += () => { onChanged(); };
            label.SetTransform(CanvasTransform.MakeDefault().WithOffsets(10f, 0f, -10f, 0f));
            row.AppendChild(label);

            var type = path.GetPropertyType();
            if (type == typeof(bool)) {
                var toggle = new ToggleButton() { };
                toggle.BindValue(path);
                if (onChanged != null) toggle.OnStateChanged += (newValue) => { onChanged(); };
                row.AppendChild(toggle);
                bindables.Add(new Bindables(path, toggle));
            } else {
                var text = new TextFieldBound() { FontSize = 14, };
                text.BindValue(path);
                if (onChanged != null) text.OnTextChanged += (newValue) => { onChanged(); };
                row.AppendChild(text);
                bindables.Add(new Bindables(path, text));
            }
            AppendChild(row);
        }
        public void UpdateValues() {
            foreach (var bindable in bindables) {
                bindable.Bindable.BindValue(bindable.Path);
            }
        }
        public void Update(float dt) {
            UpdateValues();
        }
    }

    public class UIEntityInspector : CanvasRenderable {
        public ListLayout List;
        public UIEntityInspector() {
            List = new ListLayout() { Axis = ListLayout.Axes.Vertical, };
            AppendChild(List);
        }
        public void Initialise(World world, Entity entity) {
            List.ClearChildren();
            if (world != null) {
                var entityAddr = world.Stage.RequireEntityAddress(entity);
                var entityName = $"{entity} (Arch {entityAddr.ArchetypeId} Row {entityAddr.Row})";
                List.AppendChild(new TextBlock(entityName) { FontSize = 14, TextColor = Color.Black, DisplayParameters = TextDisplayParameters.Flat });
                foreach (var component in world.GetEntityComponents(entity)) {
                    var type = component.GetComponentType();
                    List.AppendChild(new TextBlock(type.Type.Name) { FontSize = 14, TextColor = component.TypeId.IsSparse ? Color.Blue : Color.Black, DisplayParameters = TextDisplayParameters.Flat });;
                    var properties = new UIPropertiesList() { Name = "Entity Inspector" };
                    var scope = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (var field in type.Type.GetFields(scope)) {
                        properties.AppendProperty(CreatePropertyPath(component.Column.Items, component.Row, field), () => {
                            component.NotifyMutation();
                        });
                    }
                    foreach (var property in type.Type.GetProperties(scope)) {
                        properties.AppendProperty(CreatePropertyPath(component.Column.Items, component.Row, property), () => {
                            component.NotifyMutation();
                        });
                    }
                    List.AppendChild(properties);
                    var spacer = new CanvasRenderable();
                    spacer.SetTransform(CanvasTransform.MakeDefault().WithOffsets(0f, 0f, 0f, 0f));
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
        private PropertyPath CreatePropertyPath(Array owner, int index, PropertyInfo property) {
            var propPath = new PropertyPath(owner);
            propPath.DefrenceArray(index);
            propPath.DefrenceProperty(property);
            return propPath;
        }
    }

    //[Conditional("DEBUG")]
    public class EditorFieldAttribute : Attribute { }
    //[Conditional("DEBUG")]
    public class EditorButtonAttribute : Attribute { }
    public class UIEditablesInspector : CanvasRenderable {
        public List<object> Editables = new();

        public ListLayout List;
        public UIEditablesInspector() {
            List = new ListLayout() { Axis = ListLayout.Axes.Vertical, };
            AppendChild(List);
        }

        public void Refresh() {
            List.ClearChildren();
            foreach (var editable in Editables) {
                List.AppendChild(new TextBlock(editable.GetType().Name) { TextColor = Color.DarkGray, });
                var properties = new UIPropertiesList() { Name = "Editables Inspector" };
                var scope = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var field in editable.GetType().GetFields(scope)) {
                    if (field.GetCustomAttribute<EditorFieldAttribute>() != null)
                        properties.AppendProperty(new PropertyPath(editable, field));
                }
                foreach (var property in editable.GetType().GetProperties(scope)) {
                    if (property.GetCustomAttribute<EditorFieldAttribute>() != null)
                        properties.AppendProperty(new PropertyPath(editable, property));
                }
                foreach (var method in editable.GetType().GetMethods(scope)) {
                    if (method.GetCustomAttribute<EditorButtonAttribute>() != null) {
                        var btn = new TextButton(method.Name) { Transform = CanvasTransform.MakeDefault().WithOffsets(5f, 0f, -5f, 0f) };
                        btn.OnClick += () => { method.Invoke(editable, null); };
                        properties.AppendChild(btn);
                    }
                }
                List.AppendChild(properties);
            }
        }
    }

    public class UIInspector : TabbedWindow {
        public readonly ScrollView ScrollView;
        public readonly ListLayout Content;
        public UILandscapeTools LandscapeTools = new();
        public UIEntityInspector EntityInspector = new();
        public UIEditablesInspector EditablesInspector = new();
        private CanvasRenderable? activeInspector;

        public UIInspector(Editor editor) : base(editor, "Inspector") {
            ScrollView = new() { Name = "Inspector Scroll", ScrollMask = new Vector2(0f, 1f), };
            Content = new() { Name = "Inspector Content", Axis = ListLayout.Axes.Vertical, ScaleMode = ListLayout.ScaleModes.StretchOrClamp, };
            Content.AppendChild(EditablesInspector);
            ScrollView.AppendChild(Content);
            AppendChild(ScrollView);
        }

        public void AppendEditables(ObservableCollection<object> editables) {
            editables.CollectionChanged += Editables_CollectionChanged;
            Editables_CollectionChanged(this,
                new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                    System.Collections.Specialized.NotifyCollectionChangedAction.Add,
                    editables
                ));
        }

        private void Editables_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            if (e.OldItems != null) foreach (var item in e.OldItems) EditablesInspector.Editables.Remove(item);
            if (e.NewItems != null) foreach (var item in e.NewItems) EditablesInspector.Editables.Add(item);
            EditablesInspector.Refresh();
        }

        public void SetInspector(CanvasRenderable? inspector) {
            if (activeInspector != null) Content.RemoveChild(activeInspector);
            activeInspector = inspector;
            if (activeInspector != null) Content.InsertChild(0, activeInspector);
        }
    }
}
