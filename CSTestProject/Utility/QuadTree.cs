using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.Utility {
    public class QuadTree {

        [InlineArray(4)]
        public struct NodeChildren {
            public int Offset;
        }

        public struct Node {
            public int Parent;
            public NodeChildren Children;
            public bool GetIsEmpty() {
                return (Children[0] & Children[1] & Children[2] & Children[3]) < 0;
            }
            public int GetChildCount() {
                return (Children[0] >= 0 ? 1 : 0) +
                    (Children[1] >= 0 ? 1 : 0) +
                    (Children[2] >= 0 ? 1 : 0) +
                    (Children[3] >= 0 ? 1 : 0);
            }
            public static Node Create() {
                Node node = new() { Parent = -1 };
                node.Children[0] = -1;
                node.Children[1] = -1;
                node.Children[2] = -1;
                node.Children[3] = -1;
                return node;
            }
            private char RowChar(int i) {
                bool c0 = Children[i + 0] >= 0, c1 = Children[i + 2] >= 0;
                return c0 && c1 ? ':' : c0 ? '.' : c1 ? '੶' : ' ';
            }
            public override string ToString() {
                return $"{RowChar(0)}{RowChar(1)} C {GetChildCount()}";
            }
        }

        public struct NodeAddress {
            public int Index;
            public int Altitude;
            public Int2 Offset;
            public uint Size => 1u << Altitude;
            public bool IsValid => Index >= 0;
            public readonly static NodeAddress Invalid = new() { Index = -1, };

            public NodeAddress MakeParent(QuadTree tree, out int childIndex) {
                var parentNode = this;
                var self = tree.nodes[Index];
                childIndex = -1;
                if (self.Parent != -1) {
                    var parent = tree.nodes[self.Parent];
                    var child = Index;
                    // TODO: Get this from the offset of Node.Offset and Tree.root.Offset
                    childIndex = 3;
                    for (; childIndex > 0; --childIndex)
                        if (parent.Children[childIndex] == child) break;
                    if ((childIndex & 1) != 0) parentNode.Offset.X -= (int)parentNode.Size;
                    if ((childIndex & 2) != 0) parentNode.Offset.Y -= (int)parentNode.Size;
                }
                parentNode.Index = self.Parent;
                parentNode.Altitude++;
                return parentNode;
            }
            public NodeAddress MakeChild(Node self, int childIndex) {
                Debug.Assert(childIndex < 4);
                var child = this;
                child.Index = self.Children[childIndex];
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
        }

        protected SparseArray<Node> nodes = new();
        protected NodeAddress root = NodeAddress.Invalid;
        public bool HasRoot => root.Index != -1;
        public int NodeCount => nodes.PreciseCount;
        public uint RootSize => 1u << root.Altitude;
        public Int2 Minimum => root.Offset;
        public Int2 Maximum => root.Offset + (int)RootSize;
        protected NodeAddress RequireChild(NodeAddress node, int childIndex) {
            ref var parent = ref nodes[node.Index];
            if (parent.Children[childIndex] < 0) {
                Debug.Assert(node.Altitude > 0, "Cannot make a child at altitude 0");
                parent.Children[childIndex] = nodes.Add(Node.Create());
                ref var child = ref nodes[parent.Children[childIndex]];
                child.Parent = node.Index;
            }
            Debug.Assert(nodes[parent.Children[childIndex]].Parent == node.Index);
            return node.MakeChild(parent, childIndex);
        }
        protected void RequireRange(Int2 from, Int2 to) {
            if (!root.IsValid) {
                root.Index = nodes.Add(Node.Create());
                root.Offset = from;
                int maxSize = Math.Max(to.X - from.X, to.Y - from.Y);
                root.Altitude = 32 - BitOperations.LeadingZeroCount((uint)maxSize);
            }
            if (to.X < from.X) Swap(ref from.X, ref to.X);
            if (to.Y < from.Y) Swap(ref from.Y, ref to.Y);
            var rootMin = root.Offset;
            var rootMax = root.Offset + (int)root.Size;
            Int2 nearest = from, farthest = to;
            if (rootMin.X - nearest.X > farthest.X - rootMax.X) Swap(ref nearest.X, ref farthest.X);
            if (rootMin.Y - nearest.Y > farthest.Y - rootMax.Y) Swap(ref nearest.Y, ref farthest.Y);
            RequireInRoot(nearest, farthest);
            RequireInRoot(farthest);
        }
        // Iterate down as far as possible without allocating leaves
        protected int RequireBranch(Int2 pos) {
            if (!root.IsValid) {
                root.Index = nodes.Add(Node.Create());
                root.Offset = pos;
                root.Altitude = 0;
            }
            RequireInRoot(pos);
            var en = new CellEnumerator(this, pos);
            int nodeIndex = -1;
            while (en.MoveNext()) nodeIndex = en.Current;
            Debug.Assert(nodeIndex >= 0);
            return nodeIndex;
        }
        private void Swap<T>(ref T v1, ref T v2) { var t = v1; v1 = v2; v2 = t; }
        protected void RequireInRoot(Int2 position, Int2 hint = default) {
            while (true) {
                var localPos = position - root.Offset;
                var nodeSize = root.Size;
                if ((uint)localPos.X < nodeSize && (uint)localPos.Y < nodeSize) return;
                int parentChildOffset = 0;
                bool isLeft = localPos.X < 0 ? true : localPos.X >= nodeSize ? false : hint.X < nodeSize / 2;
                bool isBot = localPos.Y < 0 ? true : localPos.Y >= nodeSize ? false : hint.Y < nodeSize / 2;
                if (isLeft) parentChildOffset += 1;
                if (isBot) parentChildOffset += 2;
                var parentI = nodes.Allocate();
                ref var parent = ref nodes[parentI];
                parent = Node.Create();
                nodes[root.Index].Parent = parentI;
                parent.Children[parentChildOffset] = root.Index;
                if ((parentChildOffset & 1) != 0) root.Offset.X -= (int)root.Size;
                if ((parentChildOffset & 2) != 0) root.Offset.Y -= (int)root.Size;
                root.Altitude++;
                root.Index = parentI;
            }
        }
        public struct TreeEnumerator {
            public readonly QuadTree Tree;
            public NodeAddress Addr;

            public ref Node Node => ref Tree.nodes[Addr.Index];
            public int NodeId => Addr.Index;
            public bool IsLeaf => Addr.Altitude == 0;
            public TreeEnumerator(QuadTree tree) {
                Tree = tree;
                Addr = Tree.root;
            }
            public void VisitChild(int childIndex) {
                Addr = GetChild(childIndex);
            }
            public int VisitParent() {
                Addr = Addr.MakeParent(Tree, out var childIndex);
                return childIndex;
            }
            internal void VisitRequiredChild(int childIndex) {
                Addr = Tree.RequireChild(Addr, childIndex);
            }
            public NodeAddress GetChild(int childIndex) {
                ref var parent = ref Tree.nodes[Addr.Index];
                var child = Addr.MakeChild(parent, childIndex);
                Debug.Assert(!child.IsValid || Tree.nodes[child.Index].Parent == Addr.Index);
                return child;
            }
            public int Remove() {
                int childIndex = -1;
                Debug.Assert(Node.GetIsEmpty());
                while (true) {
                    var self = Addr;
                    childIndex = VisitParent();
                    Tree.nodes.Return(self.Index);
                    if (Addr.IsValid) Tree.nodes[Addr.Index].Children[childIndex] = -1;
                    else Tree.root.Index = -1;
                    if (!Addr.IsValid || !Node.GetIsEmpty()) break;
                }
                return childIndex;
            }
        }

        public struct RangeEnumerator : IEnumerator<int> {
            public TreeEnumerator TreeEn;
            public Int2 From, To;
            private int childIndex = -1;
            public ref Node Node => ref TreeEn.Node;
            public int NodeId => TreeEn.NodeId;
            public int Current => NodeId;
            object IEnumerator.Current => throw new NotImplementedException();
            public RangeEnumerator(QuadTree tree, Int2 from, Int2 to) {
                TreeEn = new(tree);
                From = from; To = to;
                childIndex = 0;
                TreeEn.Addr = TreeEn.Tree.root;
            }
            public void Reset() { }
            public void Dispose() { }
            public bool MoveNext() {
                while (TreeEn.Addr.IsValid) {
                    if (childIndex < 4 && TreeEn.Addr.NodeIsContained(From, To)) {
                        childIndex = 4;
                        return true;
                    }
                    if (!TreeEn.IsLeaf) {
                        for (; childIndex < 4; ++childIndex) {
                            var child = TreeEn.GetChild(childIndex);
                            if (!child.NodeIntersects(From, To)) continue;
                            TreeEn.VisitRequiredChild(childIndex);
                            if (child.NodeIsContained(From, To)) {
                                childIndex = 4;
                                return true;
                            }
                            childIndex = -1;
                        }
                    }
                    childIndex = TreeEn.VisitParent() + 1;
                }
                return false;
            }
            public void Remove() {
                childIndex = TreeEn.Remove() + 1;
            }
        }
        public struct Enumerator : IEnumerator<int> {
            public TreeEnumerator TreeEn;
            private int childIndex;
            public NodeAddress Addr => TreeEn.Addr;
            public ref Node Node => ref TreeEn.Node;
            public int NodeId => TreeEn.NodeId;
            public int Current => NodeId;
            object IEnumerator.Current => throw new NotImplementedException();
            public Enumerator(QuadTree tree) {
                TreeEn = new(tree);
                TreeEn.Addr = TreeEn.Tree.root;
                childIndex = 0;
            }
            public void Reset() { }
            public void Dispose() { }
            public bool MoveNext() {
                while (TreeEn.Addr.IsValid) {
                    for (; childIndex < 4; ++childIndex) {
                        var child = TreeEn.GetChild(childIndex);
                        if (!child.IsValid) continue;
                        TreeEn.VisitChild(childIndex);
                        childIndex = 0;
                        return true;
                    }
                    childIndex = TreeEn.VisitParent() + 1;
                }
                return false;
            }
            public void Remove() {
                childIndex = TreeEn.Remove() + 1;
            }
        }
        public struct CellEnumerator : IEnumerator<int> {
            public readonly Int2 Position;
            public TreeEnumerator TreeEn;
            public NodeAddress Addr => TreeEn.Addr;
            public ref Node Node => ref TreeEn.Node;
            public int NodeId => TreeEn.NodeId;
            public int Current => TreeEn.NodeId;
            object IEnumerator.Current => throw new NotImplementedException();
            public CellEnumerator(QuadTree tree, Int2 pos) {
                TreeEn = new(tree);
                TreeEn.Addr = NodeAddress.Invalid;
                Position = pos;
            }
            public void Reset() { }
            public void Dispose() { }
            public bool MoveNext() {
                if (!TreeEn.Addr.IsValid) {
                    TreeEn.Addr = TreeEn.Tree.root;
                    return TreeEn.Addr.IsValid && TreeEn.Addr.Contains(Position);
                }
                while (TreeEn.Addr.IsValid) {
                    var childIndex = TreeEn.Addr.GetChildIndex(Position);
                    TreeEn.VisitChild(childIndex);
                    return TreeEn.Addr.IsValid;
                }
                return false;
            }
            public CellEnumerator GetEnumerator() { return this; }
        }
        public Enumerator GetEnumerator() {
            return new Enumerator(this);
        }
        public CellEnumerator GetEnumerator(Int2 pos) {
            return new CellEnumerator(this, pos);
        }
        public override string ToString() {
            var en = GetEnumerator();
            string output = "";
            while (en.MoveNext()) {
                output += $"{new string(' ', en.Addr.Altitude)}{en.Node}\n";
            }
            return output;
        }
    }

}
