using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals {
    public static class ListExt {
        public static void RemoveAtSwapBack<T>(this List<T> list, int index) {
            list[index] = list[^1];
            list.RemoveAt(list.Count - 1);
        }
    }
}
