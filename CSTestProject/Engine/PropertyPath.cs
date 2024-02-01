using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public class PropertyPath {
        public object Owner;

        public int ArrayIndex = -1;
        public FieldInfo[] Fields = Array.Empty<FieldInfo>();

        public PropertyPath(object owner) {
            Owner = owner;
        }
        public void DefrenceArray(int index) {
            ArrayIndex = index;
        }
        public void DefrenceField(FieldInfo field) {
            Array.Resize(ref Fields, Fields.Length + 1);
            Fields[^1] = field;
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
            object head = Owner;
            if (ArrayIndex >= 0) head = ((Array)head).GetValue(ArrayIndex)!;
            for (int i = 0; i < Fields.Length; i++) head = Fields[i].GetValue(head)!;
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
            head = value!;
            for (int i = Fields.Length - 1; i >= 0; --i) {
                var item = values[--index];
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
    }
}
