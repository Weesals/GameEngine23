using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.UI {
    public interface IHitTest {
        bool HitTest(Vector2 pos);
    }
    public interface IHitTestGroup {
        bool HitTest(Vector2 pos);
    }
    public class HittestGrid {

        public struct Binding {
            public RectI LocalRect;
            public bool IsEnabled => LocalRect.X != -2;
            public bool IsValid => LocalRect.Width > 0;
            public static readonly Binding Disabled = new Binding { LocalRect = new RectI() { X = -2, } };
        }

        public class Cell : List<CanvasRenderable> {

        }

        public readonly Cell[] Cells;
        public readonly Int2 Size;
        public Int2 Resolution { get; private set; }
        public HittestGrid(Int2 size) {
            Cells = new Cell[size.X * size.Y];
            for (int i = 0; i < Cells.Length; i++) Cells[i] = new();
            Size = size;
            Resolution = 4096;  // A big enough resolution to work (inefficiently) if nothing gets specified
        }
        public void SetResolution(Int2 size) {
            Resolution = size;
        }

        private RectI ToLocalRect(RectI rect) {
            Int2 min = rect.Min * Size / Resolution;
            Int2 max = rect.Max * Size / Resolution;
            min = Int2.Max(min, 0);
            max = Int2.Min(max, Size - 1);
            if (min.X > max.X || min.Y > max.Y) return default;
            return RectI.FromMinMax(min.X, min.Y, max.X + 1, max.Y + 1);
        }
        private Int2 ToLocalInt(Int2 pnt) {
            pnt = pnt * Size / Resolution;
            pnt = Int2.Clamp(pnt, 0, Size - 1);
            return pnt;
        }
        private int ToIndex(int x, int y) {
            return x + Size.X * y;
        }

        public void UpdateItem(CanvasRenderable item, ref Binding binding, RectI rect) {
            var local = ToLocalRect(rect);
            if (binding.LocalRect == local) return;
            RemoveLocal(item, binding);
            binding = new Binding() { LocalRect = local, };
            AppendLocal(item, binding);
        }

        private class OrderComparer : IComparer<CanvasRenderable> {
            public int Compare(CanvasRenderable? x, CanvasRenderable? y) {
                return CanvasRenderable.GetGlobalOrder(y, x);
                //return x!.GetOrderId() - y!.GetOrderId();
            }
            public static readonly OrderComparer Default = new();
        }
        private void RemoveLocal(CanvasRenderable item, Binding binding) {
            Int2 min = binding.LocalRect.Min;
            Int2 max = binding.LocalRect.Max;
            for (int y = min.Y; y < max.Y; ++y) {
                for (int x = min.X; x < max.X; ++x) {
                    Cells[ToIndex(x, y)].Remove(item);
                }
            }
        }
        private void AppendLocal(CanvasRenderable item, Binding binding) {
            Int2 min = binding.LocalRect.Min;
            Int2 max = binding.LocalRect.Max;
            for (int y = min.Y; y < max.Y; ++y) {
                for (int x = min.X; x < max.X; ++x) {
                    var cell = Cells[ToIndex(x, y)];
                    var index = cell.BinarySearch(item, OrderComparer.Default);
                    if (index < 0) index = ~index;
                    cell.Insert(index, item);
                }
            }
        }

        public HitEnumerator BeginHitTest(Int2 pos) {
            var pnt = ToLocalInt(pos);
            var cellI = ToIndex(pnt.X, pnt.Y);
            return new HitEnumerator(Cells[cellI], pos);
        }

        public struct HitEnumerator : IEnumerator<CanvasRenderable> {
            Cell? cell;
            int index;
            Int2 pos;
            public CanvasRenderable Current => cell![index];
            object IEnumerator.Current => Current;
            public HitEnumerator(Cell? _cell, Int2 _pos) { cell = _cell; pos = _pos; index = cell?.Count ?? 0; }
            public void Dispose() { }
            public void Reset() { index = cell?.Count ?? 0; }
            public bool MoveNext() {
                while (true) {
                    if (cell == null || index <= 0) return false;
                    if (cell[--index].HitTest(pos)) break;
                }
                return true;
            }
            public CanvasRenderable? First() {
                return MoveNext() ? Current : default;
            }
            public HitEnumerator GetEnumerator() { return this; }
        }
    }
}
