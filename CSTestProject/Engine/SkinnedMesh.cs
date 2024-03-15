using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {

    public class Armature {
        public struct Bone {
            public string Name;
            public Matrix4x4 Transform;
            public Matrix4x4 TransformLink;
            public Matrix4x4 Lcl;
            public int Parent;

            public override string ToString() { return Name; }
        }
        public Bone[] Bones;

        public int FindBone(string name) {
            for (int i = 0; i < Bones.Length; i++) if (Bones[i].Name == name) return i;
            return -1;
        }
    }

    public class SkinnedMesh : Mesh {

        public Armature Armature;

        protected sbyte vertexBoneIndicesId = -1;
        protected sbyte vertexBoneWeightsId = -1;

        public SkinnedMesh(string _name) : base(_name) {
        }
        public void RequireBoneIndices(BufferFormat fmt = BufferFormat.FORMAT_R8G8B8A8_UINT) {
            RequireVertexElementFormat(ref vertexBoneIndicesId, fmt, "BLENDINDICES");
        }
        public void RequireBoneWeights(BufferFormat fmt = BufferFormat.FORMAT_R8G8B8A8_UNORM) {
            RequireVertexElementFormat(ref vertexBoneWeightsId, fmt, "BLENDWEIGHT");
        }
        public TypedBufferView<T> GetBoneIndicesV<T>(bool require = false) where T : unmanaged {
            if (vertexBoneIndicesId == -1) { if (require) RequireBoneIndices(); else return default; }
            return new TypedBufferView<T>(vertexBuffer.Elements[vertexBoneIndicesId], vertexBuffer.Count);
        }
        public TypedBufferView<T> GetBoneWeightsV<T>(bool require = false) where T : unmanaged {
            if (vertexBoneWeightsId == -1) { if (require) RequireBoneWeights(); else return default; }
            return new TypedBufferView<T>(vertexBuffer.Elements[vertexBoneWeightsId], vertexBuffer.Count);
        }
    }
}
