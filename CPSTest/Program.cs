using System.Text;
using Weesals.CPS;

namespace Weesals.CPSTest {
    public class Program {

        public class APIGetTeam : API, IInstructionCompiler, IInstructionInvoke2 {
            public CompileResult Compile(ref Parser code, ref CompileContext context) {
                if (code.Match("Team")) {
                    var apiIndex = context.Compiler.RequireTerm(this);
                    context.ExpressionWriter.programWriter.PushOp(Operation.Types.InvokeSustained);
                    context.ExpressionWriter.programWriter.PushConstantObject(apiIndex);
                    if (code.MatchFunction(".HasTech", out var parms)) {
                        var apiHasTech = context.Compiler.RequireTerm(HasTech.Default);
                        var techName = context.Compiler.RequireTerm(parms.TakeParameter().ToString());
                        context.ExpressionWriter.programWriter.PushOp(Operation.Types.InvokeSustained);
                        context.ExpressionWriter.programWriter.PushConstantObject(apiHasTech);
                        context.ExpressionWriter.programWriter.PushConstantObject(techName);
                        context.ExpressionWriter.programWriter.Pop();
                    }
                    return new CompileResult(1);
                }
                return default;
            }
            public void Invoke(Runtime2.EvaluatorContext context) {
                var dataStore = context.Evaluator.Runtime.GetDataStorage();
                for (int g = 0; g < context.Evaluator.EvalGroups.Count; g++) {
                    var group = context.Evaluator.EvalGroups[g];
                    group.Execution.PushStack(StackItem.CreateInt(dataStore, 0));
                }
            }
            public class HasTech : IInstructionInvoke2 {
                private StringBuilder builder = new();
                public void Invoke(Runtime2.EvaluatorContext context) {
                    var dataStore = context.Evaluator.Runtime.GetDataStorage();
                    var terms = context.Evaluator.Runtime.Script.GetTerms();
                    for (int g = 0; g < context.Evaluator.EvalGroups.Count; g++) {
                        var group = context.Evaluator.EvalGroups[g];
                        group.Execution.PopStack().TryGetAsInt(dataStore, out var team);
                        builder.Clear();
                        group.Execution.PopStack().TryGetAsString(dataStore, terms, builder);
                        group.Execution.PushStack(StackItem.CreateInt(dataStore, 0));
                    }
                }
                public static readonly HasTech Default = new();
            }
        }

        static void Main(string[] args) {
            var script = new Script();

            {
                var scripttxt = File.ReadAllText("testscript.txt");
                var compiler = new Compiler(script);
                compiler.AddInstruction(new ClassInstruction());
                compiler.AddInstruction(new PropertyInstruction());
                compiler.AddInstruction(new APIGetTeam());
                compiler.AddInstruction(new ControlInstructions());
                compiler.SetTokenReceiver(new CompileErrorDebugger());
                compiler.Parse(scripttxt);
            }

            script.LogContents();
            var villagerC = script.GetClassByName("Villager");

            var runtime2 = new Runtime2(script);
            var villagerI2 = runtime2.AllocateObject();
            runtime2.SetObjectPrototypeId(villagerI2, villagerC);
            runtime2.Resolve();
            var evalStackId = runtime2.GetObjectEvalStackId(villagerI2);
            var los = runtime2.GetEvaluationVariable(evalStackId, "LOSRange");
            if (los.TryGetAsFloat(runtime2.GetDataStorage(), out var value)) {
                Console.WriteLine($"Found LOS {value}");
            }

            /*var runtime = new Runtime(script);
            var root = runtime.CreateInstance(-1);
            var villagerI = runtime.CreateInstance(villagerC);
            runtime.Resolve();*/
        }


    }
}
