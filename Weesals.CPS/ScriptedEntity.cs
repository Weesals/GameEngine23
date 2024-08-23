using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.CPS {
    public interface IService {
    }

    public class ScriptedEntity {

        public string Name;

    }

    public struct BlockInvocation : IComparable<BlockInvocation> {
        public int BlockId;
        public int OrderId;

        public int CompareTo(BlockInvocation other) {
            return OrderId - other.OrderId;
        }
    }
    public class ClassBlocks : IComparable<ClassBlocks> {
        public int ClassI;
        public SortedList<int, BlockInvocation> BlockInvocations = new();

        public int CompareTo(ClassBlocks? other) {
            return ClassI - (other?.ClassI ?? 0);
        }

        public void RegisterBlock(int blockI) {
            BlockInvocations.Add(blockI, new BlockInvocation() {
                OrderId = 0,
                BlockId = blockI,
            });
        }
    }
    public class ScriptedRegistry : IService {
        public SortedList<int, ClassBlocks> ClassBlocks = new();
        public void RegisterBlock(int classI, int blockI) {
            if (ClassBlocks.TryGetValue(classI, out var classBlock)) {
                classBlock = new() { ClassI = classI, };
                ClassBlocks.Add(classI, classBlock);
            }
            classBlock!.RegisterBlock(blockI);
        }
    }

    public class ClassInstruction : API, IInstructionCompiler, IInstructionInvoke {
        public class ClassParameters {
            public string Name;
            public string[] Extends;
            //public string[] InputParameters;
            //public string[] OutputParameters;
            public int RootBlock;
        }
        public CompileResult Compile(ref Parser code, ref CompileContext context) {
            var type = code.Match("class") ? 0
                : code.Match("entity") ? 1
                : code.Match("tech") ? 2
                : -1;
            if (type >= 0) {
                // Create class instance
                var classParams = new ClassParameters();
                classParams.Name = code.MatchWord().TakeString();
                var extends = new PooledList<string>(8);
                if (code.Match("extends")) {
                    while (true) {
                        extends.Add(code.MatchWord().TakeString());
                        if (!code.Match(',')) break;
                    }
                }
                classParams.Extends = extends.ToArray();
                extends.Dispose();

                var classI = context.Compiler.Script.RequireClass(classParams.Name);

                context.PushBlockScope();
                context.CompileBlock(ref code);
                var blockI = context.BlockWriter.ReconcileToSingleBlock();
                context.PopBlockScope();

                if (blockI != -1) {
                    context.BlockWriter.Script.SetBlockName(blockI, $"Class {classParams.Name}");

                    context.BlockWriter.PushDependencyStack();
                    context.ExpressionWriter.programWriter.PushOp(Operation.Types.InvokeBlock);
                    context.ExpressionWriter.programWriter.PushUShort((ushort)classI);
                    context.ExpressionWriter.programWriter.PushUShort((ushort)blockI);
                    var expression = context.BlockWriter.FlushExpression(ref context.ExpressionWriter);
                    context.BlockWriter.WriteInvoke(expression);
                    var count = context.BlockWriter.PopDependencyStack();
                    Debug.Assert(count == 0, "Is depenency block required? There are no deps here");
                }

                return CompileResult.Valid;
            }
            return default;
        }
        public void Invoke(ref EvaluatorContext context) {
            var classI = context.ReadProgramData<ushort>();
            var blockI = context.ReadProgramData<ushort>();
            var registry = context.GetService<ScriptedRegistry>();
            //if (context.Evaluator.EvalGroups[0].Outputs)
            registry?.RegisterBlock(classI, blockI);
        }
    }

    public class PropertyInstruction : IInstructionCompiler {
        private static string[] Operators = new[] { "=", "+=", "-=", "*=", "/=", ":=" };
        public CompileResult Compile(ref Parser code, ref CompileContext context) {
            var mcode = code;
            var isFilter = mcode.Match(':');
            var propName = mcode.MatchWord();
            int op = Operators.Length - 1;
            for (; op >= 0; op--) if (mcode.Match(Operators[op])) break;
            if (op >= 0) {
                code = mcode;
                context.BlockWriter.PushDependencyStack();
                if (op != 0) {
                    context.BlockWriter.RequireDependencyI(propName);
                    context.BlockWriter.PushDependencyStack();
                }
                var result = context.CompileExpression<StackType>(ref code);
                code.Match(';');
                var pc = context.BlockWriter.FlushExpression(ref context.ExpressionWriter);
                if (op != 0) {
                    context.BlockWriter.PopDependencyStack();
                    // TODO: Mutation?
                }
                context.BlockWriter.WriteStore(propName, pc);
                context.BlockWriter.PopDependencyStack();
                return result;
            }
            if (mcode.PeekMatch('{')) {
                code = mcode;
                /*var varId = context.Writer.FindVariable(propName);
                context.Writer.PushLoad(StackType.IntType, 0);
                //writer.LoadVariableValue(varId);
                var result = context.Writer.PushInstruction(MutatePropertyWithBlock.Instance, CompileResult.Valid);
                var data = new IfInstruction.Datablock() { InnerBlock = -1, ElseBlock = -1 };
                var dataI = context.Writer.WriteDataBlock(data);
                var classPropsCompiler = new PropertyInstruction();
                context.Compiler.AddInstruction(classPropsCompiler);
                data.InnerBlock = context.CompileBlock(ref code);
                context.Compiler.RemoveInstructions(classPropsCompiler);
                context.Writer.OverwriteDataBlock(dataI, data);
                //writer.OverwriteVariableValue(varId);
                return result;*/
                context.PushBlockScope();
                context.CompileBlock(ref code);
                context.PopBlockScope();
                return CompileResult.Valid;
            }
            return default;
        }
    }

}
