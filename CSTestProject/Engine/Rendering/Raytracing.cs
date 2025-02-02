using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine.Rendering {
    public class Raytracing {
        [StructLayout(LayoutKind.Sequential)]
        public struct TLASInstance {
            public static readonly int Size = 64;
            public Vector4 TransformX;
            public Vector4 TransformY;
            public Vector4 TransformZ;
            public uint InstanceData;
            public uint InstanceId { get => InstanceData & 0x00ffffff; set => InstanceData = (InstanceData & 0xff000000) | (value); }
            public uint InstanceMask { get => InstanceData >> 24; set => InstanceData = (InstanceData & 0x00ffffff) | (value << 24); }
            public uint Extra;
            public uint InstanceContributionToHitGroupIndex { get => Extra & 0x00ffffff; set => Extra = (Extra & 0xff000000) | (value); }
            public uint Flags { get => Extra >> 24; set => Extra = (Extra & 0x00ffffff) | (value << 24); }
            public ulong AccelerationStructureAddress;
            static TLASInstance() {
                Debug.Assert(Marshal.SizeOf<TLASInstance>() == TLASInstance.Size);
            }
        }
        public class BLASInstance {
            public ulong Identifier;
            public nint NativeBLAS;
        }

        private Dictionary<ulong, BLASInstance> blasInstances = new();
        private BufferLayoutPersistent instanceBuffer;
        private CSTexture resultTexture = CSTexture.Create("Result", 512, 512, BufferFormat.FORMAT_R8G8B8A8_UNORM);
        private Material raytraceMaterial = new();

        public CSTexture ResultTexture => resultTexture;

        unsafe public Raytracing() {
            resultTexture.SetAllowUnorderedAccess(true);
            resultTexture.GetTextureData().AsSpan().Fill(0x80);
            resultTexture.MarkChanged();

            instanceBuffer = new(BufferLayoutPersistent.Usages.Instance, 1024);
            instanceBuffer.AppendElement(new("INSTANCE", BufferFormat.FORMAT_UNKNOWN, TLASInstance.Size));
        }

        unsafe public nint CreateInstances(CSGraphics graphics, ScenePass basePass) {
            graphics.CommitTexture(resultTexture);
            instanceBuffer.SetCount(100);
            var instances = instanceBuffer.GetElementAs<TLASInstance>(0);
            var blasIt = blasInstances.GetEnumerator();
            //basePass.RetainedRenderer.BVH.CreateFrustumEnumerator()
            for (int i = 0; i < instances.Length; i++) {
                while (!blasIt.MoveNext()) blasIt = blasInstances.GetEnumerator();
                instances[i] = new() {
                    TransformX = new(1f, 0f, 0f, (i & 15) * 5f),
                    TransformY = new(0f, 1f, 0f, 0f),
                    TransformZ = new(0f, 0f, 1f, (i / 15) * 5f),
                    InstanceId = (uint)i,
                    InstanceMask = 1,
                    AccelerationStructureAddress = CSRaytracing.GetBLASGPUAddress(blasIt.Current.Value.NativeBLAS),
                };
            }
            instanceBuffer.NotifyChanged();
            graphics.CopyBufferData(instanceBuffer);
            var instBufferCopy = this.instanceBuffer.BufferLayout;
            var tlas = CSRaytracing.CreateTLAS(graphics.GetNativeGraphics(), &instBufferCopy);
            return tlas;
        }

        unsafe public BLASInstance CreateBLAS(CSGraphics graphics, Mesh mesh) {
            var instance = new BLASInstance() { Identifier = mesh.IndexBuffer.identifier, };
            var vertexBuffer = mesh.VertexBuffer;
            var indexBuffer = mesh.IndexBuffer;
            graphics.CopyBufferData(vertexBuffer);
            graphics.CopyBufferData(indexBuffer);
            instance.NativeBLAS = CSRaytracing.CreateBLAS(graphics.GetNativeGraphics(), &vertexBuffer, &indexBuffer);
            blasInstances.Add(instance.Identifier, instance);
            return instance;
        }

        unsafe public void Dispatch(CSGraphics graphics, nint tlas) {
            var raygenShaderAddr = Resources.LoadShader("./Assets/Shader/RaycastTest.hlsl", "");
            var raygenShader = Resources.RequireShader(Core.ActiveInstance.GetGraphics(),
                raygenShaderAddr, "lib_6_5", default, default, out var loadHandle);
            if (loadHandle.IsComplete) {
                var textureSize = resultTexture.GetSize3D();
                var raytracePSO = graphics.RequireRaytracePSO(raygenShader.NativeShader);
                raytraceMaterial.SetTexture("RenderTarget", resultTexture);
                raytraceMaterial.SetValue("Resolution", new Vector4(textureSize.X, textureSize.Y, 1f / textureSize.X, 1f / textureSize.Y));
                raytraceMaterial.SetBuffer("Scene", tlas);
                using var push = Graphics.MaterialStack.Push(raytraceMaterial);
                var resources = MaterialEvaluator.ResolveResources(graphics, raytracePSO, Graphics.MaterialStack);
                graphics.DispatchRaytrace(raytracePSO, resources.AsCSSpan(), textureSize);
            }
        }
    }
}
