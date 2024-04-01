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

    public partial struct NativeGraphicsSurface
    {
    }

    public partial struct WindowBase
    {
    }

    public partial struct NativeInput
    {
    }

    public partial struct NativeCompiledShader
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

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetWName@CSIdentifier@@SA?AUCSString@@G@Z", ExactSpelling = true)]
        public static extern CSString GetWName([NativeTypeName("uint16_t")] ushort id);

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

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetSize@CSTexture@@SAXPEAVTexture@@UInt3@@@Z", ExactSpelling = true)]
        public static extern void SetSize(NativeTexture* tex, [NativeTypeName("Int3")] Weesals.Engine.Int3 size);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetSize@CSTexture@@SA?AUInt3C@@PEAVTexture@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("Int3C")]
        public static extern Weesals.Engine.Int3 GetSize(NativeTexture* tex);

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

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Swap@CSTexture@@SAXPEAVTexture@@0@Z", ExactSpelling = true)]
        public static extern void Swap(NativeTexture* from, NativeTexture* to);

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

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@CSFont@@SAXPEAVFontInstance@@@Z", ExactSpelling = true)]
        public static extern void Dispose(NativeFont* font);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetTexture@CSFont@@CAPEAVTexture@@PEBVFontInstance@@@Z", ExactSpelling = true)]
        private static extern NativeTexture* GetTexture([NativeTypeName("const NativeFont *")] NativeFont* font);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetLineHeight@CSFont@@CAHPEBVFontInstance@@@Z", ExactSpelling = true)]
        private static extern int GetLineHeight([NativeTypeName("const NativeFont *")] NativeFont* font);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetKerning@CSFont@@CAHPEBVFontInstance@@_W1@Z", ExactSpelling = true)]
        private static extern int GetKerning([NativeTypeName("const NativeFont *")] NativeFont* font, [NativeTypeName("wchar_t")] ushort c1, [NativeTypeName("wchar_t")] ushort c2);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetKerningCount@CSFont@@CAHPEBVFontInstance@@@Z", ExactSpelling = true)]
        private static extern int GetKerningCount([NativeTypeName("const NativeFont *")] NativeFont* font);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetKernings@CSFont@@CAXPEBVFontInstance@@UCSSpan@@@Z", ExactSpelling = true)]
        private static extern void GetKernings([NativeTypeName("const NativeFont *")] NativeFont* font, CSSpan kernings);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetGlyphCount@CSFont@@CAHPEBVFontInstance@@@Z", ExactSpelling = true)]
        private static extern int GetGlyphCount([NativeTypeName("const NativeFont *")] NativeFont* font);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetGlyphId@CSFont@@CAHPEBVFontInstance@@_W@Z", ExactSpelling = true)]
        private static extern int GetGlyphId([NativeTypeName("const NativeFont *")] NativeFont* font, [NativeTypeName("wchar_t")] ushort chr);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetGlyph@CSFont@@CAAEBUCSGlyph@@PEBVFontInstance@@H@Z", ExactSpelling = true)]
        [return: NativeTypeName("const CSGlyph &")]
        private static extern CSGlyph* GetGlyph([NativeTypeName("const NativeFont *")] NativeFont* font, int id);
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

        public CSIdentifier mType;

        public int mOffset;

        public int mSize;

        [NativeTypeName("uint8_t")]
        public byte mRows;

        [NativeTypeName("uint8_t")]
        public byte mColumns;

        [NativeTypeName("uint16_t")]
        public ushort mFlags;
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

        [return: NativeTypeName("const NativePipeline *")]
        public NativePipeline* GetNativePipeline()
        {
            return mPipeline;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetHasStencilState@CSPipeline@@CAHPEBUPipelineLayout@@@Z", ExactSpelling = true)]
        private static extern int GetHasStencilState([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetExpectedBindingCount@CSPipeline@@CAHPEBUPipelineLayout@@@Z", ExactSpelling = true)]
        private static extern int GetExpectedBindingCount([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetExpectedConstantBufferCount@CSPipeline@@CAHPEBUPipelineLayout@@@Z", ExactSpelling = true)]
        private static extern int GetExpectedConstantBufferCount([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetExpectedResourceCount@CSPipeline@@CAHPEBUPipelineLayout@@@Z", ExactSpelling = true)]
        private static extern int GetExpectedResourceCount([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetConstantBuffers@CSPipeline@@CA?AUCSSpan@@PEBUPipelineLayout@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetConstantBuffers([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetResources@CSPipeline@@CA?AUCSSpan@@PEBUPipelineLayout@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetResources([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetBindings@CSPipeline@@CA?AUCSSpan@@PEBUPipelineLayout@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetBindings([NativeTypeName("const NativePipeline *")] NativePipeline* pipeline);
    }

    public partial struct CSDrawConfig
    {
        public int mIndexBase;

        public int mIndexCount;

        public int mInstanceBase;

        public CSDrawConfig(int indexStart, int indexCount)
        {
            mIndexBase = indexStart;
            mIndexCount = indexCount;
        }
    }

    public unsafe partial struct CSCompiledShader
    {
        private NativeCompiledShader* mShader;

        public CSCompiledShader(NativeCompiledShader* shader)
        {
            mShader = shader;
        }

        public NativeCompiledShader* GetNativeShader()
        {
            return mShader;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?_Create@CSCompiledShader@@CAPEAVCompiledShader@@UCSIdentifier@@HHH@Z", ExactSpelling = true)]
        private static extern NativeCompiledShader* _Create(CSIdentifier name, int byteSize, int cbcount, int rbcount);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?InitializeValues@CSCompiledShader@@CAXPEAVCompiledShader@@HH@Z", ExactSpelling = true)]
        private static extern void InitializeValues(NativeCompiledShader* shader, int cb, int vcount);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetValues@CSCompiledShader@@CA?AUCSSpan@@PEAVCompiledShader@@H@Z", ExactSpelling = true)]
        private static extern CSSpan GetValues(NativeCompiledShader* shader, int cb);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetConstantBuffers@CSCompiledShader@@CA?AUCSSpan@@PEBVCompiledShader@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetConstantBuffers([NativeTypeName("const NativeCompiledShader *")] NativeCompiledShader* shader);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetResources@CSCompiledShader@@CA?AUCSSpan@@PEBVCompiledShader@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetResources([NativeTypeName("const NativeCompiledShader *")] NativeCompiledShader* shader);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetBinaryData@CSCompiledShader@@CA?AUCSSpan@@PEBVCompiledShader@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetBinaryData([NativeTypeName("const NativeCompiledShader *")] NativeCompiledShader* shader);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetStatistics@CSCompiledShader@@CAAEBUShaderStats@1@PEBVCompiledShader@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("const ShaderStats &")]
        private static extern ShaderStats* GetStatistics([NativeTypeName("const NativeCompiledShader *")] NativeCompiledShader* shader);

        public partial struct ShaderStats
        {
            public int mInstructionCount;

            public int mTempRegCount;

            public int mArrayIC;

            public int mTexIC;

            public int mFloatIC;

            public int mIntIC;

            public int mFlowIC;
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

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CreateSurface@CSGraphics@@CAPEAVGraphicsSurface@@PEAVNativeGraphics@@PEAVWindowBase@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("NativeSurface *")]
        private static extern NativeGraphicsSurface* CreateSurface(NativeGraphics* graphics, [NativeTypeName("NativeWindow *")] WindowBase* window);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetSurface@CSGraphics@@CAXPEAVNativeGraphics@@PEAVGraphicsSurface@@@Z", ExactSpelling = true)]
        private static extern void SetSurface(NativeGraphics* graphics, [NativeTypeName("NativeSurface *")] NativeGraphicsSurface* surface);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetSurface@CSGraphics@@CAPEAVGraphicsSurface@@PEAVNativeGraphics@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("NativeSurface *")]
        private static extern NativeGraphicsSurface* GetSurface(NativeGraphics* graphics);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetRenderTargets@CSGraphics@@CAXPEAVNativeGraphics@@UCSSpan@@UCSRenderTargetBinding@@@Z", ExactSpelling = true)]
        private static extern void SetRenderTargets(NativeGraphics* graphics, CSSpan colorTargets, CSRenderTargetBinding depthTarget);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CompileShader@CSGraphics@@CAPEBVCompiledShader@@PEAVNativeGraphics@@UCSString@@1UCSIdentifier@@UCSSpan@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("const NativeCompiledShader *")]
        private static extern NativeCompiledShader* CompileShader(NativeGraphics* graphics, CSString path, CSString entry, CSIdentifier identifier, CSSpan macros);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequirePipeline@CSGraphics@@CAPEBUPipelineLayout@@PEAVNativeGraphics@@UCSSpan@@PEAVCompiledShader@@2PEAX@Z", ExactSpelling = true)]
        [return: NativeTypeName("const NativePipeline *")]
        private static extern NativePipeline* RequirePipeline(NativeGraphics* graphics, CSSpan bindings, NativeCompiledShader* vertexShader, NativeCompiledShader* pixelShader, void* materialState);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequireFrameData@CSGraphics@@CAPEAXPEAVNativeGraphics@@H@Z", ExactSpelling = true)]
        private static extern void* RequireFrameData(NativeGraphics* graphics, int byteSize);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RequireConstantBuffer@CSGraphics@@CAPEAXPEAVNativeGraphics@@UCSSpan@@_K@Z", ExactSpelling = true)]
        private static extern void* RequireConstantBuffer(NativeGraphics* graphics, CSSpan span, [NativeTypeName("size_t")] nuint hash = 0);

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

    public unsafe partial struct CSGraphicsSurface
    {
        [NativeTypeName("NativeSurface *")]
        private NativeGraphicsSurface* mSurface;

        public CSGraphicsSurface([NativeTypeName("NativeSurface *")] NativeGraphicsSurface* surface)
        {
            mSurface = surface;
        }

        [return: NativeTypeName("NativeSurface *")]
        public NativeGraphicsSurface* GetNativeSurface()
        {
            return mSurface;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@CSGraphicsSurface@@SAXPEAVGraphicsSurface@@@Z", ExactSpelling = true)]
        public static extern void Dispose([NativeTypeName("NativeSurface *")] NativeGraphicsSurface* surface);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetBackBuffer@CSGraphicsSurface@@CAPEAVRenderTarget2D@@PEBVGraphicsSurface@@@Z", ExactSpelling = true)]
        private static extern NativeRenderTarget* GetBackBuffer([NativeTypeName("const NativeSurface *")] NativeGraphicsSurface* surface);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetResolution@CSGraphicsSurface@@CA?AUInt2C@@PEBVGraphicsSurface@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("Int2C")]
        private static extern Weesals.Engine.Int2 GetResolution([NativeTypeName("const NativeSurface *")] NativeGraphicsSurface* surface);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetResolution@CSGraphicsSurface@@CAXPEAVGraphicsSurface@@UInt2@@@Z", ExactSpelling = true)]
        private static extern void SetResolution([NativeTypeName("NativeSurface *")] NativeGraphicsSurface* surface, [NativeTypeName("Int2")] Weesals.Engine.Int2 res);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?RegisterDenyPresent@CSGraphicsSurface@@CAXPEAVGraphicsSurface@@H@Z", ExactSpelling = true)]
        private static extern void RegisterDenyPresent([NativeTypeName("NativeSurface *")] NativeGraphicsSurface* surface, int delta);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Present@CSGraphicsSurface@@CAXPEAVGraphicsSurface@@@Z", ExactSpelling = true)]
        private static extern void Present([NativeTypeName("NativeSurface *")] NativeGraphicsSurface* surface);
    }

    public unsafe partial struct CSWindow
    {
        [NativeTypeName("NativeWindow *")]
        private WindowBase* mWindow;

        public CSWindow([NativeTypeName("NativeWindow *")] WindowBase* window)
        {
            mWindow = window;
        }

        [return: NativeTypeName("NativeWindow *")]
        public WindowBase* GetNativeWindow()
        {
            return mWindow;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@CSWindow@@CAXPEAVWindowBase@@@Z", ExactSpelling = true)]
        private static extern void Dispose([NativeTypeName("NativeWindow *")] WindowBase* window);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetStatus@CSWindow@@CAHPEAVWindowBase@@@Z", ExactSpelling = true)]
        private static extern int GetStatus([NativeTypeName("NativeWindow *")] WindowBase* window);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?GetSize@CSWindow@@CA?AUInt2C@@PEBVWindowBase@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("Int2C")]
        private static extern Weesals.Engine.Int2 GetSize([NativeTypeName("const NativeWindow *")] WindowBase* window);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetSize@CSWindow@@CAXPEAVWindowBase@@UInt2@@@Z", ExactSpelling = true)]
        private static extern void SetSize([NativeTypeName("NativeWindow *")] WindowBase* window, [NativeTypeName("Int2")] Weesals.Engine.Int2 size);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?SetInput@CSWindow@@CAXPEAVWindowBase@@PEAVInput@@@Z", ExactSpelling = true)]
        private static extern void SetInput([NativeTypeName("NativeWindow *")] WindowBase* window, NativeInput* input);
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

    public partial struct CSKey
    {
        [NativeTypeName("unsigned char")]
        public byte mKeyId;
    }

    public unsafe partial struct CSInput
    {
        private NativeInput* mInput;

        public CSInput(NativeInput* input)
        {
            mInput = input;
        }

        public NativeInput* GetNativeInput()
        {
            return mInput;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetPointers@CSInput@@AEAA?AUCSSpanSPtr@@PEAVInput@@@Z", ExactSpelling = true)]
        private static extern CSSpanSPtr GetPointers(CSInput* pThis, NativeInput* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetKeyDown@CSInput@@AEAA?AUBool@@PEAVInput@@E@Z", ExactSpelling = true)]
        private static extern Bool GetKeyDown(CSInput* pThis, NativeInput* platform, [NativeTypeName("unsigned char")] byte key);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetKeyPressed@CSInput@@AEAA?AUBool@@PEAVInput@@E@Z", ExactSpelling = true)]
        private static extern Bool GetKeyPressed(CSInput* pThis, NativeInput* platform, [NativeTypeName("unsigned char")] byte key);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetKeyReleased@CSInput@@AEAA?AUBool@@PEAVInput@@E@Z", ExactSpelling = true)]
        private static extern Bool GetKeyReleased(CSInput* pThis, NativeInput* platform, [NativeTypeName("unsigned char")] byte key);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetPressKeys@CSInput@@AEAA?AUCSSpan@@PEAVInput@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetPressKeys(CSInput* pThis, NativeInput* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetDownKeys@CSInput@@AEAA?AUCSSpan@@PEAVInput@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetDownKeys(CSInput* pThis, NativeInput* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetReleaseKeys@CSInput@@AEAA?AUCSSpan@@PEAVInput@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetReleaseKeys(CSInput* pThis, NativeInput* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetCharBuffer@CSInput@@AEAA?AUCSSpan@@PEAVInput@@@Z", ExactSpelling = true)]
        private static extern CSSpan GetCharBuffer(CSInput* pThis, NativeInput* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?ReceiveTickEvent@CSInput@@AEAAXPEAVInput@@@Z", ExactSpelling = true)]
        private static extern void ReceiveTickEvent(CSInput* pThis, NativeInput* platform);
    }

    public unsafe partial struct CSResources
    {

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

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CreateWindow@Platform@@SAPEAVWindowBase@@PEAVNativePlatform@@UCSString@@@Z", ExactSpelling = true)]
        [return: NativeTypeName("NativeWindow *")]
        public static extern WindowBase* CreateWindow(NativePlatform* platform, CSString name);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CreateInput@Platform@@SAPEAVInput@@PEAVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern NativeInput* CreateInput(NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?CreateGraphics@Platform@@SAPEAVNativeGraphics@@PEAVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern NativeGraphics* CreateGraphics(NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?MessagePump@Platform@@SAHPEAVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern int MessagePump(NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Dispose@Platform@@SAXPEAVNativePlatform@@@Z", ExactSpelling = true)]
        public static extern void Dispose(NativePlatform* platform);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Create@Platform@@SAPEAVNativePlatform@@XZ", ExactSpelling = true)]
        public static extern NativePlatform* Create();
    }
}
