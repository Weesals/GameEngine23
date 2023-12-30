using System.Numerics;
using System.Runtime.InteropServices;

namespace Weesals.Engine
{
    public partial struct NativeMesh
    {
    }

    public partial struct NativeModel
    {
    }

    public partial struct NativeTexture
    {
    }

    public partial struct NativeBuffer
    {
    }

    public partial struct NativeRenderTarget
    {
    }

    public partial struct NativeMaterial
    {
    }

    public partial struct NativePipeline
    {
    }

    public partial struct NativeFont
    {
    }

    public partial struct NativeRenderPass
    {
    }

    public partial struct WindowBase
    {
    }

    public partial struct NativeShader
    {
    }

    public partial struct NativePlatform
    {
    }

    public partial struct NativeScene
    {
    }

    public partial struct NativeGraphics
    {
    }

    public partial struct PerformanceTest
    {

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?DoNothing@PerformanceTest@@SAMXZ", ExactSpelling = true)]
        public static extern float DoNothing();

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CSDLLInvoke@PerformanceTest@@SAMMM@Z", ExactSpelling = true)]
        public static extern float CSDLLInvoke(float f1, float f2);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CPPVirtual@PerformanceTest@@SAMXZ", ExactSpelling = true)]
        public static extern float CPPVirtual();

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CPPDirect@PerformanceTest@@SAMXZ", ExactSpelling = true)]
        public static extern float CPPDirect();
    }

    public partial struct Bool
    {
        [NativeTypeName("uint8_t")]
        public byte mValue;

        public Bool(bool value)
        {
            mValue = (byte)(value ? 1 : 0);
        }

        public bool ToBoolean()
        {
            return (mValue) != 0;
        }
    }

    public unsafe partial struct CSSpan
    {
        [NativeTypeName("const void *")]
        public void* mData;

        public int mSize;

        public CSSpan([NativeTypeName("const void *")] void* data, int size)
        {
            mData = data;
            mSize = size;
        }
    }

    public unsafe partial struct CSSpanSPtr
    {
        [NativeTypeName("const Ptr *")]
        public Ptr* mData;

        public int mSize;

        public CSSpanSPtr([NativeTypeName("const void *")] void* data, int size)
        {
            mData = (Ptr*)(data);
            mSize = size;
        }

        public unsafe partial struct Ptr
        {
            public void* mPointer;

            public void* mData;
        }
    }

    public unsafe partial struct CSString
    {
        [NativeTypeName("const wchar_t *")]
        public ushort* mBuffer;

        public int mSize;
    }

    public unsafe partial struct CSString8
    {
        [NativeTypeName("const char *")]
        public sbyte* mBuffer;

        public int mSize;

        public CSString8()
        {
            mBuffer = null;
            mSize = 0;
        }

        public CSString8([NativeTypeName("const char *")] sbyte* buffer, int size)
        {
            mBuffer = buffer;
            mSize = size;
        }
    }

    public partial struct CSIdentifier
    {
        [NativeTypeName("uint16_t")]
        public ushort mId;

        public CSIdentifier([NativeTypeName("uint16_t")] ushort id)
        {
            mId = id;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetName@CSIdentifier@@SA?AUCSString8@@G@Z", ExactSpelling = true)]
        public static extern CSString8 GetName([NativeTypeName("uint16_t")] ushort id);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetIdentifier@CSIdentifier@@SAGUCSString@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("uint16_t")]
        public static extern ushort GetIdentifier(CSString str);
    }

    public unsafe partial struct CSBufferElement
    {
        public CSIdentifier mBindName;

        [NativeTypeName("uint16_t")]
        public ushort mBufferStride;

        [NativeTypeName("BufferFormat")]
        public Weesals.Engine.BufferFormat mFormat;

        public void* mData;
    }

    public unsafe partial struct CSBufferLayout
    {
        [NativeTypeName("uint64_t")]
        public ulong identifier;

        public int revision;

        public int size;

        public CSBufferElement* mElements;

        [NativeTypeName("uint8_t")]
        public byte mElementCount;

        [NativeTypeName("uint8_t")]
        public byte mUsage;

        public int mOffset;

        public int mCount;
    }

    public unsafe partial struct CSRenderTargetBinding
    {
        public NativeRenderTarget* mTarget;

        public int mMip;

        public int mSlice;

        public CSRenderTargetBinding(NativeRenderTarget* target, int mip = 0, int slice = 0)
        {
            mTarget = target;
            mMip = mip;
            mSlice = slice;
        }
    }

    public unsafe partial struct CSTexture
    {
        public NativeTexture* mTexture;

        public CSTexture()
        {
            mTexture = null;
        }

        public CSTexture(NativeTexture* tex)
        {
            mTexture = tex;
        }

        public void SetTexture(NativeTexture* tex)
        {
            mTexture = tex;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetSize@CSTexture@@SAXPEAVTexture@@UInt2@@@Z", ExactSpelling = true)]
        public static extern void SetSize(NativeTexture* tex, [NativeTypeName("Int2")] Weesals.Engine.Int2 size);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetSize@CSTexture@@SA?AUInt2C@@PEAVTexture@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("Int2C")]
        public static extern Weesals.Engine.Int2 GetSize(NativeTexture* tex);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetFormat@CSTexture@@SAXPEAVTexture@@W4BufferFormat@@@Z", ExactSpelling = true)]
        public static extern void SetFormat(NativeTexture* tex, [NativeTypeName("BufferFormat")] Weesals.Engine.BufferFormat fmt);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetFormat@CSTexture@@SA?AW4BufferFormat@@PEAVTexture@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("BufferFormat")]
        public static extern Weesals.Engine.BufferFormat GetFormat(NativeTexture* tex);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetMipCount@CSTexture@@SAXPEAVTexture@@H@Z", ExactSpelling = true)]
        public static extern void SetMipCount(NativeTexture* tex, int count);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetMipCount@CSTexture@@SAHPEAVTexture@@@Z", ExactSpelling = true)]
        public static extern int GetMipCount(NativeTexture* tex);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetArrayCount@CSTexture@@SAXPEAVTexture@@H@Z", ExactSpelling = true)]
        public static extern void SetArrayCount(NativeTexture* tex, int count);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetArrayCount@CSTexture@@SAHPEAVTexture@@@Z", ExactSpelling = true)]
        public static extern int GetArrayCount(NativeTexture* tex);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetTextureData@CSTexture@@SA?AUCSSpan@@PEAVTexture@@HH@Z", ExactSpelling = true)]
        public static extern CSSpan GetTextureData(NativeTexture* tex, int mip, int slice);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?MarkChanged@CSTexture@@SAXPEAVTexture@@@Z", ExactSpelling = true)]
        public static extern void MarkChanged(NativeTexture* tex);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?_Create@CSTexture@@SAPEAVTexture@@UCSString@@@Z", ExactSpelling = true)]
        public static extern NativeTexture* _Create(CSString name);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@CSTexture@@SAXPEAVTexture@@@Z", ExactSpelling = true)]
        public static extern void Dispose(NativeTexture* tex);
    }

    public unsafe partial struct CSRenderTarget
    {
        public NativeRenderTarget* mRenderTarget;

        public CSRenderTarget()
        {
            mRenderTarget = null;
        }

        public CSRenderTarget(NativeRenderTarget* target)
        {
            mRenderTarget = target;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetSize@CSRenderTarget@@SA?AUInt2C@@PEAVRenderTarget2D@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("Int2C")]
        public static extern Weesals.Engine.Int2 GetSize(NativeRenderTarget* target);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetSize@CSRenderTarget@@SAXPEAVRenderTarget2D@@UInt2@@@Z", ExactSpelling = true)]
        public static extern void SetSize(NativeRenderTarget* target, [NativeTypeName("Int2")] Weesals.Engine.Int2 size);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetFormat@CSRenderTarget@@SA?AW4BufferFormat@@PEAVRenderTarget2D@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("BufferFormat")]
        public static extern Weesals.Engine.BufferFormat GetFormat(NativeRenderTarget* target);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetFormat@CSRenderTarget@@SAXPEAVRenderTarget2D@@W4BufferFormat@@@Z", ExactSpelling = true)]
        public static extern void SetFormat(NativeRenderTarget* target, [NativeTypeName("BufferFormat")] Weesals.Engine.BufferFormat format);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetMipCount@CSRenderTarget@@SAHPEAVRenderTarget2D@@@Z", ExactSpelling = true)]
        public static extern int GetMipCount(NativeRenderTarget* target);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetMipCount@CSRenderTarget@@SAXPEAVRenderTarget2D@@H@Z", ExactSpelling = true)]
        public static extern void SetMipCount(NativeRenderTarget* target, int size);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetArrayCount@CSRenderTarget@@SAHPEAVRenderTarget2D@@@Z", ExactSpelling = true)]
        public static extern int GetArrayCount(NativeRenderTarget* target);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetArrayCount@CSRenderTarget@@SAXPEAVRenderTarget2D@@H@Z", ExactSpelling = true)]
        public static extern void SetArrayCount(NativeRenderTarget* target, int size);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?_Create@CSRenderTarget@@SAPEAVRenderTarget2D@@UCSString@@@Z", ExactSpelling = true)]
        public static extern NativeRenderTarget* _Create(CSString name);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@CSRenderTarget@@SAXPEAVRenderTarget2D@@@Z", ExactSpelling = true)]
        public static extern void Dispose(NativeRenderTarget* target);
    }

    public partial struct CSGlyph
    {
        [NativeTypeName("wchar_t")]
        public ushort mGlyph;

        [NativeTypeName("Int2")]
        public Weesals.Engine.Int2 mAtlasOffset;

        [NativeTypeName("Int2")]
        public Weesals.Engine.Int2 mSize;

        [NativeTypeName("Int2")]
        public Weesals.Engine.Int2 mOffset;

        public int mAdvance;
    }

    public unsafe partial struct CSFont
    {
        private NativeFont* mFont;

        public CSFont(NativeFont* font)
        {
            mFont = font;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetTexture@CSFont@@SAPEAVTexture@@PEBVFontInstance@@@Z", ExactSpelling = true)]
        public static extern NativeTexture* GetTexture([NativeTypeName("const NativeFont *")] NativeFont* font);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetLineHeight@CSFont@@SAHPEBVFontInstance@@@Z", ExactSpelling = true)]
        public static extern int GetLineHeight([NativeTypeName("const NativeFont *")] NativeFont* font);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetKerning@CSFont@@SAHPEBVFontInstance@@_W1@Z", ExactSpelling = true)]
        public static extern int GetKerning([NativeTypeName("const NativeFont *")] NativeFont* font, [NativeTypeName("wchar_t")] ushort c1, [NativeTypeName("wchar_t")] ushort c2);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetGlyphId@CSFont@@SAHPEBVFontInstance@@_W@Z", ExactSpelling = true)]
        public static extern int GetGlyphId([NativeTypeName("const NativeFont *")] NativeFont* font, [NativeTypeName("wchar_t")] ushort chr);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetGlyph@CSFont@@SAAEBUCSGlyph@@PEBVFontInstance@@H@Z", ExactSpelling = true)]
        [return: NativeTypeName("const CSGlyph &")]
        public static extern CSGlyph* GetGlyph([NativeTypeName("const NativeFont *")] NativeFont* font, int id);
    }

    public unsafe partial struct CSMaterial
    {
        private NativeMaterial* mMaterial;

        public CSMaterial(NativeMaterial* material)
        {
            mMaterial = material;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetParameterIdentifiers@CSMaterial@@SAHPEAVMaterial@@PEAUCSIdentifier@@H@Z", ExactSpelling = true)]
        public static extern int GetParameterIdentifiers(NativeMaterial* material, CSIdentifier* outlist, int capacity);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetRenderPass@CSMaterial@@SAXPEAVMaterial@@UCSIdentifier@@@Z", ExactSpelling = true)]
        public static extern void SetRenderPass(NativeMaterial* material, CSIdentifier identifier);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetValueData@CSMaterial@@SA?AUCSSpan@@PEAVMaterial@@UCSIdentifier@@@Z", ExactSpelling = true)]
        public static extern CSSpan GetValueData(NativeMaterial* material, CSIdentifier identifier);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetValueType@CSMaterial@@SAHPEAVMaterial@@UCSIdentifier@@@Z", ExactSpelling = true)]
        public static extern int GetValueType(NativeMaterial* material, CSIdentifier identifier);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetValueFloat@CSMaterial@@SAXPEAVMaterial@@UCSIdentifier@@PEBMH@Z", ExactSpelling = true)]
        public static extern void SetValueFloat(NativeMaterial* material, CSIdentifier identifier, [NativeTypeName("const float *")] float* data, int count);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetValueInt@CSMaterial@@SAXPEAVMaterial@@UCSIdentifier@@PEBHH@Z", ExactSpelling = true)]
        public static extern void SetValueInt(NativeMaterial* material, CSIdentifier identifier, [NativeTypeName("const int *")] int* data, int count);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetValueTexture@CSMaterial@@SAXPEAVMaterial@@UCSIdentifier@@UCSTexture@@@Z", ExactSpelling = true)]
        public static extern void SetValueTexture(NativeMaterial* material, CSIdentifier identifier, CSTexture texture);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetBlendMode@CSMaterial@@SAXPEAVMaterial@@PEAX@Z", ExactSpelling = true)]
        public static extern void SetBlendMode(NativeMaterial* material, void* data);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetRasterMode@CSMaterial@@SAXPEAVMaterial@@PEAX@Z", ExactSpelling = true)]
        public static extern void SetRasterMode(NativeMaterial* material, void* data);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetDepthMode@CSMaterial@@SAXPEAVMaterial@@PEAX@Z", ExactSpelling = true)]
        public static extern void SetDepthMode(NativeMaterial* material, void* data);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?InheritProperties@CSMaterial@@SAXPEAVMaterial@@0@Z", ExactSpelling = true)]
        public static extern void InheritProperties(NativeMaterial* material, NativeMaterial* other);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RemoveInheritance@CSMaterial@@SAXPEAVMaterial@@0@Z", ExactSpelling = true)]
        public static extern void RemoveInheritance(NativeMaterial* material, NativeMaterial* other);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?ResolveResources@CSMaterial@@SA?AUCSSpan@@PEAVNativeGraphics@@PEAUPipelineLayout@@U2@@Z", ExactSpelling = true)]
        public static extern CSSpan ResolveResources(NativeGraphics* graphics, NativePipeline* pipeline, CSSpan materials);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?_Create@CSMaterial@@SAPEAVMaterial@@UCSString@@@Z", ExactSpelling = true)]
        public static extern NativeMaterial* _Create(CSString shaderPath);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@CSMaterial@@SAXPEAVMaterial@@@Z", ExactSpelling = true)]
        public static extern void Dispose(NativeMaterial* material);

        public NativeMaterial* GetNativeMaterial()
        {
            return mMaterial;
        }
    }

    public partial struct CSMeshData
    {
        public int mVertexCount;

        public int mIndexCount;

        public CSString8 mName;

        public CSBufferElement mPositions;

        public CSBufferElement mNormals;

        public CSBufferElement mTexCoords;

        public CSBufferElement mColors;

        public CSBufferElement mIndices;
    }

    public unsafe partial struct CSMesh
    {
        private NativeMesh* mMesh;

        public CSMesh(NativeMesh* mesh)
        {
            mMesh = mesh;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetVertexCount@CSMesh@@SAHPEBVMesh@@@Z", ExactSpelling = true)]
        public static extern int GetVertexCount([NativeTypeName("const NativeMesh *")] NativeMesh* mesh);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetIndexCount@CSMesh@@SAHPEBVMesh@@@Z", ExactSpelling = true)]
        public static extern int GetIndexCount([NativeTypeName("const NativeMesh *")] NativeMesh* mesh);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetVertexCount@CSMesh@@SAXPEAVMesh@@H@Z", ExactSpelling = true)]
        public static extern void SetVertexCount(NativeMesh* mesh, int count);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetIndexCount@CSMesh@@SAXPEAVMesh@@H@Z", ExactSpelling = true)]
        public static extern void SetIndexCount(NativeMesh* mesh, int count);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetVertexBuffer@CSMesh@@SAPEBUCSBufferLayout@@PEAVMesh@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("const CSBufferLayout *")]
        public static extern CSBufferLayout* GetVertexBuffer(NativeMesh* mesh);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetIndexBuffer@CSMesh@@SAPEBUCSBufferLayout@@PEAVMesh@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("const CSBufferLayout *")]
        public static extern CSBufferLayout* GetIndexBuffer(NativeMesh* mesh);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequireVertexNormals@CSMesh@@SAXPEAVMesh@@E@Z", ExactSpelling = true)]
        public static extern void RequireVertexNormals(NativeMesh* mesh, [NativeTypeName("uint8_t")] byte fmt);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequireVertexTexCoords@CSMesh@@SAXPEAVMesh@@E@Z", ExactSpelling = true)]
        public static extern void RequireVertexTexCoords(NativeMesh* mesh, [NativeTypeName("uint8_t")] byte fmt);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequireVertexColors@CSMesh@@SAXPEAVMesh@@E@Z", ExactSpelling = true)]
        public static extern void RequireVertexColors(NativeMesh* mesh, [NativeTypeName("uint8_t")] byte fmt);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetMeshData@CSMesh@@SAXPEBVMesh@@PEAUCSMeshData@@@Z", ExactSpelling = true)]
        public static extern void GetMeshData([NativeTypeName("const NativeMesh *")] NativeMesh* mesh, CSMeshData* outdata);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetMaterial@CSMesh@@SAPEAVMaterial@@PEAVMesh@@@Z", ExactSpelling = true)]
        public static extern NativeMaterial* GetMaterial(NativeMesh* mesh);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetBoundingBox@CSMesh@@SAAEBUBoundingBox@@PEAVMesh@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("const BoundingBox &")]
        public static extern Weesals.Engine.BoundingBox* GetBoundingBox(NativeMesh* mesh);

        public NativeMesh* GetNativeMesh()
        {
            return mMesh;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?_Create@CSMesh@@SAPEAVMesh@@UCSString@@@Z", ExactSpelling = true)]
        public static extern NativeMesh* _Create(CSString name);
    }

    public unsafe partial struct CSModel
    {
        private NativeModel* mModel;

        public CSModel(NativeModel* mesh)
        {
            mModel = mesh;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetMeshCount@CSModel@@SAHPEBVModel@@@Z", ExactSpelling = true)]
        public static extern int GetMeshCount([NativeTypeName("const NativeModel *")] NativeModel* model);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetMeshes@CSModel@@SA?AUCSSpanSPtr@@PEBVModel@@@Z", ExactSpelling = true)]
        public static extern CSSpanSPtr GetMeshes([NativeTypeName("const NativeModel *")] NativeModel* model);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetMesh@CSModel@@SA?AVCSMesh@@PEBVModel@@H@Z", ExactSpelling = true)]
        public static extern CSMesh GetMesh([NativeTypeName("const NativeModel *")] NativeModel* model, int id);

        public NativeModel* GetNativeModel()
        {
            return mModel;
        }
    }

    public partial struct CSInstance
    {
        private int mInstanceId;

        public CSInstance(int instanceId)
        {
            mInstanceId = instanceId;
        }

        public int GetInstanceId()
        {
            return mInstanceId;
        }
    }

    public partial struct CSUniformValue
    {
        public CSIdentifier mName;

        public int mOffset;

        public int mSize;
    }

    public partial struct CSConstantBufferData
    {
        public CSIdentifier mName;

        public int mSize;

        public int mBindPoint;
    }

    public unsafe partial struct CSConstantBuffer
    {
        private CSConstantBufferData* mConstantBuffer;

        public CSConstantBuffer(CSConstantBufferData* data)
        {
            mConstantBuffer = data;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetValues@CSConstantBuffer@@SA?AUCSSpan@@PEBUCSConstantBufferData@@@Z", ExactSpelling = true)]
        public static extern CSSpan GetValues([NativeTypeName("const CSConstantBufferData *")] CSConstantBufferData* cb);
    }

    public partial struct CSResourceBinding
    {
        public CSIdentifier mName;

        public int mBindPoint;

        public int mStride;

        [NativeTypeName("uint8_t")]
        public byte mType;
    }

    public unsafe partial struct CSPipeline
    {
        [NativeTypeName("const NativePipeline *")]
        private NativePipeline* mPipeline;

        public CSPipeline([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline)
        {
            mPipeline = pipeline;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetExpectedBindingCount@CSPipeline@@SAHPEBUPipelineLayout@@@Z", ExactSpelling = true)]
        public static extern int GetExpectedBindingCount([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetExpectedConstantBufferCount@CSPipeline@@SAHPEBUPipelineLayout@@@Z", ExactSpelling = true)]
        public static extern int GetExpectedConstantBufferCount([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetExpectedResourceCount@CSPipeline@@SAHPEBUPipelineLayout@@@Z", ExactSpelling = true)]
        public static extern int GetExpectedResourceCount([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetConstantBuffers@CSPipeline@@SA?AUCSSpan@@PEBUPipelineLayout@@@Z", ExactSpelling = true)]
        public static extern CSSpan GetConstantBuffers([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetResources@CSPipeline@@SA?AUCSSpan@@PEBUPipelineLayout@@@Z", ExactSpelling = true)]
        public static extern CSSpan GetResources([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetBindings@CSPipeline@@SA?AUCSSpan@@PEBUPipelineLayout@@@Z", ExactSpelling = true)]
        public static extern CSSpan GetBindings([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [return: NativeTypeName("const NativePipeline *")]
        public NativePipeline* GetNativePipeline()
        {
            return mPipeline;
        }
    }

    public partial struct CSDrawConfig
    {
        public int mIndexBase;

        public int mIndexCount;

        public CSDrawConfig(int indexStart, int indexCount)
        {
            mIndexBase = indexStart;
            mIndexCount = indexCount;
        }
    }

    public unsafe partial struct CSGraphics
    {
        private NativeGraphics* mGraphics;

        public CSGraphics(NativeGraphics* graphics)
        {
            mGraphics = graphics;
        }

        public NativeGraphics* GetNativeGraphics()
        {
            return mGraphics;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@CSGraphics@@CAXPEAVNativeGraphics@@@Z", ExactSpelling = true)]
        private static extern void Dispose(NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetResolution@CSGraphics@@CA?AUInt2C@@PEBVNativeGraphics@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("Int2C")]
        private static extern Weesals.Engine.Int2 GetResolution([NativeTypeName("const NativeGraphics *")] NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetResolution@CSGraphics@@CAXPEBVNativeGraphics@@UInt2@@@Z", ExactSpelling = true)]
        private static extern void SetResolution([NativeTypeName("const NativeGraphics *")] NativeGraphics* graphics, [NativeTypeName("Int2")] Weesals.Engine.Int2 res);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetRenderTargets@CSGraphics@@CAXPEAVNativeGraphics@@UCSSpan@@UCSRenderTargetBinding@@@Z", ExactSpelling = true)]
        private static extern void SetRenderTargets(NativeGraphics* graphics, CSSpan colorTargets, CSRenderTargetBinding depthTarget);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequirePipeline@CSGraphics@@CAPEBUPipelineLayout@@PEAVNativeGraphics@@UCSSpan@@1@Z", ExactSpelling = true)]
        [return: NativeTypeName("const NativePipeline *")]
        private static extern NativePipeline* RequirePipeline(NativeGraphics* graphics, CSSpan bindings, CSSpan materials);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequirePipeline@CSGraphics@@CAPEBUPipelineLayout@@PEAVNativeGraphics@@UCSSpan@@PEAVShader@@2PEAX1UCSIdentifier@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("const NativePipeline *")]
        private static extern NativePipeline* RequirePipeline(NativeGraphics* graphics, CSSpan bindings, NativeShader* vertexShader, NativeShader* pixelShader, void* materialState, CSSpan macros, CSIdentifier renderPass);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequireFrameData@CSGraphics@@CAPEAXPEAVNativeGraphics@@H@Z", ExactSpelling = true)]
        private static extern void* RequireFrameData(NativeGraphics* graphics, int byteSize);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?ImmortalizeBufferLayout@CSGraphics@@CA?AUCSSpan@@PEAVNativeGraphics@@U2@@Z", ExactSpelling = true)]
        private static extern CSSpan ImmortalizeBufferLayout(NativeGraphics* graphics, CSSpan bindings);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequireConstantBuffer@CSGraphics@@CAPEAXPEAVNativeGraphics@@UCSSpan@@@Z", ExactSpelling = true)]
        private static extern void* RequireConstantBuffer(NativeGraphics* graphics, CSSpan span);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CopyBufferData@CSGraphics@@CAXPEAVNativeGraphics@@PEBUCSBufferLayout@@UCSSpan@@@Z", ExactSpelling = true)]
        private static extern void CopyBufferData(NativeGraphics* graphics, [NativeTypeName("const CSBufferLayout *")] CSBufferLayout* layout, CSSpan ranges);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Draw@CSGraphics@@CAXPEAVNativeGraphics@@VCSPipeline@@UCSSpan@@2UCSDrawConfig@@H@Z", ExactSpelling = true)]
        private static extern void Draw(NativeGraphics* graphics, CSPipeline pipeline, CSSpan buffers, CSSpan resources, CSDrawConfig config, int instanceCount);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Reset@CSGraphics@@CAXPEAVNativeGraphics@@@Z", ExactSpelling = true)]
        private static extern void Reset(NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Clear@CSGraphics@@CAXPEAVNativeGraphics@@@Z", ExactSpelling = true)]
        private static extern void Clear(NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Execute@CSGraphics@@CAXPEAVNativeGraphics@@@Z", ExactSpelling = true)]
        private static extern void Execute(NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetViewport@CSGraphics@@CAXPEAVNativeGraphics@@URectInt@@@Z", ExactSpelling = true)]
        private static extern void SetViewport(NativeGraphics* graphics, [NativeTypeName("RectInt")] Weesals.Engine.RectI viewport);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?IsTombstoned@CSGraphics@@CA_NPEAVNativeGraphics@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("bool")]
        private static extern byte IsTombstoned(NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetGlobalPSOHash@CSGraphics@@CA_KPEAVNativeGraphics@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("uint64_t")]
        private static extern ulong GetGlobalPSOHash(NativeGraphics* graphics);
    }

    public unsafe partial struct CSWindow
    {
        [NativeTypeName("NativeWindow *")]
        private WindowBase* mWindow;

        public CSWindow([NativeTypeName("NativeWindow *")] WindowBase* window)
        {
            mWindow = window;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@CSWindow@@SAXPEAVWindowBase@@@Z", ExactSpelling = true)]
        public static extern void Dispose([NativeTypeName("NativeWindow *")] WindowBase* window);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetResolution@CSWindow@@SA?AUInt2C@@PEBVWindowBase@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("Int2C")]
        public static extern Weesals.Engine.Int2 GetResolution([NativeTypeName("const NativeWindow *")] WindowBase* window);
    }

    public unsafe partial struct CSRenderPass
    {
        private NativeRenderPass* mRenderPass;

        public CSRenderPass(NativeRenderPass* renderPass)
        {
            mRenderPass = renderPass;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetName@CSRenderPass@@SA?AUCSString8@@PEAVRenderPass@@@Z", ExactSpelling = true)]
        public static extern CSString8 GetName(NativeRenderPass* renderPass);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetFrustum@CSRenderPass@@SAAEBUFrustum@@PEAVRenderPass@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("const Frustum &")]
        public static extern Weesals.Engine.Frustum* GetFrustum(NativeRenderPass* renderPass);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetViewProjection@CSRenderPass@@SAXPEAVRenderPass@@AEBUMatrix@SimpleMath@DirectX@@1@Z", ExactSpelling = true)]
        public static extern void SetViewProjection(NativeRenderPass* renderPass, [NativeTypeName("const Matrix &")] System.Numerics.Matrix4x4* view, [NativeTypeName("const Matrix &")] System.Numerics.Matrix4x4* projection);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetView@CSRenderPass@@SAAEBUMatrix@SimpleMath@DirectX@@PEAVRenderPass@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("const Matrix &")]
        public static extern System.Numerics.Matrix4x4* GetView(NativeRenderPass* renderPass);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetProjection@CSRenderPass@@SAAEBUMatrix@SimpleMath@DirectX@@PEAVRenderPass@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("const Matrix &")]
        public static extern System.Numerics.Matrix4x4* GetProjection(NativeRenderPass* renderPass);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?AddInstance@CSRenderPass@@SAXPEAVRenderPass@@VCSInstance@@VCSMesh@@UCSSpan@@@Z", ExactSpelling = true)]
        public static extern void AddInstance(NativeRenderPass* renderPass, CSInstance instance, CSMesh mesh, CSSpan materials);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RemoveInstance@CSRenderPass@@SAXPEAVRenderPass@@VCSInstance@@@Z", ExactSpelling = true)]
        public static extern void RemoveInstance(NativeRenderPass* renderPass, CSInstance instance);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetVisible@CSRenderPass@@SAXPEAVRenderPass@@VCSInstance@@_N@Z", ExactSpelling = true)]
        public static extern void SetVisible(NativeRenderPass* renderPass, CSInstance instance, [NativeTypeName("bool")] byte visible);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetOverrideMaterial@CSRenderPass@@SAPEAVMaterial@@PEAVRenderPass@@@Z", ExactSpelling = true)]
        public static extern NativeMaterial* GetOverrideMaterial(NativeRenderPass* renderPass);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetTargetTexture@CSRenderPass@@SAXPEAVRenderPass@@PEAVRenderTarget2D@@@Z", ExactSpelling = true)]
        public static extern void SetTargetTexture(NativeRenderPass* renderPass, NativeRenderTarget* target);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetTargetTexture@CSRenderPass@@SAPEAVRenderTarget2D@@PEAVRenderPass@@@Z", ExactSpelling = true)]
        public static extern NativeRenderTarget* GetTargetTexture(NativeRenderPass* renderPass);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Bind@CSRenderPass@@SAXPEAVRenderPass@@PEAVNativeGraphics@@@Z", ExactSpelling = true)]
        public static extern void Bind(NativeRenderPass* renderPass, NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?AppendDraw@CSRenderPass@@SAXPEAVRenderPass@@PEAVNativeGraphics@@PEAUPipelineLayout@@UCSSpan@@3UInt2@@@Z", ExactSpelling = true)]
        public static extern void AppendDraw(NativeRenderPass* renderPass, NativeGraphics* graphics, NativePipeline* pipeline, CSSpan bindings, CSSpan resources, [NativeTypeName("Int2")] Weesals.Engine.Int2 instanceRange);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Render@CSRenderPass@@SAXPEAVRenderPass@@PEAVNativeGraphics@@@Z", ExactSpelling = true)]
        public static extern void Render(NativeRenderPass* renderPass, NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Create@CSRenderPass@@SAPEAVRenderPass@@PEAVNativeScene@@UCSString@@@Z", ExactSpelling = true)]
        public static extern NativeRenderPass* Create(NativeScene* scene, CSString name);
    }

    public unsafe partial struct CSScene
    {
        private NativeScene* mScene;

        public CSScene(NativeScene* scene)
        {
            mScene = scene;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@CSScene@@SAXPEAVNativeScene@@@Z", ExactSpelling = true)]
        public static extern void Dispose(NativeScene* scene);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetRootMaterial@CSScene@@SAPEAVMaterial@@PEAVNativeScene@@@Z", ExactSpelling = true)]
        public static extern NativeMaterial* GetRootMaterial(NativeScene* scene);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CreateInstance@CSScene@@SAHPEAVNativeScene@@@Z", ExactSpelling = true)]
        public static extern int CreateInstance(NativeScene* scene);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RemoveInstance@CSScene@@SAXPEAVNativeScene@@VCSInstance@@@Z", ExactSpelling = true)]
        public static extern void RemoveInstance(NativeScene* scene, CSInstance instance);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?UpdateInstanceData@CSScene@@SAXPEAVNativeScene@@VCSInstance@@HPEBEH@Z", ExactSpelling = true)]
        public static extern void UpdateInstanceData(NativeScene* scene, CSInstance instance, int offset, [NativeTypeName("const uint8_t *")] byte* data, int dataLen);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetInstanceData@CSScene@@SA?AUCSSpan@@PEAVNativeScene@@VCSInstance@@@Z", ExactSpelling = true)]
        public static extern CSSpan GetInstanceData(NativeScene* scene, CSInstance instance);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetGPUBuffer@CSScene@@SAPEAVTexture@@PEAVNativeScene@@@Z", ExactSpelling = true)]
        public static extern NativeTexture* GetGPUBuffer(NativeScene* scene);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetGPURevision@CSScene@@SAHPEAVNativeScene@@@Z", ExactSpelling = true)]
        public static extern int GetGPURevision(NativeScene* scene);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SubmitToGPU@CSScene@@SAXPEAVNativeScene@@PEAVNativeGraphics@@@Z", ExactSpelling = true)]
        public static extern void SubmitToGPU(NativeScene* scene, NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetBasePass@CSScene@@SAPEAVRenderPass@@PEAVNativeScene@@@Z", ExactSpelling = true)]
        public static extern NativeRenderPass* GetBasePass(NativeScene* scene);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetShadowPass@CSScene@@SAPEAVRenderPass@@PEAVNativeScene@@@Z", ExactSpelling = true)]
        public static extern NativeRenderPass* GetShadowPass(NativeScene* scene);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Render@CSScene@@SAXPEAVNativeScene@@PEAVNativeGraphics@@@Z", ExactSpelling = true)]
        public static extern void Render(NativeScene* scene, NativeGraphics* graphics);
    }

    public partial struct CSPointer
    {
        [NativeTypeName("unsigned int")]
        public uint mDeviceId;

        [NativeTypeName("Vector2")]
        public System.Numerics.Vector2 mPositionCurrent;

        [NativeTypeName("Vector2")]
        public System.Numerics.Vector2 mPositionPrevious;

        [NativeTypeName("Vector2")]
        public System.Numerics.Vector2 mPositionDown;

        public float mTotalDrag;

        [NativeTypeName("unsigned int")]
        public uint mCurrentButtonState;

        [NativeTypeName("unsigned int")]
        public uint mPreviousButtonState;
    }

    public unsafe partial struct CSInput
    {
        private NativePlatform* mPlatform;

        public CSInput(NativePlatform* platform)
        {
            mPlatform = platform;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetPointers@CSInput@@QEAA?AUCSSpanSPtr@@PEAVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern CSSpanSPtr GetPointers(CSInput* pThis, NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetKeyDown@CSInput@@QEAA?AUBool@@PEAVNativePlatform@@D@Z", ExactSpelling = true)]
        public static extern Bool GetKeyDown(CSInput* pThis, NativePlatform* platform, [NativeTypeName("char")] sbyte key);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetKeyPressed@CSInput@@QEAA?AUBool@@PEAVNativePlatform@@D@Z", ExactSpelling = true)]
        public static extern Bool GetKeyPressed(CSInput* pThis, NativePlatform* platform, [NativeTypeName("char")] sbyte key);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetKeyReleased@CSInput@@QEAA?AUBool@@PEAVNativePlatform@@D@Z", ExactSpelling = true)]
        public static extern Bool GetKeyReleased(CSInput* pThis, NativePlatform* platform, [NativeTypeName("char")] sbyte key);
    }

    public unsafe partial struct CSResources
    {

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?LoadShader@CSResources@@SAPEAVShader@@UCSString@@0@Z", ExactSpelling = true)]
        public static extern NativeShader* LoadShader(CSString path, CSString entryPoint);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?LoadModel@CSResources@@SAPEAVModel@@UCSString@@@Z", ExactSpelling = true)]
        public static extern NativeModel* LoadModel(CSString path);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?LoadTexture@CSResources@@SAPEAVTexture@@UCSString@@@Z", ExactSpelling = true)]
        public static extern NativeTexture* LoadTexture(CSString path);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?LoadFont@CSResources@@SAPEAVFontInstance@@UCSString@@@Z", ExactSpelling = true)]
        public static extern NativeFont* LoadFont(CSString path);
    }

    public unsafe partial struct Platform
    {
        private NativePlatform* mPlatform;

        public Platform(NativePlatform* platform)
        {
            mPlatform = platform;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetWindow@Platform@@SAPEAVWindowBase@@PEBVNativePlatform@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("NativeWindow *")]
        public static extern WindowBase* GetWindow([NativeTypeName("const NativePlatform *")] NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CreateGraphics@Platform@@SAPEAVNativeGraphics@@PEBVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern NativeGraphics* CreateGraphics([NativeTypeName("const NativePlatform *")] NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CreateScene@Platform@@SAPEAVNativeScene@@PEBVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern NativeScene* CreateScene([NativeTypeName("const NativePlatform *")] NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?MessagePump@Platform@@SAHPEAVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern int MessagePump(NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Present@Platform@@SAXPEAVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern void Present(NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@Platform@@SAXPEAVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern void Dispose(NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Create@Platform@@SAPEAVNativePlatform@@XZ", ExactSpelling = true)]
        public static extern NativePlatform* Create();
    }
}
