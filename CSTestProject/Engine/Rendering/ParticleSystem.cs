using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine.Rendering {

    public class Module {
        public readonly struct Argument {
            public readonly string Name;
            public readonly string Type;
            public Argument(string name, string type) { Name = name; Type = type; }
        }
        public readonly string Name;
        public readonly string Bootstrap;
        public readonly string Function;
        public readonly Argument[] Inputs;
        public readonly Argument[] Outputs;
        public Module(string name, string bootstrap, string function, Argument[] inputs, Argument[] outputs) {
            Name = name;
            Bootstrap = bootstrap;
            Function = function;
            Inputs = inputs;
            Outputs = outputs;
        }
        public int FindInputId(string name) {
            for (int i = 0; i < Inputs.Length; i++) {
                if (Inputs[i].Name == name) return i;
            }
            return -1;
        }
    }
    public class ModuleInstance {
        public Module Module;
        public string[] Inputs;
        public string[] Outputs;
        public ModuleInstance(Module module) {
            Module = module;
            Inputs = new string[module.Inputs.Length];
            Outputs = new string[module.Outputs.Length];
        }
        public ModuleInstance SetInput(string name, string value) {
            Inputs[Module.FindInputId(name)] = value;
            return this;
        }
    }
    public class ParticleGenerator {
        public static Module[] Modules;
        static ParticleGenerator() {
            Modules = new Module[] {
                new(
                    "PositionInSphere",
                    "",
                    "{" +
                    " float z = Permute(Seed) * 2.0 - 1.0;" +
                    " float rxy = sqrt(1.0 - z * z);" +
                    " float phi = Permute(z);" +
                    " float y = rxy * cos(phi);" +
                    " float x = rxy * sin(phi);" +
                    " float radius = Radius * sqrt(Permute(x));" +
                    " Position += float3(x, y, z) * radius;" +
                    "}",
                    new Module.Argument[] { new("Position", "float3"), new("Radius", "float"), },
                    new Module.Argument[] { new("Position", "float3"), }
                ),
                new(
                    "ApplyGravity",
                    "",
                    "{ Velocity += Gravity * DeltaTime; }",
                    new Module.Argument[] { new("Velocity", "float3"), new("Gravity", "float3"), new("DeltaTime", "float"), },
                    new Module.Argument[] { new("Velocity", "float3"), }
                ),
                new(
                    "ApplyVelocity",
                    "",
                    "{ Position += Velocity * DeltaTime; }",
                    new Module.Argument[] { new("Position", "float3"), new("Velocity", "float3"), new("DeltaTime", "float"), },
                    new Module.Argument[] { new("Position", "float3"), }
                ),
                new(
                    "ApplyDrag",
                    "",
                    "{ Velocity *= pow(Drag, DeltaTime); }",
                    new Module.Argument[] { new("Velocity", "float3"), new("Drag", "float"), new("DeltaTime", "float"), },
                    new Module.Argument[] { new("Velocity", "float3"), }
                ),
                new(
                    "Turbulence",
                    "",
                    "{" +
                    " SimplexSample3D noise = SimplexNoise3D(Position + float3(0, LocalTime, 0));" +
                    " Velocity += (noise.Sample3() * 2.0 - 1.0) * (Speed * DeltaTime);" +
                    "}",
                    new Module.Argument[] { new("Velocity", "float3"), new("Position", "float3"), new("Speed", "float"), new("DeltaTime", "float"), },
                    new Module.Argument[] { new("Velocity", "float3"), }
                ),
                new(
                    "IncrementLifetime",
                    "",
                    "{ Age += DeltaTime; }",
                    new Module.Argument[] { new("Age", "float"), new("DeltaTime", "float"), },
                    new Module.Argument[] { new("Age", "float"), }
                ),
                new(
                    "KillDeadParticles",
                    "",
                    "{ if(Age > Lifetime) KillParticle(); }",
                    new Module.Argument[] { new("Age", "float"), new("Lifetime", "float"), },
                    new Module.Argument[] { new("Age", "float"), }
                ),
                new(
                    "UVRotate",
                    "",
                    "{ UV -= 0.5; UV = UV * cos(Rotation) + float2(UV.y, -UV.x) * sin(Rotation); UV += 0.5; }",
                    new Module.Argument[] { new("UV", "float2"), new("Rotation", "float"), },
                    new Module.Argument[] { new("UV", "float2"), }
                ),
                new(
                    "UVAtlas",
                    "",
                    "{ UV = (UV - AtlasIndex) / AtlasCount; }",
                    new Module.Argument[] { new("UV", "float2"), new("AtlasCount", "float2"), new("AtlasIndex", "float2"), },
                    new Module.Argument[] { new("UV", "float2"), }
                ),
                new(
                    "TextureSample",
                    "",
                    "{ Color = Color * Texture.Sample(BilinearSampler, UV); }",
                    new Module.Argument[] { new("Color", "float4"), new("UV", "float2"), },
                    new Module.Argument[] { new("Color", "float4"), }
                ),
                new(
                    "Opacity",
                    "",
                    "{ Color.a *= Opacity; }",
                    new Module.Argument[] { new("Color", "float4"), new("Opacity", "float"), },
                    new Module.Argument[] { new("Color", "float4"), }
                ),
                new(
                    "Custom",
                    "",
                    "{ Code }",
                    new Module.Argument[] { new("Code", "Code"), },
                    new Module.Argument[] { }
                ),
            };
        }
        public class Stage {
            public enum Modes { ParticleSpawn, ParticleStep, ParticleVertex, ParticlePixel, }
            public Modes Mode;
            public ModuleInstance[] Modules = Array.Empty<ModuleInstance>();
            public Stage(Modes mode) {
                Mode = mode;
                Modules = Array.Empty<ModuleInstance>();
            }
            public ModuleInstance InsertModule(Module module) {
                if (module == null) return default!;
                Array.Resize(ref Modules, Modules.Length + 1);
                Modules[^1] = new ModuleInstance(module);
                return Modules[^1];
            }
        }
        public Stage[] Stages;
        public ParticleGenerator() {
            Stages = new[] {
                new Stage(Stage.Modes.ParticleSpawn),
                new Stage(Stage.Modes.ParticleStep),
                new Stage(Stage.Modes.ParticleVertex),
                new Stage(Stage.Modes.ParticlePixel),
            }; 
        }
        public string Generate() {
            StringBuilder builder = new();
            builder.Append(File.ReadAllText("./Assets/templates/ParticleTemplate.hlsl"));
            StringBuilder bootstrapBuilder = new();
            HashSet<Module> bootstrappedModules = new();
            foreach (var stage in Stages) {
                StringBuilder stageBuilder = new();
                foreach (var module in stage.Modules) {
                    StringBuilder moduleBuilder = new();
                    if (!string.IsNullOrEmpty(module.Module.Bootstrap)) {
                        if (bootstrappedModules.Add(module.Module)) {
                            bootstrapBuilder.AppendLine(module.Module.Bootstrap);
                        }
                    }
                    moduleBuilder.Append(module.Module.Function);
                    for (int i = 0; i < module.Inputs.Length; i++) {
                        var input = module.Inputs[i];
                        if (string.IsNullOrEmpty(input)) continue;
                        var templateInput = module.Module.Inputs[i].Name;
                        if (templateInput != input) moduleBuilder.Replace(templateInput, input);
                    }
                    for (int i = 0; i < module.Outputs.Length; i++) {
                        var output = module.Outputs[i];
                        if (string.IsNullOrEmpty(output)) continue;
                        var templateOutput = module.Module.Outputs[i].Name;
                        if (templateOutput != output) moduleBuilder.Replace(templateOutput, output);
                    }
                    stageBuilder.Append(moduleBuilder);
                    stageBuilder.AppendLine();
                }
                builder.Replace($"%{stage.Mode}%", stageBuilder.ToString());
            }
            builder.Replace($"%Bootstrap%", bootstrapBuilder.ToString());
            return builder.ToString();
        }

        public Stage GetStage(Stage.Modes mode) {
            for (int i = 0; i < Stages.Length; i++) if (Stages[i].Mode == mode) return Stages[i];
            throw new Exception($"Stage {mode} not found");
        }

        public static Module? FindModule(string name) {
            foreach (var module in Modules) if (module.Name == name) return module;
            return default;
        }
    }
    public class ParticleSystemManager {
        private List<ParticleSystem> systems = new();
        private static readonly ushort[] quadIndices = new ushort[] { 0, 1, 2, 1, 3, 2, };

        public CSRenderTarget[] PositionBuffers = new CSRenderTarget[2];
        public CSRenderTarget[] VelocityBuffers = new CSRenderTarget[2];
        public CSRenderTarget[] AttributeBuffers = new CSRenderTarget[2];
        public ref CSRenderTarget PositionBuffer => ref PositionBuffers[bufferIndex];
        public ref CSRenderTarget VelocityBuffer => ref VelocityBuffers[bufferIndex];
        public ref CSRenderTarget AttributeBuffer => ref AttributeBuffers[bufferIndex];
        public CSRenderTarget DepthStencilBuffer;

        private Material rootMaterial;
        private Material pruneMaterial;
        private Material rebaseMaterial;

        private BufferLayoutPersistent BlockBegins;
        private int bufferIndex;
        private float time;

        private struct BlockMetadata {
            public int SystemId;
            public float ExpiryTime;
        }
        private BlockMetadata[] blockMeta;
        private Int2 blockMetaSize;
        private int blockMetaNext;

        private DynamicMesh emissionMesh;
        private DynamicMesh updateMesh;

        public Int2 PoolSize => PositionBuffer.GetSize();
        public Material RootMaterial => rootMaterial;

        unsafe public void Initialise(Int2 poolSize) {
            for (int i = 0; i < PositionBuffers.Length; i++) {
                PositionBuffers[i] = CSRenderTarget.Create("Positions");
                PositionBuffers[i].SetSize(poolSize);
                PositionBuffers[i].SetFormat(BufferFormat.FORMAT_R16G16B16A16_FLOAT);
                VelocityBuffers[i] = CSRenderTarget.Create("Velocities");
                VelocityBuffers[i].SetSize(poolSize);
                VelocityBuffers[i].SetFormat(BufferFormat.FORMAT_R16G16B16A16_FLOAT);
            }
            DepthStencilBuffer = CSRenderTarget.Create("DepthStencil");
            DepthStencilBuffer.SetSize(poolSize);
            DepthStencilBuffer.SetFormat(BufferFormat.FORMAT_D24_UNORM_S8_UINT);

            BlockBegins = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Uniform);
            BlockBegins.AppendElement(new CSBufferElement("BlockBegins", BufferFormat.FORMAT_R32G32_UINT));
            BlockBegins.AllocResize(32);
            BlockBegins.BufferLayout.mCount = 2;
            var begins = new MemoryBlock<Int2>((Int2*)BlockBegins.Elements[0].mData, 2);

            rootMaterial = new();
            rootMaterial.SetBuffer("BlockBegins", BlockBegins);
            rootMaterial.SetValue("Gravity", new Vector3(0f, -10f, 0f));

            pruneMaterial = new Material(
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "VSStep"),
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "PSStep")
            );
            pruneMaterial.SetRasterMode(RasterMode.MakeNoCull());
            pruneMaterial.SetDepthMode(DepthMode.MakeWriteOnly().SetStencil(
                0xff, 0x00,
                DepthMode.StencilDesc.MakeDontChange(DepthMode.Comparisons.NEqual),
                DepthMode.StencilDesc.MakeDontChange(DepthMode.Comparisons.NEqual),
                true
            ));
            pruneMaterial.SetStencilRef(0x00);
            pruneMaterial.SetBlendMode(BlendMode.MakeNone());
            rebaseMaterial = new Material(
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "VSStep"),
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "PSStep")
            );
            rebaseMaterial.SetRasterMode(RasterMode.MakeNoCull());
            rebaseMaterial.SetDepthMode(DepthMode.MakeDefault(DepthMode.Comparisons.Less).SetStencil(0x00, 0xff));
            rebaseMaterial.SetStencilRef(0x00);
            rebaseMaterial.SetBlendMode(BlendMode.MakeNone());

            blockMetaSize = poolSize / ParticleSystem.AllocGroup.Size;
            blockMeta = new BlockMetadata[blockMetaSize.X * blockMetaSize.Y];
            blockMetaNext = 0;

            emissionMesh = new("ParticleSpawns");
            emissionMesh.RequireVertexPositions(BufferFormat.FORMAT_R32G32_FLOAT);
            emissionMesh.RequireVertexTexCoords(0);
            emissionMesh.SetIndexFormat(false);
            updateMesh = new("ParticleSteps");
            updateMesh.RequireVertexPositions(BufferFormat.FORMAT_R32G32_FLOAT);
            Span<Vector2> quadVerts = stackalloc Vector2[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f), };
            updateMesh.SetVertexCount(quadVerts.Length);
            updateMesh.GetPositionsV<Vector2>().Set(quadVerts);
            updateMesh.SetIndices(quadIndices);
        }

        public void AppendSystem(ParticleSystem system) {
            systems.Add(system);
            system.Initialise(this, systems.Count - 1);
        }
        public ParticleSystem.AllocGroup AllocateBlock(int systemId) {
            var id = (blockMetaNext++) % blockMeta.Length;
            blockMeta[id] = new BlockMetadata() {
                SystemId = systemId,
            };
            return new ParticleSystem.AllocGroup() {
                Position = new Int2(id % blockMetaSize.X, id / blockMetaSize.X) * ParticleSystem.AllocGroup.Size,
                ConsumeCount = 0,
            };
        }
        unsafe public void Update(CSGraphics graphics, float dt) {
            if (time == 0f) {
                SetTargets(graphics);
                graphics.Clear();
            }
            int oldCycle = (int)(time / 500.0f);
            time += dt;
            int newCycle = (int)(time / 500.0f);
            if (oldCycle != newCycle) PruneOld(graphics);
            rootMaterial.SetValue("DeltaTime", dt);
            rootMaterial.SetValue("LocalTime", time);
            rootMaterial.SetValue("LocalTimeZ", (time / 500.0f) % 1.0f);
            rootMaterial.SetValue("PoolSize", (int)PoolSize.X);
            rootMaterial.SetValue("BlockSizeBits", BitOperations.TrailingZeroCount(PoolSize.X) - 2);
            if (oldCycle != newCycle) Rebase(graphics);

            SetTargets(graphics);
            var bindingsPtr = stackalloc CSBufferLayout[2];
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);
            using var materials = new PooledArray<Material>(2);
            materials[1] = rootMaterial;
            graphics.CopyBufferData(BlockBegins);
            foreach (var system in systems) {
                var irange = system.UpdateEmission(graphics, emissionMesh, dt);
                if (irange.Length <= 0) continue;
                bindings[0] = emissionMesh.IndexBuffer;
                bindings[1] = emissionMesh.VertexBuffer;
                materials[0] = system.SpawnerMaterial;
                var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
                var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
                var drawConfig = CSDrawConfig.MakeDefault();
                graphics.Draw(pso, bindings, resources, drawConfig);
            }
            emissionMesh.Clear();
            FlipBuffers();
            SetTargets(graphics);
            foreach (var system in systems) {
                bindings[0] = updateMesh.IndexBuffer;
                bindings[1] = updateMesh.VertexBuffer;
                materials[0] = system.StepperMaterial;
                var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
                var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
                var drawConfig = CSDrawConfig.MakeDefault();
                graphics.Draw(pso, bindings, resources, drawConfig);
            }
        }

        unsafe private void PruneOld(CSGraphics graphics) {
            SetTargets(graphics);
            var bindingsPtr = stackalloc CSBufferLayout[2];
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);
            bindings[0] = updateMesh.IndexBuffer;
            bindings[1] = updateMesh.VertexBuffer;
            using var materials = new PooledArray<Material>(2);
            materials[1] = rootMaterial;
            materials[0] = rebaseMaterial;
            var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
            var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
            var drawConfig = CSDrawConfig.MakeDefault();
            graphics.Draw(pso, bindings, resources, drawConfig);
        }
        unsafe private void Rebase(CSGraphics graphics) {
            SetTargets(graphics);
            var bindingsPtr = stackalloc CSBufferLayout[2];
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);
            bindings[0] = updateMesh.IndexBuffer;
            bindings[1] = updateMesh.VertexBuffer;
            using var materials = new PooledArray<Material>(2);
            materials[1] = rootMaterial;
            materials[0] = rebaseMaterial;
            var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
            var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
            var drawConfig = CSDrawConfig.MakeDefault();
            graphics.Draw(pso, bindings, resources, drawConfig);
        }

        unsafe public void Draw(CSGraphics graphics, Material passMat, Material sceneRoot) {
            var bindingsPtr = stackalloc CSBufferLayout[2];
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 1);
            bindings[0] = updateMesh.IndexBuffer;
            using var materials = new PooledArray<Material>(4);
            materials[1] = rootMaterial;
            materials[2] = passMat;
            materials[3] = sceneRoot;
            foreach (var system in systems) {
                materials[0] = system.DrawMaterial;
                var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
                var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
                var drawConfig = CSDrawConfig.MakeDefault();
                graphics.Draw(pso, bindings, resources, drawConfig, PoolSize.X * PoolSize.Y);
            }
        }

        private void FlipBuffers() {
            bufferIndex = (bufferIndex + 1) % PositionBuffers.Length;
        }
        unsafe private void SetTargets(CSGraphics graphics) {
            var targetsPtr = stackalloc CSRenderTarget[2] { PositionBuffer, VelocityBuffer };
            var targets = new MemoryBlock<CSRenderTarget>(targetsPtr, 2);
            graphics.SetRenderTargets(targets, DepthStencilBuffer);
            int otherIndex = (bufferIndex + (PositionBuffers.Length - 1)) % PositionBuffers.Length;
            rootMaterial.SetTexture("PositionTexture", PositionBuffers[otherIndex]);
            rootMaterial.SetTexture("VelocityTexture", VelocityBuffers[otherIndex]);
            rootMaterial.SetTexture("AttributesTexture", AttributeBuffers[otherIndex]);
        }
    }
    public class ParticleSystem {
        private static readonly ushort[] quadIndices = new ushort[] { 0, 1, 2, 1, 3, 2, };
        public ParticleGenerator? Generator;
        public ParticleSystemManager? Manager;
        public Material SpawnerMaterial;
        public Material StepperMaterial;
        public Material DrawMaterial;
        public int SystemId = -1;

        public float SpawnRate = 5000f;

        public struct AllocGroup {
            public const int Size = 4;
            public const int Count = Size * Size;
            public Int2 Position;
            public int ConsumeCount;
            public int RemainCount => Count - ConsumeCount;
        }

        private Random random = new();
        private AllocGroup allocated;

        public ParticleSystem() {
            SpawnerMaterial = new Material(
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "VSSpawn"),
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "PSSpawn")
            );
            SpawnerMaterial.SetRasterMode(RasterMode.MakeNoCull());
            SpawnerMaterial.SetDepthMode(DepthMode.MakeWriteOnly().SetStencil(0x00, 0xff));
            SpawnerMaterial.SetBlendMode(BlendMode.MakeOpaque());
            StepperMaterial = new Material(
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "VSStep"),
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "PSStep")
            );
            StepperMaterial.SetRasterMode(RasterMode.MakeNoCull());
            StepperMaterial.SetDepthMode(DepthMode.MakeReadOnly(DepthMode.Comparisons.GEqual).SetStencil(0xff, 0x00));
            StepperMaterial.SetBlendMode(BlendMode.MakeOpaque());
            DrawMaterial = new Material(
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "VSMain"),
                ShaderBase.FromPath("./Assets/Generated/ParticleTest.hlsl", "PSMain")
            );
            DrawMaterial.SetRasterMode(RasterMode.MakeNoCull());
            DrawMaterial.SetDepthMode(DepthMode.MakeReadOnly());
            DrawMaterial.SetBlendMode(BlendMode.MakeAdditive());
        }

        internal void Initialise(ParticleSystemManager manager, int systemId) {
            Manager = manager;
            SystemId = systemId;
            SpawnerMaterial.SetStencilRef(systemId);
            StepperMaterial.SetStencilRef(systemId);
        }
        public RangeInt UpdateEmission(CSGraphics graphics, DynamicMesh mesh, float dt) {
            var count = (int)(dt * SpawnRate + random.NextSingle());
            if (count <= 0) return default;

            Span<Vector2> vpositions = stackalloc Vector2[4];
            Span<ushort> indices = stackalloc ushort[6];

            var rangeBegin = mesh.IndexCount;
            for (int b = 0; ; ++b) {
                if (count <= 0) break;
                if (allocated.RemainCount == 0) {
                    allocated = Manager.AllocateBlock(SystemId);
                }
                int toConsume = Math.Min(allocated.RemainCount, count);
                var verts = mesh.AppendVerts(4);
                var inds = mesh.AppendIndices(6);
                var size = (Vector2)Manager.PoolSize;
                vpositions[0] = (Vector2)allocated.Position + new Vector2(0f, 0f);
                vpositions[1] = (Vector2)allocated.Position + new Vector2(AllocGroup.Size, 0f);
                vpositions[2] = (Vector2)allocated.Position + new Vector2(0f, AllocGroup.Size);
                vpositions[3] = (Vector2)allocated.Position + new Vector2(AllocGroup.Size, AllocGroup.Size);
                for (int i = 0; i < vpositions.Length; i++)
                    vpositions[i] = vpositions[i] / size * 2.0f - Vector2.One;

                verts.GetPositions<Vector2>().Set(vpositions);
                int blockBegin = allocated.ConsumeCount;
                int blockEnd = blockBegin + toConsume;
                verts.GetTexCoords().Set(new Vector2(blockBegin, blockEnd));
                quadIndices.CopyTo(indices);
                for (int i = 0; i < indices.Length; ++i) indices[i] += (ushort)verts.BaseVertex;
                inds.GetIndices<ushort>().Set(indices);

                allocated.ConsumeCount += toConsume;
                count -= toConsume;
            }
            return RangeInt.FromBeginEnd(rangeBegin, mesh.IndexCount);
        }
    }
}
