using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.CPS {
    public class API {
        public struct Parameter {
            public string Name;
            public int TypeId;
        }
        public string Name;
        public Parameter[] Parameters;
        public Parameter ReturnValue;
    }

    public interface IInstructionInvoke2 {
        void Invoke(Runtime2.EvaluatorContext context);
    }
    //LedgerManager
    public class Runtime2 {

        public Script Script { get; private set; }

        public struct BlockEvaluation {
            public RangeInt OutputValues;
            public int BlockId;
            public RangeInt Children;
            public override string ToString() { return $"Block{BlockId}<O={OutputValues} C={Children}>"; }
        }
        public struct EvaluationStack {
            public int EvaluationId;
            public int PreviousId;
            public int ReferenceCount;
        }
        public struct Object {
            public int PrototypeId;
            public int EvalStackId;
            public override string ToString() { return $"Proto{PrototypeId}<Eval={EvalStackId}>"; }
        }

        public struct PartialRangeInt {
            public RangeInt Range;
            public int Count;
            public bool IsFull => Count >= Range.Length;
            public int ConsumeNext() {
                Debug.Assert(!IsFull, "Array is full");
                ++Count;
                return Count - 1;
            }
            public override string ToString() {
                return $"Range{Range}<Use={Count}>";
            }
        }
        // The context object for stepping through evaluator blocks
        // EntityIds and Parameters are set before calling APIs
        // NextBlockId is mutated by the API to denote the next block
        public struct EvaluationGroup {
            public struct ExecutionData {
                public RangeInt Parameters;
                public RangeInt Outputs;
                public List<StackItem> Stack;

                public StackItem PopStack() {
                    var value = Stack[^1];
                    Stack.RemoveAt(Stack.Count - 1);
                    return value;
                }
                public void PushStack(StackItem item) {
                    Stack.Add(item);
                }
            }
            public PEvalEnumerator EvalIterator;
            public RangeInt EntityIds;
            public int NextBlockId;
            public int EvalStackId;
            public ExecutionData Execution;

            public RangeInt Parameters => Execution.Parameters;
            public RangeInt Outputs => Execution.Outputs;
            public int EvalId => EvalIterator.EvalId;

            public bool GetIsNextBlockChild(Runtime2 runtime) {
                if (NextBlockId != -1) return true;
                var evalId = EvalId;
                var blockId = runtime.BlockEvaluations[evalId].BlockId;
                if (blockId == -1) return true;
                return false;
            }
            public int GetNextBlock(Runtime2 runtime) {
                // Instruction inserted a different blockId
                if (NextBlockId != -1) return NextBlockId;
                // Otherwise iterate the tree as normal
                return EvalIterator.GetNextBlock(runtime);
            }
            public void Reset() {
                NextBlockId = -1;
                Debug.Assert(Execution.Parameters.Length == 0);
            }
            public override string ToString() {
                return $"Group<EvalId={EvalId}, Params={Parameters}, Next={NextBlockId}>";
            }
        }

        public ref struct Evaluator {
            public Runtime2 Runtime;
            public ref PooledList<EvaluationGroup> EvalGroups;
            public ref PooledList<int> RelevantIds;
            public Evaluator(Runtime2 runtime, ref PooledList<EvaluationGroup> evalGroups, ref PooledList<int> relevantIds) {
                Runtime = runtime;
                EvalGroups = ref evalGroups;
                RelevantIds = ref relevantIds;
            }
            public void PushConstant<T>(T v) where T : unmanaged {
                foreach (var groupId in RelevantIds) {
                    var group = EvalGroups[groupId];
                    group.Execution.PushStack(StackItem.Create(Runtime.DataStorage, v));
                }
            }

            public ArraySegment<StackItem> GetVariables(RangeInt parameters) {
                return Runtime.VariableStorage.Slice(parameters);
            }
        }
        public ref struct EvaluatorContext {
            public Evaluator Evaluator;
            public ArraySegment<byte> ProgramData;
            public int ProgramCounter;
            public EvaluatorContext(ref Evaluator evaluator) {
                Evaluator = evaluator;
            }
            public unsafe T ReadProgramData<T>() where T : unmanaged {
                int offset = ProgramData.Offset + ProgramCounter;
                ProgramCounter += sizeof(T);
                fixed (byte* sourcePtr = &ProgramData.Array![offset]) {
                    return *(T*)sourcePtr;
                }
            }
            public void PushConstant<T>(T v) where T : unmanaged {
                Evaluator.PushConstant<T>(v);
            }

            public T? GetService<T>() where T : IService {
                return Evaluator.Runtime.GetService<T>();
            }
        }

        private SparseArray<BlockEvaluation> BlockEvaluations = new();
        private SparseArray<Object> Objects = new();
        private SparseArray<EvaluationGroup> EGStorage = new();
        private SparseArray<EvaluationStack> EvalStacks = new();

        private SparseArray<int> IdStorage = new();
        private SparseArray<byte> DataStorage = new();
        public SparseArray<StackItem> VariableStorage = new();

        private HashSet<int> dirtyObjects = new();
        private Dictionary<Type, IService> services = new();

        public Runtime2(Script script) {
            Script = script;
        }

        public int AllocateObject() {
            return Objects.Add(new Object() {
                PrototypeId = -1,
                EvalStackId = -1,
            });
        }
        public void SetObjectPrototypeId(int objId, int protoId) {
            var obj = Objects[objId];
            obj.PrototypeId = protoId;
            Objects[objId] = obj;
            dirtyObjects.Add(objId);
        }
        public int GetObjectEvalStackId(int objId) {
            return Objects[objId].EvalStackId;
        }

        public StackItem GetEvaluationVariable(int evalStackId, string name) {
            while (evalStackId >= 0) {
                var evalStack = EvalStacks[evalStackId];
                var blockEval = BlockEvaluations[evalStack.EvaluationId];
                var block = Script.GetBlock(blockEval.BlockId);
                var mutations = Script.GetMutations(block.Mutations);
                var variables = VariableStorage.Slice(blockEval.OutputValues);
                Debug.Assert(mutations.Count == variables.Count);
                for (int m = 0; m < mutations.Count; ++m) {
                    if (mutations[m].Name == name) return variables[m];
                }
                evalStackId = evalStack.PreviousId;
            }
            return default;
        }
        public SparseArray<byte> GetDataStorage() { return DataStorage; }

        public T? GetService<T>() where T : IService {
            if (services.TryGetValue(typeof(T), out var value)) return (T)value;
            return default;
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
                    var obj = Objects[objId];
                    if (!entityGroups.TryGetValue(obj.EvalStackId, out var range) || range.IsFull) {
                        IdStorage.Reallocate(ref range.Range, Math.Max(4, range.Range.Length * 2));
                    }
                    IdStorage[range.ConsumeNext()] = objId;
                    entityGroups[obj.EvalStackId] = range;
                    DerefEvalStack(ref obj.EvalStackId);
                    Objects[objId] = obj;
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
                            Children = default,
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
                // Get all sets that use this block
                var nextBlockId = -1;
                var relevantGroupIds = new PooledList<int>(8);
                for (var p = 0; p < evalGroups.Count; ++p) {
                    var group = evalGroups[p];
                    var tblockId = group.GetNextBlock(this);
                    if (tblockId == -1) break;
                    if (tblockId < nextBlockId || nextBlockId == -1) {
                        relevantGroupIds.Clear();
                        nextBlockId = tblockId;
                    }
                    if (tblockId == nextBlockId) {
                        relevantGroupIds.Add(p);
                    }
                }
                if (relevantGroupIds.Count == 0) break;
                foreach (var groupId in relevantGroupIds) {
                    var group = evalGroups[groupId];
                    // Force iterate (over a root node probably)
                    group.GetNextBlock(this);
                    group.Reset();
                    evalGroups[groupId] = group;
                }
                // Reevaluate those sets, optionally fork sets
                var evaluator = new Evaluator(this, ref evalGroups, ref relevantGroupIds);
                var nextBlock = Script.GetBlock(nextBlockId);
                ComputeInputs(nextBlock, evaluator);
                InitOutputs(nextBlock, evaluator);
                Evaluate(nextBlock, evaluator);
                Console.WriteLine($"## Invoking Block {nextBlockId} {nextBlock.Name}");
                foreach (var groupId in relevantGroupIds) {
                    var group = evalGroups[groupId];
                    //DeleteParameterData(nextBlockId, evaluator.GetVariables(group.Parameters));
                    var outputs = evaluator.GetVariables(group.Outputs);
                    var nextEvalI = FindEvaluation(nextBlockId, outputs);
                    if (nextEvalI != -1) DeleteParameterData(nextBlockId, outputs);
                    else nextEvalI = ConsumeToEvaluation(nextBlockId, outputs);
                    VariableStorage.Return(ref group.Execution.Parameters);
                    VariableStorage.Return(ref group.Execution.Outputs);

                    group.EvalIterator.SetCurrent(this, nextEvalI);
                    AppendEvalStack(ref group.EvalStackId, nextEvalI);
                    evalGroups[groupId] = group;
                }
                relevantGroupIds.Dispose();
            }
            foreach (var group in evalGroups) {
                var entityIds = IdStorage.Slice(group.EntityIds);
                foreach (var entityId in entityIds) {
                    var obj = Objects[entityId];
                    obj.EvalStackId = group.EvalStackId;
                    AddrefEvalStack(group.EvalStackId);
                    Objects[entityId] = obj;
                }
            }
            evalGroups.Dispose();
        }

        private void AddrefEvalStack(int evalStackId) {
            EvalStacks[evalStackId].ReferenceCount++;
        }
        private void AppendEvalStack(ref int evalStackId, int evalId) {
            if (evalStackId != -1) AddrefEvalStack(evalStackId);
            evalStackId = EvalStacks.Add(new EvaluationStack() {
                PreviousId = evalStackId,
                EvaluationId = evalId,
                ReferenceCount = 0,
            });
        }
        private void DerefEvalStack(ref int evalStackId) {
            if (evalStackId == -1) return;
            var item = EvalStacks[evalStackId];
            item.ReferenceCount--;
            if (item.ReferenceCount == 0) {
                EvalStacks.Return(evalStackId);
                DerefEvalStack(ref item.PreviousId);
            } else {
                EvalStacks[evalStackId] = item;
            }
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
                parameters[i] = StackItem.Invalid;
            }
        }

        private void ComputeInputs(Script.Block block, Evaluator evaluator) {
            var blockDeps = Script.GetDependencies(block.Dependencies);
            foreach (var groupId in evaluator.RelevantIds) {
                var evalGroup = evaluator.EvalGroups[groupId];
                VariableStorage.Reallocate(ref evalGroup.Execution.Parameters, blockDeps.Count);
                var parameters = VariableStorage.Slice(evalGroup.Parameters).AsSpan();
                for (var p = 0; p < blockDeps.Count; ++p) {
                    FindValue(ref parameters[p], evalGroup, blockDeps[p]);
                }
                evaluator.EvalGroups[groupId] = evalGroup;
            }
        }
        private void InitOutputs(Script.Block block, Evaluator evaluator) {
            foreach (var groupId in evaluator.RelevantIds) {
                var evalGroup = evaluator.EvalGroups[groupId];
                VariableStorage.Reallocate(ref evalGroup.Execution.Outputs, block.Mutations.Length);
                var outputs = VariableStorage.Slice(evalGroup.Execution.Outputs);
                for (int o = 0; o < outputs.Count; ++o) outputs[o] = StackItem.None;
                evaluator.EvalGroups[groupId] = evalGroup;
            }
        }

        private void FindValue(ref StackItem variable, EvaluationGroup evalGroup, Script.Dependency dependency) {
            var evalStackId = evalGroup.EvalStackId;
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
            }
        }

        private void Evaluate(Script.Block block, Evaluator evaluator) {
            var mutations = Script.GetMutations(block.Mutations);
            for (var m = 0; m < mutations.Count; ++m) {
                var mutation = mutations[m];
                Execute(mutation.ProgramCounter, evaluator);
                foreach (var groupId in evaluator.RelevantIds) {
                    var group = evaluator.EvalGroups[groupId];
                    if (group.Execution.Stack.Count > 0) {
                        var item = group.Execution.PopStack();
                        var outputs = VariableStorage.Slice(group.Outputs);
                        Debug.Assert(!outputs[m].HasData);
                        outputs[m] = item;
                    }
                    while (group.Execution.Stack.Count > 0) {
                        group.Execution.PopStack().Delete(DataStorage);
                    }
                }
            }
        }
        private StringBuilder builder1 = new(), builder2 = new();
        private void Execute(RangeInt programCounter, Evaluator evaluator) {
            EvaluatorContext context = new EvaluatorContext() {
                Evaluator = evaluator,
                ProgramData = Script.GetProgramBlock(programCounter),
                ProgramCounter = 0,
            };
            var op = (Operation.Types)context.ReadProgramData<byte>();
            switch (op) {
                case Operation.Types.PushBool: context.PushConstant<bool>(context.ReadProgramData<byte>() != 0); break;
                case Operation.Types.PushInt: context.PushConstant<int>(context.ReadProgramData<int>()); break;
                case Operation.Types.PushFixed: context.PushConstant<int>(context.ReadProgramData<int>()); break;
                case Operation.Types.PushObject: context.PushConstant<ushort>(context.ReadProgramData<ushort>()); break;
                case Operation.Types.Add:
                case Operation.Types.Subtract:
                case Operation.Types.Multiply:
                case Operation.Types.Divide:
                case Operation.Types.Power:
                case Operation.Types.Modulus: {
                    var dataStore = context.Evaluator.Runtime.DataStorage;
                    for (int i = 0; i < context.Evaluator.RelevantIds.Count; ++i) {
                        var group = context.Evaluator.EvalGroups[context.Evaluator.RelevantIds[i]];
                        var param0 = group.Execution.PopStack();
                        var param1 = group.Execution.PopStack();
                        if (param0.TryGetAsInt(dataStore, out var i0) && param0.TryGetAsInt(dataStore, out var i1)) {
                            var value = i0;
                            switch (op) {
                                case Operation.Types.Add: value = i0 + i1; break;
                                case Operation.Types.Subtract: value = i0 + i1; break;
                                case Operation.Types.Multiply: value = i0 + i1; break;
                                case Operation.Types.Divide: value = i0 + i1; break;
                                case Operation.Types.Power: value = (int)Math.Pow(i0, i1); break;
                                case Operation.Types.Modulus: value = i0 % i1; break;
                                default: throw new NotImplementedException();
                            }
                            group.Execution.PushStack(StackItem.CreateInt(dataStore, value));
                        } else {
                            builder1.Clear();
                            builder2.Clear();
                            var terms = context.Evaluator.Runtime.Script.GetTerms();
                            if (param0.TryGetAsString(dataStore, terms, builder1) && param0.TryGetAsString(dataStore, terms, builder2)) {
                                var value = "";
                                switch (op) {
                                    case Operation.Types.Add: value = builder1.Append(builder2).ToString(); break;
                                    default: throw new NotImplementedException();
                                }
                                var termId = context.Evaluator.Runtime.Script.RequireTerm(value);
                                group.Execution.PushStack(StackItem.CreateObject(dataStore, termId));
                            } else {
                                throw new NotImplementedException();
                            }
                        }
                        param0.Delete(dataStore);
                        param1.Delete(dataStore);
                    }
                }
                break;
                case Operation.Types.Load: {
                    var paramI = context.ReadProgramData<ushort>();
                    var varStore = context.Evaluator.Runtime.VariableStorage;
                    var dataStore = context.Evaluator.Runtime.DataStorage;
                    for (int i = 0; i < context.Evaluator.RelevantIds.Count; ++i) {
                        var group = context.Evaluator.EvalGroups[context.Evaluator.RelevantIds[i]];
                        var value = varStore[group.Parameters.Start + paramI].Copy(dataStore, dataStore);
                        group.Execution.Stack.Add(value);
                    }
                }
                break;
                case Operation.Types.Store: {
                    var outputI = context.ReadProgramData<ushort>();
                    for (int i = 0; i < context.Evaluator.RelevantIds.Count; ++i) {
                        var group = context.Evaluator.EvalGroups[context.Evaluator.RelevantIds[i]];
                        var value = group.Execution.Stack[^1];
                        group.Execution.Stack.RemoveAt(group.Execution.Stack.Count - 1);
                        Debug.Assert(!VariableStorage[group.Outputs.Start + outputI].IsValid);
                        VariableStorage[group.Outputs.Start + outputI] = value;
                    }
                }
                break;
                case Operation.Types.InvokeBlock: {
                    var classI = context.ReadProgramData<ushort>();
                    var blockI = context.ReadProgramData<ushort>();
                    for (int g = 0; g < context.Evaluator.EvalGroups.Count; ++g) {
                        var group = context.Evaluator.EvalGroups[g];
                        Debug.Assert(group.NextBlockId == -1, "Next block already set!");
                        group.NextBlockId = blockI;
                        context.Evaluator.EvalGroups[g] = group;
                    }
                }
                break;
            }
        }

        // Iterate each block within a prototype
        // Optimise for storage; pass in the Runtime for each method call
        public struct PProtoEnumerator : IDisposable {
            public struct BlockIterator {
                public int BlockId;
            }
            public PooledList<BlockIterator> BlockStack;
            public readonly bool IsValid => BlockStack.Count > 0;
            public readonly int BlockId => BlockStack[^1].BlockId;
            public PProtoEnumerator(Runtime2 runtime) {
                BlockStack = new PooledList<BlockIterator>(8);
            }
            public void AppendRoot(int rootBlockEvalId, Runtime2 runtime) {
                BlockStack.Add(new BlockIterator() {
                    BlockId = rootBlockEvalId,
                });
            }
            public bool MoveNext(Runtime2 runtime) {
                while (BlockStack.Count > 0) {
                    var top = BlockStack[BlockStack.Count - 1];
                    var topBlock = runtime.BlockEvaluations[top.BlockId];
                    BlockStack.RemoveAt(BlockStack.Count - 1);
                }
                return false;
            }
            public void Dispose() {
                BlockStack.Dispose();
            }
        }
        // Iterates evaluator blocks from within the runtime
        // and allows adding/removing the hierarchy of execution blocks
        public struct PEvalEnumerator : IDisposable {
            private int rootIterator;
            public int EvalId { get; private set; }
            public PEvalEnumerator(Runtime2 runtime) {
                EvalId = -1;
                rootIterator = -1;
            }
            public void Dispose() {
            }
            public void SetCurrent(Runtime2 runtime, int evalI) {
                EvalId = evalI;
            }

            public int GetNextBlock(Runtime2 runtime) {
                var evalId = EvalId;
                var blockId = runtime.BlockEvaluations[evalId].BlockId;
                if (blockId >= 0) {
                    var nextBlockId = runtime.Script.GetBlock(blockId).NextBlock;
                    if (nextBlockId != -1) return nextBlockId;
                }
                var roots = runtime.Script.GetRootBlocks();
                ++rootIterator;
                if (rootIterator < roots.Count) return roots[rootIterator];
                return -1;
            }
        }
    }
}
