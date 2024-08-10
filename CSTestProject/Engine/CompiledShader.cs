using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    unsafe public class Shader {
        public readonly string Path, Entry;
        public Shader(string path, string entry) {
            Path = path;
            Entry = entry;
        }

        public static Shader FromPath(string path, string entry) {
            return new Shader(path, entry);
        }
        public override int GetHashCode() { var h = (Path + Entry).ComputeStringHash(); return (int)h ^ (int)(h >> 32); }
        public override string ToString() { return $"{Path}:{Entry}"; }
    }

    public struct ShaderReflection {
        public enum ResourceTypes : byte { R_Texture, R_SBuffer, };
        public struct UniformValue {
            public CSIdentifier Name;
            public CSIdentifier Type;
            public int Offset, Size;
            public byte Rows, Columns;
            public ushort Flags;
            public override string ToString() { return $"{Name} @{Offset}"; }
            public override int GetHashCode() { return (Name.mId * 53) + Offset * 1237; }
        }
        public partial struct ConstantBuffer {
            public CSIdentifier Name;
            public int Size;
            public int BindPoint;
            public UniformValue[] Values;
            public override string ToString() { return $"{Name} +{Size}"; }
        }
        public partial struct ResourceBinding {
            public CSIdentifier Name;
            public int BindPoint;
            public int Stride;
            public ResourceTypes Type;
            public override string ToString() { return $"{Name} @{BindPoint}"; }
        }
        public partial struct InputParameter {
            public enum Types : byte { P_Unknown, P_UInt, P_SInt, P_Float, };
            public CSIdentifier Name;
            public CSIdentifier Semantic;
            public int SemanticIndex;
            public int Register;
            public byte Mask;
            public Types Type;
        }

        public ConstantBuffer[] ConstantBuffers;
        public ResourceBinding[] ResourceBindings;
        public InputParameter[] InputParameters;

        public void Serialize(BinaryWriter writer) {
            int cbcount = ConstantBuffers?.Length ?? 0;
            int rbcount = ResourceBindings?.Length ?? 0;
            int ipcount = InputParameters?.Length ?? 0;
            writer.Write(cbcount);
            for (int i = 0; i < cbcount; i++) {
                var cbuffer = ConstantBuffers![i];
                writer.Write(cbuffer.Name.ToString());
                writer.Write(cbuffer.Size);
                writer.Write(cbuffer.BindPoint);
                var values = cbuffer.Values;
                int vcount = values?.Length ?? 0;
                writer.Write(vcount);
                for (int v = 0; v < vcount; v++) {
                    var value = values![v];
                    writer.Write(value.Name.ToString());
                    writer.Write(value.Type.ToString());
                    writer.Write(value.Offset);
                    writer.Write(value.Size);
                    writer.Write(value.Rows);
                    writer.Write(value.Columns);
                    writer.Write(value.Flags);
                }
            }
            writer.Write(rbcount);
            for (int i = 0; i < rbcount; i++) {
                var rbinding = ResourceBindings![i];
                writer.Write(rbinding.Name.ToString());
                writer.Write(rbinding.BindPoint);
                writer.Write(rbinding.Stride);
                writer.Write((byte)rbinding.Type);
            }
            writer.Write(ipcount);
            for (int i = 0; i < ipcount; i++) {
                var param = InputParameters![i];
                writer.Write(param.Name.ToString());
                writer.Write(param.Semantic.ToString());
                writer.Write((int)param.SemanticIndex);
                writer.Write((int)param.Register);
                writer.Write((byte)param.Mask);
                writer.Write((byte)param.Type);
            }
            writer.Write("End");
        }
        public void Deserialize(BinaryReader reader) {
            int cbcount = reader.ReadInt32();
            ConstantBuffers = new ConstantBuffer[cbcount];
            for (int i = 0; i < cbcount; i++) {
                ref var cbuffer = ref ConstantBuffers[i];
                cbuffer.Name = new(reader.ReadString());
                cbuffer.Size = reader.ReadInt32();
                cbuffer.BindPoint = reader.ReadInt32();
                int vcount = reader.ReadInt32();
                cbuffer.Values = new UniformValue[vcount];
                for (int v = 0; v < vcount; v++) {
                    ref var value = ref cbuffer.Values[v];
                    value.Name = new(reader.ReadString());
                    value.Type = new(reader.ReadString());
                    value.Offset = reader.ReadInt32();
                    value.Size = reader.ReadInt32();
                    value.Rows = reader.ReadByte();
                    value.Columns = reader.ReadByte();
                    value.Flags = reader.ReadUInt16();
                }
            }
            int rbcount = reader.ReadInt32();
            ResourceBindings = new ResourceBinding[rbcount];
            for (int i = 0; i < rbcount; i++) {
                ref var rbinding = ref ResourceBindings[i];
                rbinding.Name = new(reader.ReadString());
                rbinding.BindPoint = reader.ReadInt32();
                rbinding.Stride = reader.ReadInt32();
                rbinding.Type = (ResourceTypes)reader.ReadByte();
            }
            int ipcount = reader.ReadInt32();
            InputParameters = new InputParameter[ipcount];
            for (int i = 0; i < ipcount; i++) {
                ref var param = ref InputParameters[i];
                param.Name = new(reader.ReadString());
                param.Semantic = new(reader.ReadString());
                param.SemanticIndex = reader.ReadInt32();
                param.Register = reader.ReadInt32();
                param.Mask = reader.ReadByte();
                param.Type = (InputParameter.Types)reader.ReadByte();
            }
            Trace.Assert(reader.ReadString() == "End");
        }
    }
    public class PreprocessedShader {

        public CSPreprocessedShader NativePreprocessed;

        public PreprocessedShader(CSPreprocessedShader preprocessed) {
            NativePreprocessed = preprocessed;
        }

    }
    public class CompiledShader {

        public byte[] CompiledBlob = Array.Empty<byte>();
        public string[] IncludeFiles = Array.Empty<string>();
        public ulong IncludeHash;
        private int dataHash;

        public ShaderReflection Reflection;

        public CSCompiledShader NativeShader;

        public int ReferenceCount;

        public void Serialize(BinaryWriter writer) {
            writer.Write(CompiledBlob.Length);
            writer.Write(CompiledBlob);
            writer.Write(IncludeFiles.Length);
            for (int i = 0; i < IncludeFiles.Length; i++) writer.Write(IncludeFiles[i]);
            writer.Write(IncludeHash);
            Reflection.Serialize(writer);
        }
        public void Deserialize(BinaryReader reader) {
            CompiledBlob = new byte[reader.ReadInt32()];
            Trace.Assert(reader.Read(CompiledBlob) == CompiledBlob.Length);
            IncludeFiles = new string[reader.ReadInt32()];
            for (int i = 0; i < IncludeFiles.Length; i++) IncludeFiles[i] = reader.ReadString();
            IncludeHash = reader.ReadUInt64();
            Reflection.Deserialize(reader);
            RecomputeHash();
        }
        public void RecomputeHash() {
            dataHash = 0;
            ulong binaryHash = 0;
            for (int i = 0; i < CompiledBlob.Length - 8; i += 8) {
                binaryHash = binaryHash * 1000003 + MemoryMarshal.Read<ulong>(CompiledBlob.AsSpan(i));
            }
            dataHash = (int)binaryHash ^ (int)(binaryHash >> 32);
        }
        public override int GetHashCode() {
            return dataHash;
        }
    }
}
