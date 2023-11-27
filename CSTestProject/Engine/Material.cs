﻿using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine {
    unsafe public class ShaderBase {
        public NativeShader* mNativeShader;
        public ShaderBase(string path, string entry) {
            mNativeShader = CSResources.LoadShader(path, entry);
        }
        unsafe public static implicit operator NativeShader*(ShaderBase? shader) { return shader == null ? null : shader.mNativeShader; }

        public static ShaderBase FromPath(string path, string entry) {
            return new ShaderBase(path, entry);
        }
    }
    public struct Parameters {
        public struct Item {
            public Type Type;
            public int ByteOffset;
            public int Count;
            public static readonly Item Default = new Item() { ByteOffset = -1, };
        }
        private SortedList<CSIdentifier, Item> Items;
        private byte[] Data;
        private int dataConsumed;
        public Parameters(bool dummy) { Items = new(); Data = Array.Empty<byte>(); }
        unsafe public Span<byte> SetValue<T>(CSIdentifier identifier, Span<T> data) where T : unmanaged {
            if (!Items.TryGetValue(identifier, out var item)) {
                item = new Item() { ByteOffset = -1, };
            }
            if (item.Type == null || (item.Type == typeof(T) ? item.Count != data.Length : item.Count * Marshal.SizeOf(item.Type) != sizeof(T) * data.Length)) {
                Debug.Assert(item.ByteOffset == -1);
                int size = sizeof(T) * data.Length;
                if (dataConsumed + size > Data.Length) {
                    Array.Resize(ref Data, Math.Max(Data.Length * 2, 256));
                }
                item.ByteOffset = dataConsumed;
                dataConsumed += size;
            }
            item.Type = typeof(T);
            item.Count = data.Length;
            Items[identifier] = item;
            var outBytes = Data.AsSpan(item.ByteOffset);
            MemoryMarshal.AsBytes(data).CopyTo(outBytes);
            return outBytes;
        }
        public Item GetValueItem(CSIdentifier identifier) {
            if (!Items.TryGetValue(identifier, out var item)) return Item.Default;
            return item;
        }
        unsafe public Span<byte> GetValueData(CSIdentifier identifier) {
            if (!Items.TryGetValue(identifier, out var item)) return default;
            return Data.AsSpan(item.ByteOffset, item.Count * Marshal.SizeOf(item.Type));
        }
        public int GetItemIdentifiers(Span<CSIdentifier> outlist) {
	        int count = 0;
	        foreach (var item in Items.Keys) {
		        if (count > outlist.Length) break;
		        outlist[count] = item;
		        ++count;
	        }
	        return count;
        }
        public Span<byte> GetDataRaw() {
	        return Data;
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
                var result = ((MaterialCollectorContext*)Context)->GetUniformSource(name);
                return MemoryMarshal.Cast<byte, T>(result)[0];
            } else {
                return ((MaterialEvaluatorContext*)Context)->GetUniform<T>(name);
            }
            //return MemoryMarshal.Cast<byte, T>(Material.GetUniformBinaryData(name))[0];
            //return Context.GetUniform<T>(name);
        }
    }
    public class ComputedParameter<T> : ComputedParameterBase where T : unmanaged {
        public delegate T Getter(ref ComputedContext context);
        public Getter Lambda;
        unsafe public ComputedParameter(CSIdentifier name, Getter lambda) : base(name, sizeof(T)) {
            Lambda = lambda;
        }
        public override void EvaluateValue(Span<byte> output, ref MaterialEvaluatorContext context) {
            var evalcontext = new ComputedContext(ref context);
            var value = Lambda(ref evalcontext);
            MemoryMarshal.Write(output, ref value);
        }
        public override void SourceValue(Span<byte> output, ref MaterialCollectorContext context) {
            var evalcontext = new ComputedContext(ref context);
            var value = Lambda(ref evalcontext);
            MemoryMarshal.Write(output, ref value);
        }
    }
    public class Material {
        public struct StateData {
            public enum Flags : byte {
                RenderPass = 0x01, Blend = 0x02, Raster = 0x04, Depth = 0x08,
                VertexShader = 0x10, PixelShader = 0x20,
            };

            // How to blend/raster/clip
            public CSIdentifier RenderPass = CSIdentifier.Invalid;
            public BlendMode BlendMode;
            public RasterMode RasterMode;
            public DepthMode DepthMode;
            public Flags Valid;

            // Shaders bound
            public ShaderBase? VertexShader;
            public ShaderBase? PixelShader;

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
                if ((newFlags & Flags.VertexShader) != 0) VertexShader = other.VertexShader;
                if ((newFlags & Flags.PixelShader) != 0) PixelShader = other.PixelShader;
                Valid |= newFlags;
            }
            public static readonly StateData Default = new StateData() {
                Valid = Flags.Blend | Flags.Raster | Flags.Depth,
                BlendMode = BlendMode.MakeOpaque(),
                RasterMode = RasterMode.MakeDefault(),
                DepthMode = DepthMode.MakeDefault(),
            };
        }
        public StateData State;

        // Parameters to be set
        Parameters Parameters = new Parameters(true);

        // Parameters are inherited from parent materials
        internal List<Material> InheritParameters = new();

        // These parameters can automatically compute themselves
        SortedList<CSIdentifier, ComputedParameterBase> ComputedParameters = new();

        // Incremented whenever data within this material changes
        int mRevision = 0;

        // Whenever a change is made that requires this material to be re-uploaded
        // (or computed parameters to recompute)
        void MarkChanged() {
            mRevision++;
        }

        public Material() { }
        public Material(string path) : this(ShaderBase.FromPath(path, "VSMain"), ShaderBase.FromPath(path, "PSMain")) { }
        public Material(ShaderBase vert, ShaderBase pix) {
            SetVertexShader(vert);
            SetPixelShader(pix);
        }
        public Parameters GetParametersRaw() { return Parameters; }

        public void SetRenderPassOverride(CSIdentifier pass) { State.RenderPass = pass; State.SetFlag(StateData.Flags.RenderPass, State.RenderPass.IsValid()); }
        public CSIdentifier GetRenderPassOverride() { return State.RenderPass; }

        // Set shaders bound to this material
        public void SetVertexShader(ShaderBase? shader) { State.VertexShader = shader; State.SetFlag(StateData.Flags.VertexShader, State.VertexShader != null); }
        public void SetPixelShader(ShaderBase? shader) { State.PixelShader = shader; State.SetFlag(StateData.Flags.PixelShader, State.PixelShader != null); }

        // Get shaders bound to this material
        public ShaderBase? GetVertexShader() { return State.VertexShader; }
        public ShaderBase? GetPixelShader() { return State.PixelShader; }

        // How to blend with the backbuffer
        public void SetBlendMode(BlendMode mode) { State.BlendMode = mode; State.Valid |= StateData.Flags.Blend; }
        public BlendMode GetBlendMode() { return State.BlendMode; }

        // How rasterize
        public void SetRasterMode(RasterMode mode) { State.RasterMode = mode; State.Valid |= StateData.Flags.Raster; }
        public RasterMode GetRasterMode() { return State.RasterMode; }

        // How to clip
        public void SetDepthMode(DepthMode mode) { State.DepthMode = mode; State.Valid |= StateData.Flags.Depth; }
        public DepthMode GetDepthMode() { return State.DepthMode; }

        public Span<byte> SetValue<T>(CSIdentifier name, Span<T> v) where T : unmanaged {
            var r = Parameters.SetValue(name, v);
            MarkChanged();
            return r;
        }

        // Set various uniform values
        unsafe public Span<byte> SetValue<T>(CSIdentifier name, T v) where T : unmanaged {
            return SetValue(name, new Span<T>(ref v));
	    }
        unsafe public Span<byte> SetTexture(CSIdentifier name, CSTexture tex) {
            return SetValue(name, (nint)tex.mTexture);
	    }
        unsafe public Span<byte> SetTexture(CSIdentifier name, CSRenderTarget tex) {
            return SetValue(name, (nint)tex.mRenderTarget);
        }

        public void SetComputedUniform<T>(CSIdentifier name, ComputedParameter<T>.Getter lambda) where T : unmanaged {
            ComputedParameters.Add(name, new ComputedParameter<T>(name, lambda));
        }
        public int FindComputedIndex(CSIdentifier name) {
            var index = ComputedParameters.IndexOfKey(name);
            return index;
        }
        public ComputedParameterBase? GetComputedByIndex(int index) {
            if (index < 0) return default;
            return ComputedParameters.ElementAt(index).Value;
	    }

        // Add a parent material that this material will inherit
        // properties from
        public void InheritProperties(Material other) {
            InheritParameters.Add(other);
        }
        public void RemoveInheritance(Material other) {
            InheritParameters.Remove(other);
        }

        // Get the binary data for a specific parameter
        public unsafe Span<byte> GetUniformBinaryData(CSIdentifier name) {
            Material self = this;
            return GetUniformBinaryData(name, new Span<Material>(ref self));
        }
        unsafe public CSTexture GetUniformTexture(CSIdentifier name) {
            Material self = this;
            return GetUniformTexture(name, new Span<Material>(ref self));
        }

        // Get the binary data for a specific parameter
        private static MaterialCollector collector = new();
        unsafe public static Span<byte> GetUniformBinaryData(CSIdentifier name, Span<Material> materialStack) {
            var context = new MaterialCollectorContext(materialStack, collector);
            var ret = context.GetUniformSource(name);
            collector.Clear();
            return ret;
        }
        unsafe public static CSTexture GetUniformTexture(CSIdentifier name, Span<Material> materialStack) {
            var data = GetUniformBinaryData(name, materialStack);
            if (data.Length < sizeof(ulong)) return default;
            return new CSTexture((NativeTexture*)BitConverter.ToUInt64(data));
        }

        unsafe public void CopyFrom(CSMaterial otherMat) {
            var identifiers = stackalloc CSIdentifier[16];
            int identCount = CSMaterial.GetParameterIdentifiers(otherMat.GetNativeMaterial(), identifiers, 16);
            for (int i = 0; i < identCount; ++i) {
                var dataPtr = CSMaterial.GetValueData(otherMat.GetNativeMaterial(), identifiers[i]);
                var type = CSMaterial.GetValueType(otherMat.GetNativeMaterial(), identifiers[i]);
                var data = new MemoryBlock<byte>((byte*)dataPtr.mData, dataPtr.mSize);
                if (type == 0) SetValue(identifiers[i], data.Reinterpret<float>().AsSpan());
                else if (type == 1) SetValue(identifiers[i], data.Reinterpret<int>().AsSpan());
                else SetValue(identifiers[i], *((nint*)data.Data));
            }
        }

        public static Material NullInstance = new();
        static Material() {
            NullInstance.SetValue("NullVec", Vector4.Zero);
        }
    }


    public class RootMaterial : Material {
        public static CSIdentifier iMMat = "Model";
        public static CSIdentifier iVMat = "View";
        public static CSIdentifier iPMat = "Projection";
        public static CSIdentifier iMVMat = "ModelView";
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
            SetComputedUniform<Matrix4x4>("InvModelViewProjection", (ref ComputedContext context) => {
                var mvp = context.GetUniform<Matrix4x4>(iMVPMat);
                return Matrix4x4.Invert(mvp, out var result) ? result : default;
            });
            SetComputedUniform<Vector3>("_ViewSpaceLightDir0", (ref ComputedContext context) => {
                var lightDir = context.GetUniform<Vector3>(iLightDir);
                var view = context.GetUniform<Matrix4x4>(iVMat);
                return Vector3.TransformNormal(lightDir, view);
            });
            SetComputedUniform<Vector3>("_ViewSpaceUpVector", (ref ComputedContext context) => {
                return Vector3.TransformNormal(Vector3.UnitY, context.GetUniform<Matrix4x4>(iVMat));
            });
        }

        public RootMaterial() : base("./assets/retained.hlsl") {
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