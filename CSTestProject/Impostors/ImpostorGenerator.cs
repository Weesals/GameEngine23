using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Utility;

namespace Weesals.Impostors {
    public class ImpostorGenerator {
        private static ProfilerMarker ProfileMarker_GenerateImpostor = new("Impostor Generate");
        private static ProfilerMarker ProfileMarker_ReadbackImpostor = new("Impostor Readback");

        public struct ConfigurationData {
            public Int2 FramesCounts;
            public Int2 AtlasResolution;
            public static readonly ConfigurationData Default =
                new() { FramesCounts = 8, AtlasResolution = 1024 };
        }

        public ConfigurationData Configuration = ConfigurationData.Default;
        public Material Material;

        private Material distanceFieldMaterial;
        private BufferLayoutPersistent instanceBuffer;
        private CSRenderTarget tempTarget1, tempTarget2;
        private CSRenderTarget depthTarget;

        private struct InstanceData {
            public Matrix4x4 Model;
            public Matrix4x4 PreviousModel;
            public Vector4 Highlight;
            public float Selected;
            public float Dummy1, Dummy2, Dummy3;
        }

        public ImpostorGenerator() {
            var frameCount = Configuration.FramesCounts.X * Configuration.FramesCounts.Y;

            tempTarget1 = RenderTargetPool.RequirePooled(Configuration.AtlasResolution, BufferFormat.FORMAT_R16G16_UNORM);
            tempTarget2 = RenderTargetPool.RequirePooled(Configuration.AtlasResolution, BufferFormat.FORMAT_R16G16_UNORM);
            depthTarget = RenderTargetPool.RequirePooled(Configuration.AtlasResolution, BufferFormat.FORMAT_D16_UNORM);

            instanceBuffer = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Instance);
            instanceBuffer.AppendElement(new CSBufferElement("INSTANCE", BufferFormat.FORMAT_R32G32B32A32_FLOAT));
            instanceBuffer.AllocResize(frameCount * 10);
            instanceBuffer.SetCount(frameCount * 10);

            distanceFieldMaterial = new Material(
                Resources.LoadShader("./Assets/Shader/DistanceField.hlsl", "VSMain"),
                Resources.LoadShader("./Assets/Shader/DistanceField.hlsl", "PSSeed")
            );
            distanceFieldMaterial.SetDepthMode(DepthMode.MakeOff());

            Material = new Material("./Assets/Shader/ImpostorBaker.hlsl");
            var view = Matrix4x4.CreateRotationX(MathF.PI / 2.0f);
            view *= Matrix4x4.CreateTranslation(new(0f, 0f, 0f));
            Matrix4x4.Invert(view, out view);
            var proj = Matrix4x4.CreateOrthographicOffCenter(
                0f, Configuration.FramesCounts.X,
                0f, Configuration.FramesCounts.Y,
                -0.5f, 0.5f).RHSToLHS();
            Material.SetValue("View", view);
            Material.SetValue("Projection", proj);
            Material.SetValue("ViewProjection", view * proj);
            Material.SetBuffer("instanceData", instanceBuffer);
        }
        public void Dispose() {
            RenderTargetPool.ReturnPooled(tempTarget1);
            RenderTargetPool.ReturnPooled(tempTarget2);
            RenderTargetPool.ReturnPooled(depthTarget);
        }

        unsafe private (CSRenderTarget albedo, CSRenderTarget normalDepth) Generate(CSGraphics graphics, Mesh mesh, float scale, Vector3 offset) {
            using var marker = ProfileMarker_GenerateImpostor.Auto();
            var frameCount = Configuration.FramesCounts.X * Configuration.FramesCounts.Y;

            var matrices = new MemoryBlock<InstanceData>((InstanceData*)instanceBuffer.Elements[0].mData, instanceBuffer.Count);
            for (int y = 0; y < Configuration.FramesCounts.Y; y++) {
                for (int x = 0; x < Configuration.FramesCounts.X; x++) {
                    var frame = 2f * new Vector2(x + 0.5f, y + 0.5f) / (Vector2)Configuration.FramesCounts - Vector2.One;
                    var up = HemiOctohedralToVector(frame).toxzy();
                    var tform = Matrix4x4.CreateTranslation(offset);
                    Matrix4x4.Invert(CreateTransform(up), out var invMat);
                    tform *= invMat;
                    tform *= Matrix4x4.CreateScale(1f / scale);
                    //tform *= Matrix4x4.CreateScale(x < 4 ? Vector3.One : Vector3.Zero);
                    tform *= Matrix4x4.CreateTranslation(
                        new Vector3(x + 0.5f, 0f, y + 0.5f)
                    );
                    matrices[x + y * Configuration.FramesCounts.X] = new() {
                        Model = tform,
                        PreviousModel = tform,
                        Highlight = default,
                        Selected = 0.0f,
                    };
                }
            }

            instanceBuffer.NotifyChanged();
            graphics.CopyBufferData(instanceBuffer);

            var AlbedoTarget = RenderTargetPool.RequirePooled(Configuration.AtlasResolution);
            var NormalDepthTarget = RenderTargetPool.RequirePooled(Configuration.AtlasResolution);

            Span<CSRenderTarget> targets = [AlbedoTarget, NormalDepthTarget];
            graphics.SetRenderTargets(targets, depthTarget);

            using var materials = new PooledArray<Material>(2) {
                [0] = mesh.Material,
                [1] = Material,
            };
            var bindingsPtr = stackalloc CSBufferLayout[2];
            var buffers = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2) {
                [0] = mesh.IndexBuffer,
                [1] = mesh.VertexBuffer,
            };
            var pso = MaterialEvaluator.ResolvePipeline(graphics, buffers, materials);
            var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);

            graphics.Clear(new CSClearConfig(new Vector4(0f, 0f, 0f, 0f), 1f));
            graphics.Draw(pso, buffers.AsCSSpan(), resources.AsCSSpan(),
                CSDrawConfig.Default, frameCount);

            distanceFieldMaterial.SetPixelShader(Resources.LoadShader("./Assets/Shader/DistanceField.hlsl", "PSSeed"));
            BlitQuad(graphics, tempTarget1, AlbedoTarget, distanceFieldMaterial);
            distanceFieldMaterial.SetPixelShader(Resources.LoadShader("./Assets/Shader/DistanceField.hlsl", "PSSpread"));
            for (int i = 5; i >= 0; --i) {
                distanceFieldMaterial.SetValue("Spread", (float)(1 << i));
                Swap(ref tempTarget1, ref tempTarget2);
                BlitQuad(graphics, tempTarget1, tempTarget2, distanceFieldMaterial);
            }
            distanceFieldMaterial.SetPixelShader(Resources.LoadShader("./Assets/Shader/DistanceField.hlsl", "PSApply"));
            distanceFieldMaterial.SetTexture("SDF", tempTarget1);
            distanceFieldMaterial.SetTexture("Mask", AlbedoTarget);

            var newAlbedoTarget = RenderTargetPool.RequirePooled(Configuration.AtlasResolution);
            var newNormalDepthTarget = RenderTargetPool.RequirePooled(Configuration.AtlasResolution);
            newAlbedoTarget.SetSize(Configuration.AtlasResolution);
            newNormalDepthTarget.SetSize(Configuration.AtlasResolution);
            distanceFieldMaterial.SetMacro("APPLYGRADIENT", "1");
            BlitQuad(graphics, newAlbedoTarget, AlbedoTarget, distanceFieldMaterial);
            distanceFieldMaterial.ClearMacro("APPLYGRADIENT");
            BlitQuad(graphics, newNormalDepthTarget, NormalDepthTarget, distanceFieldMaterial);
            //Swap(ref newAlbedoTarget, ref AlbedoTarget);
            //Swap(ref newNormalDepthTarget, ref NormalDepthTarget);
            RenderTargetPool.ReturnPooled(AlbedoTarget);
            RenderTargetPool.ReturnPooled(NormalDepthTarget);
            return (newAlbedoTarget, newNormalDepthTarget);
        }
        private void Swap<T>(ref T v1, ref T v2) {
            var t = v1;
            v1 = v2;
            v2 = t;
        }
        private Mesh quadMesh;
        unsafe private void BlitQuad(CSGraphics graphics, CSRenderTarget target, CSRenderTarget source, Material material) {
            graphics.SetRenderTargets(target, default);
            if (quadMesh == null) {
                quadMesh = new("Quad");
                quadMesh.SetVertexCount(4);
                quadMesh.SetIndices(new uint[] { 0, 1, 3, 0, 3, 2, });
                quadMesh.GetPositionsV().Set(new Vector3[] {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 1f, 0f),
                });
            }
            material.SetTexture("Texture", source);
            material.SetValue("Resolution", (Vector2)target.Size);
            var materials = new Span<Material>(ref material);
            var bindingsPtr = stackalloc CSBufferLayout[2];
            var buffers = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2) {
                [0] = quadMesh.IndexBuffer,
                [1] = quadMesh.VertexBuffer,
            };
            var pso = MaterialEvaluator.ResolvePipeline(graphics, buffers, materials);
            var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
            graphics.Clear(new CSClearConfig(new Vector4(0f, 0f, 0f, 0f), 1f));
            graphics.Draw(pso, buffers.AsCSSpan(), resources.AsCSSpan(),
                CSDrawConfig.Default);
        }

        public static Vector2 VectorToHemiOctahedral(Vector3 v) {
            Vector2 r = new(v.X + v.Y, v.X - v.Y);
            r /= Vector3.Dot(Vector3.Abs(v), Vector3.One);
            return r;
        }
        public static Vector3 HemiOctohedralToVector(Vector2 oct) {
            Vector3 r = new Vector3(oct.X + oct.Y, oct.X - oct.Y, 0.0f);
            r.Z = 2.0f - (Math.Abs(r.X) + Math.Abs(r.Y));
            return Vector3.Normalize(r);
        }

        private Matrix4x4 CreateTransform(Vector3 u) {
            Vector2 bc = new Vector2(u.X, u.Z) * u.Z / (u.Y + 1.0f);
            var r = new Vector3(u.Y + bc.Y, -u.X, -bc.X);
            var f = new Vector3(-bc.X, -u.Z, 1.0f - bc.Y);
            return new Matrix4x4(
                r.X, r.Y, r.Z, 0f,
                u.X, u.Y, u.Z, 0f,
                f.X, f.Y, f.Z, 0f,
                0f, 0f, 0f, 1f
            );
        }

        public async Task<Material> CreateImpostor(CSGraphics graphics, Mesh mesh) {
            var scale = mesh.BoundingBox.Size.Length();
            var offset = -mesh.BoundingBox.Centre;
            float maxDst2 = 0f;
            foreach (var vert in mesh.GetPositionsV()) {
                maxDst2 = Math.Max(maxDst2, Vector3.DistanceSquared(vert, -offset));
            }
            scale = MathF.Sqrt(maxDst2) * 2f;
            var (albedoTarget, normalDepthTarget) = Generate(graphics, mesh, scale, offset);
            var albedoTex = CSTexture.Create("Albedo");
            var nrmDepthTex = CSTexture.Create("Normal");
            await Task.WhenAll(
                ReadTexture(graphics, albedoTarget, albedoTex, BufferFormat.FORMAT_BC3_UNORM, true),
                ReadTexture(graphics, normalDepthTarget, nrmDepthTex, BufferFormat.FORMAT_BC3_UNORM)
            );
            RenderTargetPool.ReturnPooled(albedoTarget);
            RenderTargetPool.ReturnPooled(normalDepthTarget);
            var material = new Material(
                Resources.LoadShader("./Assets/Shader/impostor.hlsl", "VSMain"),
                Resources.LoadShader("./Assets/Shader/impostor.hlsl", "PSMain")
            );
            material.SetTexture("Albedo", albedoTex);
            material.SetTexture("Normal", nrmDepthTex);
            material.SetValue("Offset", offset);
            material.SetValue("Scale", 1f / scale);
            return material;
        }

        private static async Task ReadTexture(
            CSGraphics graphics, CSRenderTarget source, CSTexture result,
            BufferFormat format = BufferFormat.FORMAT_BC1_UNORM,
            bool normalizeAlpha = false
        ) {
            var readback = graphics.CreateReadback(source);
            await readback;
            using (ProfileMarker_ReadbackImpostor.Auto()) {
                using var data = new PooledArray<byte>(readback.GetDataSize());
                result.SetFormat(source.Format);
                result.SetSize(source.Size);
                readback.ReadAndDispose(result.GetTextureData());
            }
            var handle = JobHandle.Schedule(() => {
                result.GenerateMips(normalizeAlpha);
                result.CompressTexture(format);
            });
            await handle;
        }
    }
}
