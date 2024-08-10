using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Engine.Profiling;

namespace Weesals.Utility {
    public class BVH2 {

        public struct Branch {
            public BoundingBox Bounds;
            public int InstanceCount;
            public int BranchCount;
            public override string ToString() => Bounds.ToString();
        }

        public struct DenseArray<T> {
            public T[] Items = Array.Empty<T>();
            public int Count;
            public ref T this[int index] => ref Items[index];
            public int Capacity => Items.Length;
            public DenseArray(int capacity) { Items = new T[capacity]; }
            public void Append(T value) {
                if (Count >= Items.Length)
                    Array.Resize(ref Items, (int)BitOperations.RoundUpToPowerOf2((uint)Count + 16));
                Items[Count++] = value;
            }

            public void Insert(int index, int count) {
                if (Count + count > Items.Length)
                    Array.Resize(ref Items, (int)BitOperations.RoundUpToPowerOf2((uint)(Count + count + 16)));
                Array.Copy(Items, index, Items, index + count, Count - index);
                Count += count;
            }
        }

        /* 0 1 2 3 4 5 6 7 8 9
         * '---------'
         * '---'---'
         */

        public Scene Scene;
        private DenseArray<SceneInstance> instances = new(16);
        private DenseArray<Branch> branches = new(16);
        private DynamicBitField2 movedBranches = new();
        private PooledHashMap<SceneInstance, int> branchByInstance = new(8);

        public void Add(SceneInstance instance) {
            instances.Append(instance);
        }
        public void Remove(SceneInstance instance) {
        }
        public void MarkMoved(SceneInstance instance) {
            if (!branchByInstance.TryGetValue(instance, out var branchId)) return;
            movedBranches.Add(branchId);
        }

        public void Flush() {
            if (branches.Count == 0) branches.Append(new() { });
            var en = new BranchEnumerator(this);
            var changedEn = movedBranches.GetEnumerator();
            while (true) {
                if (!changedEn.MoveNext()) break;
                var branchId = changedEn.Current;
                while (branchId - en.BranchIndex > en.ChildCount) en.PopToParent();
                while (branchId != en.BranchIndex) {
                    if (branchId - en.BranchIndex < en.ChildCount) en.VisitChild();
                    else en.NextSibling();
                }
                Debug.Assert(en.BranchIndex == branchId);
                var bounds = BoundingBox.Invalid;
                for (int i = 0; i < en.Branch.InstanceCount; i++) {
                    var instanceIndex = en.InstanceOffset + i;
                    var instance = instances[instanceIndex];
                    var aabb = Scene.GetInstanceAABB(instance);
                    bounds = BoundingBox.Union(bounds, aabb);
                }
                en.Branch.Bounds = bounds;
            }
            movedBranches.Clear();
            en = new BranchEnumerator(this);
            Span<int> instanceCounts = stackalloc int[32];
            while (en.IsValid) {
                while (en.HasChild) {
                    en.VisitChild();
                    instanceCounts[en.Depth] = 0;
                }
                while (en.IsValid) {
                    if (en.InstanceCount - instanceCounts[en.Depth] > 20) {
                        en.CreateChildren(2);
                        // Split child
                    }
                    instanceCounts[en.Depth - 1] += en.InstanceCount;
                    if (en.HasSibling) {
                        en.NextSibling();
                        break;
                    } else {
                        en.PopToParent();
                    }
                }
            }
            ref var rootBranch = ref branches[0];
            if (rootBranch.InstanceCount != instances.Count) {
                var rootBounds = rootBranch.Bounds;
                for (int i = rootBranch.InstanceCount; i < instances.Count; i++) {
                    var instance = instances[i];
                    var aabb = Scene.GetInstanceAABB(instance);
                    rootBounds = BoundingBox.Union(rootBounds, aabb);
                }
                rootBranch.Bounds = rootBounds;
                rootBranch.InstanceCount = instances.Count;
            }
            for (int i = 0; i < branches.Count; ) {
                var branch = branches[i];
                int childCount = branch.InstanceCount;
                for (int c = 0; c < branch.BranchCount; ) {
                    var child = branches[i + c];
                    childCount -= child.InstanceCount;
                    childCount += 1;
                    c += child.BranchCount;
                }
                i += branch.BranchCount;
            }
        }

        public struct BranchEnumerator : IDisposable {
            public BVH2 BVH;
            public struct StackItem {
                public int BranchIndex;
                public int InstanceIndex;
            }
            private PooledList<StackItem> stack;
            public ref Branch Parent => ref BVH.branches[stack[^2].BranchIndex];
            public ref Branch Branch => ref BVH.branches[stack[^1].BranchIndex];
            public int BranchIndex => stack[^1].BranchIndex;
            public int InstanceOffset => stack[^1].InstanceIndex;
            public int InstanceCount => Branch.InstanceCount;
            public int SiblingIndex => stack[^1].BranchIndex - stack[^2].BranchIndex;
            public int ChildCount => Branch.BranchCount;
            public bool HasParent => stack.Count >= 2;
            public bool HasChild => Branch.BranchCount > 0;
            public bool HasSibling => SiblingIndex < Parent.BranchCount;
            public bool IsValid => stack.Count > 0;
            public int Depth => stack.Count;
            public BranchEnumerator(BVH2 bvh) {
                BVH = bvh;
                stack = new(16);
                stack.Add(new() { BranchIndex = 0, InstanceIndex = 0, });
            }
            public void Dispose() {
                stack.Dispose();
            }
            public void PopToParent() {
                stack.RemoveAt(stack.Count - 1);
            }
            public void VisitChild() {
                Debug.Assert(HasChild);
                var top = stack[^1];
                stack.Add(new() { BranchIndex = top.BranchIndex + 1, InstanceIndex = top.InstanceIndex, });
            }
            public void NextSibling() {
                ref var top = ref stack[^1];
                ref var branch = ref BVH.branches[top.BranchIndex];
                top.BranchIndex += branch.BranchCount + 1;
                top.InstanceIndex += branch.InstanceCount;
            }

            public void CreateChildren(int count) {
                BVH.branches.Insert(BranchIndex, count);
                for (int i = 0; i < stack.Count; i++) {
                    BVH.branches[stack[i].BranchIndex].BranchCount += count;
                }
            }
        }

    }

    public class BoundingVolumeHierarchy : QuadTree2 {

        private ProfilerMarker ProfileMarker_Add = new("BVH.Add");
        private ProfilerMarker ProfileMarker_Move = new("BVH.Move");
        private ProfilerMarker ProfileMarker_Remove = new("BVH.Add");

        public Scene Scene;

        public struct Branch {
            public BoundingBox Bounds;
            public override string ToString() => Bounds.ToString();
        }
        unsafe public struct Leaf {
            public RangeInt ItemsAllocated;
            public int ItemsCount;
            public RangeInt Items => new(ItemsAllocated.Start, ItemsCount);
            public override string ToString() => ItemsAllocated.ToString();
        }

        public struct Mutation {
            public int OldOffset, NewOffset;
            public int NewCount;
            public int Index;
            public int NewIndex => NewOffset + Index;
            public bool IsValid => Index >= 0;
            public static readonly Mutation None = new() { OldOffset = -1, NewOffset = -1, NewCount = -1, Index = -1 };

            public void ApplyInsert<T>(ref T[] items) {
                int end = NewOffset + NewCount;
                if (items.Length <= end) {
                    Array.Resize(ref items, (int)BitOperations.RoundUpToPowerOf2((uint)end + 4));
                }
                if (OldOffset != NewOffset) {
                    Debug.Assert(Index == 0 || NewCount > 1);
                    Array.Copy(items, OldOffset,
                        items, NewOffset, Index);
                }
                if (Index < NewCount - 1) {
                    Array.Copy(items, OldOffset + Index,
                        items, NewOffset + Index + 1,
                        NewCount - 1 - Index);
                }
            }
            public void ApplyDelete(Array items) {
                Debug.Assert(OldOffset == NewOffset);
                Array.Copy(items, NewOffset + Index + 1, items, NewOffset + Index, NewCount - Index);
            }
        }

        private SparseArray<SceneInstance> instances = new();
        private Branch[] branches = Array.Empty<Branch>();
        private SparseArray<Leaf> leaves = new();
        private InstanceMetaArray instanceMeta;

        public SceneInstance[] RawInstances => instances.Items;

        private abstract class InstanceMetaArray {
            public abstract void RequireSize(int newSize);
            public abstract void Copy(int srcOffset, int dstOffset, int count);
            public abstract void Stage(int index);
            public abstract void Unstage(int index);
        }
        private sealed class InstanceMetaArray<T> : InstanceMetaArray {
            public T[] Data;
            T staged;
            public InstanceMetaArray() {
                Data = Array.Empty<T>();
            }
            public override void RequireSize(int newSize) {
                if (Data.Length >= newSize) return;
                Array.Resize(ref Data, newSize);
            }
            public override void Copy(int srcOffset, int dstOffset, int count) {
                Array.Copy(Data, srcOffset, Data, dstOffset, count);
            }
            public override void Stage(int index) {
                staged = Data[index];
            }
            public override void Unstage(int index) {
                Data[index] = staged;
            }
        }

        public BoundingVolumeHierarchy(Scene scene) {
            Scene = scene;
        }

        public void SetInstanceMetaType<T>() {
            instanceMeta = new InstanceMetaArray<T>();
        }
        public T[] GetInstanceMeta<T>() {
            return ((InstanceMetaArray<T>)instanceMeta).Data;
        }

        unsafe public Mutation Add(Int2 pos, SceneInstance instance) {
            using var marker = ProfileMarker_Add.Auto();
            int copyDepth = 0;
            if (!HasRoot) {
                CreateRoot(pos);
                RequireBranches();
                branches[root.Index].Bounds = BoundingBox.Invalid;
                Debug.WriteLine("Create root");
            } else if (!root.Contains(pos)) {
                if (nodes[root.Index].ChildIndex < 0) {
                    var rootMax = root.Offset + (int)root.Size;
                    root.Offset = Int2.Min(root.Offset, pos);
                    var rootDelta = pos - root.Offset;
                    var rootSize = (int)root.Size;
                    rootDelta.X = Math.Max(rootDelta.X, rootMax.X - rootDelta.X);
                    rootDelta.Y = Math.Max(rootDelta.Y, rootMax.Y - rootDelta.Y);
                    var newSize = Math.Max(rootDelta.X, rootDelta.Y);
                    var newAltitude = (int)BitOperations.TrailingZeroCount(BitOperations.RoundUpToPowerOf2((uint)newSize));
                    Trace.Assert(newAltitude < 64);
                    root.Altitude = newAltitude;
                    //Debug.WriteLine($"Expand root to {newAltitude}");
                } else {
                    var oldRoot = root.Offset;
                    copyDepth = RequireInRoot(pos);
                    RequireBranches();
                    var rootAddr = root;
                    for (int i = 0; i < copyDepth; i++) {
                        var childIndex = rootAddr.GetChildIndex(oldRoot);
                        rootAddr = rootAddr.MakeChild(nodes[rootAddr.Index], childIndex);
                        branches[rootAddr.Index] = branches[root.Index];
                    }
                    //Debug.WriteLine($"Require root to {pos}");
                }
            }
            Span<int> stack = stackalloc int[32];
            int stackCount = 0;
            var addr = FindBranch(stack, ref stackCount, pos);

            while (addr.Altitude > 0) {
                var nodeIndex = stack[stackCount - 1];
                ref var node = ref nodes[nodeIndex];
                int leafIndex = ~node.ChildIndex;
                if (leafIndex < 0) break;   // Not a leaf
                var leaf = leaves[leafIndex];
                if (leaf.ItemsCount < 64) break;    // Leaf is not at capacity
                var childInstances = instances.Slice(leaf.Items);
                leaves.Return(leafIndex);
                node.ChildIndex = 0;
                node = ref SplitNode(nodeIndex);
                var pivotChild = addr.MakeChild(node, 3);
                Span<int> quads = stackalloc int[childInstances.Count];
                for (int i = 0; i < childInstances.Count; i++) {
                    var childData = Scene.GetInstanceData(childInstances[i]);
                    var childPos = RetainedRenderer.GetPosition(*(Matrix4x4*)childData.Data);
                    Debug.Assert(addr.Contains(childPos));
                    quads[i] = i + ((
                        (childPos.X < pivotChild.Offset.X ? 0 : 1) +
                        (childPos.Y < pivotChild.Offset.Y ? 0 : 2)
                        ) << 24);
                }
                int quad4 = childInstances.Count;
                int quad2 = Sort(quads, 0, quad4, 2 << 24);
                int quad1 = Sort(quads, 0, quad2, 1 << 24);
                int quad3 = Sort(quads, quad2, quad4, 3 << 24);
                for (int i = quad1; i < childInstances.Count; i++) quads[i] &= 0xffffff;
                for (int i = 0; i < childInstances.Count; i++) {
                    var n = quads[i];
                    if (n == i) continue;
                    var p = i;
                    var t = childInstances[p];
                    if (instanceMeta != null) instanceMeta.Stage(leaf.Items.Start + p);
                    while (n != i) {
                        quads[p] = p;
                        childInstances[p] = childInstances[n];
                        if (instanceMeta != null) instanceMeta.Copy(leaf.Items.Start + n, leaf.Items.Start + p, 1);
                        p = n;
                        n = quads[p];
                    }
                    quads[p] = p;
                    childInstances[p] = t;
                    if (instanceMeta != null) instanceMeta.Unstage(leaf.Items.Start + p);
                }
                RequireBranches();
                AssignSlice(addr.MakeChild(node, 3), node.ChildIndex + 3, leaf.ItemsAllocated, quad3, quad4);
                AssignSlice(addr.MakeChild(node, 2), node.ChildIndex + 2, leaf.ItemsAllocated, quad2, quad3);
                AssignSlice(addr.MakeChild(node, 1), node.ChildIndex + 1, leaf.ItemsAllocated, quad1, quad2);
                AssignSlice(addr.MakeChild(node, 0), node.ChildIndex + 0, leaf.ItemsAllocated, 0, quad1);
                addr = FindBranch(addr, stack, ref stackCount, pos);
            }

            //Debug.WriteLine($"Inserting {pos}");
            var mutation = Append(stack[stackCount - 1], instance);
            UpdateAABB(instance, stack, stackCount);
            return mutation;
        }
        public bool Move(Int2 oldPos, Int2 newPos, SceneInstance instance, out Mutation remMutation, out Mutation addMutation) {
            using var marker = ProfileMarker_Move.Auto();
            Span<int> stack = stackalloc int[32];
            int stackCount = 0;
            var oldAddress = FindBranch(stack, ref stackCount, oldPos);
            if (!oldAddress.Contains(newPos)) {
                remMutation = Remove(stack[stackCount - 1], instance);
                addMutation = Add(newPos, instance);
                if (instanceMeta != null) instanceMeta.Unstage(addMutation.NewIndex);
                return true;
            } else {
                UpdateAABB(instance, stack, stackCount);
                remMutation = Mutation.None;
                addMutation = Mutation.None;
                return false;
            }
        }
        public Mutation Remove(Int2 pos, SceneInstance instance) {
            using var marker = ProfileMarker_Remove.Auto();
            Span<int> stack = stackalloc int[32];
            int stackCount = 0;
            FindBranch(stack, ref stackCount, pos);
            return Remove(stack[stackCount - 1], instance);
        }

        unsafe private void AssignSlice(NodeAddress addr, int nodeIndex, RangeInt itemRange, int itemOff1, int itemOff2) {
            var childCount = itemOff2 - itemOff1;
            BoundingBox bounds = BoundingBox.Invalid;
            if (childCount > 0) {
                if (itemOff1 != 0) {
                    var oldRange = itemRange;
                    itemRange = instances.Allocate((childCount + 8) & ~7);
                    instances.Slice(oldRange.Start + itemOff1, childCount).CopyTo(instances.Slice(itemRange));
                    if (instanceMeta != null) {
                        instanceMeta.RequireSize(instances.Capacity);
                        instanceMeta.Copy(oldRange.Start + itemOff1, itemRange.Start, childCount);
                    }
                }
                nodes[nodeIndex].ChildIndex = ~leaves.Add(new() { ItemsAllocated = itemRange, ItemsCount = childCount, });
                for (int i = 0; i < childCount; i++) {
                    var instance = instances[itemRange.Start + i];
                    var aabb = Scene.GetInstanceAABB(instance);
                    bounds = BoundingBox.Union(bounds, aabb);
                }
            }
            branches[nodeIndex].Bounds = bounds;
        }

        private void RequireBranches() {
            if (branches.Length < nodes.Capacity) {
                Array.Resize(ref branches, nodes.Capacity);
            }
        }

        private int Sort(Span<int> quads, int from, int to, int value) {
            int i = from, e = to - 1;
            for (; ; ) {
                for (; i <= e && quads[i] < value; ++i) ;
                for (; i <= e && quads[e] >= value; --e) ;
                if (i > e) break;
                Swap(ref quads[i], ref quads[e]);
            }
            Debug.Assert(i >= to || quads[i] >= value);
            return i;
        }

        private void Swap<T>(ref T v1, ref T v2) {
            var t = v1;
            v1 = v2;
            v2 = t;
        }
        private NodeAddress FindBranch(Span<int> stack, ref int stackCount, Int2 pos) {
            stack[stackCount++] = root.Index;
            return FindBranch(root, stack, ref stackCount, pos);
        }
        private NodeAddress FindBranch(NodeAddress addr, Span<int> stack, ref int stackCount, Int2 pos) {
            while (true) {
                ref var node = ref nodes[addr.Index];
                if (node.ChildIndex <= 0) break;
                var childIndex = addr.GetChildIndex(pos);
                addr = addr.MakeChild(node, childIndex);
                stack[stackCount++] = addr.Index;
            }
            return addr;
        }

        private Mutation Append(int nodeIndex, SceneInstance instance) {
            ref var parent = ref nodes[nodeIndex];
            Debug.Assert(parent.ChildIndex <= 0);
            if (parent.ChildIndex == 0) {
                parent.ChildIndex = ~leaves.Add(default);
                branches[nodeIndex].Bounds = BoundingBox.Invalid;
            }
            ref var leaf = ref leaves[~parent.ChildIndex];
            var mutation = new Mutation() { OldOffset = leaf.ItemsAllocated.Start, };
            if (leaf.ItemsCount >= leaf.ItemsAllocated.Length) {
                int newLength = (leaf.ItemsCount + 8) & ~7;
                var oldAllocated = leaf.ItemsAllocated;
                instances.Reallocate(ref leaf.ItemsAllocated, newLength, 16);
                if (instanceMeta != null) {
                    instanceMeta.RequireSize(instances.Capacity);
                    if (oldAllocated.Start != leaf.ItemsAllocated.Start) {
                        instanceMeta.Copy(oldAllocated.Start, leaf.ItemsAllocated.Start, leaf.ItemsCount);
                    }
                }
            }
            mutation.NewOffset = leaf.ItemsAllocated.Start;
            mutation.Index = leaf.ItemsCount++;
            mutation.NewCount = leaf.ItemsCount;
            instances[leaf.ItemsAllocated.Start + mutation.Index] = instance;
            Debug.Assert(leaf.ItemsCount <= leaf.ItemsAllocated.Length);
            return mutation;
        }
        private Mutation Remove(int nodeIndex, SceneInstance instance) {
            ref var node = ref nodes[nodeIndex];
            var leafId = ~node.ChildIndex;
            ref var leaf = ref leaves[leafId];
            var nodeInstances = instances.Slice(leaf.Items);
            var mutation = new Mutation() { OldOffset = leaf.Items.Start, };
            int i = 0;
            for (; i < nodeInstances.Count; i++) if (nodeInstances[i] == instance) break;
            if (i >= nodeInstances.Count) return Mutation.None;
            nodeInstances.Slice(i + 1).CopyTo(nodeInstances.Slice(i));
            if (instanceMeta != null) {
                instanceMeta.Stage(leaf.Items.Start + i);
                instanceMeta.Copy(leaf.Items.Start + i + 1, leaf.Items.Start + i, leaf.Items.Length - i - 1);
            }
            leaf.ItemsCount--;
            mutation.NewOffset = leaf.Items.Start;
            mutation.Index = i;
            mutation.NewCount = leaf.ItemsCount;
            return mutation;
        }

        private void UpdateAABB(SceneInstance instance, Span<int> stack, int stackCount) {
            RequireBranches();
            var aabb = Scene.GetInstanceAABB(instance);
            for (int i = stackCount - 1; i >= 0; i--) {
                ref var branch = ref branches[stack[i]];
                branch.Bounds = BoundingBox.Union(branch.Bounds, aabb);
            }
        }
        public FrustumEnumerator CreateFrustumEnumerator(Frustum frustum) {
            return new(this, frustum);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SceneInstance GetInstance(int index) => instances[index];

        unsafe public struct FrustumEnumerator {
            public readonly BoundingVolumeHierarchy BVH;
            public readonly Frustum Frustum;
            public NodeAddress Addr;
            private int valueIndex;
            private fixed int stack[32];
            private int stackCount;
            public int Current => ~valueIndex;
            public bool IsParentFullyInFrustum => (stack[stackCount + 1] & 0x8) != 0;
            public bool IsFullyInFrustum => (stack[stackCount + 1] & 0x4) != 0;
            public BoundingBox ActiveBoundingBox => BVH.branches[stack[stackCount + 1] >> 4].Bounds;
            public FrustumEnumerator(BoundingVolumeHierarchy bvh, Frustum frustum) {
                BVH = bvh;
                Frustum = frustum;
                Addr = bvh.root;
                stack[stackCount = 0] = BVH.root.Index << 4;
                if (BVH.root.Index < 0) stackCount = -1;
            }
            public bool MoveNext() {
                while (stackCount >= 0) {
                    var top = stack[stackCount];
                    var parent = BVH.nodes[top >> 4];
                    if (parent.ChildIndex > 0) {
                        // Iterate past this child
                        var childIndex = (top & 0x03);
                        if (childIndex == 0x03) --stackCount;
                        else stack[stackCount] = top + 1;
                        // Determine childs visibility
                        childIndex += parent.ChildIndex;
                        if ((top & 0x4) != 0) {     // Parent bounds is fully visible
                            stack[++stackCount] = (childIndex << 4) | 0x0c;
                        } else if (Frustum.GetVisibility(BVH.branches[childIndex].Bounds, out var contained)) {
                            stack[++stackCount] = (childIndex << 4) | (contained ? 0x4 : 0x0);
                        }
                    } else {
                        --stackCount;
                        if (parent.ChildIndex != 0) {
                            // Is leaf with contents
                            valueIndex = ~parent.ChildIndex;
                            if (BVH.leaves[valueIndex].Items.Length != 0) return true;
                            BVH.leaves.Return(valueIndex);
                            parent.ChildIndex = 0;
                            BVH.nodes[top >> 4] = parent;
                        }
                    }
                }
                return false;
            }

            public RangeInt GetInstanceRange() {
                return BVH.leaves[valueIndex].Items;
            }
            public Span<SceneInstance> GetInstances() {
                return BVH.instances.Slice(BVH.leaves[valueIndex].Items);
            }
            public SceneInstance GetInstance(int index) {
                return BVH.instances[index];
            }

            public void OverwriteBoundingBox(BoundingBox bounds) {
                ref var existingBounds = ref BVH.branches[stack[stackCount + 1] >> 4].Bounds;
                Debug.Assert(
                    bounds.Min.X >= existingBounds.Min.X && bounds.Min.Y >= existingBounds.Min.Y && bounds.Min.Z >= existingBounds.Min.Z &&
                    bounds.Max.X <= existingBounds.Max.X && bounds.Max.Y <= existingBounds.Max.Y && bounds.Max.Z <= existingBounds.Max.Z
                );
                existingBounds = bounds;
            }
        }
    }

    /*public class BoundingVolumeHierarchy {

        public Scene Scene;
        private SparseArray<SceneInstance> instances = new(16);

        public struct Node {
            public RangeInt Instances;
            public BoundingBox BoundingBox;
            public bool IsLeaf => Instances.Length >= 0;
            public static readonly Node Leaf = new() { Instances = new(0, -1), };
        }
        private SparseArray<Node> nodes = new();

        public int CreateNode() {
            var nodeIndex = nodes.Allocate();
            nodes[nodeIndex] = Node.Leaf;
            return nodeIndex;
        }

        public void Insert(int nodeIndex, SceneInstance instance) {
            var aabb = Scene.GetInstanceAABB(instance);
            while (true) {
                var node = nodes[nodeIndex];
                if (node.IsLeaf) {
                    if (node.Instances.Length < 64) break;
                    SplitLeaf(nodeIndex);
                }
                int bestBranch = 0;
                float bestSizeDelta = float.MaxValue;
                for (int i = 0; i < 4; i++) {
                    var child = nodes[node.Instances.Start + i];
                    var union = BoundingBox.Union(child.BoundingBox, aabb);
                    var sizeDelta = Vector3.Dot(union.Size, Vector3.One) -
                        Vector3.Dot(child.BoundingBox.Size, Vector3.One);
                    if (sizeDelta < bestSizeDelta) {
                        bestSizeDelta = sizeDelta;
                        bestBranch = i;
                    }
                }
                nodeIndex = node.Instances.Start + bestBranch;
            }
            ref var container = ref nodes[nodeIndex];
            instances.Reallocate(ref container.Instances, container.Instances.Length + 1);
            instances[container.Instances.End - 1] = instance;
        }

        private void SplitLeaf(int nodeIndex) {
            ref var node = ref nodes[nodeIndex];
            Span<float> leftSizes = stackalloc float[node.Instances.Length];
            Span<float> rightSizes = stackalloc float[node.Instances.Length];
            if (node.BoundingBox.Size.X > node.BoundingBox.Size.Z) {
            } else {
            }
        }

    }*/
}
