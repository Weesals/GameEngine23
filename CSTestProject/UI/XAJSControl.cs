using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Editor.Assets;

namespace Weesals.UI {
    public class XAJSControl : CanvasRenderable {

        private Dictionary<string, Action<CanvasRenderable>> callbacks = new();
        private Dictionary<string, CanvasRenderable> namedElements = new();

        public XAJSControl() { }
        public XAJSControl(string path) { Load(path); }

        public void Load(string path) {
            var jXajs = new SJson(AssetDatabase.ReadAllText(path));
            if (jXajs.IsArray) {
                foreach (var jChild in jXajs) {
                    AppendChild(ParseControl(jChild));
                }
            } else {
                AppendChild(ParseControl(jXajs));
            }
        }

        private CanvasRenderable ParseControl(SJson jChild) {
            CanvasRenderable control = null;
            foreach (var (jKey, jValue) in jChild.GetFields()) {
                if (jKey.Equals("type")) {
                    control = (CanvasRenderable)Activator.CreateInstance(typeof(CanvasRenderable).Assembly.GetType("Weesals.UI." + jValue.ToString()));
                } else if (jKey.Equals("Children")) {
                    if (control == null) throw new Exception();
                    foreach (var jChild2 in jValue) {
                        var childControl = ParseControl(jChild2);
                        control.AppendChild(childControl);
                    }
                } else if (jKey.Equals("Command")) {
                    if (control == null) throw new Exception();
                    var name = jValue.ToString();
                    if (control is Button btn) {
                        btn.OnClick += () => { InvokeCallback(name, control); };
                    } else {
                        throw new NotImplementedException();
                    }
                } else if (jKey.Equals("Name")) {
                    if (control == null) throw new Exception();
                    var name = jValue.ToString();
                    namedElements.Add(name, control);
                } else {
                    var keyStr = jKey.GetStringIterator();
                    if (keyStr.MoveNext()) {
                        if (keyStr.Current == '.') {
                            if (control == null) throw new Exception();
                            var type = control.GetType();
                            var memberName = keyStr.ToString();
                            var field = type.GetField(memberName);
                            var property = field == null ? type.GetProperty(memberName) : default;
                            var fieldType = field != null ? field.FieldType : property.PropertyType;
                            var valueStr = jValue.ToString();
                            var value = fieldType.IsEnum
                                ? Enum.Parse(fieldType, valueStr)
                                : Convert.ChangeType(valueStr, fieldType);
                            if (field != null) field.SetValue(control, value);
                            if (property != null) property.SetValue(control, value);
                        }
                    }
                }
            }
            if (control == null) throw new Exception();
            return control;
        }

        public void RegisterOnCommand(string name, Action<CanvasRenderable> callback) {
            callbacks.TryGetValue(name, out var itemCallbacks);
            itemCallbacks += callback;
            callbacks[name] = itemCallbacks;
        }
        public void InvokeCallback(string name, CanvasRenderable source) {
            if (callbacks.TryGetValue(name, out var itemCallbacks)) {
                itemCallbacks.Invoke(source);
            }
        }
        public CanvasRenderable? GetNamedElement(string name) {
            return namedElements.TryGetValue(name, out var item) ? item : default;
        }
    }
}
