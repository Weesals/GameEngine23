using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.Utility {
    public class QuadTree2 {

        public struct Node {
            public int ChildIndex;
            public RangeInt ChildRange => ChildIndex > 0 ? new RangeInt(ChildIndex, 4) : default;
            public Node(int childIndex) { ChildIndex = childIndex; }
            public override string ToString() => ChildIndex.ToString();
            public static Node Create() { return new() { ChildIndex = 0, }; }
        }

        public struct NodeAddress {
            public int Index;
            public int Altitude;
            public Int2 Offset;
            public uint Size => 2u << Altitude;
            public bool IsValid => Index >= 0;
            public readonly static NodeAddress Invalid = new() { Index = -1, };

            public NodeAddress MakeChild(Node self, int childIndex) {
                Debug.Assert((uint)childIndex < 4);
                var child = this;
                child.Index = self.ChildIndex + childIndex;
                child.Altitude--;
                if ((childIndex & 1) != 0) child.Offset.X += (int)child.Size;
                if ((childIndex & 2) != 0) child.Offset.Y += (int)child.Size;
                return child;
            }
            public bool Contains(Int2 pnt) {
                pnt -= Offset;
                return (uint)pnt.X < Size && (uint)pnt.Y < Size;
            }
            public bool NodeIsContained(Int2 from, Int2 to) {
                var size = (int)Size;
                return Offset.X >= from.X && Offset.Y >= from.Y &&
                    Offset.X + size <= to.X && Offset.Y + size <= to.Y;
            }
            public bool NodeIntersects(Int2 from, Int2 to) {
                var size = (int)Size;
                return Offset.X < to.X && Offset.Y < to.Y &&
                    Offset.X + size > from.X && Offset.Y + size > from.Y;
            }
            public int GetChildIndex(Int2 position) {
                position -= Offset;
                position >>= Altitude;
                return position.X + position.Y * 2;
            }
            public override string ToString() => $"{Index} @{Altitude} Off={Offset} Size={Size}";
        }

        protected SparseArray<Node> nodes = new();
        protected NodeAddress root = NodeAddress.Invalid;

        public bool HasRoot => root.Index != -1;
        public int NodeCount => nodes.PreciseCount;
        public uint RootSize => 1u << root.Altitude;

        public Int2 Minimum => root.Offset;
        public Int2 Maximum => root.Offset + (int)RootSize;

        protected void CreateRoot(Int2 pos, int altitude = 0) {
            Debug.Assert(!root.IsValid);
            root.Index = nodes.Add(Node.Create());
            root.Offset = pos;
            root.Altitude = altitude;
            Debug.Assert(root.Index == 0);
        }
        private void Swap<T>(ref T v1, ref T v2) { var t = v1; v1 = v2; v2 = t; }
        protected int RequireInRoot(Int2 position, Int2 hint = default) {
            for (int depth = 0; ; ++depth) {
                var localPos = position - root.Offset;
                var nodeSize = root.Size;
                if ((uint)localPos.X < nodeSize && (uint)localPos.Y < nodeSize) return depth;
                int parentChildOffset = 0;
                bool isLeft = localPos.X < 0 ? true : localPos.X >= nodeSize ? false : hint.X < nodeSize / 2;
                bool isBot = localPos.Y < 0 ? true : localPos.Y >= nodeSize ? false : hint.Y < nodeSize / 2;
                if (isLeft) parentChildOffset += 1;
                if (isBot) parentChildOffset += 2;

                var childrenRange = nodes.Allocate(4);
                var children = nodes.Slice(childrenRange);
                for (int i = 0; i < children.Count; i++) children[i] = Node.Create();
                children[parentChildOffset] = nodes[root.Index];
                nodes[root.Index].ChildIndex = childrenRange.Start;
                if ((parentChildOffset & 1) != 0) root.Offset.X -= (int)root.Size;
                if ((parentChildOffset & 2) != 0) root.Offset.Y -= (int)root.Size;
                Trace.Assert(root.Altitude++ < 64);
            }
        }
        protected ref Node SplitNode(int nodeIndex) {
            var newRange = nodes.Allocate(4);   // Allocate before taking ref
            ref var node = ref nodes[nodeIndex];
            Debug.Assert(node.ChildIndex == 0);
            node.ChildIndex = newRange.Start;
            nodes.Slice(node.ChildRange).AsSpan().Fill(Node.Create());
            return ref node;
        }

    }
}
