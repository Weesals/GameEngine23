using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.CPS {
    public struct Operation {
        public struct OpMeta {
            public int OpPrecedence;
            public int StackConsume;
            public StackType StackReturn;
            public string Name;
            public OpMeta(int precedence, int consume, StackType ret, string name = "") {
                OpPrecedence = precedence;
                StackConsume = consume;
                StackReturn = ret;
                Name = name;
            }
        }
        public static OpMeta[] Meta = new OpMeta[] {
            // Save
            new(0, 1, StackType.None, "Store"), new(0, 1, StackType.None, "SaveObj"),
            // Load
            new(6, 0, StackType.AnyType, "Load"), new(6, 0, StackType.ObjectType, "LoadObj"),
            // Push To Array
            new(0, -1, StackType.AnyType, "PushToArray"), 
            // Compare Boolean
            new(0, 2, StackType.ByteType, "AND"), new(0, 2, StackType.ByteType, "OR"),
            // Compare Int
            new(1, 2, StackType.AnyType, "AEq"), new(1, 2, StackType.AnyType, "ANE"), new(1, 2, StackType.AnyType, "ALE"), new(1, 2, StackType.AnyType, "AGE"), new(1, 2, StackType.AnyType, "ALe"), new(1, 2, StackType.AnyType, "AGe"),
            // Arithmetic
            new(2, 2, StackType.AnyType, "AAdd"), new(1, 2, StackType.AnyType, "ASub"), new(3, 2, StackType.AnyType, "AMul"), new(3, 2, StackType.AnyType, "ADiv"), new(5, 2, StackType.AnyType, "APow"), new(4, 2, StackType.AnyType, "AMod"),
            // PushPop
            new(6, 0, StackType.ByteType, "CBool"), new(6, 0, StackType.IntType, "CInt"),new(6, 0, StackType.FloatType, "CFloat"),new(6, 0, StackType.ObjectType, "CObj"),new(6, 0, StackType.AnyType, "CVal"),new(6, 1, 0, "Pop"),
            // Other
            new(6, -1, -1, "Call"), new(6, -1, -1, "Await"), new(6, -1, -1, "Cancel"), new(6, -1, -1, "Stalled"),
            new(6, 1, StackType.ByteType, "Invert"),
            new(6, -1, -1, "Block"),
        };
        public enum Types {
            Store, StoreObject,
            Load, LoadObject,
            PushToArray,
            CompareAnd, CompareOr,
            CompareEqual, CompareNEqual,
            CompareLEqual, CompareGEqual, CompareLess, CompareGreater,
            Add, Subtract, Multiply, Divide, Power, Modulus, BoolInvert,
            PushBool, PushInt, PushFixed, PushObject, PushValue, Pop,
            InvokeSustained, InvokeSustainedValue, InvokeBlock,
            CallRoutine, AwaitRoutine, CancelRoutine, GetIsStalled,
        }
        public Types Type;
    }

    public class ScriptClass {
        public string Name;
        public ScriptClass(string name) { Name = name; }
        public override string ToString() { return Name; }
    }
    public class Script {
        public struct Dependency : IEquatable<Dependency> {
            public string Name;
            public bool Equals(Dependency other) { return Name == other.Name; }
            public override string ToString() { return Name; }
        }
        public struct Mutation {
            public string Name;
            public RangeInt ProgramCounter;
            public override string ToString() { return Name + ": " + ProgramCounter; }
        }
        public struct BlockOutput {
            public string Name;
            public int MutationId;
            public int StackOffset;
        }
        public struct Block {
            public RangeInt Dependencies;
            public RangeInt Mutations;
            public RangeInt Outputs;
            public int DocumentId;
            public int NextBlock;
            public string Name;
            public override string ToString() { return $"D{Dependencies} M{Mutations} Next={NextBlock}"; }
        }

        private SparseArray<Dependency> dependencies = new();
        private SparseArray<Mutation> mutations = new();
        private SparseArray<BlockOutput> blockOutputs = new();
        private SparseArray<Block> blocks = new();
        private SparseArray<byte> programData = new();
        private List<ScriptClass> classes = new();
        private List<int> rootBlocks = new();
        private List<object> terms = new();

        public int GetClassByName(ReadOnlySpan<char> name) {
            for (int i = 0; i < classes.Count; i++) {
                if (MemoryExtensions.Equals(classes[i].Name, name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }
        public int RequireClass(ReadOnlySpan<char> name) {
            int classI = GetClassByName(name);
            if (classI == -1) { classI = classes.Count; classes.Add(new ScriptClass(name.ToString())); }
            return classI;
        }
        public int CreateBlock(int documentId, Span<Dependency> blockDeps) {
            var blockDepsI = dependencies.Allocate(blockDeps.Length);
            for (int i = 0; i < blockDeps.Length; i++) dependencies[blockDepsI.Start + i] = blockDeps[i];
            return blocks.Add(new Block() {
                Dependencies = blockDepsI,
                DocumentId = documentId,
                NextBlock = -1,
            });
        }
        public Block GetBlock(int id) {
            return blocks[id];
        }
        public int RequireTerm(object value) {
            terms.Add(value);
            return terms.Count - 1;
        }
        public IReadOnlyList<object> GetTerms() { return terms; }
        public object GetTerm(int index) { return terms[index]; }
        public int FindMutationIndex(RangeInt mutationsI, Dependency dependency) {
            var mutations = this.mutations.Slice(mutationsI);
            for (int i = 0; i < mutations.Count; i++)
                if (mutations[i].Name == dependency.Name) return i;
            return -1;
        }
        public ArraySegment<byte> GetProgramBlock(RangeInt rangeI) {
            return programData.Slice(rangeI);
        }

        public ArraySegment<Dependency> GetDependencies(RangeInt items) {
            return dependencies.Slice(items);
        }
        public ArraySegment<Mutation> GetMutations(RangeInt items) {
            return mutations.Slice(items);
        }
        public ArraySegment<BlockOutput> GetBlockOutputs(RangeInt items) {
            return blockOutputs.Slice(items);
        }
        public RangeInt AppendProgram(Span<byte> newData) {
            var programI = programData.Allocate(newData.Length);
            for (int i = 0; i < programI.Length; i++) {
                programData[programI.Start + i] = newData[i];
            }
            return programI;
        }
        public void AppendMutations(int blockI, ArrayList<Mutation> newMutations, int programOffset) {
            var block = blocks[blockI];
            mutations.Reallocate(ref block.Mutations, block.Mutations.Length + newMutations.Count);
            for (int i = 0; i < newMutations.Count; i++) {
                var mut = newMutations[i];
                mut.ProgramCounter.Start += programOffset;
                mutations[block.Mutations.End - newMutations.Count + i] = mut;
            }
            blocks[blockI] = block;
        }
        public void AppendRootBlocks(Span<BlockWriter.OutputBlock> outputBlocks) {
            foreach (var blockI in outputBlocks) rootBlocks.Add(blockI.BlockId);
        }
        public IReadOnlyList<int> GetRootBlocks() { return rootBlocks; }

        public void LogContents() {
            for (var blockIt = blocks.GetEnumerator(); blockIt.MoveNext();) {
                var block = blockIt.Current;
                Console.WriteLine($"Block #{blockIt.Index} {block.Name}");
                var deps = "- Dependencies: ";
                var blockDeps = dependencies.Slice(block.Dependencies);
                for (int i = 0; i < blockDeps.Count; i++) deps += $"{blockDeps[i].Name},";
                Console.WriteLine(deps);
                var muts = "- Mutations: ";
                var blockMuts = mutations.Slice(block.Mutations);
                for (int i = 0; i < blockMuts.Count; i++) {
                    var context = new Runtime2.EvaluatorContext() {
                        ProgramData = GetProgramBlock(blockMuts[i].ProgramCounter),
                        ProgramCounter = 0,
                    };
                    var op = (Operation.Types)context.ReadProgramData<byte>();
                    switch (op) {
                        case Operation.Types.InvokeBlock: {
                            var classI = context.ReadProgramData<ushort>();
                            var blockI = context.ReadProgramData<ushort>();
                            muts += $"{blockMuts[i].Name}@{blockMuts[i].ProgramCounter} : {classI}:{blockI},";
                        }
                        break;
                        default: {
                            muts += $"{blockMuts[i].Name}@{blockMuts[i].ProgramCounter},";
                        }
                        break;
                    }
                }
                Console.WriteLine(muts);
                Console.WriteLine(block.NextBlock == -1 ? " => END" : $" => #{block.NextBlock}");
            }
            var rootOutput = "Root: ";
            foreach (var blockI in rootBlocks) rootOutput += $"#{blockI} ";
            Console.WriteLine(rootOutput);
        }
        public void LinkBlocks(int from, int to) {
            var block = blocks[from];
            block.NextBlock = to;
            blocks[from] = block;
        }

        public override string ToString() {
            return $"Blocks=#{blocks.PreciseCount} Data={programData.PreciseCount}b";
        }

        public void SetBlockName(int blockI, string name) {
            if (blockI == -1) return;
            var block = blocks[blockI];
            block.Name = name;
            blocks[blockI] = block;
        }
    }

}
