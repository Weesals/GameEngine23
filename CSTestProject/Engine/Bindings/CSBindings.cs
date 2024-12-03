using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.Utility;

namespace Weesals.Engine {
    public partial struct Bool {
        public static implicit operator Bool(bool b) { return new Bool(b); }
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
        unsafe public Span<byte> AsSpan() {
            return new Span<byte>(mBuffer, mSize);
        }
        unsafe public ulong ComputeStringHash() {
            ulong hash = 0;
            foreach (var chr in AsSpan()) {
                hash = hash * 3074457345618258799ul + chr;
            }
            return hash;
        }
    }
    public partial struct CSIdentifier : IEquatable<CSIdentifier>, IComparable<CSIdentifier> {
        unsafe public CSIdentifier(string str) {
            if (str == null) { mId = Invalid.mId; return; }
            fixed (char* chrs = str) {
                mId = GetIdentifier(new CSString(chrs, str.Length));
            }
        }
        unsafe public CSIdentifier(Span<byte> str) {
            if (str == null) { mId = Invalid.mId; return; }
            fixed (byte* chrs = str) {
                mId = GetIdentifier(new CSString8((sbyte*)chrs, str.Length));
            }
        }
        public int GetId() { return mId; }
        public bool IsValid => mId != 0;
        public Span<byte> GetAscii() { return GetName(mId).AsSpan(); }
        public string GetName() { return GetName(mId).ToString(); }
        public int GetStableHash() { return (int)GetName(mId).ComputeStringHash(); }
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
        public static CSSpan Create<T>(MemoryBlock<T> block) where T : unmanaged { return new(block.Data, block.Length); }
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
    public struct CSSpanPtr<T> : IEnumerable<T> where T : unmanaged {
        CSSpan array;
        public int Length => array.mSize;
        unsafe public ref T this[int index] {
            get => ref *((T**)array.mData)[index];
        }
        public CSSpanPtr(CSSpan _array) { array = _array; }
        public struct Enumerator : IEnumerator, IEnumerator<T> {
            private CSSpanPtr<T> span;
            private int index;
            public ref T Current => ref span[index];
            T IEnumerator<T>.Current => Current;
            object IEnumerator.Current => Current;
            public Enumerator(CSSpanPtr<T> _span) { span = _span; index = -1; }
            public void Dispose() { }
            public void Reset() { index = -1; }
            public bool MoveNext() { return ++index < span.Length; }
        }
        public Enumerator GetEnumerator() { return new Enumerator(this); }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    unsafe public static class CSSpanExt {
        public static CSSpan AsCSSpan<T>(this MemoryBlock<T> block) where T : unmanaged { return new CSSpan(block.Data, block.Length); }
    }
    unsafe public partial struct CSTexture : IEquatable<CSTexture>, IComparable<CSTexture> {
        public bool IsValid => mTexture != null;
        public BufferFormat Format => IsValid ? GetFormat() : default;
        public Int2 Size => IsValid ? GetSize() : default;
        public int MipCount => IsValid ? GetMipCount() : default;
        public int ArrayCount => IsValid ? GetArrayCount() : default;
        public CSTexture SetSize(Int2 size) { SetSize(mTexture, new Int3(size, 1)); return this; }
        public CSTexture SetSize3D(Int3 size) { SetSize(mTexture, size); return this; }
        public Int2 GetSize() { return GetSize(mTexture).XY; }
        public Int3 GetSize3D() { return GetSize(mTexture); }
        public CSTexture SetFormat(BufferFormat fmt) { SetFormat(mTexture, fmt); return this; }
        public BufferFormat GetFormat() { return GetFormat(mTexture); }
        public CSTexture SetMipCount(int count) { SetMipCount(mTexture, count); return this; }
        public int GetMipCount() { return GetMipCount(mTexture); }
        public CSTexture SetArrayCount(int count) { SetArrayCount(mTexture, count); return this; }
        public int GetArrayCount() { return GetArrayCount(mTexture); }
        public CSTexture SetAllowUnorderedAccess(bool enable) { SetAllowUnorderedAccess(mTexture, enable); return this; }
        public bool GetAllowUnorderedAccess() { return GetAllowUnorderedAccess(mTexture); }
        public MemoryBlock<byte> GetTextureData(int mip = 0, int slice = 0) { var data = GetTextureData(mTexture, mip, slice); return new MemoryBlock<byte>((byte*)data.mData, data.mSize); }
        public void MarkChanged() { MarkChanged(mTexture); }
        public void Swap(CSTexture other) { Swap(mTexture, other.mTexture); }
        public void Dispose() { Dispose(mTexture); mTexture = null; }

        public int CompareTo(CSTexture other) { return ((nint)other.mTexture).CompareTo((nint)other.mTexture); }
        public override bool Equals(object? obj) { return obj is CSTexture texture && Equals(texture); }
        public bool Equals(CSTexture other) { return mTexture == other.mTexture; }
        public override int GetHashCode() { return HashCode.Combine((ulong)mTexture); }
        public override string ToString() { return $"Size<{Size}> Fmt<{Format}>"; }
        public static bool operator ==(CSTexture left, CSTexture right) { return left.mTexture == right.mTexture; }
        public static bool operator !=(CSTexture left, CSTexture right) { return left.mTexture != right.mTexture; }

        public static implicit operator CSTexture(CSRenderTarget target) { return new CSTexture((NativeTexture*)target.mRenderTarget); }

        public static CSTexture Create(string name) {
            fixed (char* namePtr = name)
                return new CSTexture(_Create(new CSString(namePtr, name.Length)));
        }
        public static CSTexture Create(string name, int sizeX, int sizeY, BufferFormat fmt = BufferFormat.FORMAT_R8G8B8A8_UNORM) {
            fixed (char* namePtr = name) {
                var tex = new CSTexture(_Create(new CSString(namePtr, name.Length)));
                tex.SetSize(new Int2(sizeX, sizeY));
                tex.SetFormat(fmt);
                return tex;
            }
        }
    }
    public partial struct CSRenderTarget : IEquatable<CSRenderTarget> {
        unsafe public bool IsValid => mRenderTarget != null;
        public Int2 Size => IsValid ? GetSize() : default;
        public BufferFormat Format => IsValid ? GetFormat(): default;
        public int MipCount => IsValid ? GetMipCount() : default;
        unsafe public void SetSize(Int2 size) { SetSize(mRenderTarget, size); }
        unsafe public Int2 GetSize() { return GetSize(mRenderTarget); }
        unsafe public void SetFormat(BufferFormat format) { SetFormat(mRenderTarget, format); }
        unsafe public BufferFormat GetFormat() { return GetFormat(mRenderTarget); }
        unsafe public void SetMipCount(int count) { SetMipCount(mRenderTarget, count); }
        unsafe public int GetMipCount() { return GetMipCount(mRenderTarget); }
        unsafe public void Dispose() { Dispose(mRenderTarget); mRenderTarget = null; }

        public override bool Equals(object? obj) { return obj is CSTexture texture && Equals(texture); }
        unsafe public bool Equals(CSRenderTarget other) { return mRenderTarget == other.mRenderTarget; }
        unsafe public override int GetHashCode() { return HashCode.Combine((ulong)mRenderTarget); }
        unsafe public override string ToString() { return $"RT{(nint)mRenderTarget:x} Size<{Size}> Fmt<{Format}>"; }
        unsafe public static bool operator ==(CSRenderTarget left, CSRenderTarget right) { return left.mRenderTarget == right.mRenderTarget; }
        unsafe public static bool operator !=(CSRenderTarget left, CSRenderTarget right) { return left.mRenderTarget != right.mRenderTarget; }

        unsafe public static CSRenderTarget Create(string name) {
            fixed (char* namePtr = name)
                return new CSRenderTarget(_Create(new CSString(namePtr, name.Length)));
        }
    }
    unsafe public partial struct CSRenderTargetBinding : IEquatable<CSRenderTargetBinding> {
        public CSRenderTargetBinding(CSRenderTarget target) : this(target.mRenderTarget) { }
        public bool Equals(CSRenderTargetBinding other) {
            return mTarget == other.mTarget && mMip == other.mMip && mSlice == other.mSlice;
        }
        public static implicit operator CSRenderTargetBinding(CSRenderTarget target) => new(target);
    }
    public partial struct CSFont : IEquatable<CSFont> {
        unsafe public bool IsValid => mFont != null;
        unsafe public CSTexture GetTexture() { return new CSTexture(GetTexture(mFont)); }
        unsafe public int GetLineHeight() { return GetLineHeight(mFont); }
        unsafe public int GetGlyphCount() { return GetGlyphCount(mFont); }
        unsafe public int GetGlyphId(char chr) { return GetGlyphId(mFont, chr); }
        unsafe public CSGlyph GetGlyph(int id) { return *GetGlyph(mFont, id); }
        unsafe public int GetKerning(char c1, char c2) { return GetKerning(mFont, c1, c2); }
        unsafe public int GetKerningCount() { return GetKerningCount(mFont); }
        unsafe public void GetKernings(MemoryBlock<ushort> pairs) { GetKernings(mFont, CSSpan.Create(pairs)); }

        public override bool Equals(object? obj) { return obj is CSFont font && Equals(font); }
        unsafe public bool Equals(CSFont other) { return mFont == other.mFont; }
        unsafe public override int GetHashCode() { return HashCode.Combine((ulong)mFont); }
        unsafe public static bool operator ==(CSFont left, CSFont right) { return left.mFont == right.mFont; }
        unsafe public static bool operator !=(CSFont left, CSFont right) { return left.mFont != right.mFont; }
    }
    unsafe public partial struct CSBufferReference : IEquatable<CSBufferReference> {
        public enum BufferTypes : byte { None, Texture, RenderTarget, Buffer, };
        public void* mBuffer;
        public short mSubresourceId;
        public short mSubresourceCount;
        public BufferTypes mType;
        public BufferFormat mFormat;
        public short mPadding;

        public bool IsValid => mBuffer != null;
        public CSBufferReference(CSTexture texture) : this(texture.mTexture) { }
        public CSBufferReference(NativeTexture* texture) {
            mBuffer = texture;
            mType = BufferTypes.Texture;
            mSubresourceId = 0;
            mSubresourceCount = -1;
        }
        public CSBufferReference(CSRenderTarget texture) : this(texture.mRenderTarget) { }
        public CSBufferReference(NativeRenderTarget* texture) {
            mBuffer = texture;
            mType = BufferTypes.RenderTarget;
            mSubresourceId = 0;
            mSubresourceCount = -1;
        }
        public CSBufferReference(CSBufferLayout buffer, int from = 0, int length = -1) {
            mBuffer = (void*)buffer.identifier;
            mType = BufferTypes.Buffer;
            mSubresourceId = (short)from;
            mSubresourceCount = (short)length;
            Debug.Assert(from < ushort.MaxValue, "From too large for a view");
            Debug.Assert(length < ushort.MaxValue, "Length too large for a view");
        }
        public CSTexture AsTexture() {
            return mType == BufferTypes.Texture ? new CSTexture((NativeTexture*)mBuffer) : default;
        }
        public CSRenderTarget AsRenderTarget() {
            return mType == BufferTypes.RenderTarget ? new CSRenderTarget((NativeRenderTarget*)mBuffer) : default;
        }
        public Int2 GetTextureResolution() {
            return mType == BufferTypes.Texture ? new CSTexture((NativeTexture*)mBuffer).GetSize()
                : mType == BufferTypes.RenderTarget ? new CSRenderTarget((NativeRenderTarget*)mBuffer).GetSize()
                : default;
        }
        public static implicit operator CSBufferReference(CSTexture tex) { return new(tex); }
        public static implicit operator CSBufferReference(CSRenderTarget target) { return new(target); }
        public static implicit operator CSBufferReference(CSBufferLayout buffer) { return new(buffer); }
        public bool Equals(CSBufferReference other) {
            return mBuffer == other.mBuffer && mSubresourceId == other.mSubresourceId && mSubresourceCount == other.mSubresourceCount;
        }
    }
    unsafe public partial struct CSBufferElement {
        public CSBufferElement(CSIdentifier name, BufferFormat format) : this(name, format, 0, null) {
            mBufferStride = (ushort)BufferFormatType.GetMeta(format).GetByteSize();
        }
        public CSBufferElement(CSIdentifier name, BufferFormat format, int stride, void* data = null) {
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
        unsafe public void SetAllowUnorderedAccess(bool enable) { identifier &= ~(1ul << 63); if (enable) identifier |= (1ul << 63); }
        unsafe public bool GetAllowUnorderedAccess() { return (identifier & (1ul << 63)) != 0; }
        public override int GetHashCode() {
            return (int)identifier + revision * 374761393;
        }
    }
    public partial struct CSResources {
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
        unsafe public bool IsValid => mInput != null;
        unsafe public CSSpanSPtr<CSPointer> GetPointers() { return new CSSpanSPtr<CSPointer>(GetPointers(null, mInput)); }
        unsafe public bool GetKeyDown(KeyCode key) { return GetKeyDown(null, mInput, (byte)key); }
        unsafe public bool GetKeyPressed(KeyCode key) { return GetKeyPressed(null, mInput, (byte)key); }
        unsafe public bool GetKeyReleased(KeyCode key) { return GetKeyReleased(null, mInput, (byte)key); }
        unsafe public Span<CSKey> GetPressKeys() { return GetPressKeys(null, mInput).AsSpan<CSKey>(); }
        unsafe public Span<CSKey> GetDownKeys() { return GetDownKeys(null, mInput).AsSpan<CSKey>(); }
        unsafe public Span<CSKey> GetReleaseKeys() { return GetReleaseKeys(null, mInput).AsSpan<CSKey>(); }
        unsafe public Span<ushort> GetCharBuffer() { return GetCharBuffer(null, mInput).AsSpan<ushort>(); }
        unsafe public void ReceiveTickEvent() { ReceiveTickEvent(null, mInput); }
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
        public override int GetHashCode() { return (mName.mId * 53) + mOffset * 1237; }
    }
    public partial struct CSResourceBinding {
        public override string ToString() { return $"{mName} @{mBindPoint}"; }
    }
    public partial struct CSPipeline {
        unsafe public bool IsValid => mPipeline != null;
        unsafe public CSIdentifier Name => GetName(mPipeline);
        unsafe public bool HasStencilState => GetHasStencilState();
        unsafe public int BindingCount => GetBindingCount();
        unsafe public int ConstantBufferCount => GetConstantBufferCount();
        unsafe public int ResourceCount => GetResourceCount();
        unsafe public bool GetHasStencilState() { return GetHasStencilState(mPipeline) != 0; }
        unsafe public int GetBindingCount() { return GetExpectedBindingCount(mPipeline); }
        unsafe public int GetConstantBufferCount() { return GetExpectedConstantBufferCount(mPipeline); }
        unsafe public int GetResourceCount() { return GetExpectedResourceCount(mPipeline); }
        unsafe public Span<CSConstantBuffer> GetConstantBuffers() { return GetConstantBuffers(mPipeline).AsSpan<CSConstantBuffer>(); }
        unsafe public CSSpanPtr<CSResourceBinding> GetResources() { return new CSSpanPtr<CSResourceBinding>(GetResources(mPipeline)); }
        unsafe public static implicit operator NativePipeline*(CSPipeline p) { return p.mPipeline; }
        public override unsafe int GetHashCode() { return (int)mPipeline ^ (int)((ulong)mPipeline >> 32); }
        public override string ToString() { return Name.ToString(); }
    }
    public partial struct CSGraphicsSurface {
        unsafe public bool IsValid => mSurface != null;
        unsafe public void RegisterDenyPresent(int delta = 1) { RegisterDenyPresent(mSurface, delta); }
        unsafe public CSRenderTarget GetBackBuffer() { return new CSRenderTarget(GetBackBuffer(mSurface)); }
        unsafe public Int2 GetResolution() { return GetResolution(mSurface); }
        unsafe public void SetResolution(Int2 res) { SetResolution(mSurface, res); }
        unsafe public void Present() { Present(mSurface); }
        unsafe public void Dispose() { Dispose(mSurface); }
    }
    public partial struct CSPreprocessedShader {
        unsafe public CSString8 GetSourceRaw() { return GetSource(mShader); }
        unsafe public string GetSource() { return GetSource(mShader).ToString(); }
        unsafe public int GetIncludeCount() { return GetIncludeFileCount(mShader); }
        unsafe public string GetInclude(int id) { return GetIncludeFile(mShader, id).ToString(); }
        unsafe public void Dispose() { Dispose(mShader); }
        unsafe public override string ToString() {
            var src = GetSourceRaw();
            return Encoding.UTF8.GetString((byte*)src.mBuffer, src.mSize);
        }
    }
    public partial struct CSCompiledShader {
        public unsafe struct CSConstantBuffer {
            public CSConstantBufferData Data;
            public int ValueCount;
            public CSUniformValue* Values;
            public CSIdentifier mName => Data.mName;
            public int mSize => Data.mSize;
            public int mBindPoint => Data.mBindPoint;
        }
        unsafe static CSCompiledShader() {
            Trace.Assert(sizeof(CSUniformValue) == 4 * 4);
            Trace.Assert(sizeof(CSConstantBuffer) == 24);
        }
        unsafe static public CSCompiledShader Create(string name, int byteSize, int cbcount, int rbcount, int ipcount) {
            return new(_Create(new(name), byteSize, cbcount, rbcount, ipcount));
        }
        unsafe public bool IsValid => mShader != null;
        unsafe public void InitializeValues(int cb, int vcount) { InitializeValues(mShader, cb, vcount); }
        unsafe public Span<CSUniformValue> GetValues(int cb) { return GetValues(mShader, cb).AsSpan<CSUniformValue>(); }
        unsafe public Span<CSConstantBuffer> GetConstantBuffers() { return GetConstantBuffers(mShader).AsSpan<CSConstantBuffer>(); }
        unsafe public Span<CSResourceBinding> GetResources() { return GetResources(mShader).AsSpan<CSResourceBinding>(); }
        unsafe public Span<CSInputParameter> GetInputParameters() { return GetInputParameters(mShader).AsSpan<CSInputParameter>(); }
        unsafe public Span<byte> GetBinaryData() { return GetBinaryData(mShader).AsSpan<byte>(); }
        unsafe public ShaderStats GetStatistics() { return *GetStatistics(mShader); }
    }
    unsafe public partial struct CSGraphics {
        public struct AsyncReadback {
            NativeGraphics* mGraphics;
            nint mReadback;
            public AsyncReadback(NativeGraphics* graphics, nint readback) {
                mGraphics = graphics;
                mReadback = readback;
            }
            public bool GetIsDone() => GetReadbackResult(mGraphics, (ulong)mReadback) >= 0;
            public int GetDataSize() => GetReadbackResult(mGraphics, (ulong)mReadback);
            public void ReadAndDispose(MemoryBlock<byte> data) => CopyAndDisposeReadback(mGraphics, (ulong)mReadback, new CSSpan(data.Data, data.Length));
            public void ReadAndDispose(Span<byte> data) {
                fixed (byte* dataPtr = data) { ReadAndDispose(new MemoryBlock<byte>(dataPtr, data.Length)); }
            }
            public struct Awaiter : INotifyCompletion {
                private AsyncReadback Readback;
                public bool IsCompleted => Readback.GetIsDone();
                public AsyncReadback GetResult() => Readback;
                public Awaiter(AsyncReadback readback) {
                    Readback = readback;
                }
                public void OnCompleted(Action continuation) {
                    lock (onComplete) {
                        onComplete.Add(Readback.mReadback, continuation);
                    }
                }
                public static void InvokeCallbacks(CSGraphics graphics) {
                    using var done = new PooledList<nint>(4);
                    lock (onComplete) {
                        foreach (var callback in onComplete) {
                            var readback = new AsyncReadback(graphics.mGraphics, callback.Key);
                            if (readback.GetIsDone()) { done.Add(callback.Key); break; }
                        }
                    }
                    foreach (var item in done) {
                        onComplete[item]();
                    }
                    lock (onComplete) {
                        foreach (var item in done) {
                            onComplete.Remove(item);
                        }
                    }
                }
                private static Dictionary<nint, Action> onComplete = new();
            }
            public Awaiter GetAwaiter() => new(this);
        }

        public void Dispose() { Dispose(mGraphics); mGraphics = null; }
        public CSIdentifier GetDeviceName() { return new CSIdentifier(GetDeviceName(mGraphics)); }
        public CSGraphicsCapabilities GetCapabiltiies() { return GetCapabilities(mGraphics); }
        public CSRenderStatistics GetRenderStatistics() { return GetRenderStatistics(mGraphics); }
        public CSGraphicsSurface CreateSurface(CSWindow window) { return new CSGraphicsSurface(CreateSurface(mGraphics, window.GetNativeWindow())); }
        public void SetSurface(CSGraphicsSurface surface) { SetSurface(mGraphics, surface.GetNativeSurface()); }
        public CSGraphicsSurface GetSurface() { return new CSGraphicsSurface(GetSurface(mGraphics)); }
        public void SetRenderTargets(CSRenderTargetBinding colorTarget, CSRenderTargetBinding depth) {
            SetRenderTargets(mGraphics, colorTarget.mTarget != null ? new CSSpan(&colorTarget, 1) : default, depth);
        }
        [SkipLocalsInit]
        public void SetRenderTargets(Span<CSRenderTarget> targets, CSRenderTarget depth) {
            var nativeTargets = stackalloc CSRenderTargetBinding[targets.Length];
            for (int i = 0; i < targets.Length; ++i) nativeTargets[i] = new CSRenderTargetBinding(targets[i].mRenderTarget);
            SetRenderTargets(mGraphics, new CSSpan(nativeTargets, targets.Length), new CSRenderTargetBinding(depth.mRenderTarget));
        }
        public void SetRenderTargets(MemoryBlock<CSRenderTargetBinding> targets, CSRenderTargetBinding depth) {
            SetRenderTargets(mGraphics, CSSpan.Create(targets), depth);
        }
        public bool IsTombstoned() { return IsTombstoned(mGraphics) != 0; }
        public void Reset() { Reset(mGraphics); }
        public void Clear() { Clear(new(CSClearConfig.GetInvalidColor(), 1f)); }
        public void Clear(CSClearConfig clear) { Clear(mGraphics, clear); }
        public void SetViewport(RectI viewport) { SetViewport(mGraphics, viewport); }
        public void Execute() { Execute(mGraphics); }
        public void Wait() { Wait(mGraphics); }
        public nint RequireConstantBuffer(MemoryBlock<byte> data, ulong hash = 0) {
            return (nint)RequireConstantBuffer(mGraphics, CSSpan.Create(data), (nuint)hash);
        }
        static public CSPreprocessedShader PreprocessShader(string path, Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros) {
            fixed (char* pathPtr = path)
            fixed (KeyValuePair<CSIdentifier, CSIdentifier>* usmacros = macros) {
                return new CSPreprocessedShader(PreprocessShader(
                    new CSString(pathPtr, path.Length),
                    new CSSpan(usmacros, macros.Length)
                ));
            }
        }
        public CSCompiledShader CompileShader(CSString8 source, string entry, CSIdentifier profile, string dbgFilename) {
            fixed (char* dbgFilenamePtr = dbgFilename)
            fixed (char* entryPtr = entry) {
                return new CSCompiledShader(CompileShader(mGraphics, source,
                    new CSString(entryPtr, entry.Length), profile, new CSString(dbgFilenamePtr, dbgFilename.Length)));
            }
        }
        public CSPipeline RequirePipeline(Span<CSBufferLayout> bindings,
            CSCompiledShader vertexShader, CSCompiledShader pixelShader, void* materialState) {
            fixed (CSBufferLayout* usbindings = bindings) {
                return new CSPipeline(RequirePipeline(
                    mGraphics,
                    new CSSpan(usbindings, bindings.Length),
                    vertexShader.GetNativeShader(),
                    pixelShader.GetNativeShader(),
                    materialState
                ));
            }
        }
        public CSPipeline RequireMeshPipeline(Span<CSBufferLayout> bindings,
            CSCompiledShader meshShader, CSCompiledShader pixelShader, void* materialState) {
            fixed (CSBufferLayout* usbindings = bindings) {
                return new CSPipeline(RequireMeshPipeline(
                    mGraphics,
                    new CSSpan(usbindings, bindings.Length),
                    meshShader.GetNativeShader(),
                    pixelShader.GetNativeShader(),
                    materialState
                ));
            }
        }
        public CSPipeline RequireComputePSO(CSCompiledShader computeShader) {
            return new CSPipeline(RequireComputePSO(
                mGraphics,
                computeShader.GetNativeShader()
            ));
        }
        public void CopyBufferData(CSBufferLayout buffer) {
            Span<RangeInt> ranges = stackalloc RangeInt[] { new(-1, buffer.size) };
            CopyBufferData(buffer, ranges);
        }
        public void CopyBufferData(CSBufferLayout buffer, RangeInt range) {
            CopyBufferData(buffer, new Span<RangeInt>(ref range));
        }
        public void CopyBufferData(CSBufferLayout buffer, List<RangeInt> ranges) {
            CopyBufferData(buffer, CollectionsMarshal.AsSpan(ranges));
        }
        public void CopyBufferData(CSBufferLayout buffer, Span<RangeInt> ranges) {
            if (ranges.Length == 0) return;
            fixed (RangeInt* rangesPtr = ranges) {
                CopyBufferData(mGraphics, &buffer, new CSSpan(rangesPtr, ranges.Length));
            }
        }
        public void CopyBufferData(CSBufferLayout source, CSBufferLayout dest, RangeInt srcRange, int destOffset) {
            CopyBufferData(mGraphics, &source, &dest, srcRange.Start, destOffset, srcRange.Length);
        }
        public void CommitTexture(CSTexture texture) {
            CommitTexture(mGraphics, texture.mTexture);
        }
        public void CommitResources(CSPipeline pso, MemoryBlock<nint> resources) {
            foreach (var resource in resources.Slice(pso.ConstantBufferCount).Reinterpret<CSBufferReference>()) {
                if (resource.mType == CSBufferReference.BufferTypes.Texture) {
                    CommitTexture(new((NativeTexture*)resource.mBuffer));
                }
            }
        }

        private static ProfilerMarker ProfileMarker_Draw = new("Draw");
        [SkipLocalsInit]
        public void Draw(CSPipeline pso, IList<CSBufferLayout> bindings, CSSpan resources, CSDrawConfig drawConfig, int instanceCount = 1) {
            var usbindings = stackalloc CSBufferLayout[bindings.Count];
            for (int b = 0; b < bindings.Count; ++b) usbindings[b] = bindings[b];
            Draw(pso, new CSSpan(usbindings, bindings.Count), resources, drawConfig, instanceCount);
        }
        public void Draw(CSPipeline pso, CSSpan bindings, CSSpan resources, CSDrawConfig drawConfig, int instanceCount = 1) {
            using (var marker = ProfileMarker_Draw.Auto()) {
                Draw(mGraphics, pso, bindings, resources, drawConfig, instanceCount);
            }
        }
        public void DispatchCompute(CSPipeline pso, CSSpan resources, Int3 groupCount) {
            Dispatch(mGraphics, pso, resources, groupCount);
        }
        public MemoryBlock<T> RequireFrameData<T>(List<T> inData) where T : unmanaged {
            return RequireFrameData(CollectionsMarshal.AsSpan(inData));
        }
        public MemoryBlock<T> RequireFrameData<T>(Span<T> inData) where T : unmanaged {
            var outData = RequireFrameData<T>(inData.Length);
            for (int i = 0; i < outData.Length; i++) outData[i] = inData[i];
            return outData;
        }
        public MemoryBlock<T> RequireFrameData<T>(int count) where T: unmanaged {
            return new MemoryBlock<T>((T*)RequireFrameData(mGraphics, sizeof(T) * count), count);
        }
        public AsyncReadback CreateReadback(CSRenderTarget target) {
            return new(mGraphics, (nint)CreateReadback(mGraphics, target.mRenderTarget));
        }
        public override int GetHashCode() { return (int)(mGraphics) ^ (int)((ulong)mGraphics >> 32); }
        public ulong GetGlobalPSOHash() { return GetGlobalPSOHash(mGraphics); }
        public static implicit operator NativeGraphics*(CSGraphics g) { return g.mGraphics; }
    }
    public partial struct CSInstance {
        public override string ToString() { return GetInstanceId().ToString(); }
        public static implicit operator int(CSInstance instance) { return instance.mInstanceId; }
    }
    public partial struct CSWindow {
        unsafe public bool IsValid => mWindow != null;
        unsafe public bool IsAlive() { return GetStatus(mWindow) == 0; }
        unsafe public void Dispose() { Dispose(mWindow); mWindow = null; }
        unsafe public Int2 GetSize() { return GetSize(mWindow); }
        unsafe public void SetSize(Int2 size) { SetSize(mWindow, size); }
        unsafe public void SetStyle(string style) {
            fixed (char* str = style) {
                SetStyle(mWindow, new CSString(str, style.Length));
            }
        }
        unsafe public void SetVisible(bool visible) { SetVisible(mWindow, (byte)(visible ? 1 : 0)); }
        unsafe public void SetInput(CSInput input) { SetInput(mWindow, input.GetNativeInput()); }
        unsafe public CSWindowFrame GetWindowFrame() { return GetWindowFrame(mWindow); }
        unsafe public void SetWindowFrame(RectI frame, bool maximized) { SetWindowFrame(mWindow, &frame, (byte)(maximized ? 1 : 0)); }
        unsafe public void RegisterMovedCallback(Action callback, bool enable) {
            var handle = GCHandle.Alloc(callback);
            var addr = (delegate* unmanaged[Cdecl]<void>)Marshal.GetFunctionPointerForDelegate(callback);
            RegisterMovedCallback(mWindow, addr, (byte)(enable ? 1 : 0));
        }
    }
    public partial struct Platform {
        unsafe public void Dispose() { Dispose(mPlatform); mPlatform = null; }
        unsafe public void InitializeGraphics() { InitializeGraphics(mPlatform); }
        unsafe public CSWindow CreateWindow(string name) {
            fixed (char* namePtr = name)
                return new CSWindow(CreateWindow(mPlatform, new CSString(namePtr, name.Length)));
        }
        unsafe public CSResources GetResources() { return new CSResources(); }
        unsafe public CSInput CreateInput() { return new CSInput(CreateInput(mPlatform)); }
        unsafe public CSGraphics CreateGraphics() { return new CSGraphics(CreateGraphics(mPlatform)); }
        unsafe public int MessagePump() { return MessagePump(mPlatform); }
    }





    public struct BlendMode : IEquatable<BlendMode> {
        public enum BlendArg : byte {
            Zero, One,
            SrcColor, SrcInvColor, SrcAlpha, SrcInvAlpha,
            DestColor, DestInvColor, DestAlpha, DestInvAlpha,
            Src1Color, Src1InvColor, Src1Alpha, Src1InvAlpha,
        };
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

        public static BlendMode MakeOpaque() { return new BlendMode(BlendArg.One, BlendArg.Zero, BlendArg.One, BlendArg.Zero, BlendOp.Add, BlendOp.Add); }
        public static BlendMode MakeAlphaBlend() { return new BlendMode(BlendArg.SrcAlpha, BlendArg.SrcInvAlpha, BlendArg.SrcAlpha, BlendArg.SrcInvAlpha, BlendOp.Add, BlendOp.Add); }
        public static BlendMode MakeAdditive() { return new BlendMode(BlendArg.One, BlendArg.One, BlendArg.SrcAlpha, BlendArg.One, BlendOp.Add, BlendOp.Add); }
        public static BlendMode MakePremultiplied() { return new BlendMode(BlendArg.One, BlendArg.SrcInvAlpha, BlendArg.One, BlendArg.SrcInvAlpha, BlendOp.Add, BlendOp.Add); }
        public static BlendMode MakeNone() { return new BlendMode(BlendArg.Zero, BlendArg.One, BlendArg.Zero, BlendArg.One, BlendOp.Add, BlendOp.Add); }
    }
    public struct RasterMode : IEquatable<RasterMode> {
        public enum CullModes : byte { None = 1, Front = 2, Back = 3, };
        public CullModes mCullMode;
        public RasterMode(CullModes mode = CullModes.Back) { mCullMode = mode; }
        public RasterMode SetCull(CullModes mode) { mCullMode = mode; return this; }
        public static RasterMode MakeDefault() { return new RasterMode() { mCullMode = CullModes.Back, }; }
        public static RasterMode MakeNoCull() { return new RasterMode() { mCullMode = CullModes.None, }; }

        public override bool Equals(object? obj) { return obj is RasterMode mode && Equals(mode); }
        public bool Equals(RasterMode other) { return mCullMode == other.mCullMode; }
        public override int GetHashCode() { return HashCode.Combine(mCullMode); }
        public static bool operator ==(RasterMode left, RasterMode right) { return left.Equals(right); }
        public static bool operator !=(RasterMode left, RasterMode right) { return !(left == right); }
    }
    public struct DepthMode : IEquatable<DepthMode> {
        public enum Comparisons : byte { Never = 1, Less, Equal, LEqual, Greater, NEqual, GEqual, Always, };
        public enum Modes : byte { None = 0, DepthWrite = 1, StencilEnable = 2, };
        public enum StencilOp : byte { Keep = 1, Zero = 2, Replace = 3, IncrementSaturate = 4, DecrementSaturate = 5, Invert = 6, Increment = 7, Decrement = 8, };

        public struct StencilDesc {
            public StencilOp StencilFailOp;
            public StencilOp DepthFailOp;
            public StencilOp PassOp;
            public Comparisons Function;
            public StencilDesc(StencilOp stencilFailOp, StencilOp depthFailOp, StencilOp passOp, Comparisons function) {
                StencilFailOp = stencilFailOp;
                DepthFailOp = depthFailOp;
                PassOp = passOp;
                Function = function;
            }
            public static StencilDesc MakeDontChange(Comparisons comparison) {
                return new StencilDesc(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep, comparison);
            }
            public static readonly StencilDesc DefaultBack = new() { StencilFailOp = StencilOp.Keep, DepthFailOp = StencilOp.Keep, PassOp = StencilOp.Replace, Function = Comparisons.Equal, };
            public static readonly StencilDesc DefaultFront = new() { StencilFailOp = StencilOp.Keep, DepthFailOp = StencilOp.Keep, PassOp = StencilOp.Replace, Function = Comparisons.Equal, };
        }

        public Comparisons Comparison;
        public Modes Mode;
        public byte StencilReadMask = 0xff;
        public byte StencilWriteMask = 0xff;
        public StencilDesc StencilFront;
        public StencilDesc StencilBack;
        public bool DepthWrite { get => (Mode & Modes.DepthWrite) != 0; set => Mode = value ? Mode | Modes.DepthWrite : Mode & ~Modes.DepthWrite; }
        public bool StencilEnable { get => (Mode & Modes.StencilEnable) != 0; set => Mode = value ? Mode | Modes.DepthWrite : Mode & ~Modes.StencilEnable; }
        public DepthMode(Comparisons c = Comparisons.Less, bool write = true) {
            Comparison = c;
            DepthWrite = write;
        }
        public DepthMode SetStencil(byte readMask = 0xff, byte writeMask = 0xff) {
            return SetStencil(readMask, writeMask, StencilDesc.DefaultFront, StencilDesc.DefaultBack, true);
        }
        public DepthMode SetStencil(byte readMask, byte writeMask, StencilDesc stencilFront, StencilDesc stencilBack, bool enable = true) {
            if (enable) Mode |= Modes.StencilEnable; else Mode &= ~Modes.StencilEnable;
            StencilReadMask = readMask;
            StencilWriteMask = writeMask;
            StencilFront = stencilFront;
            StencilBack = stencilBack;
            return this;
        }
        public static DepthMode MakeOff() { return new DepthMode(Comparisons.Always, false); }
        public static DepthMode MakeReadOnly(Comparisons comparison = Comparisons.LEqual) { return new DepthMode(comparison, false); }
        public static DepthMode MakeDefault(Comparisons comparison = Comparisons.LEqual) { return new DepthMode(comparison, true); }
        public static DepthMode MakeWriteOnly(Comparisons comparison = Comparisons.Always) { return new DepthMode(comparison, true); }

        public override bool Equals(object? obj) { return obj is DepthMode mode && Equals(mode); }
        public bool Equals(DepthMode other) {
            bool same = Comparison == other.Comparison && Mode == other.Mode &&
                (!StencilEnable || (StencilReadMask == other.StencilReadMask && StencilWriteMask == other.StencilWriteMask));
            return same;
        }
        public override int GetHashCode() {
            var hash = HashCode.Combine(Comparison, Mode);
            if (StencilEnable) hash = HashCode.Combine(hash, StencilReadMask, StencilWriteMask);
            return hash;
        }

        public static bool operator ==(DepthMode left, DepthMode right) { return left.Equals(right); }
        public static bool operator !=(DepthMode left, DepthMode right) { return !(left == right); }
    }
    public partial struct CSDrawConfig {
        public static CSDrawConfig Default = new CSDrawConfig(0, -1);
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
