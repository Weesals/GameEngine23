using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Engine {
    public partial struct Bool {
        public static implicit operator bool(Bool b) { return b.mValue != 0; }
    }
    public partial struct CSString {
        unsafe public CSString(char* chrs, int len) {
            Debug.Assert(sizeof(ushort) == sizeof(char));
            mBuffer = (ushort*)chrs; mSize = len;
        }
        unsafe public override string ToString() {
            return Encoding.Unicode.GetString((byte*)mBuffer, mSize);
        }
    }
    public partial struct CSString8 {
        unsafe public override string ToString() {
            return Encoding.UTF8.GetString((byte*)mBuffer, mSize);
        }
    }
    public partial struct CSIdentifier : IEquatable<CSIdentifier>, IComparable<CSIdentifier> {
        unsafe public CSIdentifier(string str) {
            fixed (char* chrs = str) {
                mId = GetIdentifier(new CSString(chrs, str.Length));
            }
        }
        public int GetId() { return mId; }
        public bool IsValid() { return mId != 0; }
        public string GetName() { return GetName(mId).ToString(); }
        public bool Equals(CSIdentifier other) { return mId == other.mId; }
        public int CompareTo(CSIdentifier other) { return mId - other.mId; }
        public override bool Equals(object? obj) { return obj is CSIdentifier identifier && Equals(identifier); }
        public override int GetHashCode() { return HashCode.Combine(mId); }
        public override string ToString() { return GetName(); }
        public static bool operator ==(CSIdentifier left, CSIdentifier right) { return left.Equals(right); }
        public static bool operator !=(CSIdentifier left, CSIdentifier right) { return !(left == right); }
        public static implicit operator CSIdentifier(string name) { return new CSIdentifier(name); }
        public static readonly CSIdentifier Invalid = new CSIdentifier() { mId = 0, };
    }
    public struct CSName : IEquatable<CSName> {
        public string Name { get; private set; }
        private CSIdentifier mBase;
        public CSName(string name) {
            Name = name;
            mBase = new CSIdentifier(name);
        }
        public string GetName() { return Name; }
        public int GetId() { return mBase.mId; }

        public override bool Equals(object? obj) { return obj is CSName name && Equals(name); }
        public bool Equals(CSName other) { return mBase.Equals(other.mBase); }
        public override int GetHashCode() { return HashCode.Combine(mBase); }
        public static implicit operator CSIdentifier(CSName name) { return name.mBase; }
        public static bool operator ==(CSName left, CSName right) { return left.mBase.Equals(right.mBase); }
        public static bool operator !=(CSName left, CSName right) { return !left.mBase.Equals(right.mBase); }

        public static CSName None = default;
    }
    public unsafe partial struct CSSpan {
        public Span<T> AsSpan<T>() where T : unmanaged { return new Span<T>((T*)mData, mSize); }
        public MemoryBlock<T> AsMemoryBlock<T>() where T : unmanaged { return new MemoryBlock<T>((T*)mData, mSize); }
    }
    public unsafe struct CSSpanT<T> where T : unmanaged {
        public T* mData;
        public int mSize;
        public CSSpanT(T* data, int size) {
            mData = data;
            mSize = size;
        }
    }
    // Casts smart ptr to T type (ie. assumes T is a wrapped type)
    public struct CSSpanSPtrWapped<T> where T : unmanaged {
        CSSpanSPtr array;
        public int Length => array.mSize;
        unsafe public ref T this[int index] {
            get => ref *(T*)&array.mData[index];
        }
        public CSSpanSPtrWapped(CSSpanSPtr _array) { array = _array; }
        public struct Enumerator : IEnumerator {
            private CSSpanSPtrWapped<T> span;
            private int index;
            public ref T Current => ref span[index];
            object IEnumerator.Current => Current;
            public Enumerator(CSSpanSPtrWapped<T> _span) { span = _span; index = -1; }
            public void Dispose() { }
            public void Reset() { index = -1; }
            public bool MoveNext() { return ++index < span.Length; }
        }
        public Enumerator GetEnumerator() { return new Enumerator(this); }
    }
    public struct CSSpanSPtr<T> where T : unmanaged {
        CSSpanSPtr array;
        public int Length => array.mSize;
        unsafe public ref T this[int index] {
            get => ref *(T*)array.mData[index].mPointer;
        }
        public CSSpanSPtr(CSSpanSPtr _array) { array = _array; }
        public struct Enumerator : IEnumerator {
            private CSSpanSPtr<T> span;
            private int index;
            public ref T Current => ref span[index];
            object IEnumerator.Current => Current;
            public Enumerator(CSSpanSPtr<T> _span) { span = _span; index = -1; }
            public void Dispose() { }
            public void Reset() { index = -1; }
            public bool MoveNext() { return ++index < span.Length; }
        }
        public Enumerator GetEnumerator() { return new Enumerator(this); }
    }
    // std::span<T*>
    public struct CSSpanPtr<T> where T : unmanaged {
        CSSpan array;
        public int Length => array.mSize;
        unsafe public ref T this[int index] {
            get => ref *((T**)array.mData)[index];
        }
        public CSSpanPtr(CSSpan _array) { array = _array; }
        public struct Enumerator : IEnumerator {
            private CSSpanPtr<T> span;
            private int index;
            public ref T Current => ref span[index];
            object IEnumerator.Current => Current;
            public Enumerator(CSSpanPtr<T> _span) { span = _span; index = -1; }
            public void Dispose() { }
            public void Reset() { index = -1; }
            public bool MoveNext() { return ++index < span.Length; }
        }
        public Enumerator GetEnumerator() { return new Enumerator(this); }
    }
    public partial struct CSTexture : IEquatable<CSTexture> {
        unsafe public bool IsValid() { return mTexture != null; }
        unsafe public CSTexture SetSize(Int2 size) { SetSize(mTexture, size); return this; }
        unsafe public Int2 GetSize() { return GetSize(mTexture); }
        unsafe public CSTexture SetFormat(BufferFormat fmt) { SetFormat(mTexture, fmt); return this; }
        unsafe public BufferFormat GetFormat() { return GetFormat(mTexture); }
        unsafe public CSTexture SetMipCount(int count) { SetMipCount(mTexture, count); return this; }
        unsafe public int GetMipCount() { return GetMipCount(mTexture); }
        unsafe public CSTexture SetArrayCount(int count) { SetArrayCount(mTexture, count); return this; }
        unsafe public int GetArrayCount() { return GetArrayCount(mTexture); }
        unsafe public MemoryBlock<byte> GetTextureData(int mip = 0, int slice = 0) { var data = GetTextureData(mTexture, mip, slice); return new MemoryBlock<byte>((byte*)data.mData, data.mSize); }
        unsafe public void MarkChanged() { MarkChanged(mTexture); }
        unsafe public void Dispose() { Dispose(mTexture); mTexture = null; }

        public override bool Equals(object? obj) { return obj is CSTexture texture && Equals(texture); }
        unsafe public bool Equals(CSTexture other) { return mTexture == other.mTexture; }
        unsafe public override int GetHashCode() { return HashCode.Combine((ulong)mTexture); }
        unsafe public static bool operator ==(CSTexture left, CSTexture right) { return left.mTexture == right.mTexture; }
        unsafe public static bool operator !=(CSTexture left, CSTexture right) { return left.mTexture != right.mTexture; }

        unsafe public static implicit operator CSTexture(CSRenderTarget target) { return new CSTexture((NativeTexture*)target.mRenderTarget); }

        unsafe public static CSTexture Create(string name) {
            fixed (char* namePtr = name)
                return new CSTexture(_Create(new CSString(namePtr, name.Length)));
        }
        unsafe public static CSTexture Create(string name, int sizeX, int sizeY, BufferFormat fmt = BufferFormat.FORMAT_R8G8B8A8_UNORM) {
            fixed (char* namePtr = name) {
                var tex = new CSTexture(_Create(new CSString(namePtr, name.Length)));
                tex.SetSize(new Int2(sizeX, sizeY));
                //tex.SetFormat(fmt);
                return tex;
            }
        }
    }
    public partial struct CSRenderTarget : IEquatable<CSRenderTarget> {
        unsafe public bool IsValid() { return mRenderTarget != null; }
        unsafe public void SetSize(Int2 size) { SetSize(mRenderTarget, size); }
        unsafe public Int2 GetSize() { return GetSize(mRenderTarget); }
        unsafe public void SetFormat(BufferFormat format) { SetFormat(mRenderTarget, format); }
        unsafe public BufferFormat GetFormat() { return GetFormat(mRenderTarget); }
        unsafe public void SetMipCount(int count) { SetMipCount(mRenderTarget, count); }
        unsafe public int GetMipCount() { return GetMipCount(mRenderTarget); }
        unsafe public void Dispose() { Dispose(mRenderTarget); }

        public override bool Equals(object? obj) { return obj is CSTexture texture && Equals(texture); }
        unsafe public bool Equals(CSRenderTarget other) { return mRenderTarget == other.mRenderTarget; }
        unsafe public override int GetHashCode() { return HashCode.Combine((ulong)mRenderTarget); }
        unsafe public override string ToString() { return $"RT{(nint)mRenderTarget}"; }
        unsafe public static bool operator ==(CSRenderTarget left, CSRenderTarget right) { return left.mRenderTarget == right.mRenderTarget; }
        unsafe public static bool operator !=(CSRenderTarget left, CSRenderTarget right) { return left.mRenderTarget != right.mRenderTarget; }

        unsafe public static CSRenderTarget Create(string name) {
            fixed (char* namePtr = name)
                return new CSRenderTarget(_Create(new CSString(namePtr, name.Length)));
        }
    }
    public partial struct CSFont : IEquatable<CSFont> {
        unsafe public bool IsValid() { return mFont != null; }
        unsafe public CSTexture GetTexture() { return new CSTexture(GetTexture(mFont)); }
        unsafe public int GetLineHeight() { return GetLineHeight(mFont); }
        unsafe public int GetGlyphId(char chr) { return GetGlyphId(mFont, chr); }
        unsafe public CSGlyph GetGlyph(int id) { return *GetGlyph(mFont, id); }
        unsafe public int GetKerning(char c1, char c2) { return GetKerning(mFont, c1, c2); }

        public override bool Equals(object? obj) { return obj is CSFont font && Equals(font); }
        unsafe public bool Equals(CSFont other) { return mFont == other.mFont; }
        unsafe public override int GetHashCode() { return HashCode.Combine((ulong)mFont); }
        unsafe public static bool operator ==(CSFont left, CSFont right) { return left.mFont == right.mFont; }
        unsafe public static bool operator !=(CSFont left, CSFont right) { return left.mFont != right.mFont; }
    }
    public partial struct CSMaterial : IEquatable<CSMaterial>, IDisposable {
        unsafe public bool IsValid() { return mMaterial != null; }
        unsafe public void SetRenderPass(CSIdentifier identifier) {
            SetRenderPass(mMaterial, identifier);
        }
        unsafe public void SetValue(CSIdentifier identifier, int value) {
            CSMaterial.SetValueInt(mMaterial, identifier, &value, 1);
        }
        unsafe public void SetValue(CSIdentifier identifier, float value) {
            CSMaterial.SetValueFloat(mMaterial, identifier, &value, 1);
        }
        unsafe public void SetValue(CSIdentifier identifier, Vector2 value) {
            CSMaterial.SetValueFloat(mMaterial, identifier, &value.X, 2);
        }
        unsafe public void SetValue(CSIdentifier identifier, Vector3 value) {
            CSMaterial.SetValueFloat(mMaterial, identifier, &value.X, 3);
        }
        unsafe public void SetValue(CSIdentifier identifier, Vector4 value) {
            CSMaterial.SetValueFloat(mMaterial, identifier, &value.X, 4);
        }
        unsafe public void SetValue(CSIdentifier identifier, Matrix4x4 value) {
            CSMaterial.SetValueFloat(mMaterial, identifier, &value.M11, 16);
        }
        unsafe public void SetTexture(CSIdentifier identifier, CSTexture texture) {
            CSMaterial.SetValueTexture(mMaterial, identifier, texture);
        }
        unsafe public void SetTexture(CSIdentifier identifier, CSRenderTarget texture) {
            CSMaterial.SetValueTexture(mMaterial, identifier, new CSTexture((NativeTexture*)texture.mRenderTarget));
        }
        unsafe private T GetValueData<T>(CSIdentifier identifier) where T : unmanaged {
            var res = CSMaterial.GetValueData(mMaterial, identifier);
            if (res.mSize >= sizeof(T)) return *(T*)res.mData;
            return default;
        }
        unsafe public int GetInt(CSIdentifier identifier) {
            return GetValueData<int>(identifier);
        }
        unsafe public float GetFloat(CSIdentifier identifier) {
            return GetValueData<float>(identifier);
        }
        unsafe public Vector2 GetVector2(CSIdentifier identifier) {
            return GetValueData<Vector2>(identifier);
        }
        unsafe public Vector3 GetVector3(CSIdentifier identifier) {
            return GetValueData<Vector3>(identifier);
        }
        unsafe public Vector4 GetVector4(CSIdentifier identifier) {
            return GetValueData<Vector4>(identifier);
        }
        unsafe public Matrix4x4 GetMatrix(CSIdentifier identifier) {
            return GetValueData<Matrix4x4>(identifier);
        }

        unsafe public void SetBlendMode(BlendMode mode) { SetBlendMode(mMaterial, &mode); }
        unsafe public void SetRasterMode(RasterMode mode) { SetRasterMode(mMaterial, &mode); }
        unsafe public void SetDepthMode(DepthMode mode) { SetDepthMode(mMaterial, &mode); }

        unsafe public void InheritProperties(CSMaterial other) { InheritProperties(mMaterial, other.mMaterial); }

        public unsafe void Dispose() { Dispose(mMaterial); }
        public unsafe static CSMaterial Create() {
            return new CSMaterial(_Create(default));
        }
        public unsafe static CSMaterial Create(string shaderPath) {
            fixed (char* shaderPathPtr = shaderPath) {
                var csstr = new CSString(shaderPathPtr, shaderPath.Length);
                return new CSMaterial(_Create(csstr));
            }
        }
        public unsafe static CSSpan ResolveResources(NativeGraphics* graphics, NativePipeline* pipeline, IList<CSMaterial> materials) {
            var usmaterials = stackalloc CSMaterial[materials.Count];
            for (int m = 0; m < materials.Count; ++m) usmaterials[m] = materials[m];
            var resources = ResolveResources(graphics, pipeline, new CSSpan(usmaterials, materials.Count));
            return resources;
        }

        public override bool Equals(object? obj) { return obj is CSMaterial mat && mat == this; }
        unsafe public bool Equals(CSMaterial other) { return mMaterial == other.mMaterial; }
        unsafe public override int GetHashCode() { return ((int)mMaterial ^ (int)((ulong)mMaterial >> 32)); }
        unsafe public override string ToString() {
            if (mMaterial == null) return "-none-";
            var items = stackalloc CSIdentifier[20];
            int count = GetParameterIdentifiers(mMaterial, items, 20);
            StringBuilder builder = new();
            builder.Append("{");
            for (int i = 0; i < count; ++i) {
                if (i != 0) builder.Append(", ");
                builder.Append(items[i].GetId() + ": " + items[i].GetName());
            }
            builder.Append("}");
            return builder.ToString();
        }
        unsafe public static bool operator ==(CSMaterial m1, CSMaterial m2) { return m1.mMaterial == m2.mMaterial; }
        unsafe public static bool operator !=(CSMaterial m1, CSMaterial m2) { return m1.mMaterial != m2.mMaterial; }
    }
    unsafe public partial struct CSBufferElement {
        public CSBufferElement(CSIdentifier name, BufferFormat format) : this(name, format, 0, null) {
            mBufferStride = (ushort)BufferFormatType.GetMeta(format).GetByteSize();
        }
        public CSBufferElement(CSIdentifier name, BufferFormat format, int stride, void* data) {
            mBindName = name;
            mFormat = format;
            mBufferStride = (ushort)stride;
            mData = data;
        }
    }
    unsafe public partial struct CSBufferLayout {
        public Span<CSBufferElement> GetElements() {
            return new Span<CSBufferElement>(mElements, mElementCount);
        }
    }
    public partial struct CSMeshData {
        unsafe public Span<Vector3> GetPositions() {
            Debug.Assert(mPositions.mBufferStride == sizeof(Vector3));
            return new Span<Vector3>((Vector3*)mPositions.mData, mVertexCount);
        }
    }
    public partial struct CSMesh : IEquatable<CSMesh>, IComparable<CSMesh> {
        unsafe public bool IsValid() { return mMesh != null; }
        unsafe public CSMeshData GetMeshData() {
            CSMeshData data;
            GetMeshData(&data);
            return data;
        }
        unsafe public int GetVertexCount() { return GetVertexCount(mMesh); }
        unsafe public int GetIndexCount() { return GetIndexCount(mMesh); }
        unsafe public void SetVertexCount(int count) { SetVertexCount(mMesh, count); }
        unsafe public void SetIndexCount(int count) { SetIndexCount(mMesh, count); }
        unsafe public CSBufferLayout* GetVertexBuffer() { return GetVertexBuffer(mMesh); }
        unsafe public CSBufferLayout* GetIndexBuffer() { return GetIndexBuffer(mMesh); }
        unsafe public void RequireVertexNormals(BufferFormat fmt = BufferFormat.FORMAT_R32G32B32_FLOAT) { RequireVertexNormals(mMesh, (byte)fmt); }
        unsafe public void RequireVertexTexCoods(BufferFormat fmt = BufferFormat.FORMAT_R32G32_FLOAT) { RequireVertexTexCoords(mMesh, (byte)fmt); }
        unsafe public void RequireVertexColors(BufferFormat fmt = BufferFormat.FORMAT_R8G8B8A8_UNORM) { RequireVertexColors(mMesh, (byte)fmt); }
        unsafe public void GetMeshData(CSMeshData* meshdata) { GetMeshData(mMesh, meshdata); }
        unsafe public CSMaterial GetMaterial() { return new CSMaterial(GetMaterial(mMesh)); }
        unsafe public readonly BoundingBox GetBoundingBox() { return *GetBoundingBox(mMesh); }
        public unsafe static CSMesh Create(string name) {
            fixed (char* shaderPathPtr = name) {
                var csstr = new CSString(shaderPathPtr, name.Length);
                return new CSMesh(_Create(default));
            }
        }
        unsafe public override string ToString() {
            if (mMesh == null) return "-none-";
            var data = GetMeshData();
            return "Mesh {" + data.mName.ToString() + "}";
        }
        public static bool operator ==(CSMesh left, CSMesh right) { return left.Equals(right); }
        public static bool operator !=(CSMesh left, CSMesh right) { return !(left == right); }
        unsafe public bool Equals(CSMesh other) { return mMesh == other.mMesh; }
        unsafe public int CompareTo(CSMesh other) { return (int)(mMesh - other.mMesh); }
        unsafe public override bool Equals([NotNullWhen(true)] object? obj) { return obj is CSMesh o && o.mMesh == mMesh; }
        unsafe public override int GetHashCode() { return HashCode.Combine((nint)mMesh); }
    }
    public partial struct CSModel {
        unsafe public int GetMeshCount() { return GetMeshCount(mModel); }
        unsafe public CSMesh GetMesh(int i) { return GetMesh(mModel, i); }
        unsafe public CSSpanSPtrWapped<CSMesh> Meshes {
            get {
                var meshes = GetMeshes(mModel);
                return new CSSpanSPtrWapped<CSMesh>(meshes);
            }
        }
    }
    public partial struct CSResources {
        unsafe public static NativeShader* LoadShader(string path, string entry) {
            fixed (char* pathPtr = path, entryPtr = entry) {
                return LoadShader(
                    new CSString(pathPtr, path.Length),
                    new CSString(entryPtr, entry.Length)
                );
            }
        }
        unsafe public static CSModel LoadModel(string str) {
            fixed (char* chrs = str) {
                return new CSModel(LoadModel(new CSString(chrs, str.Length)));
            }
        }
        unsafe public static CSTexture LoadTexture(string str) {
            fixed (char* chrs = str) {
                return new CSTexture(LoadTexture(new CSString(chrs, str.Length)));
            }
        }
        unsafe public static CSFont LoadFont(string str) {
            fixed (char* chrs = str) {
                return new CSFont(LoadFont(new CSString(chrs, str.Length)));
            }
        }
    }
    public partial struct CSInput {
        unsafe public bool IsValid() { return mPlatform != null; }
        unsafe public CSSpanSPtr<CSPointer> GetPointers() { return new CSSpanSPtr<CSPointer>(GetPointers(null, mPlatform)); }
        unsafe public bool GetKeyDown(char key) { return GetKeyDown(null, mPlatform, (sbyte)key); }
        unsafe public bool GetKeyPressed(char key) { return GetKeyPressed(null, mPlatform, (sbyte)key); }
        unsafe public bool GetKeyReleased(char key) { return GetKeyReleased(null, mPlatform, (sbyte)key); }
    }
    public partial struct CSConstantBuffer {
        unsafe public CSIdentifier mName => mConstantBuffer->mName;
        unsafe public int mSize => mConstantBuffer->mSize;
        unsafe public int mBindPoint => mConstantBuffer->mBindPoint;
        unsafe public Span<CSUniformValue> GetValues() { return GetValues(mConstantBuffer).AsSpan<CSUniformValue>(); }
        public override string ToString() { return $"{mName} +{mSize}"; }
    }
    public partial struct CSUniformValue {
        public override string ToString() { return $"{mName} @{mOffset}"; }
    }
    public partial struct CSResourceBinding {
        public override string ToString() { return $"{mName} @{mBindPoint}"; }
    }
    public partial struct CSPipeline {
        unsafe public bool IsValid() { return mPipeline != null; }
        unsafe public int GetBindingCount() { return GetExpectedBindingCount(mPipeline); }
        unsafe public int GetConstantBufferCount() { return GetExpectedConstantBufferCount(mPipeline); }
        unsafe public int GetResourceCount() { return GetExpectedResourceCount(mPipeline); }
        unsafe public Span<CSConstantBuffer> GetConstantBuffers() { return GetConstantBuffers(mPipeline).AsSpan<CSConstantBuffer>(); }
        unsafe public CSSpanPtr<CSResourceBinding> GetResources() { return new CSSpanPtr<CSResourceBinding>(GetResources(mPipeline)); }
        unsafe public static implicit operator NativePipeline*(CSPipeline p) { return p.mPipeline; }
    }
    public partial struct CSGraphics {
        unsafe public void Dispose() { Dispose(mGraphics); mGraphics = null; }
        unsafe public Int2 GetResolution() { return GetResolution(mGraphics); }
        unsafe public void SetRenderTargets(CSRenderTargetBinding colorTarget, CSRenderTargetBinding depth) {
            SetRenderTargets(mGraphics, colorTarget.mTarget != null ? new CSSpan(&colorTarget, 1) : default, depth);
        }
        unsafe public void SetRenderTargets(Span<CSRenderTarget> targets, CSRenderTarget depth) {
            var nativeTargets = stackalloc CSRenderTargetBinding[targets.Length];
            for (int i = 0; i < targets.Length; ++i) nativeTargets[i] = new CSRenderTargetBinding(targets[i].mRenderTarget);
            SetRenderTargets(mGraphics, new CSSpan(nativeTargets, targets.Length), new CSRenderTargetBinding(depth.mRenderTarget));
        }
        unsafe public void SetRenderTargets(MemoryBlock<CSRenderTargetBinding> targets, CSRenderTargetBinding depth) {
            SetRenderTargets(mGraphics, new CSSpan(targets.Data, targets.Length), depth);
        }
        unsafe public bool IsTombstoned() { return IsTombstoned(mGraphics) != 0; }
        unsafe public void SetResolution(Int2 res) { SetResolution(mGraphics, res); }
        unsafe public void Reset() { Reset(mGraphics); }
        unsafe public void Clear() { Clear(mGraphics); }
        unsafe public void SetViewport(RectI viewport) { SetViewport(mGraphics, viewport); }
        unsafe public void Execute() { Execute(mGraphics); }
        unsafe public nint RequireConstantBuffer(Span<byte> data) {
            fixed (byte* dataPtr = data) {
                return (nint)RequireConstantBuffer(mGraphics, new CSSpan(dataPtr, data.Length));
            }
        }
        unsafe public CSPipeline RequirePipeline(Span<CSBufferLayout> bindings, NativeShader* vertexShader, NativeShader* pixelShader, void* materialState, Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros, CSIdentifier renderPass) {
            //var usbindings = stackalloc CSBufferLayout[bindings.Length];
            //for (int b = 0; b < bindings.Length; ++b) usbindings[b] = bindings[b];
            fixed (CSBufferLayout* usbindings = bindings)
            fixed (KeyValuePair<CSIdentifier, CSIdentifier>* usmacros = macros) {
                return new CSPipeline(RequirePipeline(
                    mGraphics,
                    new CSSpan(usbindings, bindings.Length),
                    vertexShader, pixelShader,
                    materialState,
                    new CSSpan(usmacros, macros.Length),
                    renderPass
                ));
            }
        }
        unsafe public CSPipeline RequirePipeline(List<CSBufferLayout> bindings, List<CSMaterial> materials, CSIdentifier renderPass) {
            return RequirePipeline(CollectionsMarshal.AsSpan(bindings), CollectionsMarshal.AsSpan(materials), renderPass);
        }
        //[SuppressMessage("Reliability", "CA2014:Do not use stackalloc in loops", Justification = "Is fine bruh")]
        unsafe public CSPipeline RequirePipeline(Span<CSBufferLayout> bindings, Span<CSMaterial> materials, CSIdentifier renderPass) {
            var usbindings = stackalloc CSBufferLayout[bindings.Length];
            var usbindingPtrs = stackalloc CSBufferLayout*[bindings.Length];
            for (int b = 0; b < bindings.Length; ++b) {
                usbindings[b] = bindings[b];
                usbindingPtrs[b] = &usbindings[b];
            }
            var usmaterials = stackalloc CSMaterial[materials.Length];
            for (var m = 0; m < materials.Length; ++m) usmaterials[m] = materials[m];
            return new CSPipeline(RequirePipeline(mGraphics,
                new CSSpan(usbindingPtrs, bindings.Length),
                new CSSpan(usmaterials, materials.Length)
            ));
        }
        unsafe public CSPipeline RequirePipeline(CSSpan bindings, CSSpan materials, CSIdentifier renderPass) {
            return new CSPipeline(RequirePipeline(mGraphics, bindings, materials));
        }
        unsafe public void Draw(CSPipeline pso, IList<CSBufferLayout> bindings, CSSpan resources, CSDrawConfig drawConfig, int instanceCount = 1) {
            var usbindings = stackalloc CSBufferLayout[bindings.Count];
            for (int b = 0; b < bindings.Count; ++b) usbindings[b] = bindings[b];
            Draw(pso, new CSSpan(usbindings, bindings.Count), resources, drawConfig, instanceCount);
        }
        unsafe public void Draw(CSPipeline pso, CSSpan bindings, CSSpan resources, CSDrawConfig drawConfig, int instanceCount = 1) {
            Draw(mGraphics, pso, bindings, resources, drawConfig, instanceCount);
        }
        unsafe public MemoryBlock<T> RequireFrameData<T>(List<T> inData) where T : unmanaged {
            return RequireFrameData(CollectionsMarshal.AsSpan(inData));
        }
        unsafe public MemoryBlock<T> RequireFrameData<T>(Span<T> inData) where T : unmanaged {
            var outData = RequireFrameData<T>(inData.Length);
            for (int i = 0; i < outData.Length; i++) outData[i] = inData[i];
            return outData;
        }
        unsafe public MemoryBlock<T> RequireFrameData<T>(int count) where T: unmanaged {
            return new MemoryBlock<T>((T*)RequireFrameData(mGraphics, sizeof(T) * count), count);
        }
        unsafe public CSSpan ImmortalizeBufferLayout(CSSpan data) {
            return ImmortalizeBufferLayout(mGraphics, data);
        }
        unsafe public override int GetHashCode() { return (int)(mGraphics) ^ (int)((ulong)mGraphics >> 32); }
        unsafe public ulong GetGlobalPSOHash() { return GetGlobalPSOHash(mGraphics); }
        unsafe public static implicit operator NativeGraphics*(CSGraphics g) { return g.mGraphics; }
    }
    public partial struct CSRenderPass {
        public readonly unsafe Frustum GetFrustum() { return *GetFrustum(mRenderPass); }
        unsafe public void SetViewProjection(Matrix4x4 view, Matrix4x4 proj) { SetViewProjection(mRenderPass, &view, &proj); }
        unsafe public ref Matrix4x4 GetView() { return ref *GetView(mRenderPass); }
        unsafe public ref Matrix4x4 GetProjection() { return ref *GetProjection(mRenderPass); }
        unsafe public void AddInstance(CSInstance instance, CSMesh mesh, IList<CSMaterial> materials) {
            var usmaterials = stackalloc CSMaterial[materials.Count];
            for (int m = 0; m < materials.Count; ++m) usmaterials[m] = materials[m];
            AddInstance(mRenderPass, instance, mesh, new CSSpan(usmaterials, materials.Count));
        }
        unsafe public void RemoveInstance(CSInstance instance) { RemoveInstance(mRenderPass, instance); }
        unsafe public void SetVisible(CSInstance instance, bool visible) { SetVisible(mRenderPass, instance, (byte)(visible ? 1 : 0)); }
        unsafe public CSMaterial GetOverrideMaterial() { return new CSMaterial(GetOverrideMaterial(mRenderPass)); }
        unsafe public void SetTargetTexture(CSRenderTarget target) { SetTargetTexture(mRenderPass, target.mRenderTarget); }
        unsafe public CSRenderTarget GetTargetTexture() { return new CSRenderTarget(GetTargetTexture(mRenderPass)); }
        unsafe public void Bind(CSGraphics graphics) { Bind(mRenderPass, graphics); }
        unsafe public void Render(CSGraphics graphics) { Render(mRenderPass, graphics); }
        unsafe public void AppendDraw(CSGraphics graphics, CSPipeline pipeline, IList<CSBufferLayout> bindings, CSSpan resources, Int2 instanceRange) {
            var usbindings = stackalloc CSBufferLayout[bindings.Count];
            for (int b = 0; b < bindings.Count; ++b) usbindings[b] = bindings[b];
            var ibindings = graphics.ImmortalizeBufferLayout(new CSSpan(usbindings, bindings.Count));
            AppendDraw(mRenderPass, graphics, pipeline, ibindings, resources, instanceRange);
        }
    }
    public partial struct CSScene {
        unsafe public void Dispose() { Dispose(mScene); mScene = null; }
        unsafe public CSTexture GetGPUBuffer() { return new CSTexture(GetGPUBuffer(mScene)); }
        unsafe public int GetGPURevision() { return GetGPURevision(mScene); }
        unsafe public CSMaterial GetRootMaterial() { return new CSMaterial(GetRootMaterial(mScene)); }
        unsafe public CSInstance CreateInstance() { return new CSInstance(CreateInstance(mScene)); }
        unsafe public void UpdateInstanceData(CSInstance instance, int offset, void* data, int dataLen) { UpdateInstanceData(mScene, instance, offset, (byte*)data, dataLen); }
        unsafe public MemoryBlock<Vector4> GetInstanceData(CSInstance instance) { return GetInstanceData(mScene, instance).AsMemoryBlock<Vector4>(); }
        unsafe public CSRenderPass GetBasePass() { return new CSRenderPass(GetBasePass(mScene)); }
        unsafe public CSRenderPass GetShadowPass() { return new CSRenderPass(GetShadowPass(mScene)); }
        public unsafe CSRenderPass CreateRenderPass(string name) {
            fixed (char* namePtr = name) {
                return new CSRenderPass(CSRenderPass.Create(mScene, new CSString(namePtr, name.Length)));
            }
        }
        unsafe public void Render(CSGraphics graphics) { Render(mScene, graphics); }
    }
    public partial struct CSWindow {
        unsafe public void Dispose() { Dispose(mWindow); mWindow = null; }
        unsafe public Int2 GetResolution() { return GetResolution(mWindow); }
    }
    public partial struct Platform {
        unsafe public void Dispose() { Dispose(mPlatform); mPlatform = null; }
        unsafe public CSInput GetInput() { return new CSInput(mPlatform); }
        unsafe public CSWindow GetWindow() { return new CSWindow(GetWindow(mPlatform)); }
        unsafe public CSResources GetResources() { return new CSResources(); }
        unsafe public CSGraphics CreateGraphics() { return new CSGraphics(CreateGraphics(mPlatform)); }
        unsafe public CSScene CreateScene() { return new CSScene(CreateScene(mPlatform)); }
        unsafe public int MessagePump() { return MessagePump(mPlatform); }
        unsafe public void Present() { Present(mPlatform); }
    }





    public struct BlendMode : IEquatable<BlendMode> {
        public enum BlendArg : byte { Zero, One, SrcColor, SrcInvColor, SrcAlpha, SrcInvAlpha, DestColor, DestInvColor, DestAlpha, DestInvAlpha, };
        public enum BlendOp : byte { Add, Sub, RevSub, Min, Max, };
        public BlendArg mSrcAlphaBlend;
        public BlendArg mDestAlphaBlend;
        public BlendArg mSrcColorBlend;
        public BlendArg mDestColorBlend;
        public BlendOp mBlendAlphaOp;
        public BlendOp mBlendColorOp;
        public BlendMode(BlendArg srcAlpha, BlendArg destAlpha, BlendArg srcColor, BlendArg destColor, BlendOp blendAlpha, BlendOp blendColor) {
            mSrcAlphaBlend = srcAlpha;
            mDestAlphaBlend = destAlpha;
            mSrcColorBlend = srcColor;
            mDestColorBlend = destColor;
            mBlendAlphaOp = blendAlpha;
            mBlendColorOp = blendColor;
        }
        public static BlendMode MakeOpaque() { return new BlendMode(BlendArg.One, BlendArg.Zero, BlendArg.One, BlendArg.Zero, BlendOp.Add, BlendOp.Add); }
        public static BlendMode MakeAlphaBlend() { return new BlendMode(BlendArg.SrcAlpha, BlendArg.SrcInvAlpha, BlendArg.SrcAlpha, BlendArg.SrcInvAlpha, BlendOp.Add, BlendOp.Add); }
        public static BlendMode MakeAdditive() { return new BlendMode(BlendArg.One, BlendArg.One, BlendArg.SrcAlpha, BlendArg.SrcInvAlpha, BlendOp.Add, BlendOp.Add); }
        public static BlendMode MakePremultiplied() { return new BlendMode(BlendArg.One, BlendArg.SrcInvAlpha, BlendArg.One, BlendArg.SrcInvAlpha, BlendOp.Add, BlendOp.Add); }

        public override bool Equals(object? obj) { return obj is BlendMode mode && Equals(mode); }
        public bool Equals(BlendMode other) {
            return mSrcAlphaBlend == other.mSrcAlphaBlend && mDestAlphaBlend == other.mDestAlphaBlend &&
                   mSrcColorBlend == other.mSrcColorBlend && mDestColorBlend == other.mDestColorBlend &&
                   mBlendAlphaOp == other.mBlendAlphaOp && mBlendColorOp == other.mBlendColorOp;
        }
        public override int GetHashCode() {
            return HashCode.Combine(mSrcAlphaBlend, mDestAlphaBlend, mSrcColorBlend, mDestColorBlend, mBlendAlphaOp, mBlendColorOp);
        }
        public static bool operator ==(BlendMode left, BlendMode right) { return left.Equals(right); }
        public static bool operator !=(BlendMode left, BlendMode right) { return !(left == right); }
    }
    public struct RasterMode : IEquatable<RasterMode> {
        public enum CullModes : byte { None = 1, Front = 2, Back = 3, };
        public CullModes mCullMode;
        public RasterMode(CullModes mode = CullModes.Back) { mCullMode = mode; }
        public RasterMode SetCull(CullModes mode) { mCullMode = mode; return this; }
        public static RasterMode MakeDefault() { return new RasterMode() { mCullMode = CullModes.Back, }; }

        public override bool Equals(object? obj) { return obj is RasterMode mode && Equals(mode); }
        public bool Equals(RasterMode other) { return mCullMode == other.mCullMode; }
        public override int GetHashCode() { return HashCode.Combine(mCullMode); }
        public static bool operator ==(RasterMode left, RasterMode right) { return left.Equals(right); }
        public static bool operator !=(RasterMode left, RasterMode right) { return !(left == right); }
    }
    public struct DepthMode : IEquatable<DepthMode> {
        public enum Comparisons : byte { Never = 1, Less, Equal, LEqual, Greater, NEqual, GEqual, Always, };
        public Comparisons mComparison;
        public bool mWriteEnable;
        public DepthMode(Comparisons c = Comparisons.Less, bool write = true) {
            mComparison = c;
            mWriteEnable = write;
        }
        public static DepthMode MakeOff() { return new DepthMode(Comparisons.Always, false); }
        public static DepthMode MakeReadOnly(Comparisons comparison = Comparisons.LEqual) { return new DepthMode(comparison, false); }
        public static DepthMode MakeDefault(Comparisons comparison = Comparisons.LEqual) { return new DepthMode(comparison, true); }

        public override bool Equals(object? obj) { return obj is DepthMode mode && Equals(mode); }
        public bool Equals(DepthMode other) { return mComparison == other.mComparison && mWriteEnable == other.mWriteEnable; }
        public override int GetHashCode() { return HashCode.Combine(mComparison, mWriteEnable); }
        public static bool operator ==(DepthMode left, DepthMode right) { return left.Equals(right); }
        public static bool operator !=(DepthMode left, DepthMode right) { return !(left == right); }
    }
    public partial struct CSDrawConfig {
        public static CSDrawConfig MakeDefault() { return new CSDrawConfig(0, -1); }
    }


    public static class MatrixExt {
        public static Matrix4x4 RHSToLHS(this Matrix4x4 mat) {
            mat.M31 *= -1f;
            mat.M32 *= -1f;
            mat.M33 *= -1f;
            mat.M34 *= -1f;
            return mat;
        }
    }

}
