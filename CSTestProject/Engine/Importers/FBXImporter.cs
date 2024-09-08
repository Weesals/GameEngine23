using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Numerics;
using System.IO.Compression;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;

namespace Weesals.Engine.Importers {
    public class FBXImporter {

        public struct LoadConfig {
            public bool EnableNormals;
            public bool EnableTangents;
            public bool EnableUV;
            public bool EnableColors;
            public static readonly LoadConfig Default = new() { EnableNormals = true, EnableTangents = true, EnableUV = true, EnableColors = true, };
        }

        private static Dictionary<string, Model> modelCache = new();

        private struct HashApplier {
            public int[] Indices;
            public int[] IndexHash;
            private int[] indexToPoly;
            public void Apply<T>(T[] data) where T : unmanaged {
                for (int i = 0; i < Indices.Length; i++) {
                    var index = Indices[i];
                    index = index < 0 ? ~index : index;
                    IndexHash[i] = data[index].GetHashCode();
                }
            }
            public void Apply<T>(FBXParser.VertexData<T> data) where T : unmanaged {
                if (data.Mapping == FBXParser.VertexData<T>.Mappings.PolyVert) {
                    Debug.Assert(Indices.Length == data.Data.Length);
                    for (int i = 0; i < Indices.Length; i++) {
                        IndexHash[i] = HashCode.Combine(IndexHash[i], data.Data[i].GetHashCode());
                    }
                } else if (data.Mapping == FBXParser.VertexData<T>.Mappings.Polygon) {
                    if (indexToPoly == null) {
                        indexToPoly = new int[Indices.Length];
                        var poly = 0;
                        for (int i = 0; i < Indices.Length; i++) {
                            indexToPoly[i] = poly;
                            if (Indices[i] < 0) ++poly;
                        }
                    }
                    for (int i = 0; i < Indices.Length; ++i) {
                        IndexHash[i] = HashCode.Combine(IndexHash[i], data.Data[indexToPoly[i]].GetHashCode());
                    }
                }
            }
            public void CopyRemapped<T>(ref FBXParser.VertexData<T> data, int count) {
                if (!data.IsValid) return;
                var newData = new T[count];
                if (data.Mapping == FBXParser.VertexData<T>.Mappings.PolyVert) {
                    Debug.Assert(Indices.Length == data.Data.Length);
                    for (int i = 0; i < Indices.Length; i++) {
                        var index = Indices[i];
                        index = index < 0 ? ~index : index;
                        newData[index] = data.Data[i];
                    }
                } else if (data.Mapping == FBXParser.VertexData<T>.Mappings.Polygon) {
                    for (int i = 0; i < Indices.Length; i++) {
                        var index = Indices[i];
                        index = index < 0 ? ~index : index;
                        newData[index] = data.Data[indexToPoly[i]];
                    }
                } else if (data.Mapping == FBXParser.VertexData<T>.Mappings.Uniform) {
                    for (int i = 0; i < count; i++) {
                        newData[i] = data.Data[0];
                    }
                } else {
                    for (int i = 0; i < count; i++) {
                        newData[i] = data.Data[IndexHash[i]];
                    }
                }
                data.Data = newData;
            }
        }

        public enum FBXObjectTypes : int { None, Geometry, Transform, Mesh, Bone, Material, Texture, Cluster, Skin, AnimLayer, AnimCurve, AnimCurveNode, }
        public struct FBXObjectRef : IEquatable<FBXObjectRef> {
            public FBXObjectTypes Type;
            public int Index;
            public bool IsValid => Type != FBXObjectTypes.None;
            public FBXObjectRef(FBXObjectTypes type, int index) { Type = type; Index = index; }
            public override string ToString() { return $"{Type} {Index}"; }
            public override bool Equals(object? obj) { return obj is FBXObjectRef @ref && Equals(@ref); }
            public bool Equals(FBXObjectRef other) { return Type == other.Type && Index == other.Index; }
            public override int GetHashCode() { return HashCode.Combine(Type, Index); }
            public static bool operator ==(FBXObjectRef left, FBXObjectRef right) { return left.Equals(right); }
            public static bool operator !=(FBXObjectRef left, FBXObjectRef right) { return !(left == right); }
        }

        public struct FBXCluster {
            public FBXObjectRef Bone;
            public Matrix4x4 TransformLink;
            public Matrix4x4 Transform;
            public int[] Indices;
            public float[] Weights;
        }
        public struct FBXSkin {
            public List<FBXObjectRef> Clusters;
        }
        public struct FBXGeo {
            public FBXObjectRef Skin;
            public FBXParser.FBXNode Node;
        }
        public struct FBXTransform {
            public string Name;
            public FBXObjectRef Parent;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
            public FBXObjectRef Contents;
            public Matrix4x4 GetMatrix() {
                return  Matrix4x4.CreateScale(Scale)
                    * Matrix4x4.CreateFromQuaternion(CreateQuaternion(Rotation))
                    * Matrix4x4.CreateTranslation(Position);
            }

            public static Quaternion CreateQuaternion(Vector3 euler) {
                var degToRad = MathF.PI / 180f;
                return
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, euler.Z * degToRad) *
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, euler.Y * degToRad) *
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, euler.X * degToRad);
            }
        }
        public struct FBXMesh {
            public FBXObjectRef Geometry;
            public FBXObjectRef Material;
        }
        public struct FBXBone {
        }
        public struct FBXAnimLayer {
            public List<FBXObjectRef> Curves;
            public string Name;
        }
        public struct FBXAnimCurve {
            public enum Targets { Translation, Rotation, Scale, }
            public Targets Target;
            public long[] Times;
            public float[] Values;
        }
        public struct FBXAnimCurveNode {
            public string Name;
            public FBXObjectRef Bone;
            public FBXObjectRef CurveX;
            public FBXObjectRef CurveY;
            public FBXObjectRef CurveZ;
            public Vector3 DefaultValue;
        }

        public class FBXObjectMap : Dictionary<ulong, FBXObjectRef> {
            public System.Collections.IList[] ItemLists = new System.Collections.IList[12];
            private struct Helper<T> {
                public static FBXObjectTypes TypeId;
            }
            public FBXObjectMap() {
                Initialize<FBXGeo>(FBXObjectTypes.Geometry);
                Initialize<FBXTransform>(FBXObjectTypes.Transform);
                Initialize<FBXMesh>(FBXObjectTypes.Mesh);
                Initialize<FBXBone>(FBXObjectTypes.Bone);
                Initialize<Material>(FBXObjectTypes.Material);
                Initialize<CSTexture>(FBXObjectTypes.Texture);
                Initialize<FBXCluster>(FBXObjectTypes.Cluster);
                Initialize<FBXSkin>(FBXObjectTypes.Skin);
                Initialize<FBXAnimLayer>(FBXObjectTypes.AnimLayer);
                Initialize<FBXAnimCurve>(FBXObjectTypes.AnimCurve);
                Initialize<FBXAnimCurveNode>(FBXObjectTypes.AnimCurveNode);
                EnsureCapacity(128);
            }

            private void Initialize<T>(FBXObjectTypes type) {
                Helper<T>.TypeId = type;
                ItemLists[(int)type] = new List<T>();
            }
            private FBXObjectTypes GetType<T>() {
                return Helper<T>.TypeId;
            }
            public List<T> GetArray<T>() {
                return (List<T>)ItemLists[(int)GetType<T>()];
            }

            public FBXObjectRef Append<T>(T value) {
                var type = GetType<T>();
                var arr = (List<T>)ItemLists[(int)type];
                var index = arr.Count;
                arr.Add(value);
                return new(type, index);
            }
            public FBXObjectRef Append<T>(ulong id, T value) {
                var addr = Append(value);
                base.Add(id, addr);
                return addr;
            }

            public ref T Get<T>(FBXObjectRef itemRef) {
                return ref CollectionsMarshal.AsSpan((List<T>)ItemLists[(int)itemRef.Type])[itemRef.Index];
            }
        }

        unsafe public static Model Import(string path) {
            var model = Import(path, out var handle);
            handle.Complete();
            return model;
        }
        unsafe public static Model Import(string path, out JobHandle handle) {
            return Import(path, LoadConfig.Default, out handle);
        }
        unsafe public static Model Import(string path, LoadConfig config, out JobHandle handle) {
            handle = default;
            if (modelCache.TryGetValue(path, out var model)) return model;

            // Path might be denormalized, check if we loaded it with a different path
            var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
            if (modelCache.TryGetValue(normalizedPath, out model)) {
                modelCache.Add(path, model);
                return model;
            }

            model = new Model();

            // Perform actual load
            var deferred = JobHandle.CreateDeferred();
            JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("FBX").Auto();
                ParseModel(model, path, config, deferred);
            });
            handle = deferred;

            modelCache.Add(path, model);
            modelCache.Add(normalizedPath, model);

            return model;
        }
        unsafe private static JobHandle ParseModel(Model model, string path, LoadConfig config, JobHandle deferred) {
            JobHandle handle = default;
            var rootPath = Path.GetDirectoryName(path);
            FBXParser parser;
            FBXParser.FBXScene fbxScene;
            Matrix4x4 globalTransform = Matrix4x4.Identity;
            var fbxObjectMap = new FBXObjectMap();

            var hash = path.ComputeStringHash();
            var color = new Color((uint)(hash | 0xff000000));
            var name = Path.GetFileNameWithoutExtension(path);

            using (new ProfilerMarker("FBX Parse").Auto(color).WithText(name)) {
                parser = new FBXParser(path);
                fbxScene = parser.Parse();
            }

            var version = fbxScene.FindNode("FBXHeaderExtension").FindChild("FBXVersion").Properties[0].AsI32(parser.Data);
            if (version < 7100) {
                /*var strBuilder = new StringBuilder();
                parser.Print(fbxScene, strBuilder);
                Debug.WriteLine(strBuilder.ToString());*/
                Debug.WriteLine($"Unsupported FBX version {version}");
                JobHandle.MarkDeferredComplete(deferred);
                return default;
            }

            using (new ProfilerMarker("FBX Load").Auto(color).WithText(name)) {
                var scale = 100.0f;
                var fwdAxis = Vector3.UnitZ;
                var upAxis = Vector3.UnitY;

                var fbxSettings = fbxScene.FindNode("GlobalSettings");
                var fbxSettings70 = fbxSettings?.FindChild("Properties70");
                if (fbxSettings70 != null) {
                    var AxisToVector = (int axis) => {
                        return new Vector3(axis == 0 ? 1f : 0f, axis == 1 ? 1f : 0f, axis == 2 ? 1f : 0f);
                    };
                    foreach (var fbxProp in fbxSettings70.Children) {
                        if (fbxProp.Properties[0].ValueEquals(parser.Data, "UnitScaleFactor"u8)) {
                            scale = (float)fbxProp.Properties[4].AsDouble(parser.Data);
                        }
                        if (fbxProp.Properties[0].ValueEquals(parser.Data, "UpAxis"u8)) {
                            upAxis = AxisToVector(fbxProp.Properties[4].AsI32(parser.Data));
                        }
                        if (fbxProp.Properties[0].ValueEquals(parser.Data, "FrontAxis"u8)) {
                            fwdAxis = -AxisToVector(fbxProp.Properties[4].AsI32(parser.Data));
                        }
                    }
                }
                globalTransform = Matrix4x4.CreateScale(scale / 100.0f) *
                    Matrix4x4.CreateWorld(Vector3.Zero, fwdAxis, upAxis);

                var fbxObjects = fbxScene.FindNode("Objects");
                foreach (var fbxObj in fbxObjects.Children) {
                    var id = fbxObj.Properties.Count == 0 ? 0 : fbxScene.RequireId(parser.Data, fbxObj.Properties[0]);
                    if (fbxObj.Id == "Geometry") {
                        var fbxLastProp = fbxObj.Properties[^1];
                        if (fbxLastProp.ValueEquals(parser.Data, "Mesh"u8)) {
                            fbxObjectMap.Append(id, new FBXGeo() { Node = fbxObj });
                            // TODO: Submeshes and materials
                        }
                    } else if (fbxObj.Id == "Material") {
                        //outObj = ParseMaterial(fbxObj);
                        var material = new Material();
                        var fbxProps = fbxObj.FindChild("Properties70");
                        if (fbxProps != null) {
                            foreach (var fbxProp in fbxProps.Children) {
                                if (fbxProp.Properties[0].ValueEquals(parser.Data, "DiffuseColor"u8)) {
                                    var col = new Vector3((float)fbxProp.Properties[4].AsDouble(parser.Data), (float)fbxProp.Properties[5].AsDouble(parser.Data), (float)fbxProp.Properties[6].AsDouble(parser.Data));
                                    material.SetValue("DiffuseColor", col);
                                }
                            }
                        }
                        fbxObjectMap.Append(id, material);
                    } else if (fbxObj.Id == "Texture") {
                        //var tex = new FBXTexture(GetNodeName(fbxObj));
                        CSTexture tex = default;
                        foreach (var fbxTexChild in fbxObj.Children) {
                            if (fbxTexChild.Id == "FileName" || fbxTexChild.Id == "RelativeFilename")
                                tex = Resources.LoadTexture(Path.Combine(rootPath, Path.GetFileName(fbxTexChild.Properties[0].AsString(parser.Data))));
                            if (tex.IsValid) break;
                            //if (fbxTexChild.Id == "Media") tex.Media = fbxTexChild.Properties[0].Value;
                        }
                        fbxObjectMap.Append(id, tex);
                    } else if (fbxObj.Id == "Deformer") {
                        var fbxClassProp = fbxObj.Properties[2];
                        if (fbxClassProp.ValueEquals(parser.Data, "Skin"u8)) {
                            fbxObjectMap.Append(id, new FBXSkin() { Clusters = new(), });
                        } else if (fbxClassProp.ValueEquals(parser.Data, "Cluster"u8)) {
                            var cluster = new FBXCluster() { Transform = Matrix4x4.Identity, TransformLink = Matrix4x4.Identity };
                            var fbxTLink = fbxObj.FindChild("TransformLink");
                            if (fbxTLink != null) {
                                parser.ParseBinaryArrayRaw(fbxTLink.Properties[0], new Span<Matrix4x4>(ref cluster.TransformLink));
                            }
                            var fbxTransform = fbxObj.FindChild("Transform");
                            if (fbxTransform != null) {
                                parser.ParseBinaryArrayRaw(fbxTransform.Properties[0], new Span<Matrix4x4>(ref cluster.Transform));
                            }
                            var fbxWeights = fbxObj.FindChild("Weights");
                            if (fbxWeights != null) {
                                parser.ParseTypedArray<float>(fbxWeights.Properties[0], out cluster.Weights);
                            }
                            var fbxIndices = fbxObj.FindChild("Indexes");
                            if (fbxIndices != null) {
                                parser.ParseTypedArray<int>(fbxIndices.Properties[0], out cluster.Indices);
                            }
                            fbxObjectMap.Append(id, cluster);
                        }
                        /*} else if (fbxObj.Id == "Pose") {
                            var fbxPose = fbxObj.FindChild("PoseNode");
                            if (fbxPose != null) {
                                var fbxNode = fbxPose.FindChild("Node");
                                var fbxMatrix = fbxPose.FindChild("Matrix");
                                Matrix4x4 pose = default;
                                FBXParser.ParseArrayRaw(fbxMatrix.Properties[0], new Span<Matrix4x4>(ref pose));
                                var nodeId = fbxNode.Properties[0].AsU64();
                            }*/
                    } else if (fbxObj.Id == "Model") {
                        var fbxClassProp = fbxObj.Properties[^1];
                        var fbxModelRef = fbxObjectMap.Append(id, new FBXTransform() { Name = parser.GetNodeName(fbxObj), Scale = Vector3.One, });
                        ref var fbxTransform = ref fbxObjectMap.Get<FBXTransform>(fbxModelRef);
                        if (fbxClassProp.ValueEquals(parser.Data, "Mesh"u8)) {
                            fbxTransform.Contents = fbxObjectMap.Append(new FBXMesh());
                        } else {
                            fbxTransform.Contents = fbxObjectMap.Append(new FBXBone());
                        }
                        var fbxProps = fbxObj.FindChild("Properties70");
                        if (fbxProps == null) {
                            fbxProps = fbxObj.FindChild("Properties60");
                        }
                        if (fbxProps != null) {
                            foreach (var fbxProp in fbxProps.Children) {
                                var index = fbxProp.Properties.Count - 3;
                                if (fbxProp.Properties[0].ValueEquals(parser.Data, "Lcl Translation"u8)) {
                                    fbxTransform.Position = new Vector3((float)fbxProp.Properties[index].AsDouble(parser.Data), (float)fbxProp.Properties[index + 1].AsDouble(parser.Data), (float)fbxProp.Properties[index + 2].AsDouble(parser.Data));
                                }
                                if (fbxProp.Properties[0].ValueEquals(parser.Data, "Lcl Rotation"u8)) {
                                    fbxTransform.Rotation = new Vector3((float)fbxProp.Properties[index].AsDouble(parser.Data), (float)fbxProp.Properties[index + 1].AsDouble(parser.Data), (float)fbxProp.Properties[index + 2].AsDouble(parser.Data));
                                }
                                if (fbxProp.Properties[0].ValueEquals(parser.Data, "Lcl Scaling"u8)) {
                                    fbxTransform.Scale = new Vector3((float)fbxProp.Properties[index].AsDouble(parser.Data), (float)fbxProp.Properties[index + 1].AsDouble(parser.Data), (float)fbxProp.Properties[index + 2].AsDouble(parser.Data));
                                }
                            }
                        }
                    } else if (fbxObj.Id == "AnimationLayer") {
                        fbxObjectMap.Append(id, new FBXAnimLayer() { Name = parser.GetNodeName(fbxObj), Curves = new(), });
                    } else if (fbxObj.Id == "AnimationCurve") {
                        var animCurve = new FBXAnimCurve();
                        var fbxTime = fbxObj.FindChild("KeyTime");
                        var fbxValue = fbxObj.FindChild("KeyValueFloat");
                        if (fbxTime != null) parser.ParseTypedArray(fbxTime.Properties[0], out animCurve.Times);
                        if (fbxValue != null) parser.ParseTypedArray(fbxValue.Properties[0], out animCurve.Values);
                        Debug.Assert(animCurve.Times.Length == animCurve.Values.Length);
                        fbxObjectMap.Append(id, animCurve);
                    } else if (fbxObj.Id == "AnimationCurveNode") {
                        var node = new FBXAnimCurveNode() { Name = parser.GetNodeName(fbxObj), };
                        var fbxProps = fbxObj.FindChild("Properties70");
                        var dx = fbxProps.FindChild("d|X");
                        var dy = fbxProps.FindChild("d|Y");
                        var dz = fbxProps.FindChild("d|Z");
                        node.DefaultValue.X = dx != null ? (float)dx.Properties[4].AsDouble(parser.Data) : 0f;
                        node.DefaultValue.Y = dy != null ? (float)dy.Properties[4].AsDouble(parser.Data) : 0f;
                        node.DefaultValue.Z = dz != null ? (float)dz.Properties[4].AsDouble(parser.Data) : 0f;
                        fbxObjectMap.Append(id, node);
                    }
                }
                foreach (var connection in fbxScene.Connections) {
                    if (!fbxObjectMap.TryGetValue(connection.To, out var parentRef)) continue;
                    if (!fbxObjectMap.TryGetValue(connection.From, out var childRef)) continue;
                    if (parentRef.Type == FBXObjectTypes.Material) {
                        var parentMat = fbxObjectMap.Get<Material>(parentRef);
                        if ("DiffuseColor"u8.SequenceEqual(parser.GetSpan(connection.ToProperty))) {
                            parentMat.SetTexture("Texture", fbxObjectMap.Get<CSTexture>(childRef));
                        } else if ("NormalMap"u8.SequenceEqual(parser.GetSpan(connection.ToProperty))) {
                        } else if ("SpecularColor"u8.SequenceEqual(parser.GetSpan(connection.ToProperty))) {
                        }
                    }
                    if (parentRef.Type == FBXObjectTypes.Transform) {
                        ref var parentTForm = ref fbxObjectMap.Get<FBXTransform>(parentRef);
                        if (childRef.Type == FBXObjectTypes.Transform) {
                            ref var childTForm = ref fbxObjectMap.Get<FBXTransform>(childRef);
                            childTForm.Parent = parentRef;
                        } else if (parentTForm.Contents.Type == FBXObjectTypes.Mesh) {
                            ref var parentMesh = ref fbxObjectMap.Get<FBXMesh>(parentTForm.Contents);
                            if (childRef.Type == FBXObjectTypes.Geometry) {
                                parentMesh.Geometry = childRef;
                            } else if (childRef.Type == FBXObjectTypes.Material) {
                                parentMesh.Material = childRef;
                            }
                        }
                    }
                    if (parentRef.Type == FBXObjectTypes.Geometry) {
                        ref var parentGeo = ref fbxObjectMap.Get<FBXGeo>(parentRef);
                        if (childRef.Type == FBXObjectTypes.Skin) {
                            parentGeo.Skin = childRef;
                        }
                    }
                    if (parentRef.Type == FBXObjectTypes.Skin) {
                        ref var parentSkin = ref fbxObjectMap.Get<FBXSkin>(parentRef);
                        if (childRef.Type == FBXObjectTypes.Cluster) {
                            parentSkin.Clusters.Add(childRef);
                        }
                    }
                    if (parentRef.Type == FBXObjectTypes.Cluster) {
                        ref var parentCluster = ref fbxObjectMap.Get<FBXCluster>(parentRef);
                        if (childRef.Type == FBXObjectTypes.Transform) {
                            parentCluster.Bone = childRef;
                        }
                    }
                    if (childRef.Type == FBXObjectTypes.AnimCurveNode) {
                        ref var animNode = ref fbxObjectMap.Get<FBXAnimCurveNode>(childRef);
                        if (parentRef.Type == FBXObjectTypes.Transform) {
                            animNode.Bone = parentRef;
                        }
                    }
                    if (parentRef.Type == FBXObjectTypes.AnimLayer) {
                        ref var animLayer = ref fbxObjectMap.Get<FBXAnimLayer>(parentRef);
                        if (childRef.Type == FBXObjectTypes.AnimCurveNode) {
                            animLayer.Curves.Add(childRef);
                        }
                    }
                    if (parentRef.Type == FBXObjectTypes.AnimCurveNode) {
                        ref var animNode = ref fbxObjectMap.Get<FBXAnimCurveNode>(parentRef);
                        if (childRef.Type == FBXObjectTypes.AnimCurve) {
                            ref var animCurve = ref fbxObjectMap.Get<FBXAnimCurve>(childRef);
                            if (parser.GetSpan(connection.ToProperty).SequenceEqual("d|X"u8)) animNode.CurveX = childRef;
                            if (parser.GetSpan(connection.ToProperty).SequenceEqual("d|Y"u8)) animNode.CurveY = childRef;
                            if (parser.GetSpan(connection.ToProperty).SequenceEqual("d|Z"u8)) animNode.CurveZ = childRef;
                        }
                    }
                }
            }
            if (fbxObjectMap.GetArray<FBXAnimLayer>().Count > 0) {
                var skeletalAnimations = new AnimationProvider();
                var allAnims = fbxObjectMap.GetArray<FBXAnimLayer>();
                var outAnims = new SkeletalAnimation[allAnims.Count];
                JobHandle allAnimJob = default;
                for (int i = 0; i < allAnims.Count; i++) {
                    int a = i;
                    var animJob = JobHandle.Schedule(() => {
                        using var marker = new ProfilerMarker("Parse FBX Anim").Auto(color).WithText(name);
                        var fbxAnimLayer = allAnims[a];
                        List<SkeletalAnimation.BoneCurve> bones = new();
                        Dictionary<FBXObjectRef, int> boneMap = new();
                        fbxAnimLayer.Curves.Sort(new CurveSorter(fbxObjectMap));
                        for (int l = 0; l < fbxAnimLayer.Curves.Count; l++) {
                            var fbxCurveNode = fbxObjectMap.Get<FBXAnimCurveNode>(fbxAnimLayer.Curves[l]);
                            if (!boneMap.TryGetValue(fbxCurveNode.Bone, out var boneId)) {
                                var fbxBone = fbxObjectMap.Get<FBXTransform>(fbxCurveNode.Bone);
                                boneId = bones.Count;
                                bones.Add(new() {
                                    Name = fbxBone.Name,
                                    ParentBone = fbxBone.Parent.IsValid ? boneMap[fbxBone.Parent] : -1,
                                });
                                boneMap.Add(fbxCurveNode.Bone, boneId);
                            }
                            var bone = bones[boneId];
                            var fbxCurveX = fbxObjectMap.Get<FBXAnimCurve>(fbxCurveNode.CurveX);
                            var fbxCurveY = fbxObjectMap.Get<FBXAnimCurve>(fbxCurveNode.CurveY);
                            var fbxCurveZ = fbxObjectMap.Get<FBXAnimCurve>(fbxCurveNode.CurveZ);

                            var times = new List<long>();
                            InsertTimes(fbxCurveX.Times, times);
                            InsertTimes(fbxCurveY.Times, times);
                            InsertTimes(fbxCurveZ.Times, times);

                            int itX = 0, itY = 0, itZ = 0;
                            if (fbxCurveNode.Name[0] == 'R') {
                                var curve = new QuaternionCurve(times.Count);
                                for (int t = 0; t < times.Count; t++) {
                                    var time = times[t];
                                    ref var keyframe = ref curve.Keyframes[t];
                                    keyframe.Time = (float)FBXParser.UnpackTime(time);
                                    keyframe.Value = FBXTransform.CreateQuaternion(new(
                                        EvaluateCurve(fbxCurveX, time, ref itX),
                                        EvaluateCurve(fbxCurveY, time, ref itY),
                                        EvaluateCurve(fbxCurveZ, time, ref itZ)
                                    ));
                                }
                                curve.Optimize();
                                if (fbxCurveNode.Name[0] == 'R') bone.Rotation = curve;
                            } else {
                                var curve = new Vector3Curve(times.Count);
                                for (int t = 0; t < times.Count; t++) {
                                    var time = times[t];
                                    ref var keyframe = ref curve.Keyframes[t];
                                    keyframe.Time = (float)FBXParser.UnpackTime(time);
                                    keyframe.Value.X = EvaluateCurve(fbxCurveX, time, ref itX);
                                    keyframe.Value.Y = EvaluateCurve(fbxCurveY, time, ref itY);
                                    keyframe.Value.Z = EvaluateCurve(fbxCurveZ, time, ref itZ);
                                }
                                if (fbxCurveNode.Name[0] == 'T') {
                                    bone.Position = curve;
                                } else if (fbxCurveNode.Name[0] == 'S') bone.Scale = curve;
                            }
                        }
                        float duration = 0f;
                        foreach (var bone in bones) {
                            if (bone.Position != null) duration = Math.Max(duration, bone.Position.Duration);
                            if (bone.Rotation != null) duration = Math.Max(duration, bone.Rotation.Duration);
                            if (bone.Scale != null) duration = Math.Max(duration, bone.Scale.Duration);
                        }
                        var anim = new SkeletalAnimation() {
                            Name = fbxAnimLayer.Name,
                            Duration = TimeSpan.FromSeconds(duration),
                            BoneCurves = bones.ToArray(),
                        };
                        outAnims[a] = anim;
                    });
                    allAnimJob = JobHandle.CombineDependencies(allAnimJob, animJob);
                }
                allAnimJob = JobHandle.Schedule(() => {
                    using var marker = new ProfilerMarker("Push Anim").Auto(color).WithText(name);
                    foreach (var anim in outAnims) {
                        skeletalAnimations.Animations.Add(anim);
                    }
                }, allAnimJob);
                handle = JobHandle.CombineDependencies(handle, allAnimJob);
                model.AnimationProvider = skeletalAnimations;
            }
            {
                var allMeshes = fbxObjectMap.GetArray<FBXMesh>();
                var outMeshes = new Mesh[allMeshes.Count];
                JobHandle meshJobs = default;
                for (int i = 0; i < allMeshes.Count; i++) {
                    int m = i;
                    var meshJob = JobHandle.Schedule(() => {
                        using var marker = new ProfilerMarker("Parse FBX Mesh").Auto(color).WithText(name);
                        var fbxMesh = allMeshes[m];
                        if (!fbxMesh.Geometry.IsValid) return;
                        var fbxMeshRef = new FBXObjectRef(FBXObjectTypes.Mesh, m);
                        Matrix4x4 meshTransform = globalTransform;
                        var fbxGeo = fbxObjectMap.Get<FBXGeo>(fbxMesh.Geometry);
                        var fbxSkin = fbxGeo.Skin.IsValid ? fbxObjectMap.Get<FBXSkin>(fbxGeo.Skin) : default;
                        if (!fbxGeo.Skin.IsValid) {
                            foreach (var fbxTransform in fbxObjectMap.GetArray<FBXTransform>()) {
                                if (fbxTransform.Contents == fbxMeshRef) { meshTransform = fbxTransform.GetMatrix() * meshTransform; break; }
                            }
                        }
                        var mesh = GenerateMeshFromFBX(parser, fbxGeo.Node, config, meshTransform, fbxSkin, fbxObjectMap);
                        if (fbxMesh.Material.IsValid) {
                            var fbxMat = fbxObjectMap.Get<Material>(fbxMesh.Material);
                            if (fbxMat != null) mesh.Material.InheritProperties(fbxMat);
                        }
                        if (mesh is SkinnedMesh skinnedMesh) {
                            List<Armature.Bone> bones = new();
                            Dictionary<FBXObjectRef, int> boneMap = new();
                            fbxSkin.Clusters.Sort(new ClusterSorter(fbxObjectMap));
                            for (int c = 0; c < fbxSkin.Clusters.Count; c++) {
                                var fbxCluster = fbxObjectMap.Get<FBXCluster>(fbxSkin.Clusters[c]);
                                var fbxTForm = fbxObjectMap.Get<FBXTransform>(fbxCluster.Bone);
                                var bone = new Armature.Bone() {
                                    Name = fbxTForm.Name,
                                    Parent = fbxTForm.Parent.IsValid && boneMap.TryGetValue(fbxTForm.Parent, out var parentId) ? parentId : -1,
                                };
                                bone.Transform = fbxCluster.Transform;
                                bone.TransformLink = fbxCluster.TransformLink;
                                float deg2Rad = MathF.PI / 180f;
                                bone.Lcl =
                                    Matrix4x4.CreateFromYawPitchRoll(
                                        fbxTForm.Rotation.Y * deg2Rad,
                                        fbxTForm.Rotation.X * deg2Rad,
                                        fbxTForm.Rotation.Z * deg2Rad
                                    ) *
                                    Matrix4x4.CreateTranslation(fbxTForm.Scale) *
                                    Matrix4x4.CreateTranslation(fbxTForm.Position);
                                boneMap.Add(fbxCluster.Bone, bones.Count);
                                bones.Add(bone);
                            }
                            skinnedMesh.Armature = new();
                            skinnedMesh.Armature.Bones = bones.ToArray();
                            Span<Matrix4x4> boneTransforms = stackalloc Matrix4x4[bones.Count];
                            boneTransforms.Fill(Matrix4x4.Identity);
                            /*for (int i = 0; i < boneTransforms.Length; i++) {
                                boneTransforms[i] = bones[i].TransformLink;
                            }*/
                            mesh.Material.SetArrayValue("BoneTransforms", boneTransforms);
                        }
                        if (fbxGeo.Skin.IsValid) {
                            mesh.Material.SetVertexShader(Resources.LoadShader("./Assets/skinned.hlsl", "VSMain"));
                            mesh.Material.SetPixelShader(Resources.LoadShader("./Assets/skinned.hlsl", "PSMain"));
                        }
                        outMeshes[m] = mesh;
                    });
                    meshJobs = JobHandle.CombineDependencies(meshJobs, meshJob);
                }
                meshJobs = JobHandle.Schedule(() => {
                    using var marker = new ProfilerMarker("Push Mesh").Auto(color).WithText(name);
                    foreach (var mesh in outMeshes) model.AppendMesh(mesh);
                }, meshJobs);
                handle = JobHandle.CombineDependencies(handle, meshJobs);
                JobHandle.ConvertDeferred(deferred, handle);
            }
            return handle;
        }

        public class CurveSorter : IComparer<FBXObjectRef> {
            public readonly FBXObjectMap FbxObjectMap;
            public CurveSorter(FBXObjectMap fbxObjectMap) {
                FbxObjectMap = fbxObjectMap;
            }
            public int Compare(FBXObjectRef x, FBXObjectRef y) {
                var item1 = FbxObjectMap.Get<FBXAnimCurveNode>(x);
                var item2 = FbxObjectMap.Get<FBXAnimCurveNode>(y);
                return item1.Bone.Index.CompareTo(item2.Bone.Index);
            }
        }
        public class ClusterSorter : IComparer<FBXObjectRef> {
            public readonly FBXObjectMap FbxObjectMap;
            public ClusterSorter(FBXObjectMap fbxObjectMap) {
                FbxObjectMap = fbxObjectMap;
            }
            public int Compare(FBXObjectRef x, FBXObjectRef y) {
                var item1 = FbxObjectMap.Get<FBXCluster>(x);
                var item2 = FbxObjectMap.Get<FBXCluster>(y);
                return item1.Bone.Index.CompareTo(item2.Bone.Index);
            }
        }

        private static void InsertTimes(long[] times, List<long> outTimes) {
            int t = 0;
            for (int i = 0; i < times.Length; i++) {
                var time = times[i];
                if (t < outTimes.Count && time > outTimes[t]) ++t;
                if (t >= outTimes.Count || time < outTimes[t]) outTimes.Insert(i, time);
                ++t;
            }
        }
        private static float EvaluateCurve(FBXAnimCurve curve, long time, ref int indexCache) {
            while (indexCache < curve.Times.Length && curve.Times[indexCache] < time) ++indexCache;
            if (indexCache >= curve.Times.Length) return curve.Values[^1];
            if (indexCache <= 0) return curve.Values[0];
            var time0 = curve.Times[indexCache - 1];
            var time1 = curve.Times[indexCache];
            var value0 = curve.Values[indexCache - 1];
            var value1 = curve.Values[indexCache];
            return value0 + (value1 - value0) * ((time - time0) / (time1 - time0));
        }

        private static Mesh GenerateMeshFromFBX(FBXParser parser, FBXParser.FBXNode fbxGeo, LoadConfig config, Matrix4x4 transform, FBXSkin fbxSkin = default, FBXObjectMap fbxObjectMap = default) {
            var name = parser.GetNodeName(fbxGeo);

            var inds = parser.ParseGeometryIndices(fbxGeo);
            var verts = parser.ParseGeometryVertices(fbxGeo);
            var matData = parser.ParseVertexData<int>(fbxGeo, "LayerElementMaterial", "Materials", null);
            var uvData = config.EnableUV ? parser.ParseVertexData<Vector2>(fbxGeo, "LayerElementUV", "UV", "UVIndex") : default;
            var normalData = config.EnableNormals ? parser.ParseVertexData<Vector3>(fbxGeo, "LayerElementNormal", "Normals", "NormalsIndex") : default;
            var tangentData = config.EnableTangents ? parser.ParseVertexData<Vector3>(fbxGeo, "LayerElementTangents", "Tangents", "TangentsIndex") : default;
            var colorData = config.EnableColors ? parser.ParseVertexData<Vector4>(fbxGeo, "LayerElementColor", "Colors", "ColorIndex") : default;

            var boneIndices = new FBXParser.VertexData<Int4>() { Mapping = FBXParser.VertexData<Int4>.Mappings.Vertex };
            var boneWeights = new FBXParser.VertexData<Vector4>() { Mapping = FBXParser.VertexData<Vector4>.Mappings.Vertex };

            Vector3 boundsMin = new Vector3(float.MaxValue);
            Vector3 boundsMax = new Vector3(float.MinValue);
            if (fbxSkin.Clusters != null) {
                boneIndices.Data = new Int4[verts.Length];
                boneWeights.Data = new Vector4[verts.Length];
                boneIndices.Data.AsSpan().Fill(0);
                boneWeights.Data.AsSpan().Fill(Vector4.Zero);
                for (int c = 0; c < fbxSkin.Clusters.Count; c++) {
                    var fbxCluster = fbxObjectMap.Get<FBXCluster>(fbxSkin.Clusters[c]);
                    if (fbxCluster.Indices == null) continue;
                    for (int i = 0; i < fbxCluster.Indices.Length; i++) {
                        var index = fbxCluster.Indices[i];
                        ref var localBoneIndices = ref boneIndices.Data[index];
                        ref var localBoneWeights = ref boneWeights.Data[index];
                        int boneI = 0;
                        //for (; boneI < 3 && localBoneIndices[boneI] != -1; ++boneI) ;
                        for (; boneI < 3 && localBoneWeights[boneI] >= fbxCluster.Weights[i]; ++boneI) ;
                        for (int q = 3; q > boneI; --q) {
                            localBoneIndices[q - 1] = localBoneIndices[q];
                            localBoneWeights[q - 1] = localBoneWeights[q];
                        }
                        localBoneIndices[boneI] = c;
                        localBoneWeights[boneI] = fbxCluster.Weights[i];
                    }
                }
                foreach (ref var weight in boneWeights.Data.AsSpan()) weight /= Vector4.Dot(weight, Vector4.One);
                var fbxCluster0 = fbxObjectMap.Get<FBXCluster>(fbxSkin.Clusters[0]);
                Matrix4x4.Invert(transform, out var invTransform);
                var boneTform0 = /*invTransform * */fbxCluster0.TransformLink * transform;
                foreach (var vert in verts) {
                    var tvert = Vector3.Transform(vert, boneTform0);
                    boundsMin = Vector3.Min(boundsMin, tvert);
                    boundsMax = Vector3.Max(boundsMax, tvert);
                }
            } else {
                foreach (ref var vert in verts.AsSpan()) vert = Vector3.Transform(vert, transform);
                if (normalData.IsValid) {
                    foreach (ref var norm in normalData.Data.AsSpan()) norm = Vector3.Transform(norm, transform);
                }
                foreach (var vert in verts) {
                    boundsMin = Vector3.Min(boundsMin, vert);
                    boundsMax = Vector3.Max(boundsMax, vert);
                }
                transform = Matrix4x4.Identity;
            }

            if (uvData.IsValid) {
                foreach (ref var uv in uvData.Data.AsSpan()) uv.Y = 1.0f - uv.Y;
            }

            if (normalData.HasPerPolyData || uvData.HasPerPolyData) {
                var applier = new HashApplier() {
                    Indices = inds,
                    IndexHash = new int[inds.Length],
                };
                applier.Apply(verts);
                applier.Apply(matData);
                applier.Apply(uvData);
                applier.Apply(normalData);
                applier.Apply(tangentData);
                applier.Apply(colorData);
                applier.Apply(boneIndices);
                applier.Apply(boneWeights);
                var ihashLookup = new Dictionary<int, int>();
                for (int i = 0; i < applier.IndexHash.Length; i++) {
                    var hash = applier.IndexHash[i];
                    var index = applier.Indices[i];
                    var polyEnd = index < 0;
                    if (!ihashLookup.TryGetValue(hash, out var vertId)) {
                        ihashLookup.Add(hash, vertId = ihashLookup.Count);
                        // Preserve faces
                        applier.Indices[i] = polyEnd ? ~vertId : vertId;
                        // Remove sign from IndexHash
                        applier.IndexHash[vertId] = polyEnd ? ~index : index;
                    } else {
                        // Refer to existing vert (preserve face)
                        applier.Indices[i] = polyEnd ? ~vertId : vertId;
                    }
                }
                // Indices is already remapped
                // IndexHash contains mapping of VertexId => First Index
                var newVerts = new Vector3[ihashLookup.Count];
                for (int v = 0; v < ihashLookup.Count; v++) newVerts[v] = verts[applier.IndexHash[v]];
                applier.CopyRemapped(ref matData, ihashLookup.Count);
                applier.CopyRemapped(ref uvData, ihashLookup.Count);
                applier.CopyRemapped(ref normalData, ihashLookup.Count);
                applier.CopyRemapped(ref tangentData, ihashLookup.Count);
                applier.CopyRemapped(ref colorData, ihashLookup.Count);
                applier.CopyRemapped(ref boneIndices, ihashLookup.Count);
                applier.CopyRemapped(ref boneWeights, ihashLookup.Count);
                verts = newVerts;
            }

            inds = Triangulate(inds);
            //foreach (ref var vert in verts.AsSpan()) vert *= 0.01f;
            var skinnedMesh = boneIndices.IsValid ? new SkinnedMesh(name) : default;
            var mesh = skinnedMesh != null ? skinnedMesh : new Mesh(name);
            mesh.SetVertexCount(verts.Length);
            mesh.GetPositionsV().Set(verts);
            if (uvData.IsValid) mesh.GetTexCoordsV(0, true).Set(uvData.Data);
            if (normalData.IsValid) mesh.GetNormalsV(true).Set(normalData.Data);
            if (tangentData.IsValid) mesh.GetTangentsV(true).Set(tangentData.Data);
            if (colorData.IsValid) mesh.GetColorsV<Vector4>(true).Set(colorData.Data);
            if (boneIndices.IsValid) skinnedMesh!.GetBoneIndicesV<Int4>(true).Set(boneIndices.Data);
            if (boneWeights.IsValid) skinnedMesh!.GetBoneWeightsV<Vector4>(true).Set(boneWeights.Data);
            mesh.SetIndexCount(inds.Length);
            mesh.GetIndicesV<int>().Set(inds);
            mesh.SetBoundingBox(BoundingBox.FromMinMax(boundsMin, boundsMax));
            mesh.Transform = transform;
            return mesh;
        }

        private static int[] Triangulate(int[] inds) {
            if (inds.Length == 0) return inds;
            List<int> outIndices = new();
            int triBegin = inds[0];
            for (int i = 2; i < inds.Length; i++) {
                var index = inds[i];
                index = index < 0 ? ~index : index;
                outIndices.Add(triBegin);
                outIndices.Add(inds[i - 1]);
                outIndices.Add(index);
                if (inds[i] < 0) {
                    ++i;
                    if (i < inds.Length) triBegin = inds[i];
                    ++i;
                }
            }
            return outIndices.ToArray();
        }
    }
}
