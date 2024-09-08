using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Engine.Serialization;

namespace Weesals.Engine {
    public class Model {
        private static ProfilerMarker ProfileMarker_Serialize = new("Model.Serialize");
        private static ProfilerMarker ProfileMarker_SerializeAnim = new("Model.Serialize.Anim");
        private static ProfilerMarker ProfileMarker_SerializeArmature = new("Model.Serialize.Arm");

        public struct AnimationSet {
            public readonly AnimationProvider Provider;
            public int Count => Provider.AnimationCount;
            public AnimationHandle this[int index] => new AnimationHandle(Provider, index);
            public AnimationHandle this[string name] {
                get {
                    if (Provider == null) return default;
                    for (int i = 0; i < Provider.Animations.Count; i++) {
                        var anim = Provider.Animations[i];
                        if (anim.Name == name) return new AnimationHandle(Provider, i);
                    }
                    return default;
                }
            }
            public AnimationSet(AnimationProvider provider) { Provider = provider; }
        }

        public string Name;

        private List<Mesh> meshes = new();
        public IReadOnlyList<Mesh> Meshes => meshes;

        public AnimationProvider? AnimationProvider;
        public AnimationSet Animations => new AnimationSet(AnimationProvider);

        public void AppendMesh(Mesh mesh) {
            meshes.Add(mesh);
        }

        public override string ToString() { return Name; }

        public void Serialize(TSONNode serializer) {
            using var marker = ProfileMarker_Serialize.Auto();
            using (var sMeshes = serializer.CreateChild("Meshes")) {
                sMeshes.Serialize(meshes,
                    m => (m is SkinnedMesh ? "S" : "M") + m.Name,
                    n => n.StartsWith('S') ? new SkinnedMesh(n.Substring(1)) : new Mesh(n.Substring(1)),
                    (Mesh mesh, ref TSONNode sMesh) => {
                        var skinned = mesh as SkinnedMesh;
                        using (var sMaterial = sMesh.CreateChild("Material")) {
                            mesh.Material.Serialize(sMesh);
                        }
                        using (var sVertices = sMesh.CreateChild("Vertices")) {
                            using (var sBinary = sVertices.CreateRawBinary()) {
                                var vcount = sBinary.SerializeInline(mesh.VertexCount);
                                //var posFmt = sBinary.SerializeInline(mesh.GetPositionsFormat());
                                var nrmFmt = sBinary.SerializeInline(mesh.GetNormalsFormat());
                                var tanFmt = sBinary.SerializeInline(mesh.GetTangentsFormat());
                                var uvFmt = sBinary.SerializeInline(mesh.GetTexcoordFormat(0));
                                var colFmt = sBinary.SerializeInline(mesh.GetColorsFormat());
                                var boneIFmt = skinned == null ? default : sBinary.SerializeInline(skinned.GetBoneIndicesV().mFormat);
                                var boneWFmt = skinned == null ? default : sBinary.SerializeInline(skinned.GetBoneWeightsV().mFormat);
                                if (serializer.IsReading) {
                                    mesh.SetVertexCount(vcount);
                                    //mesh.RequireVertexPositions(posFmt);
                                    mesh.RequireVertexNormals(nrmFmt);
                                    mesh.RequireVertexTangents(tanFmt);
                                    mesh.RequireVertexTexCoords(0, uvFmt);
                                    mesh.RequireVertexColors(colFmt);
                                    if (skinned != null) {
                                        skinned.RequireBoneIndices(boneIFmt);
                                        skinned.RequireBoneWeights(boneWFmt);
                                    }
                                }
                                SerializeBufferData(sBinary, mesh.VertexBuffer, mesh.GetPositionsV());
                                SerializeBufferData(sBinary, mesh.VertexBuffer, mesh.GetNormalsV());
                                SerializeBufferData(sBinary, mesh.VertexBuffer, mesh.GetTangentsV());
                                SerializeBufferData(sBinary, mesh.VertexBuffer, mesh.GetTexCoordsV());
                                SerializeBufferData(sBinary, mesh.VertexBuffer, mesh.GetColorsV());
                                if (skinned != null) {
                                    SerializeBufferData(sBinary, skinned.VertexBuffer, skinned.GetBoneIndicesV());
                                    SerializeBufferData(sBinary, skinned.VertexBuffer, skinned.GetBoneWeightsV());
                                }
                            }
                        }
                        mesh.CalculateBoundingBox();
                        using (var sIndices = sMesh.CreateChild("Indices")) {
                            using (var sBinary = sIndices.CreateRawBinary()) {
                                var icount = sBinary.SerializeInline(mesh.IndexCount);
                                var indFmt = sBinary.SerializeInline(mesh.GetIndicesFormat());
                                if (serializer.IsReading) {
                                    mesh.SetIndexFormat(indFmt == BufferFormat.FORMAT_R32_UINT);
                                    mesh.SetIndexCount(icount);
                                }
                                SerializeBufferData(sBinary, mesh.IndexBuffer, mesh.GetIndicesV());
                            }
                        }
                        if (skinned != null) {
                            using var markerArmature = ProfileMarker_SerializeArmature.Auto();
                            using (var sArmature = serializer.CreateChild("Armature")) {
                                if (skinned.Armature == null) skinned.Armature = new();
                                int boneCount = skinned.Armature.Bones?.Length ?? 0;
                                using (var sBin = sArmature.CreateRawBinary()) {
                                    sBin.Serialize(ref boneCount);
                                }
                                if (skinned.Armature.Bones == null || skinned.Armature.Bones.Length != boneCount) {
                                    skinned.Armature.Bones = new Armature.Bone[boneCount];
                                }
                                for (int i = 0; i < boneCount; i++) {
                                    ref var bone = ref skinned.Armature.Bones[i];
                                    using (var sBone = sArmature.CreateChild(bone.Name)) {
                                        if (sBone.IsReading) bone.Name = sBone.Name;
                                        using (var sBin = sBone.CreateRawBinary()) {
                                            sBin.SerializeUnmanaged(ref bone.Transform);
                                            sBin.SerializeUnmanaged(ref bone.TransformLink);
                                            sBin.SerializeUnmanaged(ref bone.Lcl);
                                            sBin.SerializeUnmanaged(ref bone.Parent);
                                        }
                                    }
                                }
                            }
                            skinned.Material.SetVertexShader(Resources.LoadShader("./Assets/skinned.hlsl", "VSMain"));
                            skinned.Material.SetPixelShader(Resources.LoadShader("./Assets/skinned.hlsl", "PSMain"));
                        }
                        using (var sCheck = sMesh.CreateChild("Check")) {
                            Trace.Assert(sCheck.Name == "Check");
                        }
                    });
            }
            using (var sAnimations = serializer.CreateChild(AnimationProvider != null ? "Animations" : null)) {
                if (sAnimations.IsValid) {
                    if (AnimationProvider == null) AnimationProvider = new();
                    sAnimations.Serialize(AnimationProvider.Animations,
                        a => (a is SkeletalAnimation ? "S" : "_") + a.Name,
                        n => n.StartsWith('S') ? new SkeletalAnimation() { Name = n.Substring(1) } : new Animation() { Name = n.Substring(1) },
                        (Animation anim, ref TSONNode sAnim) => {
                            using var markerAnim = ProfileMarker_SerializeAnim.Auto();
                            using (var sBin = sAnim.CreateRawBinary()) {
                                sBin.Serialize(ref anim.Duration);
                            }
                            if (anim is SkeletalAnimation skeletal) {
                                var curveCount = skeletal.BoneCurves?.Length ?? 0;
                                using (var sBin = sAnim.CreateRawBinary()) {
                                    sBin.Serialize(ref curveCount);
                                }
                                if ((skeletal.BoneCurves?.Length ?? 0) != curveCount) {
                                    skeletal.BoneCurves = new SkeletalAnimation.BoneCurve[curveCount];
                                }
                                for (int i = 0; i < curveCount; i++) {
                                    ref var curve = ref skeletal.BoneCurves[i];
                                    if (curve == null) curve = new();
                                    using (var sCurve = sAnim.CreateChild(curve.Name)) {
                                        if (sCurve.IsReading) curve.Name = sCurve.Name;
                                        using (var sBin = sCurve.CreateRawBinary()) {
                                            sBin.Serialize(ref curve.ParentBone);
                                            curve.Position.Serialize(sBin);
                                            curve.Rotation.Serialize(sBin);
                                            curve.Scale.Serialize(sBin);
                                        }
                                    }
                                }
                            }
                        });
                } else {
                    AnimationProvider = null;
                }
            }
        }
        unsafe private void SerializeBufferData<S, T>(S sBinary, CSBufferLayout buffer, TypedBufferView<T> view) where S : IBinarySerializer where T : unmanaged {
            if (view.mData == null) return;
            int size = view.mCount * BufferFormatType.GetMeta(view.mFormat).GetByteSize();
            sBinary.Serialize(new Span<byte>(view.mData, size));
        }
    }

    public class Animation {
        public string Name;
        public TimeSpan Duration;
        public override string ToString() { return Name; }
    }
    public class SkeletalAnimation : Animation {
        public class BoneCurve {
            public string Name;
            public int ParentBone;
            public Vector3Curve Position = new();
            public QuaternionCurve Rotation = new();
            public Vector3Curve Scale = new();
            public override string ToString() {
                return $"<{Name} Pos={Position}>";
            }
        }
        public BoneCurve[] BoneCurves;
        public int FindBone(string name) {
            for (int i = 0; i < BoneCurves.Length; i++) if (BoneCurves[i].Name == name) return i;
            return -1;
        }
    }

    public struct AnimationMetadata {
        public string Name;
        public TimeSpan Duration;
        public bool Looping;
        public bool IsValid => Name != null;
        public override string ToString() { return Name ?? "none"; }
        public static readonly AnimationMetadata Invalid = new();
    }
    public class AnimationProvider {
        public int AnimationCount => Animations.Count;
        public List<Animation> Animations = new();
        public AnimationMetadata GetMetadata(int animationId) {
            var animation = Animations[animationId];
            return new AnimationMetadata() {
                Name = animation.Name,
                Duration = animation.Duration,
                Looping = false,
            };
        }
        public Animation GetRawAnimation(int identifier) {
            return Animations[identifier];
        }
    }
    public struct AnimationHandle : IEquatable<AnimationHandle> {
        public readonly AnimationProvider Provider;
        public readonly int Identifier;
        public bool IsValid => Provider != null;
        public TimeSpan Duration => GetMetadata().Duration;
        public AnimationHandle(AnimationProvider provider, int identifier) {
            Provider = provider;
            Identifier = identifier;
        }
        public AnimationMetadata GetMetadata() { return Provider.GetMetadata(Identifier); }
        public T GetAs<T>() where T : Animation { return (T)Provider.GetRawAnimation(Identifier); }
        public bool Equals(AnimationHandle other) { return Provider == other.Provider && Identifier == other.Identifier; }
        public override string? ToString() {
            if (!IsValid) return "<invalid>";
            return GetMetadata().ToString();
        }
    }

    /*
     * Animation is a vertex factory module
     * Can be skinned, cached, or ATT
     * Animation requests must go through layer somehow
     * Animation data separate? As weighted layers?
     */
    public class AnimationPlayback {

        public enum BlendModes { Normal, Additive, }

        public struct Layer {
            public Animation Animation;
            public BlendModes BlendMode;
            public float Weight;
            public Layer(Animation animation) { Animation = animation; BlendMode = BlendModes.Normal; Weight = 1f; }
        }
        public readonly SkinnedMesh Mesh;
        public List<Layer> Layers = new();

        private Matrix4x4[] skinTransforms;

        public AnimationPlayback(SkinnedMesh mesh) {
            Mesh = mesh;
        }

        public void SetAnimation(Animation animation) {
            if (Layers.Count == 1 && Layers[0].Animation == animation) return;
            Layers.Clear();
            Layers.Add(new Layer(animation));
        }

        public void UpdateClip(float time) {
            if (skinTransforms == null)
                skinTransforms = new Matrix4x4[Mesh.Armature.Bones.Length];
            skinTransforms.AsSpan().Fill(Matrix4x4.Identity);

            foreach (var layer in Layers) {
                var animation = layer.Animation as SkeletalAnimation;
                var boneTransforms = ArrayPool<Matrix4x4>.Shared.Rent(animation.BoneCurves.Length);
                for (int i = 0; i < animation.BoneCurves.Length; i++) {
                    var animBone = animation.BoneCurves[i];
                    var pos = animBone.Position.Evaluate(time);
                    var rot = animBone.Rotation.Evaluate(time);
                    var scale = animBone.Scale.Evaluate(time);
                    var animTransform =
                        Matrix4x4.CreateFromQuaternion(rot) *
                        Matrix4x4.CreateScale(scale) *
                        Matrix4x4.CreateTranslation(pos);
                    boneTransforms[i] = animTransform;
                }
                for (int i = 0; i < animation.BoneCurves.Length; i++) {
                    var animBone = animation.BoneCurves[i];
                    if (animBone.ParentBone < 0) continue;
                    Debug.Assert(animBone.ParentBone < i);
                    boneTransforms[i] = boneTransforms[i] * boneTransforms[animBone.ParentBone];
                }
                for (int i = 0; i < Mesh.Armature.Bones.Length; i++) {
                    var skinBone = Mesh.Armature.Bones[i];
                    var boneId = animation.FindBone(skinBone.Name);
                    var tform = boneId >= 0 ? boneTransforms[boneId] : Matrix4x4.Identity;
                    skinTransforms[i] = tform;
                }
                ArrayPool<Matrix4x4>.Shared.Return(boneTransforms);
            }
        }
        public Matrix4x4[] ApplyBindPose(SkinnedMesh mesh) {
            //DrawGizmos(mesh);
            for (int i = 0; i < mesh.Armature.Bones.Length; i++) {
                var skinBone = mesh.Armature.Bones[i];
                skinTransforms[i] = skinBone.Transform * skinTransforms[i];
            }
            return skinTransforms;
        }

        private void DrawGizmos(SkinnedMesh mesh) {
            for (int i = 0; i < mesh.Armature.Bones.Length; i++) {
                var skinBone = mesh.Armature.Bones[i];
                if (skinBone.Parent < 0) {
                    Gizmos.DrawWireCube(skinTransforms[i].Translation, Vector3.One * 0.2f, Color.Red);
                } else {
                    Handles.DrawLine(
                        skinTransforms[i].Translation,
                        skinTransforms[skinBone.Parent].Translation,
                        Color.Blue
                    );
                }
            }
        }
    }

}
