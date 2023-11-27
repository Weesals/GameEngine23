using GameEngine23.Interop;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine {
    public struct TextureDesc {
        public Int2 Size;
        public BufferFormat Format;
    }
    public class RenderPass {
        public readonly string Name;
        public readonly RenderQueue RenderQueue;
        public readonly RetainedRenderer RetainedRenderer;
        public Matrix4x4 View { get; protected set; }
        public Matrix4x4 Projection { get; protected set; }
        public Frustum Frustum { get; protected set; }
        public CSRenderTarget RenderTarget { get; protected set; }
        public Material OverrideMaterial { get; protected set; }

        private TextureDesc targetDesc;
        private List<RenderPass> dependencies = new();

        public RenderPass(Scene scene, string name) {
            Name = name;
            RenderQueue = new();
            RetainedRenderer = new(scene);
            OverrideMaterial = new();
            OverrideMaterial.SetValue("View", Matrix4x4.Identity);
            OverrideMaterial.SetValue("Projection", Matrix4x4.Identity);
        }

        public void SetTargetDesc(TextureDesc desc) {
            targetDesc = desc;
        }
        public Material GetPassMaterial() {
            return OverrideMaterial;
        }
        public Frustum GetFrustum() {
            return Frustum;
        }

        public void RegisterDependency(RenderPass other) {
            dependencies.Add(other);
        }

        public void AddDependency(string name, RenderPass other) {
            if (!other.RenderTarget.IsValid()) {
                var target = CSRenderTarget.Create();
                var otherDesc = other.targetDesc;
                target.SetSize(otherDesc.Size != 0 ? otherDesc.Size : 1024);
                if (otherDesc.Format != BufferFormat.FORMAT_UNKNOWN)
                    target.SetFormat(otherDesc.Format);
                other.RenderTarget = target;
            }
            GetPassMaterial().SetTexture(name, other.RenderTarget);
        }

        public void SetViewProjection(in Matrix4x4 view, in Matrix4x4 proj) {
            View = view;
            Projection = proj;
            Frustum = new Frustum(view * proj);
            OverrideMaterial.SetValue(RootMaterial.iVMat, view);
            OverrideMaterial.SetValue(RootMaterial.iPMat, proj);
        }
        public void AddInstance(CSInstance instance, Mesh mesh, Span<Material> materials) {
            RetainedRenderer.AppendInstance(mesh, materials, instance.GetInstanceId());
        }
        public void SetVisible(CSInstance instance, bool visible) {
            RetainedRenderer.SetVisible(instance.GetInstanceId(), visible);
        }

        public void Bind(CSGraphics graphics) {
            graphics.SetRenderTarget(RenderTarget);
            RenderQueue.Clear();
        }
        public void Render(CSGraphics graphics) {
            RetainedRenderer.SubmitToRenderQueue(graphics, RenderQueue, Frustum);
            RenderQueue.Render(graphics);
        }
    }

    public class ShadowPass : RenderPass {
        public ShadowPass(Scene scene, string name) : base(scene, name) {
            SetTargetDesc(new TextureDesc() { Size = 1024, Format = BufferFormat.FORMAT_D24_UNORM_S8_UINT, });
            OverrideMaterial.SetRenderPassOverride("ShadowCast");
        }
        public void UpdateShadowFrustum(RenderPass basePass) {
            // Create shadow projection based on frustum near/far corners
            var frustum = basePass.GetFrustum();
            Span<Vector3> corners = stackalloc Vector3[8];
            frustum.GetCorners(corners);
            var lightViewMatrix = Matrix4x4.CreateLookAt(new Vector3(20, 50, -100), new Vector3(0, -5, 0), Vector3.UnitY);
            foreach (ref var corner in corners) corner = Vector3.Transform(corner, lightViewMatrix);
            var lightFMin = new Vector3(float.MaxValue);
            var lightFMax = new Vector3(float.MinValue);
            foreach (var corner in corners) {
                lightFMin = Vector3.Min(lightFMin, corner);
                lightFMax = Vector3.Max(lightFMax, corner);
            }
            // Or project onto terrain if smaller
            frustum.IntersectPlane(Vector3.UnitY, 0.0f, corners);
            frustum.IntersectPlane(Vector3.UnitY, 5.0f, corners.Slice(4));
            foreach (ref var corner in corners) corner = Vector3.Transform(corner, lightViewMatrix);
            var lightTMin = new Vector3(float.MaxValue);
            var lightTMax = new Vector3(float.MinValue);
            foreach (var corner in corners) {
                lightTMin = Vector3.Min(lightTMin, corner);
                lightTMax = Vector3.Max(lightTMax, corner);
            }

            var lightMin = Vector3.Max(lightFMin, lightTMin);
            var lightMax = Vector3.Min(lightFMax, lightTMax);

            lightViewMatrix.Translation = lightViewMatrix.Translation - (lightMin + lightMax) / 2.0f;
            var lightSize = lightMax - lightMin;
            SetViewProjection(
                lightViewMatrix,
                Matrix4x4.CreateOrthographic(lightSize.X, lightSize.Y, -lightSize.Z / 2.0f, lightSize.Z / 2.0f)
            );
        }
    }
    public class BasePass : RenderPass {
        public BasePass(Scene scene, string name) : base(scene, name) {
        }
        public void UpdateShadowParameters(RenderPass shadowPass) {
            var basePassMat = OverrideMaterial;
            var shadowPassViewProj = shadowPass.View * shadowPass.Projection;
            Matrix4x4.Invert(View, out var basePassInvView);
            basePassMat.SetValue("ShadowViewProjection", shadowPassViewProj);
            basePassMat.SetValue("ShadowIVViewProjection", basePassInvView * shadowPassViewProj);
            basePassMat.SetValue("_WorldSpaceLightDir0", Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, basePassInvView)));
            basePassMat.SetValue("_LightColor0", 2 * new Vector3(1.0f, 0.98f, 0.95f));
        }
    }
}
