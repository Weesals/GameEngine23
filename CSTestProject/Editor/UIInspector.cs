﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

namespace Weesals.Editor {

    public interface IInspectorGameOverlay {
        CanvasRenderable GameViewOverlay { get; }
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
            public override SizingResult GetDesiredSize(SizingParameters sizing) {
                var size = base.GetDesiredSize(sizing);
                size.X = sizing.ClampWidth(Math.Max(size.X, 200.0f));
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
                if (NumberConverter.ConvertTo(1.0, Binding.GetPropertyType()) == null) { events.Yield(); return; }
                dragOver = 0;
            }
            private double ToLinear(double value) => Math.Sign(value) * Math.Pow(Math.Abs(value), 1.0f / 3.0f);
            private double ToExponent(double value) => value * value * value;
            public void OnDrag(PointerEvent events) {
                try {
                    var oldValue = Binding.GetValueAs<object>();
                    var value = ToLinear((double)Convert.ChangeType(oldValue, typeof(double))!);
                    var drag = events.CurrentPosition - events.PreviousPosition;
                    dragOver += drag.X * 0.05f;
                    value += dragOver;
                    var newValue = NumberConverter.ConvertTo(ToExponent(value), Binding.GetPropertyType());
                    var newValueDbl = ToLinear((double)Convert.ChangeType(newValue, typeof(double))!);
                    dragOver = value - newValueDbl;
                    if (oldValue.Equals(newValue)) return;
                    Binding.SetValueAs<object>(newValue);
                    OnValueChanged?.Invoke();
                } catch { }
            }
            public override SizingResult GetDesiredSize(SizingParameters sizing) {
                var size = base.GetDesiredSize(sizing);
                size.X = Math.Max(size.X, 100.0f);
                return size;
            }
        }
        public class CurveEditor : CanvasRenderable, IBeginDragHandler, IDragHandler {
            public readonly PropertyPath Binding;
            public Action OnValueChanged;
            public CanvasText TitleText;
            public CanvasCurve CurveDisplay;
            public CurveEditor(PropertyPath path) {
                Binding = path;
                TitleText = new(path.GetPropertyName());
            }
            public override void Initialise(CanvasBinding binding) {
                base.Initialise(binding);
                TitleText.Initialize(Canvas);
            }
            public override void Uninitialise(CanvasBinding binding) {
                TitleText.Dispose(Canvas);
                base.Uninitialise(binding);
            }
            public override void Compose(ref CanvasCompositor.Context composer) {
                if (HasDirtyFlag(DirtyFlags.Layout)) TitleText.MarkLayoutDirty();

                var layout = mLayoutCache;
                //TitleText.UpdateLayout(Canvas, layout.SliceTop(TitleText.GetPreferredHeight()));
                //TitleText.Append(ref composer);

                ref var canvasBG = ref composer.CreateTransient<CanvasImage>(Canvas);
                if (canvasBG.HasDirtyFlags) {
                    canvasBG.Color = new Color(0xff333333);
                    canvasBG.SetTexture(default);
                    canvasBG.UpdateLayout(Canvas, layout);
                }
                canvasBG.Append(ref composer);
                ref var curveDisp = ref composer.CreateTransient<CanvasCurve>(Canvas);
                var curve = Binding.GetValueAs<FloatCurve>();
                using var points = new PooledList<Vector3>();
                var curveLayout = layout.Inset(10);
                for (int i = 1; i < curve.Keyframes.Length; i++) {
                    var kf0 = curve.Keyframes[i - 1];
                    var kf1 = curve.Keyframes[i + 0];
                    int kfPoints = 1;
                    if (kf0.Interpolation == CurveInterpolation.Bezier) {
                        var stdTan = (kf1.Value - kf0.Value) / Math.Max(kf1.Time - kf0.Time, 0.0001f);
                        var curvature = Math.Abs(stdTan - kf0.OutTangent) + Math.Abs(stdTan - kf1.InTangent);
                        kfPoints = (int)Math.Clamp(curvature * 10, 1, 10);
                    }
                    for (int p = i == 1 ? 0 : 1; p <= kfPoints; p++) {
                        var key = (float)p / kfPoints;
                        key = Easing.Lerp(kf0.Time, kf1.Time, key);
                        var value = curve.Evaluate(key);
                        points.Add(curveLayout.TransformPosition2DN(new Vector2(key, value)));
                    }
                }
                curveDisp.Update(Canvas, points);
                curveDisp.Append(ref composer);

                base.Compose(ref composer);
            }
            public void OnBeginDrag(PointerEvent events) {
                if (!events.GetIsButtonDown(0)) { events.Yield(); return; }
            }
            public void OnDrag(PointerEvent events) {
            }
            public override SizingResult GetDesiredSize(SizingParameters sizing) {
                var size = base.GetDesiredSize(sizing);
                size = Vector2.Max(size, new Vector2(100f, 100f));
                size = sizing.ClampSize(size);
                return size;
            }
        }
        public class DropDownSelector : TextButton {
            public PropertyPath DataList;
            public PropertyPath Property;
            public ListLayout ItemsContainer;
            private DetachedSized detached;
            public bool IsOpen => detached != null && detached.IsInCanvas;
            public DropDownSelector(PropertyPath dataList, PropertyPath property) {
                DataList = dataList;
                Property = property;
                UpdateLabel();
            }
            public override void InvokeClick() {
                base.InvokeClick();
                if (IsOpen) Close(); else Open();
            }
            private void UpdateLabel() {
                var selected = Property.GetValueAs<object>();
                Text = selected?.ToString() ?? "-none-";
            }
            public void Open() {
                var items = DataList.GetValueAs<IReadOnlyList<object>>();
                if (ItemsContainer == null) {
                    detached = new() { FreeAxes = CanvasAxes.Vertical, ZIndex = 1, };
                    ItemsContainer = new() { };
                    detached.AppendChild(ItemsContainer);
                }
                ItemsContainer.ClearChildren();
                foreach (var iter in items) {
                    var item = iter;
                    var btn = new TextButton(item.ToString());
                    btn.OnClick += () => {
                        Property.SetValueAs(item);
                        UpdateLabel();
                        Close();
                    };
                    ItemsContainer.AppendChild(btn);
                }
                AppendChild(detached);
            }
            public void Close() {
                RemoveChild(detached);
            }
            public override SizingResult GetDesiredSize(SizingParameters sizing) {
                return base.GetDesiredSize(sizing);
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
            Axis = CanvasAxes.Vertical;
        }
        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            if (Canvas != null) Canvas.Updatables.RegisterUpdatable(this, true);
        }
        public override void Uninitialise(CanvasBinding binding) {
            if (Canvas != null) Canvas.Updatables.RegisterUpdatable(this, false);
            base.Uninitialise(binding);
        }
        public new void ClearChildren() {
            base.ClearChildren();
            bindables.Clear();
        }
        public void AppendProperty(PropertyPath path, Action onChanged = null) {
            var row = new ListLayout() { Axis = CanvasAxes.Horizontal, ScaleMode = ScaleModes.StretchOrClamp, };
            var label = new PropertyLabel(path) { FontSize = 14, };
            if (onChanged != null) label.OnValueChanged += () => { onChanged(); };
            label.SetTransform(CanvasTransform.MakeDefault().WithOffsets(10f, 0f, -10f, 0f));
            row.AppendChild(label);

            var type = path.GetPropertyType();
            if (path.Member.GetCustomAttribute<RangeAttribute>() is RangeAttribute range) {
                var slider = new Slider() { MinimumValue = Convert.ToSingle(range.Minimum), MaximumValue = Convert.ToSingle(range.Maximum) };
                slider.BindValue(path);
                if (onChanged != null) slider.OnStateChanged += (newValue) => { onChanged(); };
                row.AppendChild(slider);
                bindables.Add(new Bindables(path, slider));
            } else if (type == typeof(bool)) {
                var toggle = new ToggleButton() { };
                toggle.BindValue(path);
                if (onChanged != null) toggle.OnStateChanged += (newValue) => { onChanged(); };
                row.AppendChild(toggle);
                bindables.Add(new Bindables(path, toggle));
            } else if (type == typeof(FloatCurve)) {
                var curve = new CurveEditor(path) { };
                row.AppendChild(curve);
            } else {
                var text = new TextFieldBound() { FontSize = 14, };
                text.BindValue(path);
                if (onChanged != null) text.OnTextChanged += (newValue) => { onChanged(); };
                row.AppendChild(text);
                bindables.Add(new Bindables(path, text));
            }
            AppendChild(row);
        }
        public void AppendSelector(PropertyPath dataList, PropertyPath path, Action onChanged = null) {
            var row = new ListLayout() { Axis = CanvasAxes.Horizontal, ScaleMode = ScaleModes.StretchOrClamp, };
            var label = new PropertyLabel(path) { FontSize = 14, };
            label.SetTransform(CanvasTransform.MakeDefault().WithOffsets(10f, 0f, -10f, 0f));
            row.AppendChild(label);
            var selector = new DropDownSelector(dataList, path) { };
            row.AppendChild(selector);
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

        public void AppendPropertiesFrom(object target) {
            var scope = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var member in target.GetType().GetMembers(scope)) {
                AppendMemberOptional(target, member);
            }
            foreach (var method in target.GetType().GetMethods(scope)) {
                if (method.GetCustomAttribute<EditorButtonAttribute>() != null) {
                    var btn = new TextButton(method.Name) { Transform = CanvasTransform.MakeDefault().WithOffsets(5f, 0f, -5f, 0f) };
                    btn.OnClick += () => { method.Invoke(target, null); };
                    AppendChild(btn);
                }
            }
        }

        private void AppendMemberOptional(object target, MemberInfo member) {
            var selector = member.GetCustomAttribute<EditorSelectorAttribute>();
            if (selector != null) {
                var dataField = target.GetType().GetMember(selector.DataFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
                AppendSelector(new PropertyPath(target, dataField), new PropertyPath(target, member));
            } else if (member.GetCustomAttribute<EditorFieldAttribute>() != null) {
                AppendProperty(new PropertyPath(target, member));
            }
        }
    }

    public class UIEntityInspector : CanvasRenderable {
        public ListLayout List;
        public class HeaderTextBlock : TextBlock {
            private bool isSparse = false;
            protected override StyleDictionary.Resolver styleResolver => isSparse ? static (resolved) => { resolved.Foreground = Color.Lerp(resolved.Foreground, Color.Blue, 0.5f); return resolved; } : base.styleResolver;
            public HeaderTextBlock(string title, bool _isSparse) : base(title) {
                isSparse = _isSparse;
            }
        }
        public UIEntityInspector() {
            List = new ListLayout() { Axis = CanvasAxes.Vertical, };
            AppendChild(List);
        }
        public void Initialise(World world, Entity entity) {
            List.ClearChildren();
            if (world != null) {
                var entityAddr = world.Manager.RequireEntityAddress(entity);
                var entityName = $"{entity} (Arch {entityAddr.ArchetypeId} Row {entityAddr.Row})";
                List.AppendChild(new TextBlock(entityName) { FontSize = 14, DisplayParameters = TextDisplayParameters.Flat });
                foreach (var component in world.GetEntityComponents(entity)) {
                    var type = component.GetComponentType();
                    var header = new TextBlock(type.Type.Name) { FontSize = 14, DisplayParameters = TextDisplayParameters.Flat };
                    if (component.TypeId.IsSparse) header.TextColor = Color.Blue;
                    List.AppendChild(header);
                    var properties = new UIPropertiesList() { Name = "Entity Inspector" };
                    var scope = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (var field in type.Type.GetFields(scope)) {
                        var item = component.RawItem;
                        properties.AppendProperty(CreatePropertyPath(item.Array, item.Index, field), () => {
                            component.NotifyMutation();
                        });
                    }
                    foreach (var property in type.Type.GetProperties(scope)) {
                        var item = component.RawItem;
                        properties.AppendProperty(CreatePropertyPath(item.Array, item.Index, property), () => {
                            component.NotifyMutation();
                        });
                    }
                    Debug.Assert(properties.Children.Count > 0);
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

    public class EditorFieldAttribute : Attribute { }
    public class EditorSelectorAttribute : EditorFieldAttribute {
        public string DataFieldName { get; private set; }
        public EditorSelectorAttribute(string dataFieldName) { DataFieldName = dataFieldName; }
    }
    public class EditorButtonAttribute : Attribute { }
    public class UIEditablesInspector : CanvasRenderable {
        public List<object> Editables = new();

        public ListLayout List;
        public UIEditablesInspector() {
            List = new ListLayout() { Axis = CanvasAxes.Vertical, };
            AppendChild(List);
        }

        public void Refresh() {
            List.ClearChildren();
            foreach (var editable in Editables) {
                List.AppendChild(new TextBlock(editable.GetType().Name) { });
                var properties = new UIPropertiesList() { Name = "Editables Inspector" };
                properties.AppendPropertiesFrom(editable);
                List.AppendChild(properties);
            }
        }
    }

    public class UIInspector : TabbedWindow {
        protected readonly ScrollView scrollView = new() { Name = "Inspector Scroll", ScrollMask = new Vector2(0f, 1f), };
        protected readonly ListLayout content = new() { Name = "Inspector Content", Axis = CanvasAxes.Vertical, ScaleMode = ListLayout.ScaleModes.StretchOrClamp, };
        public UILandscapeTools LandscapeTools = new();
        public UIEntityInspector EntityInspector = new();
        public UIEditablesInspector GenericInspector = new();
        public UIEditablesInspector EditablesInspector = new();
        private CanvasRenderable? activeInspector;

        public Action<CanvasRenderable, bool> OnRegisterInspector;

        //protected override StyleDictionary.Resolver styleResolver => static (style) => { style.Foreground = Color.Black; return style; };

        public UIInspector(Editor editor) : base(editor, "Inspector") {
            content.AppendChild(EditablesInspector);
            scrollView.AppendChild(content);
            AppendChild(scrollView);
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
            if (activeInspector != null) {
                OnRegisterInspector?.Invoke(activeInspector, false);
                content.RemoveChild(activeInspector);
            }
            activeInspector = inspector;
            if (activeInspector != null) {
                content.InsertChild(0, activeInspector);
                OnRegisterInspector?.Invoke(activeInspector, true);
            }
        }
    }
}
