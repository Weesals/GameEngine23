using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.CPS {
    /*public class BlockTest {

        public class Block {
            public int NextBlockId;
        }

        public struct BlockExecSet {
            public RangeInt OutputData;
        }
        public struct BlockExecution {
            public BlockExecSet[] OutputSets;
        }

        public void Resolve() {
            var ex = new BlockExecution();
            var set = ex.OutputSets[0];
        }

    }*/

    public class EvaluationGraph {

        public struct EvaluationNode {
            public int ChildId;
            public int PreviousId;
            public int ReferenceCount;
            public int UserData;

            public bool HasChildren => ChildId >= 0;
            public bool IsLeaf => ChildId < 0;
            public int ChildIndex => ChildId >= 0 ? ChildId : ~ChildId;
        }

        private SparseArray<EvaluationNode> nodes = new();

        public int AllocateNode(int blockIndex) {
            return nodes.Add(new() {
                ChildId = ~blockIndex,
                PreviousId = -1,
                ReferenceCount = 1,
            });
        }
        public void AppendBlock(ref int nodeId, int blockIndex) {
            nodeId = nodes.Add(new() {
                ChildId = ~blockIndex,
                PreviousId = nodeId,
                ReferenceCount = 1,
            });
        }

        public ref EvaluationNode GetAt(int evalGraphId) {
            return ref nodes[evalGraphId];
        }

        public void AddrefNode(int evalStackId) {
            nodes[evalStackId].ReferenceCount++;
        }
        public void DerefNode(int evalStackId) {
            ref var node = ref nodes[evalStackId];
            node.ReferenceCount--;
            if (node.ReferenceCount == 0) {
                if (node.PreviousId != -1) DerefNode(node.PreviousId);
                if (node.HasChildren) DerefNode(node.ChildIndex);
                else {
                    throw new NotImplementedException();
                }
            }
        }

        unsafe public struct ReadOnlyEnumerator {
            private fixed int stack[6];
            private int count;

            public int Count => count;
            public bool IsValid => count > 0;
            public int Index => stack[count - 1];

            public ReadOnlyEnumerator(EvaluationGraph graph, int root) {
                count = 0;
                stack[count++] = root;
                MoveToLeaf(graph);
            }
            private bool MoveToLeaf(EvaluationGraph graph) {
                while (count > 0) {
                    ref var top = ref stack[count - 1];
                    ref var topNode = ref graph.nodes[top];
                    stack[count++] = topNode.ChildIndex;
                    if (!topNode.HasChildren) return true;
                }
                return false;
            }
            public bool MoveNext(EvaluationGraph graph) {
                for (--count; count > 0; --count) {
                    ref var top = ref stack[count - 1];
                    ref var topNode = ref graph.nodes[top];
                    top = topNode.PreviousId;
                    if (top != -1) break;
                }
                return MoveToLeaf(graph);
            }
        }
        unsafe public struct Enumerator {
            private fixed int stack[6];
            private int count;

            public int Count => count;

            public Enumerator() {
            }
            public void AppendSibling(EvaluationGraph graph, int blockIndex) {
                if (count == 0) {
                    stack[count++] = graph.AllocateNode(blockIndex);
                } else {
                    ref int top = ref stack[count - 1];
                    graph.AppendBlock(ref top, blockIndex);
                }
            }
            public void AppendChild(EvaluationGraph graph, int blockIndex) {
                ref int top = ref stack[count - 1];
                ref var topNode = ref graph.nodes[top];
                if (topNode.IsLeaf) {
                    topNode.ChildId = graph.AllocateNode(topNode.ChildId);
                }
                graph.AppendBlock(ref topNode.ChildId, blockIndex);
                stack[count++] = topNode.ChildId;
            }
            public void Pop(EvaluationGraph graph) {
                --count;
            }
            public int GetRootIndex() {
                return stack[0];
            }
            public int GetCurrentIndex() {
                return stack[count - 1];
            }
            public int GetIndex(int index) {
                return stack[index];
            }
            public int GetIndex(Index index) {
                return stack[index.GetOffset(count)];
            }
            public ref EvaluationNode GetCurrent(EvaluationGraph graph) {
                return ref graph.nodes[stack[count - 1]];
            }
            public ref EvaluationNode GetAt(EvaluationGraph graph, int index) {
                return ref graph.nodes[stack[index]];
            }
            public ref EvaluationNode GetAt(EvaluationGraph graph, Index index) {
                return ref graph.nodes[stack[index.GetOffset(count)]];
            }

            public ref struct StackEnumerator {
                public ref Enumerator Enumerator;
                private int index;
                public int Current => Enumerator.GetIndex(index);
                public StackEnumerator(ref Enumerator enumerator) {
                    Enumerator = ref enumerator;
                    index = -1;
                }
                public void Dispose() { throw new NotImplementedException(); }
                public void Reset() { throw new NotImplementedException(); }
                public bool MoveNext() {
                    return ++index < Enumerator.Count;
                }
            }
        }

    }
}
