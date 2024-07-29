using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Serialization;

namespace Weesals.Engine {
    public class Model {

        public struct AnimationSet {
            public readonly AnimationProvider Provider;
            public int Count => Provider.AnimationCount;
            public AnimationHandle this[int index] => new AnimationHandle(Provider, index);
            public AnimationHandle this[string name] {
                get {
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
            throw new NotImplementedException();
            using (var sMeshes = serializer.CreateChild("Meshes")) {
                sMeshes.Serialize(meshes, m => m.Name, n => new Mesh(n),
                    (Mesh mesh, ref TSONNode sMesh) => {
                        var vcount = mesh.VertexCount;
                        sMesh.Serialize(ref vcount);
                        using (var sIndices = sMesh.CreateChild("Indices")) {
                            using (var sBinary = sIndices.CreateBinary()) {
                                var icount = mesh.IndexCount;
                                sBinary.Serialize(ref icount);
                                mesh.SetIndexCount(icount);
                            }
                        }
                    });
            }
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
