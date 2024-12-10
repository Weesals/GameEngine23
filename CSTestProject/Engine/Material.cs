using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Profiling;
using Weesals.Engine.Serialization;
using Weesals.Utility;

namespace Weesals.Engine {
    public struct Parameters {
        public struct Item : IComparable<Item>, IEquatable<Item> {
            public CSIdentifier Identifier;
            public Type Type;
            public int ByteOffset;
            public int Count;
            public static readonly Item Default = new Item() { ByteOffset = -1, };
            public int CompareTo(Item other) { return Identifier.CompareTo(other.Identifier); }
            public bool Equals(Item other) { return Identifier.Equals(other.Identifier); }
        }
        private Item[] Items = Array.Empty<Item>();
        private int itemCount;
        private byte[] Data = Array.Empty<byte>();
        private int dataConsumed;
        public Parameters() { }
        unsafe public Span<byte> SetValue<T>(CSIdentifier identifier, ReadOnlySpan<T> data) where T : unmanaged {
            var index = Items.AsSpan(0, itemCount).BinarySearch(new Item() { Identifier = identifier, });
            if (index < 0) {
                index = ~index;
                ArrayExt.InsertAt(ref Items, ref itemCount, index, new Item() { Identifier = identifier, ByteOffset = -1, });
            }
            ref var item = ref Items[index];
            if (item.Type == null || (item.Type == typeof(T) ? item.Count != data.Length : item.Count * Marshal.SizeOf(item.Type) != sizeof(T) * data.Length)) {
                Debug.Assert(item.ByteOffset == -1);
                int size = sizeof(T) * data.Length;
                if (dataConsumed + size > Data.Length) {
                    Array.Resize(ref Data, (int)BitOperations.RoundUpToPowerOf2((uint)(dataConsumed + size)));
                }
                item.ByteOffset = dataConsumed;
                dataConsumed += size;
            }
            item.Type = typeof(T);
            item.Count = data.Length;
            var outBytes = Data.AsSpan(item.ByteOffset);
            MemoryMarshal.AsBytes(data).CopyTo(outBytes);
            return outBytes;
        }
        public bool ClearValue(CSIdentifier identifier) {
            var index = Items.AsSpan(0, itemCount).BinarySearch(new Item() { Identifier = identifier, });
            if (index < 0) return false;
            ArrayExt.RemoveAt(ref Items, ref itemCount, index);
            return true;
        }
        public int GetItemIndex(CSIdentifier identifier) {
            var index = Items.AsSpan(0, itemCount).BinarySearch(new Item() { Identifier = identifier, });
            if (index < 0) return -1;
            return index;
        }
        public Item GetValueItem(CSIdentifier identifier) {
            var index = Items.AsSpan(0, itemCount).BinarySearch(new Item() { Identifier = identifier, });
            if (index < 0) return Item.Default;
            return Items[index];
        }
        unsafe public Span<byte> GetItemData(Item item) {
            return Data.AsSpan(item.ByteOffset, item.Count * Marshal.SizeOf(item.Type));
        }
        unsafe public Span<byte> GetValueData(CSIdentifier identifier) {
            var index = Items.AsSpan(0, itemCount).BinarySearch(new Item() { Identifier = identifier, });
            if (index < 0) return default;
            var item = Items[index];
            return Data.AsSpan(item.ByteOffset, item.Count * Marshal.SizeOf(item.Type));
        }
        public int GetItemIdentifiers(Span<CSIdentifier> outlist) {
	        int count = 0;
	        foreach (var item in Items) {
		        if (count > outlist.Length) break;
		        outlist[count] = item.Identifier;
		        ++count;
	        }
	        return count;
        }
        public Span<byte> GetDataRaw() {
	        return Data;
        }
        public Span<Item> GetItemsRaw() {
            return Items.AsSpan(0, itemCount);
        }
        public void Clear() {
            itemCount = 0;
            dataConsumed = 0;
        }
        public override int GetHashCode() {
            int hash = 0;
            foreach (var item in Items.AsSpan(0, itemCount)) {
                var data = Data.AsSpan((int)item.ByteOffset, (int)(item.Count * Marshal.SizeOf((Type)item.Type)));
                int dataHash = 0;
                for (int i = 0; i < data.Length; ++i) dataHash = dataHash * 53 + data[i];
                hash += HashCode.Combine(item.Identifier, dataHash);
            }
            return hash;
        }
    }

    public abstract class ComputedParameterBase {
        protected CSIdentifier Name;
        protected int DataSize;
        protected ComputedParameterBase(CSIdentifier name, int dataSize) {
            Name = name;
            DataSize = dataSize;
        }
        public CSIdentifier GetName() {
            return Name;
        }
        public int GetDataSize() {
            return DataSize;
        }
        public abstract void EvaluateValue(Span<byte> output, ref MaterialEvaluatorContext context);
        public abstract void SourceValue(Span<byte> output, ref MaterialCollectorContext context);
    }
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
    unsafe public ref struct ComputedContext {
        public enum Modes { Source, Evaluate, }
        public void* Context;
        public Modes Mode;
        public ComputedContext(ref MaterialEvaluatorContext eval) {
            fixed (void* ptr = &eval) Context = ptr;
            Mode = Modes.Evaluate;
        }
        public ComputedContext(ref MaterialCollectorContext eval) {
            fixed (void* ptr = &eval) Context = ptr;
            Mode = Modes.Source;
        }
        public T GetUniform<T>(CSIdentifier name) where T : unmanaged {
            if (Mode == Modes.Source) {
                var result = ((MaterialCollectorContext*)Context)->GetUniform(name);
                return MemoryMarshal.Read<T>(result);
            } else {
                return ((MaterialEvaluatorContext*)Context)->GetUniform<T>(name);
            }
            //return MemoryMarshal.Cast<byte, T>(Material.GetUniformBinaryData(name))[0];
            //return Context.GetUniform<T>(name);
        }
    }
#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
    public class ComputedParameter<T> : ComputedParameterBase where T : unmanaged {
        public delegate T Getter(ref ComputedContext context);
        public Getter Lambda;
        unsafe public ComputedParameter(CSIdentifier name, Getter lambda) : base(name, sizeof(T)) {
            Lambda = lambda;
        }
        public override void EvaluateValue(Span<byte> output, ref MaterialEvaluatorContext context) {
            var evalcontext = new ComputedContext(ref context);
            var value = Lambda(ref evalcontext);
            MemoryMarshal.Write(output, in value);
        }
        public override void SourceValue(Span<byte> output, ref MaterialCollectorContext context) {
            var evalcontext = new ComputedContext(ref context);
            var value = Lambda(ref evalcontext);
            MemoryMarshal.Write(output, in value);
        }
    }
    public class MaterialPropertyBlock {
        // Parameters to be set
        protected Parameters Parameters = new();

        // Incremented whenever data within this material changes
        protected int mRevision = 0;
        protected int hashCache = 0;

        // Whenever a change is made that requires this material to be re-uploaded
        // (or computed parameters to recompute)
        protected void MarkChanged() {
            mRevision++;
            hashCache = 0;
        }

        public ref Parameters GetParametersRaw() { return ref Parameters; }

        public Span<byte> SetArrayValue<T>(CSIdentifier name, Span<T> v) where T : unmanaged {
            return SetArrayValue(name, (ReadOnlySpan<T>)v);
        }
        public Span<byte> SetArrayValue<T>(CSIdentifier name, ReadOnlySpan<T> v) where T : unmanaged {
            var r = Parameters.SetValue(name, v);
            MarkChanged();
            return r;
        }
        public T GetValue<T>(CSIdentifier name) where T : unmanaged {
            return MemoryMarshal.Read<T>(GetUniformBinaryData(name));
        }
        public Span<T> GetValueArray<T>(CSIdentifier name) where T : unmanaged {
            return MemoryMarshal.Cast<byte, T>(GetUniformBinaryData(name));
        }

        // Set various uniform values
        unsafe public Span<byte> SetValue<T>(CSIdentifier name, T v) where T : unmanaged {
#pragma warning disable CS9087 // This returns a parameter by reference but it is not a ref parameter
            return SetArrayValue(name, new ReadOnlySpan<T>(ref v));
#pragma warning restore CS9087 // This returns a parameter by reference but it is not a ref parameter
        }
        unsafe public Span<byte> SetTexture(CSIdentifier name, CSTexture tex) {
            return SetValue(name, new CSBufferReference(tex));
        }
        unsafe public Span<byte> SetTexture(CSIdentifier name, CSRenderTarget tex) {
            return SetValue(name, new CSBufferReference(tex));
        }
        unsafe public void SetBuffer(CSIdentifier name, CSBufferLayout buffer) {
            SetValue(name, new CSBufferReference(buffer));
        }
        public void ClearValues() {
            Parameters.Clear();
        }

        // Get the binary data for a specific parameter
        public unsafe Span<byte> GetUniformBinaryData(CSIdentifier name) {
            var self = this;
#pragma warning disable CS9091 // This returns local by reference but it is not a ref local
            return GetUniformBinaryData(name, new Span<MaterialPropertyBlock>(ref self));
#pragma warning restore CS9091 // This returns local by reference but it is not a ref local
        }
        unsafe public CSBufferReference GetUniformBuffer(CSIdentifier name) {
            var self = this;
            return GetUniformBuffer(name, new Span<MaterialPropertyBlock>(ref self));
        }
        unsafe public CSTexture GetUniformTexture(CSIdentifier name) {
            return GetUniformBuffer(name).AsTexture();
        }
        unsafe public CSRenderTarget GetUniformRenderTarget(CSIdentifier name) {
            return GetUniformBuffer(name).AsRenderTarget();
        }

        // Get the binary data for a specific parameter
        [ThreadStatic] private static MaterialCollectorStacked collector;
        public static MaterialCollectorContext BeginGetUniforms(Span<Material> materials) {
            collector ??= new();
            Debug.Assert(collector.IsEmpty);
            return MaterialCollectorContext.Create(collector, materials);
        }
        public static MaterialCollectorContext BeginGetUniforms(Span<MaterialPropertyBlock> properties) {
            collector ??= new();
            Debug.Assert(collector.IsEmpty);
            return MaterialCollectorContext.Create(collector, properties);
        }
        unsafe public static Span<byte> GetUniformBinaryData(CSIdentifier name, Span<Material> materialStack) {
            using var context = BeginGetUniforms(materialStack);
            return context.GetUniform(name);
        }
        unsafe public static Span<byte> GetUniformBinaryData(CSIdentifier name, Span<MaterialPropertyBlock> materialStack) {
            using var context = BeginGetUniforms(materialStack);
            return context.GetUniform(name);
        }
        unsafe public static CSBufferReference GetUniformBuffer(CSIdentifier name, Span<Material> materialStack) {
            var data = GetUniformBinaryData(name, materialStack);
            if (data.Length < sizeof(CSBufferReference)) return default;
            return MemoryMarshal.Read<CSBufferReference>(data);
        }
        unsafe public static CSBufferReference GetUniformBuffer(CSIdentifier name, Span<MaterialPropertyBlock> materialStack) {
            var data = GetUniformBinaryData(name, materialStack);
            if (data.Length < sizeof(CSBufferReference)) return default;
            return MemoryMarshal.Read<CSBufferReference>(data);
        }
        public override int GetHashCode() {
            if (hashCache == 0) hashCache = Parameters.GetHashCode();
            return hashCache;
        }
    }
    public class Material : MaterialPropertyBlock {
        private static ProfilerMarker ProfileMarker_Serialize = new("Material.Serialize");

        public struct StateData : IEquatable<StateData> {
            public enum Flags : byte {
                RenderPass = 0x01, Blend = 0x02, Raster = 0x04, Depth = 0x08,
                MeshShader = 0x10, VertexShader = 0x20, PixelShader = 0x40,
            };

            // How to blend/raster/clip
            public CSIdentifier RenderPass = CSIdentifier.Invalid;
            public BlendMode BlendMode;
            public RasterMode RasterMode;
            public DepthMode DepthMode;
            public Flags Valid;

            // Shaders bound
            public Shader? MeshShader;
            public Shader? VertexShader;
            public Shader? PixelShader;

            public StateData() { }

            public void SetFlag(Flags flag, bool enable) {
                if (enable) Valid |= flag; else Valid &= ~flag;
            }

            public void MergeWith(in StateData other) {
                var newFlags = other.Valid & ~Valid;
                if (newFlags == 0) return;
                if ((newFlags & Flags.RenderPass) != 0) RenderPass = other.RenderPass;
                if ((newFlags & Flags.Blend) != 0) BlendMode = other.BlendMode;
                if ((newFlags & Flags.Raster) != 0) RasterMode = other.RasterMode;
                if ((newFlags & Flags.Depth) != 0) DepthMode = other.DepthMode;
                if ((newFlags & Flags.MeshShader) != 0) MeshShader = other.MeshShader;
                if ((newFlags & Flags.VertexShader) != 0) VertexShader = other.VertexShader;
                if ((newFlags & Flags.PixelShader) != 0) PixelShader = other.PixelShader;
                Valid |= newFlags;
            }

            public bool Equals(StateData other) {
                return RenderPass == other.RenderPass && BlendMode == other.BlendMode &&
                    RasterMode == other.RasterMode && DepthMode == other.DepthMode &&
                    MeshShader == other.MeshShader &&
                    VertexShader == other.VertexShader && PixelShader == other.PixelShader;
            }
            public override int GetHashCode() {
                return HashCode.Combine(RenderPass, BlendMode, RasterMode, DepthMode, MeshShader, VertexShader, PixelShader);
            }

            public static readonly StateData Default = new StateData() {
                Valid = Flags.Blend | Flags.Raster | Flags.Depth,
                BlendMode = BlendMode.MakeOpaque(),
                RasterMode = RasterMode.MakeDefault(),
                DepthMode = DepthMode.MakeDefault(),
            };
        }
        public StateData State;
        public struct RealtimeStateData : IEquatable<RealtimeStateData> {
            public enum Flags : byte { None = 0x00, StencilRef = 0x01, };

            public byte StencilRef;
            public Flags Valid;

            public RealtimeStateData() { }

            public void SetFlag(Flags flag, bool enable) {
                if (enable) Valid |= flag; else Valid &= ~flag;
            }

            public void MergeWith(in RealtimeStateData other) {
                var newFlags = other.Valid & ~Valid;
                if (newFlags == 0) return;
                if ((newFlags & Flags.StencilRef) != 0) StencilRef = other.StencilRef;
                Valid |= newFlags;
            }

            public bool Equals(RealtimeStateData other) { return StencilRef == other.StencilRef; }
            public override int GetHashCode() { return HashCode.Combine(StencilRef); }

            public static readonly RealtimeStateData Default = new RealtimeStateData() {
                Valid = Flags.None,
            };
        }
        public RealtimeStateData RealtimeData;

        protected List<KeyValuePair<CSIdentifier, CSIdentifier>> Macros;

        // Parameters are inherited from parent materials
        internal List<Material> InheritParameters;

        // These parameters can automatically compute themselves
        SortedList<CSIdentifier, ComputedParameterBase> ComputedParameters;

        // Incremented whenever data within this material changes
        int mIdentifier;
        static int gIdentifier = 0;

        public int Priority { get; set; }

        public Material() { mIdentifier = gIdentifier++; }
        public Material(Material parent) : this() {
            if (parent != null) InheritProperties(parent);
        }
        public Material(Shader vert, Shader pix, Material? parent = null) : this() {
            SetVertexShader(vert);
            SetPixelShader(pix);
            if (parent != null) InheritProperties(parent);
        }
        public Material(string path, Material parent = null) {
            //: this(Resources.LoadShader(path, "VSMain"), Resources.LoadShader(path, "PSMain")) 
            var material = Resources.LoadMaterial(path);
            if (material != null) InheritProperties(material);
            if (parent != null) InheritProperties(parent);
        }

        public void SetRenderPassOverride(CSIdentifier pass) { State.RenderPass = pass; State.SetFlag(StateData.Flags.RenderPass, State.RenderPass.IsValid); }
        public CSIdentifier GetRenderPassOverride() { return State.RenderPass; }

        // Set shaders bound to this material
        public void SetMeshShader(Shader? shader) { State.MeshShader = shader; State.SetFlag(StateData.Flags.MeshShader, State.MeshShader != null); }
        public void SetVertexShader(Shader? shader) { State.VertexShader = shader; State.SetFlag(StateData.Flags.VertexShader, State.VertexShader != null); }
        public void SetPixelShader(Shader? shader) { State.PixelShader = shader; State.SetFlag(StateData.Flags.PixelShader, State.PixelShader != null); }

        // Get shaders bound to this material
        public Shader? GetMeshShader() { return State.MeshShader; }
        public Shader? GetVertexShader() { return State.VertexShader; }
        public Shader? GetPixelShader() { return State.PixelShader; }

        // How to blend with the backbuffer
        public void SetBlendMode(BlendMode mode) { State.BlendMode = mode; State.Valid |= StateData.Flags.Blend; }
        public BlendMode GetBlendMode() { return State.BlendMode; }

        // How rasterize
        public void SetRasterMode(RasterMode mode) { State.RasterMode = mode; State.Valid |= StateData.Flags.Raster; }
        public RasterMode GetRasterMode() { return State.RasterMode; }

        // How to clip
        public void SetDepthMode(DepthMode mode) { State.DepthMode = mode; State.Valid |= StateData.Flags.Depth; }
        public DepthMode GetDepthMode() { return State.DepthMode; }

        public List<KeyValuePair<CSIdentifier, CSIdentifier>> GetMacrosRaw() { return Macros; }

        // Configure shader feature set
        public void SetMacro(CSIdentifier name, CSIdentifier v) {
            // Invalid v means clear value
            if (!v.IsValid) { ClearMacro(name); return; }

            Macros ??= new();
            var index = 0;
            for (; index < Macros.Count; ++index) if (Macros[index].Key == name) break;

            if (index >= Macros.Count) Macros.Add(new(name, v));
            else if (Macros[index].Value != v) Macros[index] = new(name, v);
            else return;

            MarkChanged();
        }
        public void ClearMacro(CSIdentifier name) {
            if (Macros == null) return;
            var index = 0;
            for (; index < Macros.Count; ++index) if (Macros[index].Key == name) break;
            if (index >= Macros.Count) return;
            Macros.RemoveAtSwapBack(index);
            MarkChanged();
        }

        public void SetComputedUniform<T>(CSIdentifier name, ComputedParameter<T>.Getter lambda) where T : unmanaged {
            if (ComputedParameters == null) ComputedParameters = new();
            ComputedParameters.Add(name, new ComputedParameter<T>(name, lambda));
            MarkChanged();
        }
        public int FindComputedIndex(CSIdentifier name) {
            var index = ComputedParameters == null ? -1 : ComputedParameters.IndexOfKey(name);
            return index;
        }
        public ComputedParameterBase? GetComputedByIndex(int index) {
            if (index < 0) return default;
            return ComputedParameters.Values[index];
	    }

        // Add a parent material that this material will inherit
        // properties from
        public void InheritProperties(Material other) {
            if (InheritParameters == null) InheritParameters = new();
            InheritParameters.Add(other);
            MarkChanged();
        }
        public void RemoveInheritance(Material other) {
            InheritParameters.Remove(other);
            MarkChanged();
        }

        public override string ToString() {
            var builder = new StringBuilder();
            builder.Append("Mat");
            if (State.PixelShader != null) {
                builder.Append("{");
                builder.Append(State.PixelShader.ToString());
                builder.Append("}");
            }
            builder.Append("<");
            var parameters = Parameters.GetItemsRaw();
            for (int i = 0; i < parameters.Length; i++) {
                if (i > 0) builder.Append(",");
                builder.Append(parameters[i].Identifier.GetName());
            }
            builder.Append(">");
            return builder.ToString();
        }
        public int GetIdentifier() { return mIdentifier; }
        public override int GetHashCode() {
            if (hashCache == 0) {
                hashCache = HashCode.Combine(State.GetHashCode(), base.GetHashCode());
                if (Macros != null) {
                    foreach (var item in Macros) hashCache += HashCode.Combine(item.Key.GetHashCode(), item.Value.GetHashCode());
                }
            }
            return hashCache;
        }

        public void SetStencilRef(int systemId) {
            RealtimeData.StencilRef = (byte)systemId;
            RealtimeData.Valid |= RealtimeStateData.Flags.StencilRef;
        }

        public void Serialize(TSONNode sMaterial) {
            using var marker = ProfileMarker_Serialize.Auto();
            using (var sInherit = sMaterial.CreateChild("Inherit")) {
                int count = InheritParameters?.Count ?? 0;
                sInherit.Serialize(ref count);
                if (count > 0) {
                    InheritParameters ??= new();
                    if (sInherit.IsReading) InheritParameters.Clear();
                    for (int i = 0; i < count; i++) {
                        var child = sInherit.IsWriting ? InheritParameters[i] : new();
                        using (var sChild = sMaterial.CreateChild("Child")) {
                            child.Serialize(sChild);
                        }
                        if (sInherit.IsReading) InheritParameters.Add(child);
                    }
                }
            }
            Span<byte> tmpData = stackalloc byte[4096];
            ref var parameters = ref GetParametersRaw();
            if (sMaterial.IsWriting) {
                var items = parameters.GetItemsRaw();
                for (int i = 0; i < items.Length; i++) {
                    var item = items[i];
                    var typeName =
                        item.Type == typeof(float) ? "F1"u8 :
                        item.Type == typeof(Vector2) ? "F2"u8 :
                        item.Type == typeof(Vector3) ? "F3"u8 :
                        item.Type == typeof(Vector4) ? "F4"u8 :
                        item.Type == typeof(int) ? "I1"u8 :
                        item.Type == typeof(Int2) ? "I2"u8 :
                        item.Type == typeof(Int3) ? "I3"u8 :
                        item.Type == typeof(Int4) ? "I4"u8 :
                        item.Type == typeof(Matrix4x4) ? "M4"u8 :
                        item.Type == typeof(CSBufferReference) ? "BR"u8 :
                        null;
                    if (typeName == null) continue;
                    scoped var data = parameters.GetItemData(item);
                    if (item.Type == typeof(CSBufferReference)) {
                        var bufferReference = MemoryMarshal.Read<CSBufferReference>(data);
                        if (bufferReference.mType == CSBufferReference.BufferTypes.Texture) {
                            var path = Resources.FindAssetPath(bufferReference.AsTexture());
                            if (string.IsNullOrEmpty(path)) continue;
                            int len = Encoding.UTF8.GetBytes(path, tmpData);
                            data = tmpData.Slice(0, len);
                            typeName = "Te"u8;
                        } else {
                            // Ignore item
                            continue;
                        }
                    }
                    using (var sItem = sMaterial.CreateChild(item.Identifier.ToString())) {
                        using (var sBin = sItem.CreateRawBinary()) {
                            sBin.Require(typeName);
                        }
                        using (var sBin = sItem.CreateRawBinary()) {
                            sBin.Serialize(ref data);
                        }
                    }
                }
            } else {
                Span<byte> typeName = stackalloc byte[2];
                while (true) {
                    using (var sItem = sMaterial.CreateChild(null)) {
                        if (!sItem.IsValid) break;
                        var identifier = new CSIdentifier(sItem.Name);
                        using (var sBin = sItem.CreateRawBinary()) {
                            sBin.Serialize(typeName);
                        }
                        using (var sBin = sItem.CreateRawBinary()) {
                            var itemData = tmpData;
                            sBin.Serialize(ref itemData);
                            if (typeName.SequenceEqual("F1"u8)) {
                                parameters.SetValue<float>(identifier, MemoryMarshal.Cast<byte, float>(itemData));
                            } else if (typeName.SequenceEqual("F2"u8)) {
                                parameters.SetValue<Vector2>(identifier, MemoryMarshal.Cast<byte, Vector2>(itemData));
                            } else if (typeName.SequenceEqual("F3"u8)) {
                                parameters.SetValue<Vector3>(identifier, MemoryMarshal.Cast<byte, Vector3>(itemData));
                            } else if (typeName.SequenceEqual("F4"u8)) {
                                parameters.SetValue<Vector4>(identifier, MemoryMarshal.Cast<byte, Vector4>(itemData));
                            } else if (typeName.SequenceEqual("I1"u8)) {
                                parameters.SetValue<int>(identifier, MemoryMarshal.Cast<byte, int>(itemData));
                            } else if (typeName.SequenceEqual("I2"u8)) {
                                parameters.SetValue<Int2>(identifier, MemoryMarshal.Cast<byte, Int2>(itemData));
                            } else if (typeName.SequenceEqual("I3"u8)) {
                                parameters.SetValue<Int3>(identifier, MemoryMarshal.Cast<byte, Int3>(itemData));
                            } else if (typeName.SequenceEqual("I4"u8)) {
                                parameters.SetValue<Int4>(identifier, MemoryMarshal.Cast<byte, Int4>(itemData));
                            } else if (typeName.SequenceEqual("M4"u8)) {
                                parameters.SetValue<Matrix4x4>(identifier, MemoryMarshal.Cast<byte, Matrix4x4>(itemData));
                            } else if (typeName.SequenceEqual("Te"u8)) {
                                var path = Encoding.UTF8.GetString(itemData);
                                var tex = Resources.LoadTexture(path);
                                SetTexture(identifier, tex);
                            }
                        }
                    }
                }
            }
        }

        public static NullMaterial NullInstance = new();
    }

    public class NullMaterial : Material {
        public static readonly CSIdentifier iNullMat = "NullMat";
        public NullMaterial() {
            SetValue(iNullMat, default(Matrix4x4));
        }
        public Span<byte> GetNullMat() { return GetParametersRaw().GetValueData(iNullMat); }
    }

    public class RootMaterial : Material {
        public static CSIdentifier iMMat = "Model";
        public static CSIdentifier iVMat = "View";
        public static CSIdentifier iPMat = "Projection";
        public static CSIdentifier iMVMat = "ModelView";
        public static CSIdentifier iVPMat = "ViewProjection";
        public static CSIdentifier iMVPMat = "ModelViewProjection";
        public static CSIdentifier iLightDir = "_WorldSpaceLightDir0";
        public static CSIdentifier iRes = "Resolution";

        void InitialiseDefaults() {
            SetValue("Model", Matrix4x4.Identity);
            SetView(Matrix4x4.CreateLookAt(new Vector3(0, 5, -10), new Vector3(0, 0, 0), new Vector3(0, 1, 0)));
            SetProjection(Matrix4x4.CreatePerspectiveFieldOfView(1.0f, 1.0f, 1.0f, 500.0f));
            SetComputedUniform<Matrix4x4>("ModelView", (ref ComputedContext context) => {
                var m = context.GetUniform<Matrix4x4>(iMMat);
                var v = context.GetUniform<Matrix4x4>(iVMat);
                return (m * v);
            });
            SetComputedUniform<Matrix4x4>("ViewProjection", (ref ComputedContext context) => {
                var v = context.GetUniform<Matrix4x4>(iVMat);
                var p = context.GetUniform<Matrix4x4>(iPMat);
                return (v * p);
            });
            SetComputedUniform<Matrix4x4>("ModelViewProjection", (ref ComputedContext context) => {
                var mv = context.GetUniform<Matrix4x4>(iMVMat);
                var p = context.GetUniform<Matrix4x4>(iPMat);
                return (mv * p);
            });
            SetComputedUniform<Matrix4x4>("InvModel", (ref ComputedContext context) => {
                var m = context.GetUniform<Matrix4x4>(iMMat);
                return Matrix4x4.Invert(m, out var result) ? result : default;
            });
            SetComputedUniform<Matrix4x4>("InvView", (ref ComputedContext context) => {
                var v = context.GetUniform<Matrix4x4>(iVMat);
                return Matrix4x4.Invert(v, out var result) ? result : default;
            });
            SetComputedUniform<Matrix4x4>("InvProjection", (ref ComputedContext context) => {
                var p = context.GetUniform<Matrix4x4>(iPMat);
                return Matrix4x4.Invert(p, out var result) ? result : default;
            });
            SetComputedUniform<Matrix4x4>("InvModelView", (ref ComputedContext context) => {
                var mv = context.GetUniform<Matrix4x4>(iMVMat);
                return Matrix4x4.Invert(mv, out var result) ? result : default;
            });
            SetComputedUniform<Matrix4x4>("InvViewProjection", (ref ComputedContext context) => {
                var vp = context.GetUniform<Matrix4x4>(iVPMat);
                return Matrix4x4.Invert(vp, out var result) ? result : default;
            });
            SetComputedUniform<Matrix4x4>("InvModelViewProjection", (ref ComputedContext context) => {
                var mvp = context.GetUniform<Matrix4x4>(iMVPMat);
                return Matrix4x4.Invert(mvp, out var result) ? result : default;
            });
            SetComputedUniform<Vector3>("_ViewSpaceLightDir0", (ref ComputedContext context) => {
                var lightDir = -context.GetUniform<Vector3>(iLightDir);
                var view = context.GetUniform<Matrix4x4>(iVMat);
                //Matrix4x4.Invert(view, out view);
                var dir = Vector3.TransformNormal(lightDir, view);
                //Debug.WriteLine(dir.ToString());
                return -dir;
            });
            SetComputedUniform<Vector3>("_ViewSpaceUpVector", (ref ComputedContext context) => {
                var view = context.GetUniform<Matrix4x4>(iVMat);
                //Matrix4x4.Invert(view, out view);
                return Vector3.TransformNormal(Vector3.UnitY, view);
            });
            SetComputedUniform<Vector4>("_ZBufferParams", (ref ComputedContext context) => {
                var proj = context.GetUniform<Matrix4x4>(iPMat);
                float near = Math.Abs(proj.M43 / proj.M33);
                float far = Math.Abs(proj.M43 / (proj.M33 - 1));
                Vector2 ZBufferParams = new(1.0f / far - 1.0f / near, 1.0f / near);
                return new Vector4(ZBufferParams, ZBufferParams.X / ZBufferParams.Y, 1.0f / ZBufferParams.Y);
            });
        }

        public RootMaterial() : base("./Assets/opaque.hlsl") {
            InitialiseDefaults();
        }

        public void SetResolution(Vector2 res) {
            SetValue(iRes, res);
        }
        public void SetView(in Matrix4x4 view) {
            SetValue(iVMat, view);
        }
        public void SetProjection(in Matrix4x4 proj) {
            SetValue(iPMat, proj);
        }
    }
}
