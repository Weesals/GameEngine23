using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace GameEngine23.Interop {
    public partial struct CSString {
        unsafe public CSString(ushort* str, int len) {
            mBuffer = str;
            mSize = len;
        }
    }
    public partial struct Bool {
        public static implicit operator bool(Bool b) { return b.mValue != 0; }
    }
    public partial struct CSString {
        unsafe public override string ToString() {
            return Encoding.Unicode.GetString((byte*)mBuffer, mSize);
        }
    }
    public partial struct CSString8 {
        unsafe public override string ToString() {
            return Encoding.UTF8.GetString((byte*)mBuffer, mSize);
        }
    }
    public partial struct CSMeshData {
        unsafe public Span<Vector3> GetPositions() {
            Debug.Assert(mPositions.mStride == sizeof(Vector3));
            return new Span<Vector3>((Vector3*)mPositions.mData, mVertexCount);
        }
    }
    public partial struct CSMesh {
        unsafe public void GetMeshData(CSMeshData* meshdata) { fixed (CSMesh* self = &this) GetMeshData(self, meshdata); }
    }
    public partial struct CSModel {
        unsafe public int GetMeshCount() { fixed (CSModel* self = &this) return GetMeshCount(self); }
        unsafe public CSMesh GetMesh(int i) { fixed (CSModel* self = &this) return GetMesh(self, i); }
        public struct MeshEnumerator : IEnumerator<CSMesh> {
            CSModel model;
            int index;
            public CSMesh Current => model.GetMesh(index);
            object IEnumerator.Current => Current;
            public MeshEnumerator(CSModel _model) { model = _model; index = -1; }
            public void Dispose() { }
            public void Reset() { index = -1; }
            public bool MoveNext() { return ++index < model.GetMeshCount(); }

            public MeshEnumerator GetEnumerator() { return this; }
        }
        public MeshEnumerator Meshes => new MeshEnumerator(this);
    }
    public partial struct CSResources {
        unsafe public CSModel LoadModel(string str) {
            fixed (CSResources* self = &this)
            fixed (char* ptr = str) {
                Debug.Assert(sizeof(ushort) == sizeof(char));
                return LoadModel(self, new CSString((ushort*)ptr, str.Length));
            }
        }
    }
    public partial struct CSInput {
        unsafe public bool IsKeyDown(char key) {
            fixed (CSInput* self = &this) {
                return GetKeyDown(self, (sbyte)key);
            }
        }
    }
    public partial struct CSGraphics {
        unsafe public void Clear() { fixed (CSGraphics* self = &this) Clear(self); }
        unsafe public void Execute() { fixed (CSGraphics* self = &this) Execute(self); }
    }
    public partial struct CSScene {
        unsafe public CSInstance CreateInstance(CSMesh mesh) { fixed (CSScene* self = &this) return CreateInstance(self, mesh); }
        unsafe public void UpdateInstanceData(CSInstance instance, byte* data, int dataLen) { fixed (CSScene* self = &this) UpdateInstanceData(self, instance, data, dataLen); }
        unsafe public void Render(CSGraphics* graphics) { fixed (CSScene* self = &this) Render(self, graphics); }
    }
    public partial struct Platform {
        unsafe public CSInput GetInput() { fixed (Platform* self = &this) return GetInput(self); }
        unsafe public CSGraphics GetGraphics() { fixed (Platform* self = &this) return GetGraphics(self); }
        unsafe public CSResources GetResources() { fixed (Platform* self = &this) return GetResources(self); }
        unsafe public CSScene CreateScene() { fixed (Platform* self = &this) return CreateScene(self); }
        unsafe public int MessagePump() { fixed (Platform* self = &this) return MessagePump(self); }
        unsafe public void Present() { fixed (Platform* self = &this) Present(self); }
    }
}
