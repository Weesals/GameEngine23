using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    public interface IInstructionInvoke {
        void Invoke(ref EvaluatorContext context);
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

    // Iterates evaluator blocks from within the runtime
    // and allows adding/removing the hierarchy of execution blocks
    public struct PEvalEnumerator : IDisposable {
        private int rootIterator;
        public int EvalId { get; private set; }
        public PEvalEnumerator(Runtime runtime) {
            EvalId = -1;
            rootIterator = -1;
        }
        public void Dispose() {
        }
        public void SetCurrent(Runtime runtime, int evalI) {
            EvalId = evalI;
        }

        public int GetNextBlock(Runtime runtime) {
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

        public bool GetIsNextBlockChild(Runtime runtime) {
            if (NextBlockId != -1) return true;
            var evalId = EvalId;
            var blockId = runtime.BlockEvaluations[evalId].BlockId;
            if (blockId == -1) return true;
            return false;
        }
        public int GetNextBlock(Runtime runtime) {
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
        public Runtime Runtime;
        public ref PooledList<EvaluationGroup> EvalGroups;
        public ref PooledList<int> RelevantIds;
        public Evaluator(Runtime runtime, ref PooledList<EvaluationGroup> evalGroups, ref PooledList<int> relevantIds) {
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
            return Runtime.GetVariables(parameters);
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

    public class Evaluation {
        private StringBuilder builder1 = new(), builder2 = new();

        public void Evaluate(Script.Block block, Evaluator evaluator) {
            var runtime = evaluator.Runtime;
            var script = runtime.Script;
            var dataStore = runtime.DataStorage;
            var varStore = runtime.VariableStorage;
            var mutations = script.GetMutations(block.Mutations);
            for (var m = 0; m < mutations.Count; ++m) {
                var mutation = mutations[m];
                // Evaluate the mutation
                if (!Execute(mutation.ProgramCounter, evaluator)) {
                    Console.WriteLine("Failed to evaluate block " + block.Name);
                }
                foreach (var groupId in evaluator.RelevantIds) {
                    var group = evaluator.EvalGroups[groupId];
                    // Grab the top item on the stack as the output value
                    if (mutation.OutputId >= 0) {
                        var blockOutput = script.GetBlockOutputs(block.Outputs)[mutation.OutputId];
                        if (group.Execution.Stack.Count > 0) {
                            ref var output = ref varStore[group.Outputs.Start + mutation.OutputId];
                            Debug.Assert(!output.HasData);
                            output = group.Execution.PopStack();
                        } else {
                            Console.WriteLine($"Mutation '{mutation.Name}' didnt output anything");
                        }
                    }
                    // Remove any extra items on the stack (TODO: Shouldnt be required?)
                    while (group.Execution.Stack.Count > 0) {
                        group.Execution.PopStack().Delete(dataStore);
                    }
                }
            }
        }

        public bool Execute(RangeInt programCounter, Evaluator evaluator) {
            var runtime = evaluator.Runtime;
            var script = runtime.Script;
            var dataStore = runtime.DataStorage;
            var varStore = runtime.VariableStorage;
            EvaluatorContext context = new EvaluatorContext() {
                Evaluator = evaluator,
                ProgramData = script.GetProgramBlock(programCounter),
                ProgramCounter = 0,
            };
            while (context.ProgramCounter < programCounter.Length) {
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
                        for (int i = 0; i < evaluator.RelevantIds.Count; ++i) {
                            ref var group = ref evaluator.EvalGroups[evaluator.RelevantIds[i]];
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
                                var terms = script.GetTerms();
                                if (param0.TryGetAsString(dataStore, terms, builder1) && param0.TryGetAsString(dataStore, terms, builder2)) {
                                    var value = "";
                                    switch (op) {
                                        case Operation.Types.Add: value = builder1.Append(builder2).ToString(); break;
                                        default: throw new NotImplementedException();
                                    }
                                    var termId = script.RequireTerm(value);
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
                        for (int i = 0; i < evaluator.RelevantIds.Count; ++i) {
                            ref var group = ref evaluator.EvalGroups[evaluator.RelevantIds[i]];
                            var value = varStore[group.Parameters.Start + paramI].Clone(dataStore, dataStore);
                            group.Execution.Stack.Add(value);
                        }
                    }
                    break;
                    case Operation.Types.Store: {
                        var outputI = context.ReadProgramData<ushort>();
                        for (int i = 0; i < evaluator.RelevantIds.Count; ++i) {
                            ref var group = ref evaluator.EvalGroups[evaluator.RelevantIds[i]];
                            var value = group.Execution.Stack[^1];
                            group.Execution.Stack.RemoveAt(group.Execution.Stack.Count - 1);
                            Debug.Assert(!varStore[group.Outputs.Start + outputI].IsValid);
                            varStore[group.Outputs.Start + outputI] = value;
                        }
                    }
                    break;
                    case Operation.Types.InvokeBlock: {
                        var classI = context.ReadProgramData<ushort>();
                        var blockI = context.ReadProgramData<ushort>();
                        for (int g = 0; g < evaluator.EvalGroups.Count; ++g) {
                            ref var group = ref evaluator.EvalGroups[g];
                            Debug.Assert(group.NextBlockId == -1, "Next block already set!");
                            group.NextBlockId = blockI;
                            evaluator.EvalGroups[g] = group;
                        }
                    }
                    break;
                    case Operation.Types.InvokeSustained: {
                        var instrObjId = context.ReadProgramData<ushort>();
                        var instruction = (IInstructionInvoke)script.GetTerm(instrObjId);
                        instruction.Invoke(ref context);
                    }
                    break;
                    default: {
                        Console.WriteLine($"Unsupported op {op}");
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
