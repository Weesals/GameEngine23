using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.CPS {
    public class ControlInstructions : IInstructionCompiler {

        public CompileResult Compile(ref Parser code, ref CompileContext context) {
            Parser items;
            if (code.MatchFunction("if", out items)) {
                var result = context.CompileExpression<bool>(items);
                if (!result.IsValid) context.Compiler.LogError(new Token(items, "Expected boolean expression"));
                context.InvokeAPI(Singleton<IfInstruction>.Instance, result);
                var blockIPC = context.ExpressionWriter.programWriter.PushUInt(0);
                var expression = context.BlockWriter.FlushExpression(ref context.ExpressionWriter);
                blockIPC += expression.Start;
                context.BlockWriter.WriteInvoke(expression);
                context.BlockWriter.PushBlockScope();
                context.CompileBlock(ref code);
                var blockI = context.BlockWriter.ReconcileToSingleBlock();
                Unsafe.WriteUnaligned(ref context.BlockWriter.Script.GetProgramBlock(new RangeInt(blockIPC, 4)).AsSpan()[0], (uint)blockI);
                //context.ExpressionWriter.programWriter.WriteUInt(blockIPC, (uint)blockI);
                context.BlockWriter.PopBlockScope();
                return CompileResult.Valid;
            }
            return default;
        }

        public static class Singleton<T> where T : class, new() { public static T Instance = new(); }

        public class IfInstruction : API, IInstructionInvoke {
            public void Invoke(ref EvaluatorContext context) {
                var dataStore = context.Evaluator.Runtime.GetDataStorage();
                var nextBlockId = context.ReadProgramData<uint>();
                for (int g = 0; g < context.Evaluator.EvalGroups.Count; g++) {
                    ref var group = ref context.Evaluator.EvalGroups[g];
                    group.Execution.PopStack().TryGetAsInt(dataStore, out var condition);
                    if (condition != 0) {
                        group.NextBlockId = (int)nextBlockId;
                    }
                }
            }
        }
    }
    /*
     * Eventual structure:
     * E = Entity, GE = Graph Evaluation, BE: BlockEvaluation
     * E1: GE1
     * E2: GE1
     * E3: EG2
     * 
     * IfInstruction:
     * ->BE1 [E1, E2], BE2 [E3]
     * E2 flips to BE2
     * ->BE1 [E1] BE2 [E2, E3]
     * 
     * Need a graphi structure; decision points
     * Evaluate APIs as normal
     * With results, walk decision tree
     * Where an entity makes a different decision:
     *   Call EndInvoke on the old branch
     *   Call BeginInvoke on the new branch
     * How do I know how far the branch goes?
     *   Always just child branches?
     *   API can inform when branches activate/deactivate
     *   
     * If statement:
     *   Discriminates only on input parameters
     * Team statement:
     *   Discriminates only on entities
     * HasUpgrade
     *   Discriminates on both parameters and "instances"
     *   
     * Graph:
     *   int ObjectId = CreateObject("class_name")
     *   
     * APIs:
     *   void Invoke(
     *      mode: [Begin, Step, End],
     *      objects[]: ObjectId,
     *      inBlocks[]: { parameters: [keyvalue] }
     *      outBlocks[]: { value: any, branch }
     *   )
     *   
     *   IfStatement:
     *     Begin(context) {
     *       trueStatements
     *     }
     * APIs should determine block mutations
     *   Entity [1, 6, 7] move from B1, to B2
     * APIs should not control flow; is up to CJMP instruction
     * CJMP will pull bool from stack top, push specified block id if true
     * All entities can look up their current block id from their ledger
     *   If the block ID does not match, apply the differences
     *   
     * Block Invocation:
     *   Entities are grouped by blocks (based on current block ids in entity ledgers)
     *   Code/APIs are invoked as normal
     *   Code/APIs can fork as desired
     *   After all code runs:
     *   State is hashed, forked blocks are joined where appropriate
     *   Final block is determined for each entity
     *   Entity ledger is updated (and block changes propagated; ie NextBlockId)
     *   
     */
}
