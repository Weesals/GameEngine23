using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Editor {
    public class PropertyPath {
        public object Owner;

        public int ArrayIndex = -1;
        public FieldInfo[] Fields = Array.Empty<FieldInfo>();
        public PropertyInfo? Property;

        public MemberInfo? Member => Property != null ? Property : Fields.Length > 0 ? Fields[^1] : null;

        public PropertyPath(object owner) {
            Owner = owner;
        }
        public PropertyPath(object owner, MemberInfo member) : this(owner) {
            if (member is FieldInfo field) Fields = new[] { field, };
            else if (member is PropertyInfo prop) Property = prop;
            else Debug.Assert(member == null);
        }
        public void DefrenceArray(int index) {
            ArrayIndex = index;
        }
        public void DefrenceField(FieldInfo field) {
            Array.Resize(ref Fields, Fields.Length + 1);
            Fields[^1] = field;
        }
        public void DefrenceProperty(PropertyInfo property) {
            Debug.Assert(Property == null);
            Property = property;
        }

        /*public void CreateFromPath(string path) {
            for (int i = 0; i < Path.Length; ) {
                var delim = Path[i++];
                if (delim == '[') {
                    var index = ReadInt(Path, ref i);
                    Trace.Assert(Path[i++] == ']');
                    head = ((Array)head).GetValue(index)!;
                } else if (delim == '.') {
                    var name = ReadName(Path, ref i);
                    var field = head.GetType().GetField(name);
                    head = field.GetValue(head);
                } else {
                    Debug.Fail("Unexpected character");
                }
            }
        }*/

        public T? GetValueAs<T>() {
            object? head = Owner;
            if (ArrayIndex >= 0) head = ((Array)head).GetValue(ArrayIndex)!;
            for (int i = 0; i < Fields.Length; i++) if ((head = Fields[i].GetValue(head)) == null) return default;
            if (Property != null) if ((head = Property.GetValue(head)) == null) return default;
            return head is T tval ? tval : default;
        }
        public void SetValueAs<T>(T value) {
            object head = Owner;
            object[] values = new object[1 + Fields.Length - 1];
            int index = 0;
            if (ArrayIndex >= 0) values[index++] = head = ((Array)head).GetValue(ArrayIndex)!;
            for (int i = 0; i < Fields.Length - 1; i++) {
                values[index++] = head = Fields[i].GetValue(head)!;
            }
            if (Property != null) Property.SetValue(head, value);
            else head = value!;
            for (int i = Fields.Length - 1; i >= 0; --i) {
                var item = index == 0 ? Owner : values[--index];
                Fields[i].SetValue(item, head);
                head = item;
            }
            if (ArrayIndex >= 0) ((Array)Owner).SetValue(head, ArrayIndex);
        }

        private static int ReadInt(string path, ref int i) {
            int index = 0;
            while (i < path.Length && char.IsNumber(path[i]))
                index = index * 10 + (path[i++] - '0');
            return index;
        }
        private static string ReadName(string path, ref int i) {
            int nBegin = i;
            while (i < path.Length && char.IsLetterOrDigit(path[i])) ++i;
            return path.Substring(nBegin, i - nBegin);
        }

        public string GetPropertyName() {
            if (Property != null) return Property.Name;
            return Fields[^1].Name;
        }
        public Type GetPropertyType() {
            if (Property != null) return Property.PropertyType;
            return Fields[^1].FieldType;
        }
    }
}
