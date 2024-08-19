

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Weesals.CPS {
    public struct CompileResult {
        private bool isValid;
        public short Parameters;
        public bool IsValid => isValid;
        public CompileResult(int parameters = 0) { isValid = true; Parameters = (short)parameters; }
        public static CompileResult operator +(CompileResult v1, CompileResult v2) {
            return new CompileResult(v1.Parameters + v2.Parameters);
        }
        public static CompileResult operator +(CompileResult v1, int paramCount) {
            return new CompileResult(v1.Parameters + paramCount);
        }
        public static CompileResult operator ++(CompileResult v1) {
            return new CompileResult(v1.Parameters + 1);
        }
        public static implicit operator bool(CompileResult r) { return r.IsValid; }
        public override string ToString() { return Parameters.ToString(); }
        public static readonly CompileResult Valid = new CompileResult(0);
        public static readonly CompileResult Invalid = default;
    }
    public struct CompileContext {
        public Compiler Compiler;
        public ref BlockWriter BlockWriter => ref Compiler.blockWriter;
        public ref ExpressionWriter ExpressionWriter => ref Compiler.expressionWriter;
        public StackType ReturnType;
        public CompileContext(Compiler compiler) : this(compiler, StackType.None) { }
        public CompileContext(Compiler compiler, StackType returnType) {
            Compiler = compiler;
            ReturnType = returnType;
        }
        public CompileResult CompileExpression<T>(Parser code) { return CompileExpression<T>(ref code); }
        public CompileResult CompileExpression<T>(ref Parser code) {
            var result = Compiler.CompileExpression<T>(ref code);
            if (!result) return result;
            //if (!code.IsAtEnd && Compiler.TokenReceiver != null)
            //Compiler.TokenReceiver.Error(new Token(code, "Unexpected expression"));
            return result;
        }
        public void CompileBlock(ref Parser code) {
            var isBlock = code.Match('{');
            Compiler.CompileStatements(ref code);
            if (isBlock) code.Match('}');
        }
        public bool MatchFunction(ref Parser code, string name, out Parser parameters) {
            var match = code.MatchFunction(name, out parameters);
            /*if (match && code[-1] != ')' && Compiler.TokenReceiver != null) {
                Compiler.TokenReceiver.Error(new Token(code, "Expected ')' after function call"));
            }*/
            return match;
        }

        public void PushBlockScope() {
            BlockWriter.PushBlockScope();
        }

        public void PopBlockScope() {
            BlockWriter.PopBlockScope();
        }

        public void InvokeAPI(API api, CompileResult result) {
            ExpressionWriter.programWriter.PushOp(Operation.Types.InvokeSustained);
            var apiIndex = Compiler.RequireTerm(api);
            ExpressionWriter.programWriter.PushConstantObject(apiIndex);
        }

        //public int BeginChildBlock(object userData = null) { return Compiler.BeginChildBlock(userData); }
        //public int EndChildBlock() { return Compiler.EndChildBlock(); }
        //public object GetBlockUserData() { return Compiler.blockUserData[^1]; }

        /*public int CompileBlock(ref Parser code, object userData = null) {
            code.Match('{');
            int blockId = BeginChildBlock(userData);
            CompileStatements(ref code);
            EndChildBlock();
            code.Match('}');
            return blockId;
        }*/

    }

    public interface IInstructionCompiler {
        CompileResult Compile(ref Parser code, ref CompileContext context);
    }

    public struct ProgramWriter {
        private byte[] programData;
        private int programIndex;
        public int ProgramCounter => programIndex;
        public void Allocate() {
            programData = Array.Empty<byte>();
        }
        public void Dispose() {
        }
        private int AllocateProgram(int size) {
            if (programIndex + size > programData.Length) {
                int newSize = Math.Max(64, programData.Length * 2);
                while (newSize < programIndex + size) newSize *= 2;
                Array.Resize(ref programData, newSize);
            }
            var pc = programIndex;
            programIndex += size;
            return pc;
        }
        internal void WriteByte(int pc, byte type) {
            programData[pc] = (byte)type;
        }
        unsafe internal void WriteUShort(int pc, ushort v) {
            Unsafe.WriteUnaligned(ref programData[pc], v);
        }
        unsafe internal void WriteUInt(int pc, uint v) {
            Unsafe.WriteUnaligned(ref programData[pc], v);
        }
        public int PushOp(Operation.Types type) {
            int pc = AllocateProgram(1);
            WriteByte(pc, (byte)type);
            return pc;
        }
        internal int PushUShort(ushort v) {
            int pc = AllocateProgram(2);
            WriteUShort(pc, v);
            return pc;
        }
        internal int PushUInt(uint v) {
            int pc = AllocateProgram(4);
            WriteUInt(pc, v);
            return pc;
        }
        public void PushLoadVariable(int depId) {
            PushOp(Operation.Types.Load);
            PushUShort((ushort)depId);
        }
        public void Pop() {
            PushOp(Operation.Types.Pop);
        }
        public void PushConstantInt(int v) {
            PushOp(Operation.Types.PushInt);
            PushUInt((uint)v);
        }
        public void PushConstantFixed(int v) {
            PushOp(Operation.Types.PushFixed);
            PushUInt(Unsafe.As<int, uint>(ref v));
        }
        public void PushConstantObject(int index) {
            PushOp(Operation.Types.PushObject);
            PushUShort((ushort)index);
        }
        public void PushUnaryOp(Operation.Types type) {
            PushOp(type);
        }
        public void PushBinaryOp(Operation.Types type) {
            PushOp(type);
        }
        public void PushToArray(int count) {
            PushOp(Operation.Types.PushToArray);
            PushUShort((ushort)count);
        }
        public Span<byte> GetProgramData() {
            return programData.AsSpan().Slice(0, programIndex);
        }
        public void Clear() {
            programIndex = 0;
        }
    }

    public struct ExpressionWriter {
        public ProgramWriter programWriter;
        public int ProgramCounter => programWriter.ProgramCounter;

        public void Allocate() {
            programWriter.Allocate();
        }
        public void Dispose() {
            programWriter.Dispose();
        }
        public void Clear() {
            programWriter.Clear();
        }
    }

    public struct BlockWriter : IDisposable {
        public Script Script;

        public struct OutputBlock {
            public int BlockId;
            public override string ToString() { return BlockId.ToString(); }
        }
        public struct ActiveBlock {
            public int BlockId;
            public override string ToString() { return BlockId.ToString(); }
        }
        public struct BlockStackItem {
            public int ActiveBlockOffset;
        }
        private ArrayList<Script.Mutation> mutations;
        private ArrayList<Script.BlockOutput> blockOutputs;
        private ArrayList<Script.Dependency> dependencies;
        private ArrayList<int> dependencyStack;

        private ArrayList<OutputBlock> outputBlocks;
        private ArrayList<int> outputBlockStack;

        private ArrayList<ActiveBlock> activeBlocks;
        private ArrayList<BlockStackItem> blockStack;

        public void Allocate(Script script) {
            Script = script;
            mutations = new();
            dependencies = new();
            outputBlocks = new();
            activeBlocks = new();
            blockStack = new();
            dependencyStack = new();
            outputBlockStack = new();
        }
        public void Dispose() { }

        public int GetDependencyI(ReadOnlySpan<char> name) {
            for (int i = 0; i < dependencies.Count; i++) {
                var dp = dependencies[i];
                if (MemoryExtensions.Equals(dp.Name, name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }
        public int AddDependency(ReadOnlySpan<char> name) {
            dependencies.Add(new Script.Dependency() { Name = name.ToString(), });
            return dependencies.Count - 1;
        }
        public int RequireDependencyI(ReadOnlySpan<char> name) {
            var depI = GetDependencyI(name);
            if (depI == -1) depI = AddDependency(name);
            return depI;
        }
        public Span<Script.Dependency> GetDependencies() {
            return dependencies;
        }

        public void PushDependencyStack() {
            dependencyStack.Add(dependencies.Count);
        }
        public int PopDependencyStack() {
            var count = PruneFrom(dependencies, dependencyStack[^1]);
            dependencyStack.RemoveAt(dependencyStack.Count - 1);
            return count;
        }
        public void PushOutputBlockStack() {
            outputBlockStack.Add(outputBlocks.Count);
        }
        public Span<OutputBlock> GetOutputBlocks() {
            var from = outputBlockStack.Count > 0 ? outputBlockStack[^1] : 0;
            return outputBlocks.AsSpan(from, outputBlocks.Count - from);
        }
        public void PopOutputBlockStack() {
            PruneFrom(outputBlocks, outputBlockStack[^1]);
            outputBlockStack.RemoveAt(outputBlockStack.Count - 1);
        }

        public void PushBlockScope() {
            PushDependencyStack();
            PushOutputBlockStack();
            blockStack.Add(new BlockStackItem() {
                ActiveBlockOffset = activeBlocks.Count,
            });
        }
        public RangeInt FlushExpression(ref ExpressionWriter expression) {
            //var blockI = RequireBlock(dependencies);
            var programI = Script.AppendProgram(expression.programWriter.GetProgramData());
            //Script.AppendMutations(blockI, mutations, programI.start);
            //mutations.Clear();
            //PruneFrom(dependencies, dependencyStack.Count > 0 ? dependencyStack[^1] : 0);
            expression.Clear();
            return programI;
        }
        public void PopBlockScope() {
            var scope = blockStack[^1];
            blockStack.RemoveAt(blockStack.Count - 1);
            PruneFrom(activeBlocks, scope.ActiveBlockOffset);
            PopDependencyStack();
            PopOutputBlockStack();
        }

        private int RequireBlock(int documentId, Span<Script.Dependency> dependencies) {
            var firstBlock = blockStack.Count > 0 ? blockStack[^1].ActiveBlockOffset : 0;
            for (int b = firstBlock; b < activeBlocks.Count; b++) {
                var oblock = Script.GetBlock(activeBlocks[b].BlockId);
                var odeps = Script.GetDependencies(oblock.Dependencies);
                if (odeps.Count != dependencies.Length) continue;
                bool match = true;
                for (int i = 0; i < odeps.Count; i++) {
                    if (!odeps[i].Equals(dependencies[i])) { match = false; break; }
                }
                if (!match) continue;
                return activeBlocks[b].BlockId;
            }
            var blockI = Script.CreateBlock(documentId, dependencies);
            activeBlocks.Add(new ActiveBlock() { BlockId = blockI });
            outputBlocks.Add(new OutputBlock() { BlockId = blockI, });
            return blockI;
        }
        public bool HasOutputBlocks => (outputBlockStack.Count > 0 ? outputBlockStack[^1] : 0) < outputBlocks.Count;
        public int ReconcileToSingleBlock() {
            var from = outputBlockStack.Count > 0 ? outputBlockStack[^1] : 0;
            for (int i = from + 1; i < outputBlocks.Count; i++) {
                var prev = outputBlocks[i - 1];
                var cur = outputBlocks[i];
                Script.LinkBlocks(prev.BlockId, cur.BlockId);
            }
            if (from < outputBlocks.Count) return outputBlocks[from].BlockId;
            return -1;
        }

        private int PruneFrom<T>(ArrayList<T> items, int offset) {
            int count = items.Count - offset;
            if (offset < items.Count) items.RemoveRange(offset, items.Count - offset);
            return count;
        }

        private int FlushMutations(RangeInt programI) {
            // TODO: Should this only include deps starting from depstack[^1]?
            var blockI = RequireBlock(programI.Start, dependencies);
            Script.AppendMutations(blockI, mutations, programI.Start);
            mutations.Clear();
            PruneFrom(dependencies, dependencyStack.Count > 0 ? dependencyStack[^1] : 0);
            return blockI;
        }
        public void WriteStore(Parser propName, RangeInt pc) {
            mutations.Add(new Script.Mutation() {
                Name = propName.AsString(),
                ProgramCounter = pc,
            });
            FlushMutations(default);
        }
        public void WriteInvoke(RangeInt pc) {
            mutations.Add(new Script.Mutation() {
                Name = "$Invoke",
                ProgramCounter = pc,
            });
            FlushMutations(default);
            activeBlocks.Clear();
        }
    }

    public class Instruction {
        public int ParameterCount;
        public int ReturnCount;
    }

    public struct Token {
        public Parser Code;
        public string Error;
        public bool IsValid => Error != null;
        public Token(Parser parser, string error) {
            Code = parser;
            Error = error;
        }
        public override string ToString() {
            return Error;
        }
    }
    public interface ITokenReceiver {
        void Error(Token token);
    }
    public class CompileErrorDebugger : ITokenReceiver {
        public void Error(Token error) {
            var code = error.Code;
            if (code.Length > 20) code.End = code.I + 40;
            var line = error.Code.String.Substring(0, error.Code.I).AsSpan().Count('\n') + 1;
            Console.Error.WriteLine($"{error.Error} @{error.Code.I} L{line}" +
                (code.IsValid ? " \"" + code.ToString().Replace("\r", "").Replace("\n", "") + "\"" : ""));
        }
    }

    public class Compiler : IDisposable {

        public Script Script { get; private set; }
        public ITokenReceiver TokenReceiver { get; private set; }
        internal List<IInstructionCompiler> instructionCompilers = new();
        public ExpressionWriter expressionWriter;
        public BlockWriter blockWriter;

        public Compiler(Script script) {
            Script = script;
            expressionWriter.Allocate();
            blockWriter.Allocate(Script);
        }
        public void Dispose() {
            expressionWriter.Dispose();
            blockWriter.Dispose();
        }

        public void SetTokenReceiver(ITokenReceiver receiver) {
            TokenReceiver = receiver;
        }
        public void LogError(Token token) {
            if (TokenReceiver != null) TokenReceiver.Error(token);
        }

        public void AddInstruction(IInstructionCompiler compiler) {
            instructionCompilers.Add(compiler);
        }
        public void RemoveInstructions(IInstructionCompiler compiler) {
            instructionCompilers.Remove(compiler);
        }
        public int RequireTerm(object value) {
            return Script.RequireTerm(value);
        }

        public void Parse(string code) {
            var parser = new Parser(code);
            blockWriter.PushBlockScope();
            CompileStatements(ref parser);
            Script.AppendRootBlocks(blockWriter.GetOutputBlocks());
            blockWriter.PopBlockScope();
        }
        public void CompileStatements(ref Parser code) {
            while (!code.IsAtEnd) {
                Parser.SkipComments(ref code);
                if (code.PeekMatch('}')) break;
                if (code.IsAtEnd) break;
                int i = code.I;
                var result = CompileStatement(ref code);
                if (code.I <= i) {
                    if (TokenReceiver != null) TokenReceiver.Error(new Token(code, "Failed to progress parse"));
                    break;
                }
                if (!result.IsValid) {
                    if (TokenReceiver != null) TokenReceiver.Error(new Token(code, "Failed to compile instruction"));
                    break;
                }
            }
        }
        public CompileResult CompileStatement(ref Parser instance) {
            var result = CompileAPICall(ref instance, StackType.None);
            if (result) {
                // Each statement should end with this
                instance.Match(';');
                return result;
            }
            return CompileResult.Invalid;
        }
        public CompileResult CompileExpression<T>(ref Parser instance) {
            CompileResult result = default;
            using var expressionCompiler = new ExpressionCompiler(this);
            while (true) {
                Parser.SkipComments(ref instance);
                if (instance.Match('(')) {
                    expressionCompiler.BeginBrackets();
                    continue;
                }
                if (instance.Match('!')) {
                    expressionCompiler.PushUnary(Operation.Types.BoolInvert);
                    continue;
                }
                if (!instance.IsAtEnd) {
                    var tresult = expressionCompiler.CompileTerm(ref instance, StackType.None);
                    if (!tresult.IsValid) {
                        if (TokenReceiver != null) TokenReceiver.Error(new Token(instance, "Failed to parse term"));
                        break;
                    }
                    result += tresult;
                    Parser.SkipComments(ref instance);
                    if (instance.Match(')')) {
                        expressionCompiler.EndBrackets(ref result);
                    }
                }
                if (instance.IsAtEnd || instance.PeekMatch(';')) {
                    expressionCompiler.Flush(result: ref result);
                    break;
                }
                if (!expressionCompiler.ParseOperation(ref instance, ref result)) {
                    if (TokenReceiver != null) TokenReceiver.Error(new Token(instance, "Failed to find operator"));
                    break;
                }
            }
            return result;
        }
        public struct ExpressionCompiler : IDisposable {
            private Compiler compiler;
            private PooledList<Operation.Types> ops;
            public ExpressionCompiler(Compiler compiler) {
                this.compiler = compiler;
                ops = new PooledList<Operation.Types>(8);
            }
            public void Dispose() {
                Debug.Assert(ops.Count == 0);
                ops.Dispose();
            }
            public void BeginBrackets() {
                ops.Add((Operation.Types)(-1));
            }
            public void EndBrackets(ref CompileResult result) {
                Flush(ref result);
                Debug.Assert((int)ops[^1] == -1, "Unmatched parentheses");
                ops.RemoveAt(ops.Count - 1);
            }
            public void PushUnary(Operation.Types type) {
                ops.Add(type);
            }
            public bool ParseOperation(ref Parser instance, ref CompileResult result) {
                var op = (Operation.Types)(-1);
                if (instance.Match("&&")) op = Operation.Types.CompareAnd;
                else if (instance.Match("||")) op = Operation.Types.CompareOr;
                else if (instance.Match("==")) op = Operation.Types.CompareEqual;
                else if (instance.Match("!=")) op = Operation.Types.CompareNEqual;
                else if (instance.Match("<=")) op = Operation.Types.CompareLEqual;
                else if (instance.Match(">=")) op = Operation.Types.CompareGEqual;
                else if (instance.Match("<")) op = Operation.Types.CompareLess;
                else if (instance.Match(">")) op = Operation.Types.CompareGreater;
                else if (instance.Match("+")) op = Operation.Types.Add;
                else if (instance.Match("-")) op = Operation.Types.Subtract;
                else if (instance.Match("*")) op = Operation.Types.Multiply;
                else if (instance.Match("/")) op = Operation.Types.Divide;
                if (op < 0) return false;
                FlushOps(ref ops, 0, Operation.Meta[(int)op].OpPrecedence, ref result);
                ops.Add(op);
                return true;
            }
            public void Flush(ref CompileResult result) {
                FlushOps(ref ops, 0, 0, ref result);
            }
            public void FlushOps(ref PooledList<Operation.Types> ops, int fromI, int precedence, ref CompileResult result) {
                while (ops.Count > fromI) {
                    var top = ops[ops.Count - 1];
                    if ((int)top == -1) break;
                    var topmeta = Operation.Meta[(int)top];
                    if (topmeta.OpPrecedence < precedence) break;
                    compiler.expressionWriter.programWriter.PushBinaryOp(top);
                    ops.RemoveAt(ops.Count - 1);
                    Debug.Assert(topmeta.StackConsume >= 0, "Invalid consume count");
                    result.Parameters += (short)((topmeta.StackReturn.IsNone ? 0 : 1) - topmeta.StackConsume);
                }
            }

            public CompileResult CompileTerm(ref Parser instance, StackType returnType) {
                CompileResult result = CompileResult.Invalid;
                if ((result = CompileConstant(ref instance)).IsValid) {
                } else if ((result = CompileArray(ref instance)).IsValid) {
                } else if ((result = CompileObject(ref instance)).IsValid) {
                } else if ((result = compiler.CompileAPICall(ref instance, returnType)).IsValid) {
                } else if ((result = CompileVariable(ref instance)).IsValid) {
                }
                return result;
            }
            private StringBuilder builder = new();
            public CompileResult CompileConstant(ref Parser instance) {
                ref var programWriter = ref compiler.expressionWriter.programWriter;
                var n = instance.TakeNumber();
                if (n.IsValid) {
                    if (n.IsInteger) {
                        programWriter.PushConstantInt(n.ReadInteger());
                        return new CompileResult(1);
                    } else if (n.IsFloat) {
                        programWriter.PushConstantFixed((int)(n.ReadFloat() * (1 << 16)));
                        return new CompileResult(1);
                    } else throw new NotImplementedException();
                } else if (instance.PeekMatch('"')) {
                    var strIt = new Parser.StringIterator(instance.String, instance.I, instance.End);
                    builder.Clear();
                    while (strIt.MoveNext()) builder.Append(strIt.Current);
                    instance.I = strIt.Index;
                    if (instance.I < instance.End && instance.String[instance.I] == '"') ++instance.I;
                    else if (compiler.TokenReceiver != null) compiler.TokenReceiver.Error(new Token(instance, "Expected '\"' after string token."));
                    var termId = compiler.RequireTerm(builder.ToString());
                    programWriter.PushConstantObject(termId);
                    return new CompileResult(1);
                } else if (instance.Match("true")) {
                    programWriter.PushConstantInt(1);
                    return new CompileResult(1);
                } else if (instance.Match("false")) {
                    programWriter.PushConstantInt(0);
                    return new CompileResult(1);
                } else if (instance.Match("null")) {
                    programWriter.PushConstantObject(-1);
                    return new CompileResult(1);
                }
                return default;
            }
            private CompileResult CompileArray(ref Parser code) {
                if (code.Match('[')) {
                    ref var programWriter = ref compiler.expressionWriter.programWriter;
                    CompileResult result = default;
                    while (true) {
                        var param = code.TakeParameter();
                        if (!param.IsValid) break;
                        result += compiler.CompileExpression<StackType>(ref param.Value);
                    }
                    code.Match(']');
                    programWriter.PushToArray(result.Parameters);
                    result = new CompileResult(1);
                    return result;
                }
                return default;
            }
            private CompileResult CompileObject(ref Parser code) {
                if (!code.Match('{')) return default;
                compiler.CompileStatements(ref code);
                code.Match('}');
                return CompileResult.Valid;
            }
            private CompileResult CompileVariable(ref Parser instance) {
                var ninst = instance;
                var name = ninst.MatchWord().AsSpan();
                var variableId = compiler.blockWriter.RequireDependencyI(name);
                if (variableId == -1) return default;
                ref var programWriter = ref compiler.expressionWriter.programWriter;
                programWriter.PushLoadVariable(variableId);
                instance = ninst;
                return new CompileResult(1);
            }
        }
        private CompileResult CompileAPICall(ref Parser instance, StackType returnType) {
            var context = new CompileContext(this, returnType);
            foreach (var item in instructionCompilers) {
                var result = item.Compile(ref instance, ref context);
                if (result.IsValid) return result;
            }
            return CompileResult.Invalid;
        }

    }
}
