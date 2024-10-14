using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.CPS {
    //LedgerManager
    public class Runtime {

        public Script Script { get; private set; }

        // An instance of a block
        public struct BlockEvaluation {
            public RangeInt OutputValues;
            public int BlockId;
            public override string ToString() { return $"Block{BlockId}<O={OutputValues}"; }
        }
        // Reference to a block instance that was executed for an entity
        public struct EvaluationStack {
            public int EvaluationId;
            public int PreviousId;
            public int ReferenceCount;
        }
        // An entity
        public struct Object {
            public int PrototypeId;
            //public int EvalStackId;     // Points to the last eval stack item
            public int GraphEvalId;     // Points to the last eval stack item
            public override string ToString() { return $"Proto{PrototypeId}<Eval={GraphEvalId}>"; }
        }


        public SparseArray<BlockEvaluation> BlockEvaluations = new();
        private SparseArray<Object> Objects = new();
        private SparseArray<EvaluationGroup> EGStorage = new();
        //public SparseArray<EvaluationStack> EvalStacks = new();

        private SparseArray<int> IdStorage = new();
        public SparseArray<byte> DataStorage = new();
        public SparseArray<StackItem> VariableStorage = new();

        private HashSet<int> dirtyObjects = new();
        private Dictionary<Type, IService> services = new();
        private Evaluation evaluation = new();

        public EvaluationGraph EvaluationGraph = new();

        public Runtime(Script script) {
            Script = script;
        }

        public int AllocateObject() {
            return Objects.Add(new Object() {
                PrototypeId = -1,
                GraphEvalId = -1,
            });
        }
        public void SetObjectPrototypeId(int objId, int protoId) {
            var obj = Objects[objId];
            obj.PrototypeId = protoId;
            Objects[objId] = obj;
            dirtyObjects.Add(objId);
        }
        public int GetObjectEvalStackId(int objId) {
            return Objects[objId].GraphEvalId;
        }

        public StackItem GetEvaluationVariable(int evalStackId, string name) {
            var en = new EvaluationGraph.ReadOnlyEnumerator(EvaluationGraph, evalStackId);
            for (; en.IsValid; en.MoveNext(EvaluationGraph)) {
                var evalId = en.Index;
                var blockEval = BlockEvaluations[evalId];
                /*while (evalStackId >= 0) {
                var evalStack = EvalStacks[evalStackId];
                var blockEval = BlockEvaluations[evalStack.EvaluationId];*/
                var block = Script.GetBlock(blockEval.BlockId);
                var outputs = Script.GetBlockOutputs(block.Outputs);
                var variables = VariableStorage.Slice(blockEval.OutputValues);
                Debug.Assert(outputs.Count == variables.Count);
                for (int m = 0; m < outputs.Count; ++m) {
                    if (outputs[m].Name == name) return variables[m];
                }
                //evalStackId = evalStack.PreviousId;
            }
            return default;
        }
        public SparseArray<byte> GetDataStorage() { return DataStorage; }

        public T? GetService<T>() where T : IService {
            if (services.TryGetValue(typeof(T), out var value)) return (T)value;
            return default;
        }

        public ArraySegment<StackItem> GetVariables(RangeInt items) {
            return VariableStorage.Slice(items);
        }

        public void Resolve() {
            /* 
             * Separate changed objects by evaluation id
             * Before each block, collect objects that share same block id
             * Evaluate block
             * - Block splits blocks as required
             * Merge equal blocks (remove temporaries created via Team.GetUnlock() that returns same value)
             * For all entities whose block id changed
             * - If a child block was removed: EndInvoke for that branch
             * - If a child block was added: BeginInvoke for that branch
             * Continue
             */
            var evalGroups = new PooledList<EvaluationGroup>(8);
            {
                // Create temporary storage of relevant objects
                using var entityGroups = new PooledHashMap<int, PartialRangeInt>(8);
                // Organise all dirty entities by their EvaluationId
                // (which is -1 if not yet evaluated)
                foreach (var objId in dirtyObjects) {
                    ref var obj = ref Objects[objId];
                    ref var range = ref entityGroups[obj.GraphEvalId];
                    if (range.IsFull) {
                        IdStorage.Reallocate(ref range.Range, Math.Max(4, range.Range.Length * 2));
                    }
                    IdStorage[range.ConsumeNext()] = objId;
                    DerefEvalStack(ref obj.GraphEvalId);
                }
                // Generate EvaluationGroups from the grouped entities
                foreach (var group in entityGroups) {
                    var range = group.Value;
                    if (range.Range.Length != range.Count)
                        IdStorage.Reallocate(ref range.Range, range.Count);
                    var evalGroup = new EvaluationGroup() {
                        EntityIds = range.Range,
                        EvalIterator = new PEvalEnumerator(this),
                        NextBlockId = -1,
                        EvalStackId = -1,
                        Execution = new EvaluationGroup.ExecutionData() {
                            Stack = new List<StackItem>(),
                        },
                    };
                    int evalI = -1;// group.Key;
                    if (evalI == -1) {
                        evalI = BlockEvaluations.Add(new BlockEvaluation() {
                            BlockId = -1,
                            OutputValues = default,
                        });
                    }
                    if (evalI != -1) {
                        evalGroup.EvalIterator.SetCurrent(this, evalI);
                        evalGroups.Add(evalGroup);
                    }
                }
                dirtyObjects.Clear();
            }

            for (; ; ) {
                // Find the most nested block
                // TODO: Instead find the block that references
                // the earliest document point
                // Get all groups that use this block
                var execBlockId = -1;
                var relevantGroupIds = new PooledList<int>(8);
                for (var p = 0; p < evalGroups.Count; ++p) {
                    ref var group = ref evalGroups[p];
                    var tblockId = group.GetNextBlock(this);
                    if (tblockId == -1) break;
                    if (tblockId < execBlockId || execBlockId == -1) {
                        relevantGroupIds.Clear();
                        execBlockId = tblockId;
                    }
                    if (tblockId == execBlockId) {
                        relevantGroupIds.Add(p);
                    }
                }
                if (relevantGroupIds.Count == 0) break;
                foreach (var groupId in relevantGroupIds) {
                    ref var group = ref evalGroups[groupId];
                    // Force iterate (over a root node probably)
                    group.GetNextBlock(this);
                    group.Reset();
                }
                // Reevaluate those sets, optionally fork sets
                var evaluator = new Evaluator(this, ref evalGroups, ref relevantGroupIds);
                var execBlock = Script.GetBlock(execBlockId);
                ComputeInputs(execBlock, evaluator);
                InitOutputs(execBlock, evaluator);
                evaluation.Evaluate(execBlock, evaluator);
                Console.WriteLine($"## Invoking Block {execBlockId} {execBlock.Name}");
                FinishEvaluation(evaluator, execBlockId);
                relevantGroupIds.Dispose();
            }
            foreach (var group in evalGroups) {
                var entityIds = IdStorage.Slice(group.EntityIds);
                foreach (var entityId in entityIds) {
                    ref var obj = ref Objects[entityId];
                    //obj.EvalStackId = group.EvalStackId;
                    //AddrefEvalStack(group.EvalStackId);
                    obj.GraphEvalId = group.GraphEnumerator.GetRootIndex();
                }
            }
            evalGroups.Dispose();
        }

        // All leaf items can be mutated (promote to node, append EvalI as sibling)
        // All nodes fully contained can be mutated (append EvalI as sibling)
        // Any nodes not fully contained must be popped until contained
        // - If reach root, insert new shared root node
        // - Can then append EvalI as sibling
        private void FinishEvaluation(Evaluator evaluator, int blockId) {
            // Deref top
            for (int i = 0; i < evaluator.RelevantIds.Count; i++) {
                var groupId = evaluator.RelevantIds[i];
                ref var group = ref evaluator.EvalGroups[groupId];
                if (group.GraphEnumerator.Count < 2) continue;
                ref var node = ref group.GraphEnumerator.GetAt(EvaluationGraph, ^2);
                node.UserData--;
            }
            static bool GetIsUnique(ref EvaluationGroup group, EvaluationGraph graph) {
                // We only have leaves
                if (group.GraphEnumerator.Count <= 1) return false;
                // This groups parent is entirely contained (not shared)
                if (group.GraphEnumerator.GetAt(graph, ^2).UserData == 0) return false;
                return true;
            }
            for (bool anyRemain = true; anyRemain;) {
                anyRemain = false;
                int depth = -1;
                for (int i = 0; i < evaluator.RelevantIds.Count; i++) {
                    var groupId = evaluator.RelevantIds[i];
                    ref var group = ref evaluator.EvalGroups[groupId];
                    if (GetIsUnique(ref group, EvaluationGraph)) continue;
                    depth = Math.Max(depth, group.GraphEnumerator.Count);
                }
                if (depth < 2) break;
                for (int i = 0; i < evaluator.RelevantIds.Count; i++) {
                    var groupId = evaluator.RelevantIds[i];
                    ref var group = ref evaluator.EvalGroups[groupId];
                    // Not a deep group
                    if (group.GraphEnumerator.Count != depth) continue;
                    // Already not shared
                    if (GetIsUnique(ref group, EvaluationGraph)) continue;
                    Debug.Assert(group.GraphEnumerator.Count > 1, "Leaves shouldnt appear here!");
                    group.GraphEnumerator.GetAt(EvaluationGraph, ^2).UserData++;
                    group.GraphEnumerator.Pop(EvaluationGraph);
                    group.GraphEnumerator.GetAt(EvaluationGraph, ^2).UserData--;
                }
            }
            // Add ref to top
            for (int i = 0; i < evaluator.RelevantIds.Count; i++) {
                var groupId = evaluator.RelevantIds[i];
                ref var group = ref evaluator.EvalGroups[groupId];
                if (group.GraphEnumerator.Count < 2) continue;
                ref var node = ref group.GraphEnumerator.GetAt(EvaluationGraph, ^2);
                node.UserData++;
            }

            // First item = EvalId to be inserted
            // Second item = Current Graph node index
            Span<(int, int)> resolvedEvalIds = stackalloc (int, int)[evaluator.RelevantIds.Count];
            for (int i = 0; i < evaluator.RelevantIds.Count; i++) {
                var groupId = evaluator.RelevantIds[i];
                ref var group = ref evaluator.EvalGroups[groupId];
                var outputs = evaluator.GetVariables(group.Outputs);
                int evalI = ApplyOutputs(blockId, outputs);
                ClearExecution(ref group.Execution);
                resolvedEvalIds[i] = (evalI, group.GraphEnumerator.GetCurrentIndex());
                // Iterate forward
                group.EvalIterator.SetCurrent(this, evalI);
                // And add this evaluation to the group
                //AppendEvalStack(ref group.EvalStackId, evalI);
            }
            MemoryExtensions.Sort(evaluator.RelevantIds.AsSpan(), resolvedEvalIds);
            for (int i = 0; i < evaluator.RelevantIds.Count; ) {
                var evalI = resolvedEvalIds[i];
                int begin = i;
                for (++i; i < evaluator.RelevantIds.Count; ++i)
                    if (resolvedEvalIds[i] != evalI) break;
                for (int g = begin; g < i; g++) {
                    var groupId = evaluator.RelevantIds[g];
                    ref var group = ref evaluator.EvalGroups[groupId];
                    group.GraphEnumerator.AppendSibling(EvaluationGraph, evalI.Item1);
                }
            }
        }

        private int ApplyOutputs(int blockId, ArraySegment<StackItem> outputs) {
            var evalI = FindEvaluation(blockId, outputs);
            // Already evaluated this, drop outputs (should we overwrite?)
            if (evalI != -1) DeleteParameterData(blockId, outputs);
            else evalI = ConsumeToEvaluation(blockId, outputs);

            return evalI;
        }
        // Cleanup evaluation data for this group
        private void ClearExecution(ref EvaluationGroup.ExecutionData execution) {
            VariableStorage.Return(ref execution.Parameters);
            VariableStorage.Return(ref execution.Outputs);
        }

        private void AddrefEvalStack(int evalStackId) {
            //EvalStacks[evalStackId].ReferenceCount++;
            EvaluationGraph.AddrefNode(evalStackId);
        }
        /*private void AppendEvalStack(ref int evalStackId, int evalId) {
            if (evalStackId != -1) AddrefEvalStack(evalStackId);
            evalStackId = EvalStacks.Add(new EvaluationStack() {
                PreviousId = evalStackId,
                EvaluationId = evalId,
                ReferenceCount = 0,
            });
        }*/
        private void DerefEvalStack(ref int evalStackId) {
            if (evalStackId == -1) return;
            EvaluationGraph.DerefNode(evalStackId);
            /*var item = EvalStacks[evalStackId];
            item.ReferenceCount--;
            if (item.ReferenceCount == 0) {
                EvalStacks.Return(evalStackId);
                DerefEvalStack(ref item.PreviousId);
            } else {
                EvalStacks[evalStackId] = item;
            }*/
            evalStackId = -1;
        }

        private int FindEvaluation(int blockId, ArraySegment<StackItem> parameters) {
            for (var it = BlockEvaluations.GetEnumerator(); it.MoveNext();) {
                if (it.Current.BlockId != blockId) continue;
                var results = it.Current.OutputValues;
                Debug.Assert(results.Length == parameters.Count);
                bool same = true;
                for (int i = 0; i < results.Length; ++i) {
                    if (!VariableStorage[results.Start + i].ExactEquals(DataStorage, parameters[i])) {
                        same = false;
                        break;
                    }
                }
                if (same) return it.Index;
            }
            return -1;
        }
        // Moves parameter data into the evaluation
        private int ConsumeToEvaluation(int blockId, ArraySegment<StackItem> outputs) {
            var block = Script.GetBlock(blockId);
            Debug.Assert(block.Outputs.Length == outputs.Count);
            var eval = new BlockEvaluation() {
                BlockId = blockId,
                OutputValues = VariableStorage.Allocate(outputs.Count),
            };
            var outParameters = VariableStorage.Slice(eval.OutputValues);
            if (outputs.Count != 0) {
                outputs.CopyTo(outParameters);
                for (int o = 0; o < outputs.Count; ++o) outputs[o] = StackItem.Invalid;
            }
            return BlockEvaluations.Add(eval);
        }
        // Deletes parameter data
        private void DeleteParameterData(int blockId, ArraySegment<StackItem> parameters) {
            for (int i = 0; i < parameters.Count; i++) {
                parameters[i].Delete(DataStorage);
            }
            parameters.AsSpan().Fill(StackItem.Invalid);
        }

        private void ComputeInputs(Script.Block block, Evaluator evaluator) {
            var varStorage = evaluator.Runtime.VariableStorage;
            var blockDeps = Script.GetDependencies(block.Dependencies);
            foreach (var groupId in evaluator.RelevantIds) {
                ref var evalGroup = ref evaluator.EvalGroups[groupId];
                varStorage.Reallocate(ref evalGroup.Execution.Parameters, blockDeps.Count);
                var parameters = varStorage.Slice(evalGroup.Parameters).AsSpan();
                for (var p = 0; p < blockDeps.Count; ++p) {
                    FindValue(ref parameters[p], evalGroup, blockDeps[p]);
                }
            }
        }

        private void InitOutputs(Script.Block block, Evaluator evaluator) {
            foreach (var groupId in evaluator.RelevantIds) {
                ref var evalGroup = ref evaluator.EvalGroups[groupId];
                VariableStorage.Reallocate(ref evalGroup.Execution.Outputs, block.Outputs.Length);
                var outputs = VariableStorage.Slice(evalGroup.Execution.Outputs);
                outputs.AsSpan().Fill(StackItem.None);
            }
        }

        private void FindValue(ref StackItem variable, EvaluationGroup evalGroup, Script.Dependency dependency) {
            var en = new EvaluationGraph.ReadOnlyEnumerator(EvaluationGraph, evalGroup.GraphEvalId);
            for (; en.IsValid; en.MoveNext(EvaluationGraph)) {
                var evalId = en.Index;
                var blockEval = BlockEvaluations[evalId];
                if (blockEval.BlockId == -1) continue;
                var block = Script.GetBlock(blockEval.BlockId);
                var mutationI = Script.FindMutationIndex(block.Mutations, dependency);
                if (mutationI == -1) continue;
                variable = VariableStorage[blockEval.OutputValues.Start + mutationI];
                break;
            }
            /*var evalStackId = evalGroup.EvalStackId;
            while (evalStackId != -1) {
                var evalStack = EvalStacks[evalStackId];
                evalStackId = evalStack.PreviousId;
                var blockEval = BlockEvaluations[evalStack.EvaluationId];
                if (blockEval.BlockId == -1) continue;
                var block = Script.GetBlock(blockEval.BlockId);
                var mutationI = Script.FindMutationIndex(block.Mutations, dependency);
                if (mutationI == -1) continue;
                variable = VariableStorage[blockEval.OutputValues.Start + mutationI];
                break;
            }*/
        }



    }
}
