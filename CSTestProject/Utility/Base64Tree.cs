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
    /*
     * Can either store IsLeaf in node:
     * - Sparse tree, each branch can be a leaf
     * Or compute implicitly from altitude
     * - Each coord must resolve to unique leaf
     * 
     * Require sparse - store IsLeaf in node
     * 
     * Need a GetLeafIdAt(position)
     * Insert:
     *  Get the leaf at pos
     *  Insert new item into leaf
     *  If leaf has too many items, subdivide it (externally with access to transform lookup)
     *  Update bounds
     * Remove:
     *  Get leaf at pos
     *  Remove item
     *  If leaf is empty, prune it (internally - using IsEmpty, propagating upward)
     *  Update bounds
     * Query:
     *  Get all leaves intersecting range
     *  Iterate all objects within leaves
     *  
     * Note:
     *  Tree must support external iteration logic (to check bounds)
     *  => Just provide interface to guide Enumerator
     *  => Store bounds in separate array (sized to nodes)
     *  
     * Insert:
     *  Must be able to push parents when out of bounds
     *  => Try to centre the current tree and new point?
     *  => Try to be as tight as possible? (Probably not)
     */

    public class Base64Tree<Value> : IEnumerable<Value> {
        const ulong ALLBITS = unchecked((ulong)(-1));
        public struct Node {
#if ENABLEDEBUG
            public ulong Id;
#endif
            public ulong ChildMask;
            public int ChildCount => BitOperations.PopCount(ChildMask);
            public RangeInt Allocation;
            public bool IsLeaf => ChildMask == 0;
            public bool IsEmpty => ChildMask == 0 && Allocation.Length == 0;
            public Node(ulong id, int altitude) {
#if ENABLEDEBUG
                Id = id & (ALLBITS << altitude);
#endif
                Debug.Assert((uint)altitude < 0x7f);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public int GetLocalChildById(int childId) {
                return BitOperations.PopCount(ChildMask & ~(ALLBITS << childId));
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public int GetChildById(int childId) {
                return Allocation.Start + BitOperations.PopCount(ChildMask & ~(ALLBITS << childId));
            }
            public override string ToString() { return $"x{Allocation} => {ChildMask:X}"; }
        }
        public struct NodeReference {
            // Index in the "nodes" array (or very rarely, "values" array).
            public int Index;
            // Altitude is 0 for leaves, and increases by 6 for each level
            // (root is generally > 0 altitude)
            public int Altitude;
            public bool IsValid => Index != -1;
            // Leaf nodes contain "value" children. Other nodes contain "node" children
            public bool IsLeaf => Altitude <= 0;
            public NodeReference(int index, int altitude) {
                Index = index;
                Altitude = altitude;
            }
            public override string ToString() { return $"{Index} << {IsValid}"; }
            public static implicit operator int(NodeReference r) { return r.Index; }
            public static NodeReference Invalid = new(-1, -1);
        }
        protected SparseArray<Node> nodes = new(64);
        protected SparseArray<Value> values = new(64);
        protected NodeReference rootIndex = NodeReference.Invalid;
        public bool IsEmpty => !rootIndex.IsValid;
        public int ValueCount => values.PreciseCount;

        public ref Value this[ulong id] {
            get => ref values[RequireIndex(id)];
        }
        public void Set(ulong id, Value value) {
            values[RequireIndex(id)] = value;
        }
        public bool Contains(ulong id) {
            var nodeRef = GetOwnerIndex(id);
            if (nodeRef < 0) return false;
            var node = nodes[nodeRef];
            int childId = (int)(id >> nodeRef.Altitude) & 63;
            if ((node.ChildMask & (1ul << childId)) == 0) return false;
            return true;
        }
        public bool TryGet(ulong id, out Value value) {
            value = default;
            var nodeRef = GetOwnerIndex(id);
            if (nodeRef < 0) return false;
            var node = nodes[nodeRef];
            int childId = (int)(id >> nodeRef.Altitude) & 63;
            if ((node.ChildMask & (1ul << childId)) == 0) return false;
            var childIndex = BitOperations.PopCount(node.ChildMask & ~(ALLBITS << childId));
            value = values[node.Allocation.Start + childIndex];
            return true;
        }
        public bool Exists(ulong id) {
            var nodeRef = GetOwnerIndex(id);
            if (nodeRef < 0) return false;
            var node = nodes[nodeRef];
            int childId = (int)(id >> nodeRef.Altitude) & 63;
            if ((node.ChildMask & (1ul << childId)) == 0) return false;
            return true;
        }
        public bool Delete(ulong id) {
            int count = 0;
            Span<NodeReference> stack = stackalloc NodeReference[12];
            // Generate a list of nodes from root to leaf
            var index = rootIndex;
            for (; ; ) {
                stack[count++] = index;
                if (index.Altitude < 6) break;
                ref var node = ref nodes[index.Index];
                index = new(node.GetChildById((int)(id >> index.Altitude) & 63), index.Altitude - 6);
            }
            // Iterate backwards, removing the child bit and deleting if node becomes empty
            for (int i = count - 1; ; --i) {
                var nodeRef = stack[i];
                ref var node = ref nodes[nodeRef.Index];
                node.ChildMask &= ~(1ul << ((int)(id >> nodeRef.Altitude) & 63));
                // If node still contains children, stop
                if (!node.IsEmpty) {
                    // Shift other children down
                    var childIndex = index.Index;
                    var childEnd = node.Allocation.Start + node.ChildCount;
                    if (node.IsLeaf) {
                        for (int c = childIndex; c < childEnd; ++c) values[c] = values[c + 1];
                    } else {
                        for (int c = childIndex; c < childEnd; ++c) nodes[c] = nodes[c + 1];
                    }
                    break;
                }
                // Otherwise delete it
                var alloc = node.Allocation;
                if (node.IsLeaf) values.Return(ref alloc);
                else nodes.Return(ref alloc);
                // If we reach the root, the root is deleted
                if (i == 0) { rootIndex = NodeReference.Invalid; break; }

                index = nodeRef;
            }
            return true;
        }
        // Get the leaf node which owns this id
        protected NodeReference GetOwnerIndex(ulong id) {
            var index = GetRootNode(id);
            // Iterate until a "leaf" or "invalid"
            while (index.IsValid) {
                ref var node = ref nodes[index];
                if (node.IsLeaf) break;
                index = GetChildNode(id, index);
            }
            return index;
        }
        // Always returns a valid index into "values" for the "id"
        public int RequireIndex(ulong id) {
            if (rootIndex == -1) {
                int altitude = ((int)(64 - BitOperations.LeadingZeroCount(id)) + 5) / 6 * 6;
                rootIndex = new(nodes.Add(new Node(id, altitude)), altitude);
            }
            // Add parents
            while (true) {
                var node = nodes[rootIndex];
                var localId = id >> rootIndex.Altitude;
                if (localId < 64) break;
                var newAltitude = rootIndex.Altitude + 6;
                rootIndex = new(nodes.Add(new Node(id, newAltitude) {
                    Allocation = new RangeInt(rootIndex, 1),
                    ChildMask = 0x01,
                }), newAltitude);
            }
            var index = rootIndex;
            // Add children
            while (true) {
                var node = nodes[index];
                int childId = (int)(id >> index.Altitude) & 63;
                if ((node.ChildMask & (1ul << childId)) == 0)
                    InsertChild(id, index, childId);

                var parentRef = index;
                ref var parent = ref nodes[index];
                index = GetChildById(index, childId);
#if DEBUG
                Debug.Assert(index.IsValid);
#endif
                if (parent.IsLeaf) break;
            }
            return index;
        }

        // Check that the root is valid for this id
        protected NodeReference GetRootNode(ulong id) {
            if (rootIndex == -1) return NodeReference.Invalid;
            if (id >= 64ul << rootIndex.Altitude) return NodeReference.Invalid;
            return rootIndex;
        }
        // Step to child node, or return invalid if no child exists for this id
        protected NodeReference GetChildNode(ulong id, NodeReference nodeRef) {
            var node = nodes[nodeRef.Index];
            int childId = (int)(id >> nodeRef.Altitude) & 63;
            if ((node.ChildMask & (1ul << childId)) == 0) return NodeReference.Invalid;
            var childRef = new NodeReference(node.GetChildById(childId), nodeRef.Altitude - 6);
            return childRef;
        }
        // Assumes that a child exists for this id
        protected NodeReference GetChildNode_Fast(ulong id, NodeReference nodeRef) {
            var node = nodes[nodeRef.Index];
            int childId = (int)(id >> nodeRef.Altitude) & 63;
            return new(node.GetChildById(childId), nodeRef.Altitude - 6);
        }
        // Get a child index (dense array) from child id (sparse 64-items)
        protected NodeReference GetChildById(NodeReference index, int childId) {
            var node = nodes[index];
            var childIndex = node.GetChildById(childId);
            return new(childIndex, index.Altitude - 6);
        }

        // Insert a node for the specified "childId" into the "index" node
        // "id" is only required for debugging
        protected void InsertChild(ulong id, NodeReference index, int childId) {
            InsertChild(id, index, childId, index.Altitude - 6);
        }
        protected void InsertChild(ulong id, NodeReference index, int childId, int altitude) {
            ref var node = ref nodes[index];
            node.ChildMask |= 1ul << childId;
            int childCount = node.ChildCount;
            // Resize if needed
            if (childCount > node.Allocation.Length) {
                int allocCount = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(2, childCount));
                var alloc = node.Allocation;
#if DEBUG
                Debug.Assert(node.IsLeaf || !alloc.Contains(index));
#endif
                if (node.IsLeaf) values.Reallocate(ref alloc, allocCount);
                else nodes.Reallocate(ref alloc, allocCount);
                node = ref nodes[index];
#if DEBUG
                if (!node.IsLeaf && node.Allocation.Start != alloc.Start)
                    nodes.Slice(node.Allocation).AsSpan().Fill(default);
#endif
                node.Allocation = alloc;
            }
            // Shift children to make room for new item
            var childIndex = node.GetLocalChildById(childId);
            if (node.IsLeaf) {
                var nodeChildren = values.Slice(node.Allocation).AsSpan();
                for (int i = childCount - 1; i > childIndex; --i) nodeChildren[i] = nodeChildren[i - 1];
            } else {
                var nodeChildren = nodes.Slice(node.Allocation).AsSpan();
                for (int i = childCount - 1; i > childIndex; --i) nodeChildren[i] = nodeChildren[i - 1];
                nodeChildren[childIndex] = new Node(id, altitude);
            }
        }
        public struct Enumerator : IEnumerator<Value> {
            public readonly Base64Tree<Value> Tree;

            [InlineArray(12)]
            private struct Array12 { public NodeReference Value; }
            private Array12 Stack;
            private int stackIndex;

            public ulong Index { get; private set; }
            public NodeReference NodeRef => Stack[stackIndex];
            public ref Node Node => ref Tree.nodes[NodeRef];
            public int ValueIndex => Node.GetChildById((int)(Index >> Stack[stackIndex].Altitude) & 63);
            public Value Current => Tree.values[ValueIndex];
            object IEnumerator.Current => Current;

            public Enumerator(Base64Tree<Value> tree) {
                Tree = tree;
                stackIndex = -1;
                Index = 0;
            }
            public void Reset() { }
            public void Dispose() { }
            public bool MoveNext() {
                while (true) {
                    while (stackIndex >= 0) {
                        if (!RepairBits(1)) {
                            if (stackIndex == 0) return false;
                            --stackIndex;
                            continue;
                        }
                        ref var node = ref Node;
                        if (node.IsLeaf) return true;
                        break;
                    }
                    if (stackIndex == -1) {
                        if (Tree.rootIndex == -1) return false;
                        Stack[++stackIndex] = Tree.rootIndex;
                    }
                    while (true) {
                        var nodeRef = NodeRef;
                        var node = Node;
                        if (node.IsEmpty) break;
                        RepairBits();
                        if (node.IsLeaf) return true;
                        var childNodeIndex = GetChildIndex();
                        Stack[++stackIndex] = new(childNodeIndex, nodeRef.Altitude - 6);
                    }
                }
            }

            private int GetChildIndex() {
                return Node.GetChildById((int)(Index >> Stack[stackIndex].Altitude) & 63);
            }
            private bool RepairBits(int skipBits = 0) {
                var nodeRef = NodeRef;
                var node = Node;
                uint localBits = (uint)(Index >> nodeRef.Altitude) & 63;
                localBits += (uint)skipBits;
                if (localBits >= 64) return false;
                int localBit = BitOperations.TrailingZeroCount(
                    node.ChildMask & (ALLBITS << ((int)localBits)));
                if (localBit >= 64) return false;
                Index = (Index & (ALLBITS << (nodeRef.Altitude + 6)))
                    + (ulong)(localBit << nodeRef.Altitude);
                return true;
            }

            private static int ExtractNodeIndex(uint v) { return (int)(v >> 6); }
            private static int ExtractChildIndex(uint v) { return (int)(v & 63); }
            private static uint MakeData(int nodeI, int childI) { return (uint)((nodeI << 6) + childI); }
        }
        public Enumerator GetEnumerator() { return new(this); }

        IEnumerator<Value> IEnumerable<Value>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

}
