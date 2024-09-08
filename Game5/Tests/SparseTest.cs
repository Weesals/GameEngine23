using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals;
using Weesals.ECS;

namespace Game5.Tests {
    public class SparseTest {
        public static void Sparse2() {
            var rnd = new Random(0);
            var sparse2 = new SparseRanges();
            var alloc = new List<RangeInt>();
            for (int i = 0; i < 10000; i++) {
                if (rnd.Next(0, 10) < 6 && alloc.Count > 0) {
                    var rangeI = rnd.Next(0, alloc.Count);
                    var range = alloc[rangeI];
                    alloc[rangeI] = alloc[^1];
                    alloc.RemoveAt(alloc.Count - 1);
                    sparse2.SetRange(range.Start, range.Length, false);
                } else {
                    var size = rnd.Next(1, 100);
                    var start = sparse2.FindAndSetRange(size);
                    if (start == -1) continue;
                    alloc.Add(new(start, size));
                }
            }
        }
        /*public static void B64MapTest() {
            var b64 = new Base64Map<int>();
            b64.Insert(new Vector2(0f, 0f), 1);
            b64.Insert(new Vector2(-5f, -5f), 2);
            b64.Insert(new Vector2(0f, 5f), 3);
            b64.Insert(new Vector2(2f, 2f), 4);
            b64.Insert(new Vector2(3f, 1f), 5);
            b64.Remove(new Vector2(0f, 0f), 1);
        }*/
    }
}
