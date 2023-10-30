using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GameEngine23.Interop
{
    public partial struct NativePlatform
    {
    }

    public partial struct NativeScene
    {
    }

    public partial struct NativeModel
    {
    }

    public partial struct NativeMesh
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
    }

    public partial struct CSBufferFormat
    {
        [NativeTypeName("CSBufferFormat::Format")]
        public Format mFormat;

        [NativeTypeName("uint8_t")]
        public byte mComponents;

        [NativeTypeName("uint8_t")]
        public enum Format : byte
        {
            Float,
            Int,
            Short,
            Byte,
        }
    }

    public unsafe partial struct CSBuffer
    {
        [NativeTypeName("const void *")]
        public void* mData;

        public int mStride;

        public CSBufferFormat mFormat;
    }

    public partial struct CSMeshData
    {
        public int mVertexCount;

        public int mIndexCount;

        public CSString8 mName;

        public CSBuffer mPositions;

        public CSBuffer mNormals;

        public CSBuffer mTexCoords;

        public CSBuffer mColors;

        public CSBuffer mIndices;
    }

    public unsafe partial struct CSMesh
    {
        private NativeMesh* mMesh;

        public CSMesh(NativeMesh* mesh)
        {
            mMesh = mesh;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetVertexCount@CSMesh@@QEBAHXZ", ExactSpelling = true)]
        public static extern int GetVertexCount(CSMesh* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetIndexCount@CSMesh@@QEBAHXZ", ExactSpelling = true)]
        public static extern int GetIndexCount(CSMesh* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetMeshData@CSMesh@@QEBAXPEAUCSMeshData@@@Z", ExactSpelling = true)]
        public static extern void GetMeshData(CSMesh* pThis, CSMeshData* outdata);

        public NativeMesh* GetNativeMesh()
        {
            return mMesh;
        }
    }

    public unsafe partial struct CSModel
    {
        private NativeModel* mModel;

        public CSModel(NativeModel* mesh)
        {
            mModel = mesh;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetMeshCount@CSModel@@QEAAHXZ", ExactSpelling = true)]
        public static extern int GetMeshCount(CSModel* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetMesh@CSModel@@QEBA?AVCSMesh@@H@Z", ExactSpelling = true)]
        public static extern CSMesh GetMesh(CSModel* pThis, int id);

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

    public unsafe partial struct CSGraphics
    {
        private NativeGraphics* mGraphics;

        public CSGraphics(NativeGraphics* graphics)
        {
            mGraphics = graphics;
        }

        public NativeGraphics* GetGraphics()
        {
            return mGraphics;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?Clear@CSGraphics@@QEAAXXZ", ExactSpelling = true)]
        public static extern void Clear(CSGraphics* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?Execute@CSGraphics@@QEAAXXZ", ExactSpelling = true)]
        public static extern void Execute(CSGraphics* pThis);
    }

    public unsafe partial struct CSScene
    {
        private NativeScene* mScene;

        public CSScene(NativeScene* scene)
        {
            mScene = scene;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?CreateInstance@CSScene@@QEAA?AVCSInstance@@VCSMesh@@@Z", ExactSpelling = true)]
        public static extern CSInstance CreateInstance(CSScene* pThis, CSMesh mesh);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?UpdateInstanceData@CSScene@@QEAAXVCSInstance@@PEBEH@Z", ExactSpelling = true)]
        public static extern void UpdateInstanceData(CSScene* pThis, CSInstance instance, [NativeTypeName("const uint8_t *")] byte* data, int dataLen);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?Render@CSScene@@QEAAXPEAVCSGraphics@@@Z", ExactSpelling = true)]
        public static extern void Render(CSScene* pThis, CSGraphics* graphics);
    }

    public unsafe partial struct CSInput
    {
        private NativePlatform* mPlatform;

        public CSInput(NativePlatform* platform)
        {
            mPlatform = platform;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetKeyDown@CSInput@@QEAA?AUBool@@D@Z", ExactSpelling = true)]
        public static extern Bool GetKeyDown(CSInput* pThis, [NativeTypeName("char")] sbyte key);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetKeyPressed@CSInput@@QEAA?AUBool@@D@Z", ExactSpelling = true)]
        public static extern Bool GetKeyPressed(CSInput* pThis, [NativeTypeName("char")] sbyte key);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetKeyReleased@CSInput@@QEAA?AUBool@@D@Z", ExactSpelling = true)]
        public static extern Bool GetKeyReleased(CSInput* pThis, [NativeTypeName("char")] sbyte key);
    }

    public unsafe partial struct CSResources
    {
        private void* MakeUnsafe;

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?LoadModel@CSResources@@QEAA?AVCSModel@@UCSString@@@Z", ExactSpelling = true)]
        public static extern CSModel LoadModel(CSResources* pThis, CSString name);
    }

    public unsafe partial struct Platform
    {
        public void** lpVtbl;

        private NativePlatform* mPlatform;

        public Platform(NativePlatform* platform)
        {
            mPlatform = platform;
        }

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetInput@Platform@@QEBA?AVCSInput@@XZ", ExactSpelling = true)]
        public static extern CSInput GetInput(Platform* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetGraphics@Platform@@QEBA?AVCSGraphics@@XZ", ExactSpelling = true)]
        public static extern CSGraphics GetGraphics(Platform* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?GetResources@Platform@@QEBA?AVCSResources@@XZ", ExactSpelling = true)]
        public static extern CSResources GetResources(Platform* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?CreateScene@Platform@@QEBA?AVCSScene@@XZ", ExactSpelling = true)]
        public static extern CSScene CreateScene(Platform* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?MessagePump@Platform@@QEAAHXZ", ExactSpelling = true)]
        public static extern int MessagePump(Platform* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?Present@Platform@@QEAAXXZ", ExactSpelling = true)]
        public static extern void Present(Platform* pThis);

        [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "?Create@Platform@@SA?AV1@XZ", ExactSpelling = true)]
        public static extern Platform Create();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            ((delegate* unmanaged[Thiscall]<Platform*, void>)(lpVtbl[0]))((Platform*)Unsafe.AsPointer(ref this));
        }
    }
}
