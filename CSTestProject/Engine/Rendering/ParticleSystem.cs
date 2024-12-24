using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine.Profiling;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Engine {

    namespace Particles {
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
                    " float z = (Seed) * 2.0 - 1.0;" +
                    " float rxy = sqrt(1.0 - z * z);" +
                    " float phi = Permute(z) * (3.14 * 2.0);" +
                    " float y = cos(phi);" +
                    " float x = sin(phi);" +
                    " Position += float3(x, y, z) * (Radius * pow(Permute(phi), 1.0/3.0));" +
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
                    " SimplexSample3D noise = CreateSimplex3D(Position + float3(0, LocalTime, 0));" +
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
                    "{ UV = (saturate(UV) - AtlasIndex) / AtlasCount; }",
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
            public struct RenderStateData {
                public BlendMode BlendMode;
                public Material BaseMaterial;
                public RenderTags Tag;
                public string ShaderTemplate;
                public static readonly RenderStateData Default = new() { BlendMode = BlendMode.MakeAlphaBlend(), };
            }
            public struct MetaData {
                public float SpawnRate;
                public float MaximumDuration;
                public static readonly MetaData Default = new () { SpawnRate = 10f, MaximumDuration = 5f, };
            }
            public string Name;
            public Stage[] Stages;
            public MetaData Meta = MetaData.Default;
            public RenderStateData RenderState = RenderStateData.Default;
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
                var templatePath = RenderState.ShaderTemplate ?? "./Assets/templates/ParticleTemplate.hlsl";
                builder.Append(File.ReadAllText(templatePath));
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

            public void LoadJSON(Scene scene, string path) {
                Name = Path.GetFileNameWithoutExtension(path);
                var json = new SJson(File.ReadAllText(path));
                foreach (var jStage in json.GetFields()) {
                    if (jStage.Key == "RenderState") {
                        if (RenderState.BaseMaterial == null)
                            RenderState.BaseMaterial = new();
                        foreach (var jField in jStage.Value.GetFields()) {
                            if (jField.Key == "BlendMode") {
                                if (jField.Value == "Opaque") RenderState.BlendMode = BlendMode.MakeOpaque();
                                if (jField.Value == "AlphaBlend") RenderState.BlendMode = BlendMode.MakeAlphaBlend();
                                if (jField.Value == "Additive") RenderState.BlendMode = BlendMode.MakeAdditive();
                                if (jField.Value == "Premultiplied") RenderState.BlendMode = BlendMode.MakePremultiplied();
                            } else if (jField.Key == "Texture") {
                                RenderState.BaseMaterial.SetTexture("Texture", Resources.LoadTexture(jField.Value.ToString()));
                            } else if (jField.Key == "RenderTag") {
                                RenderState.Tag |= scene.TagManager.RequireTag((string)jField.Value);
                            } else if (jField.Key == "ShaderTemplate") {
                                RenderState.ShaderTemplate = (string)jField.Value;
                            }
                        }
                        continue;
                    }
                    if (jStage.Key == "Metadata") {
                        foreach (var jField in jStage.Value.GetFields()) {
                            if (jField.Key == "SpawnRate") Meta.SpawnRate = jField.Value;
                            if (jField.Key == "MaximumDuration") Meta.MaximumDuration = jField.Value;
                        }
                        continue;
                    }
                    Stage stage = null;
                    for (int i = 0; i < Stages.Length; i++) if (Stages[i].Mode.ToString() == jStage.Key) { stage = Stages[i]; break; }
                    foreach (var jModule in jStage.Value) {
                        ModuleInstance moduleInstance = null;
                        foreach (var jField in jModule.GetFields()) {
                            if (jField.Key == "name") {
                                var moduleType = ParticleGenerator.FindModule(jField.Value.ToString());
                                moduleInstance = stage.InsertModule(moduleType);
                                continue;
                            }
                            moduleInstance.SetInput(jField.Key.ToString(), jField.Value.ToString());
                        }
                    }
                }
            }

            public void WriteHLSL(string path) {
                Directory.CreateDirectory("./Assets/Generated/");
                File.WriteAllText(path, Generate());
            }
            public ParticleSystem CreateParticleSystem(string path) {
                var system = new ParticleSystem(Name, path);
                // TODO: Serialize this data into the hlsl (as comment? or pragma?)
                system.DrawMaterial.SetBlendMode(RenderState.BlendMode);
                if (RenderState.BaseMaterial != null) {
                    system.CommonMaterial.InheritProperties(RenderState.BaseMaterial);
                }
                if (RenderState.Tag.Mask != 0) system.Tag = RenderState.Tag;
                system.SpawnRate = Meta.SpawnRate;
                system.MaximumDuration = Meta.MaximumDuration;
                return system;
            }
        }
    }

    public class ParticleSystemManager {
        private List<ParticleSystem> systems = new();
        private static readonly ushort[] quadIndices = new ushort[] { 0, 1, 2, 1, 3, 2, };

        public CSRenderTarget[] PositionBuffers = new CSRenderTarget[2];
        public CSRenderTarget[] VelocityBuffers = new CSRenderTarget[2];
        public CSRenderTarget[] AttributeBuffers = new CSRenderTarget[2];
        public CSRenderTarget PositionBuffer => PositionBuffers[bufferIndex];
        public CSRenderTarget VelocityBuffer => VelocityBuffers[bufferIndex];
        public CSRenderTarget AttributeBuffer => AttributeBuffers[bufferIndex];
        public CSRenderTarget DepthStencilBuffer;

        private Material rootMaterial;
        private Material pruneMaterial;
        private Material rebaseMaterial;
        private Material expireMaterial;

        private BufferLayoutPersistent ActiveBlocks;
        private bool requireActiveBlocksUpdate;
        private int bufferIndex;
        private float time;

        private struct BlockMetadata {
            public int SystemId;
            public static readonly BlockMetadata Invalid = new() { SystemId = -1, };
        }
        private BlockMetadata[] blockMeta;
        private Int2 blockMetaSize;
        private int blockMetaNext;

        private DynamicMesh emissionMesh;
        private DynamicMesh updateMesh;
        public int TimeMS => (int)(time * 1000.0f);

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
            DepthStencilBuffer = CSRenderTarget.Create("ParticleDepthStencil");
            DepthStencilBuffer.SetSize(poolSize);
            DepthStencilBuffer.SetFormat(BufferFormat.FORMAT_D24_UNORM_S8_UINT);

            ActiveBlocks = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Uniform);
            ActiveBlocks.AppendElement(new CSBufferElement("BlockBegins", BufferFormat.FORMAT_R32_UINT));
            ActiveBlocks.AllocResize(256);
            ActiveBlocks.BufferLayout.mCount = 0;

            rootMaterial = new();
            rootMaterial.SetBuffer("ActiveBlocks", ActiveBlocks);
            rootMaterial.SetValue("Gravity", new Vector3(0f, -10f, 0f));

            pruneMaterial = new Material(
                Shader.FromPath("./Assets/Shader/ParticleUtility.hlsl", "VSBlank"),
                Shader.FromPath("./Assets/Shader/ParticleUtility.hlsl", "PSBlank")
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
                Shader.FromPath("./Assets/Shader/ParticleUtility.hlsl", "VSBlank"),
                Shader.FromPath("./Assets/Shader/ParticleUtility.hlsl", "PSBlank")
            );
            rebaseMaterial.SetRasterMode(RasterMode.MakeNoCull());
            rebaseMaterial.SetDepthMode(DepthMode.MakeDefault(DepthMode.Comparisons.Less).SetStencil(0x00, 0xff));
            rebaseMaterial.SetStencilRef(0x00);
            rebaseMaterial.SetBlendMode(BlendMode.MakeNone());

            expireMaterial = new Material(
                Shader.FromPath("./Assets/Shader/ParticleUtility.hlsl", "VSBlank"),
                Shader.FromPath("./Assets/Shader/ParticleUtility.hlsl", "PSBlank")
            );
            expireMaterial.SetValue("LocalTimeZ", 0.001f);
            expireMaterial.SetRasterMode(RasterMode.MakeNoCull());
            expireMaterial.SetDepthMode(DepthMode.MakeWriteOnly().SetStencil(0x00, 0xff));
            expireMaterial.SetStencilRef(0x00);
            //expireMaterial.SetBlendMode(BlendMode.MakeNone());
            expireMaterial.SetBlendMode(BlendMode.MakeOpaque());

            blockMetaSize = poolSize / ParticleSystem.AllocGroup.Size;
            blockMeta = new BlockMetadata[blockMetaSize.X * blockMetaSize.Y];
            blockMeta.AsSpan().Fill(BlockMetadata.Invalid);
            blockMetaNext = 0;

            emissionMesh = new("ParticleSpawns");
            emissionMesh.RequireVertexPositions(BufferFormat.FORMAT_R32G32_FLOAT);
            emissionMesh.RequireVertexTexCoords(0);
            emissionMesh.RequireVertexColors(BufferFormat.FORMAT_R32G32_FLOAT);
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
        public ParticleSystem? FindSystem(string name) {
            foreach (var system in systems) if (system.Name == name) return system;
            return default;
        }
        public ParticleSystem.AllocGroup AllocateBlock(int systemId) {
            var index = (blockMetaNext++) % blockMeta.Length;
            var alloc = new ParticleSystem.AllocatedBlock() {
                BlockPnt = new UShort2(
                    (ushort)(index % blockMetaSize.X),
                    (ushort)(index / blockMetaSize.X)
                ),
            };
            if (blockMeta[index].SystemId >= 0) {
                Trace.Assert(systems[blockMeta[index].SystemId].RemoveBlock(alloc.BlockId));
            }
            blockMeta[index] = new BlockMetadata() {
                SystemId = systemId,
            };
            return new ParticleSystem.AllocGroup() {
                Alloc = alloc,
                RemainCount = ParticleSystem.AllocGroup.Count,
            };
        }
        public void DeallocBlock(uint blockId, int systemId) {
            var blockX = blockId & 0xffff;
            var blockY = blockId >> 16;
            var blockIndex = blockX + blockY * blockMetaSize.X;
            Debug.Assert(blockMeta[blockIndex].SystemId == systemId);
            blockMeta[blockIndex] = BlockMetadata.Invalid;
        }
        unsafe public void Update(CSGraphics graphics, float dt) {
            using (new GPUMarker(graphics, "Particles Simulate")) {
                if (dt > 0.2f) dt = 0.2f;
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

                var bindingsPtr = stackalloc CSBufferLayout[2];
                var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);
                using var materials = new PooledArray<Material>(2);
                materials[1] = rootMaterial;

                // Delete expired blocks
                foreach (var system in systems) {
                    system.UpdateExpired(graphics, emissionMesh, dt);
                }
                if (emissionMesh.IndexCount > 0) {
                    SetTargets(graphics, 1);
                    bindings[0] = emissionMesh.IndexBuffer;
                    bindings[1] = emissionMesh.VertexBuffer;
                    materials[0] = expireMaterial;
                    var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
                    var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
                    graphics.Draw(pso, bindings.AsCSSpan(), resources.AsCSSpan(), CSDrawConfig.Default);
                    SetTargets(graphics);
                    pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
                    resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
                    graphics.Draw(pso, bindings.AsCSSpan(), resources.AsCSSpan(), CSDrawConfig.Default);
                    emissionMesh.Clear();
                }
                SetTargets(graphics);

                // Spawn new blocks
                foreach (var system in systems) {
                    var irange = system.UpdateEmission(graphics, emissionMesh, dt);
                    if (irange.Length <= 0) continue;
                    bindings[0] = emissionMesh.IndexBuffer;
                    bindings[1] = emissionMesh.VertexBuffer;
                    materials[0] = system.SpawnerMaterial;
                    var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
                    var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
                    var drawConfig = CSDrawConfig.Default;
                    graphics.Draw(pso, bindings.AsCSSpan(), resources.AsCSSpan(), drawConfig);
                    emissionMesh.Clear();
                }

                // Update valid blocks
                FlipBuffers();
                SetTargets(graphics);
                foreach (var system in systems) {
                    if (!system.HasParticles) continue;
                    bindings[0] = updateMesh.IndexBuffer;
                    bindings[1] = updateMesh.VertexBuffer;
                    materials[0] = system.StepperMaterial;
                    var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
                    var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
                    var drawConfig = CSDrawConfig.Default;
                    graphics.Draw(pso, bindings.AsCSSpan(), resources.AsCSSpan(), drawConfig);
                }

                requireActiveBlocksUpdate = true;
            }
        }

        unsafe private void PruneOld(CSGraphics graphics) {
            SetTargets(graphics);
            var bindingsPtr = stackalloc CSBufferLayout[2] { updateMesh.IndexBuffer, updateMesh.VertexBuffer };
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);
            using var materials = new PooledArray<Material>(2);
            materials[1] = rootMaterial;
            materials[0] = rebaseMaterial;
            var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
            var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
            var drawConfig = CSDrawConfig.Default;
            graphics.Draw(pso, bindings.AsCSSpan(), resources.AsCSSpan(), drawConfig);
        }
        unsafe private void Rebase(CSGraphics graphics) {
            SetTargets(graphics);
            var bindingsPtr = stackalloc CSBufferLayout[2] { updateMesh.IndexBuffer, updateMesh.VertexBuffer };
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);
            using var materials = new PooledArray<Material>(2);
            materials[1] = rootMaterial;
            materials[0] = rebaseMaterial;
            var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
            var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
            var drawConfig = CSDrawConfig.Default;
            graphics.Draw(pso, bindings.AsCSSpan(), resources.AsCSSpan(), drawConfig);
        }

        public void Draw(CSGraphics graphics) {
            using (new GPUMarker(graphics, "Particles")) {
                Draw(graphics, RenderTag.Default | RenderTag.Transparent);
            }
        }
        unsafe public void Draw(CSGraphics graphics, RenderTags tags) {
            var bindingsPtr = stackalloc CSBufferLayout[1] { updateMesh.IndexBuffer, };
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 1);
            ref var materials = ref Graphics.MaterialStack;
            using var push = materials.Push(rootMaterial);
            ActiveBlocks.Clear();
            Span<int> idOffsets = stackalloc int[systems.Count];
            for (int i = 0; i < systems.Count; i++) {
                idOffsets[i] = systems[i].SetActiveBlocks(ref ActiveBlocks);
            }
            if (requireActiveBlocksUpdate) {
                ActiveBlocks.BufferLayout.revision++;
                graphics.CopyBufferData(ActiveBlocks);
            }
            requireActiveBlocksUpdate = false;
            for (int i = 0; i < systems.Count; i++) {
                var system = systems[i];
                if (!tags.HasAny(system.Tag)) continue;
                var from = idOffsets[i];
                var to = i + 1 >= idOffsets.Length ? ActiveBlocks.Count : idOffsets[i + 1];
                if (to == from) continue;
                using var push2 = materials.Push(system.DrawMaterial);
                rootMaterial.SetValue("ActiveBlocks", new CSBufferReference(ActiveBlocks, from, to - from));
                var pso = MaterialEvaluator.ResolvePipeline(graphics, bindings, materials);
                var resources = MaterialEvaluator.ResolveResources(graphics, pso, materials);
                foreach (var resource in resources.Reinterpret<CSBufferReference>()) {
                    if (resource.mType == CSBufferReference.BufferTypes.Texture) {
                        graphics.CommitTexture(new CSTexture((NativeTexture*)resource.mBuffer));
                    }
                }
                var drawConfig = CSDrawConfig.Default;
                drawConfig.mInstanceBase = from * ParticleSystem.AllocGroup.Count;
                graphics.Draw(pso, bindings.AsCSSpan(), resources.AsCSSpan(), drawConfig,
                    (to - from) * ParticleSystem.AllocGroup.Count);
            }
        }

        private void FlipBuffers() {
            bufferIndex = (bufferIndex + 1) % PositionBuffers.Length;
        }
        unsafe private void SetTargets(CSGraphics graphics, int delta = 0) {
            int selfIndex = (bufferIndex + delta) % PositionBuffers.Length;
            var targetsPtr = stackalloc CSRenderTarget[2] { PositionBuffers[selfIndex], VelocityBuffers[selfIndex] };
            var targets = new MemoryBlock<CSRenderTarget>(targetsPtr, 2);
            graphics.SetRenderTargets(targets, DepthStencilBuffer);
            int otherIndex = (bufferIndex + (PositionBuffers.Length - 1)) % PositionBuffers.Length;
            rootMaterial.SetTexture("PositionTexture", PositionBuffers[otherIndex]);
            rootMaterial.SetTexture("VelocityTexture", VelocityBuffers[otherIndex]);
            rootMaterial.SetTexture("AttributesTexture", AttributeBuffers[otherIndex]);
        }

        public ParticleSystem GetBlockSystem(uint blockId) {
            var blockX = blockId & 0xffff;
            var blockY = blockId >> 16;
            var blockIndex = blockX + blockY * blockMetaSize.X;
            return systems[blockMeta[blockIndex].SystemId];
        }

        public ParticleSystem RequireSystemFromJSON(Scene scene, string filepath) {
            var name = Path.GetFileNameWithoutExtension(filepath);
            var system = FindSystem(name);
            if (system != null) return system;
            var stParticleGenerator = new Particles.ParticleGenerator();
            var outpath = $"./Assets/Generated/{name}.hlsl";
            stParticleGenerator.LoadJSON(scene, filepath);
            if (!File.Exists(outpath) || File.GetLastWriteTimeUtc(outpath) < File.GetLastWriteTimeUtc(filepath)) {
                stParticleGenerator.WriteHLSL(outpath);
            }
            var stParticles = stParticleGenerator.CreateParticleSystem(outpath);
            AppendSystem(stParticles);
            return stParticles;
        }
    }
    public class ParticleSystem {
        private static readonly ushort[] quadIndices = new ushort[] { 0, 1, 2, 1, 3, 2, };
        private readonly int BufferDirtyFlag = unchecked((int)0x80000000);
        public readonly string? Name;
        public ParticleSystemManager? Manager;
        public Material CommonMaterial;
        public Material SpawnerMaterial;
        public Material StepperMaterial;
        public Material DrawMaterial;
        public RenderTags Tag = RenderTag.Transparent;
        private BufferLayoutPersistent emitterData;
        public int SystemId = -1;
        private float maximumLifetime = 5f;

        public float SpawnRate = 5000f;
        public float MaximumDuration {
            get => maximumLifetime;
            set { maximumLifetime = value; CommonMaterial.SetValue("Lifetime", maximumLifetime); }
        }

        public class Emitter {
            public FloatCurve CountOverTime = new();
            public int BurstCount = 10;
            public Vector3 Position;
            public float Age;
            public float Lifetime = -1f;        //-1f == last forever

            public bool IsAlive => Age >= 0f;
            public void MarkDead() { Age = -1f; }   // Just used for external code to query emitter state
            public void SetDelayedDeath(float duration) {
                Debug.Assert(IsAlive, "Emitter already dead");
                Lifetime = Age + duration;
            }
        }

        public struct EmissionInstance {
            public Emitter EmissionType;
            public Matrix4x4 ShapeTransform;
            public Material EmissionParameters;
        }

        public struct AllocGroup {
            public const int Size = 4;
            public const int Count = Size * Size;
            public AllocatedBlock Alloc;
            public UShort2 BlockPnt => Alloc.BlockPnt;
            public int ConsumeCount => IsValid ? Count - RemainCount : 0;
            public int RemainCount;
            public bool IsValid => RemainCount != -1;
            public uint BlockId => (uint)BlockPnt.X + ((uint)BlockPnt.Y << 16);
            public static readonly AllocGroup Invalid = new AllocGroup() { RemainCount = -1, };
        }
        public struct AllocatedBlock {
            public UShort2 BlockPnt;
            public int ExpireTimeMS;
            public uint BlockId => (uint)BlockPnt.X + ((uint)BlockPnt.Y << 16);
            public override string ToString() { return $"Block {BlockPnt}"; }
        }

        private Random random = new();
        private AllocGroup allocated = AllocGroup.Invalid;
        private List<AllocatedBlock> blocks = new();
        private List<Emitter> emitters = new();

        public bool HasParticles => blocks.Count > 0 || (allocated.ConsumeCount > 0 && !GetIsExpired(allocated.Alloc.ExpireTimeMS));

        unsafe public ParticleSystem(string name, string particleShader) {
            Name = name;
            CommonMaterial = new();
            CommonMaterial.SetValue("Lifetime", maximumLifetime);
            emitterData = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Uniform);
            emitterData.AppendElement(new CSBufferElement("POSITION", BufferFormat.FORMAT_R32G32B32_FLOAT));
            emitterData.AllocResize(256);
            SpawnerMaterial = new Material(
                Shader.FromPath(particleShader, "VSSpawn"),
                Shader.FromPath(particleShader, "PSSpawn")
            );
            SpawnerMaterial.InheritProperties(CommonMaterial);
            SpawnerMaterial.SetRasterMode(RasterMode.MakeNoCull());
            SpawnerMaterial.SetBuffer("Emitters", emitterData);
            var spawnDS = DepthMode.MakeWriteOnly().SetStencil(0x00, 0xff);
            spawnDS.StencilFront = spawnDS.StencilBack =
                new DepthMode.StencilDesc(DepthMode.StencilOp.Replace, DepthMode.StencilOp.Replace, DepthMode.StencilOp.Replace, DepthMode.Comparisons.Always);
            SpawnerMaterial.SetDepthMode(spawnDS);
            SpawnerMaterial.SetBlendMode(BlendMode.MakeOpaque());
            StepperMaterial = new Material(
                Shader.FromPath(particleShader, "VSStep"),
                Shader.FromPath(particleShader, "PSStep")
            );
            StepperMaterial.InheritProperties(CommonMaterial);
            StepperMaterial.SetRasterMode(RasterMode.MakeNoCull());
            StepperMaterial.SetDepthMode(DepthMode.MakeReadOnly(DepthMode.Comparisons.GEqual).SetStencil(0xff, 0x00));
            StepperMaterial.SetBlendMode(BlendMode.MakeOpaque());
            DrawMaterial = new Material(
                Shader.FromPath(particleShader, "VSMain"),
                Shader.FromPath(particleShader, "PSMain")
            );
            DrawMaterial.InheritProperties(CommonMaterial);
            DrawMaterial.SetRasterMode(RasterMode.MakeNoCull());
            DrawMaterial.SetDepthMode(DepthMode.MakeReadOnly());
        }

        internal void Initialise(ParticleSystemManager manager, int systemId) {
            Manager = manager;
            SystemId = systemId;
            SpawnerMaterial.SetStencilRef(systemId + 1);
            StepperMaterial.SetStencilRef(systemId + 1);
        }
        public Emitter CreateEmitter(Vector3 pos) {
            var emitter = new Emitter();
            emitter.CountOverTime.SetConstant(SpawnRate);
            emitter.Position = pos;
            emitters.Add(emitter);
            emitterData.BufferLayout.revision |= BufferDirtyFlag;
            return emitter;
        }
        private bool GetIsExpired(int expireTimeMS) {
            var delta = Manager.TimeMS - expireTimeMS;
            return delta >= 0;
        }
        public bool RemoveBlock(uint blockId) {
            if (blocks.Count > 0 && blocks[0].BlockId == blockId) {
                blocks.RemoveAt(0);
                return true;
            }
            if (allocated.BlockId == blockId) {
                allocated = AllocGroup.Invalid;
                return true;
            }
            return false;
        }
        public RangeInt UpdateExpired(CSGraphics graphics, DynamicMesh mesh, float dt) {
            var rangeBegin = mesh.IndexCount;
            int i = 0;
            for (; i < blocks.Count; i++) {
                var block = blocks[i];
                if (!GetIsExpired(block.ExpireTimeMS)) break;
                AppendBlockQuad(mesh, block.BlockPnt);
                Manager.DeallocBlock(block.BlockId, SystemId);
            }
            if (i > 0) blocks.RemoveRange(0, i);
            return RangeInt.FromBeginEnd(rangeBegin, mesh.IndexCount);
        }
        public RangeInt UpdateEmission(CSGraphics graphics, DynamicMesh mesh, float dt) {
            if (emitters.Count == 0) return default;

            //Debug.Assert(emitters.Count < 255, "Too many emitters!");
            var rangeBegin = mesh.IndexCount;
            for (int i = 0; i < emitters.Count; i++) {
                var emitter = emitters[i];
                var count = (int)(dt * emitter.CountOverTime.Evaluate(emitter.Age) + random.NextSingle());
                if (emitter.Age == 0 && dt > 0) count += emitter.BurstCount;
                emitter.Age += dt;
                for (int b = 0; count > 0; ++b) {
                    if (allocated.RemainCount <= 0) {
                        if (allocated.IsValid) {
                            blocks.Add(new AllocatedBlock() { BlockPnt = allocated.BlockPnt, ExpireTimeMS = Manager.TimeMS + (int)(MaximumDuration * 1000), });
                        }
                        allocated = Manager.AllocateBlock(SystemId);
                    }

                    int toConsume = Math.Min(allocated.RemainCount, count);
                    var verts = AppendBlockQuad(mesh, allocated.BlockPnt, allocated.ConsumeCount, toConsume);
                    verts.Mesh.GetColorsV<Vector2>().Slice(verts.Range).Set(new Vector2(i, dt));
                    allocated.RemainCount -= toConsume;
                    allocated.Alloc.ExpireTimeMS = Manager.TimeMS + (int)(MaximumDuration * 1000);
                    count -= toConsume;
                }
                if (emitter.Lifetime >= 0f && emitter.Age > emitter.Lifetime) {
                    emitter.MarkDead();
                    emitters.RemoveAt(i--);
                    emitterData.BufferLayout.revision |= BufferDirtyFlag;
                }
            }
            if ((emitterData.BufferLayout.revision & BufferDirtyFlag) != 0) {
                emitterData.BufferLayout.revision &= ~BufferDirtyFlag;
                int emitterCount = emitters.Count;// Math.Min(emitters.Count, 255);
                if (emitterCount > emitterData.BufferCapacityCount)
                    emitterData.AllocResize(emitterCount);
                emitterData.SetCount(emitterCount);
                var emitterPositions = new TypedBufferView<Vector3>(emitterData.Elements[0], emitterData.Count);
                for (int i = 0; i < emitterCount; i++) {
                    var emitter = emitters[i];
                    emitterPositions[i] = emitter.Position;
                }
                emitterData.NotifyChanged();
                graphics.CopyBufferData(emitterData, new RangeInt(0, emitterData.BufferStride * emitterCount));
            }
            return RangeInt.FromBeginEnd(rangeBegin, mesh.IndexCount);
        }

        private DynamicMesh.VertexRange AppendBlockQuad(DynamicMesh mesh, UShort2 blockPnt, int blockBegin = 0, int blockCount = AllocGroup.Count) {
            Span<Vector2> vpositions = stackalloc Vector2[4];
            Span<ushort> indices = stackalloc ushort[6];

            var verts = mesh.AppendVerts(4);
            var inds = mesh.AppendIndices(6);
            var size = (Vector2)Manager.PoolSize;
            var pos = blockPnt.ToVector2() * ParticleSystem.AllocGroup.Size;
            vpositions[0] = pos + new Vector2(0f, 0f);
            vpositions[1] = pos + new Vector2(AllocGroup.Size, 0f);
            vpositions[2] = pos + new Vector2(0f, AllocGroup.Size);
            vpositions[3] = pos + new Vector2(AllocGroup.Size, AllocGroup.Size);
            for (int i = 0; i < vpositions.Length; i++)
                vpositions[i] = (vpositions[i] / size * 2.0f - Vector2.One) * new Vector2(1.0f, -1.0f);

            verts.GetPositions<Vector2>().Set(vpositions);
            verts.GetTexCoords().Set(new Vector2(blockBegin, blockBegin + blockCount));
            quadIndices.CopyTo(indices);
            for (int i = 0; i < indices.Length; ++i) indices[i] += (ushort)verts.BaseVertex;
            inds.GetIndices<ushort>().Set(indices);
            return verts;
        }

        unsafe public int SetActiveBlocks(ref BufferLayoutPersistent activeBlocks) {
            int begin = activeBlocks.Count;
            int count = blocks.Count + (allocated.ConsumeCount > 0 ? 1 : 0);
            activeBlocks.BufferLayout.mCount += count;
            if (activeBlocks.BufferCapacityCount < activeBlocks.Count)
                activeBlocks.AllocResize((int)BitOperations.RoundUpToPowerOf2((uint)activeBlocks.Count));
            var activeBlockIds = new MemoryBlock<uint>((uint*)activeBlocks.Elements[0].mData + begin, count);
            for (int i = 0; i < blocks.Count; i++) activeBlockIds[i] = blocks[i].BlockId;
            if (allocated.ConsumeCount > 0) activeBlockIds[blocks.Count] = allocated.BlockId;
            return begin;
        }
        public override string ToString() {
            return DrawMaterial.ToString();
        }
    }

    public struct CParticles {
        public ParticleSystem ParticleSystem;
        public Vector3 LocalPosition;
    }
    [SparseComponent]
    public struct ECParticleBinding {
        public ParticleSystem.Emitter Emitter;
    }

    public class ParticleDebugWindow : ApplicationWindow {
        public readonly ParticleSystemManager ParticleManager;
        private Canvas canvas;
        private Image posImage, velImage;
        public ParticleDebugWindow(ParticleSystemManager particleManager) {
            ParticleManager = particleManager;
        }
        public override void RegisterRootWindow(CSWindow window) {
            base.RegisterRootWindow(window);
            Window.SetSize(new Int2(400, 200));
            CreateCanvas();
        }
        public void CreateCanvas() {
            canvas = new Canvas();
            posImage = new Image() { AspectMode = Image.AspectModes.PreserveAspectContain, };
            posImage.Element.RequireMaterial().SetMacro("NEARESTNEIGHBOUR", "1");
            posImage.AppendChild(new TextBlock("Position"));
            velImage = new Image() { AspectMode = Image.AspectModes.PreserveAspectContain, };
            velImage.Element.RequireMaterial().SetMacro("NEARESTNEIGHBOUR", "1");
            velImage.AppendChild(new TextBlock("Velocity"));
            var grid = new GridLayout();
            grid.AppendChild(velImage, new Int2(0, 0));
            grid.AppendChild(posImage, new Int2(1, 0));
            canvas.AppendChild(grid);
        }
        public void UpdateFrom(ParticleSystemManager particleManager) {
            posImage.Texture = particleManager.PositionBuffer;
            velImage.Texture = particleManager.VelocityBuffer;
        }
        public override void Render(float dt, CSGraphics graphics) {
            UpdateFrom(ParticleManager);

            graphics.Reset();
            graphics.SetSurface(Surface);
            var rt = Surface.GetBackBuffer();
            graphics.SetRenderTargets(new Span<CSRenderTarget>(ref rt), default);
            graphics.Clear();
            canvas.SetSize(WindowSize);
            canvas.Update(dt);
            canvas.RequireComposed();
            canvas.Render(graphics);
            graphics.Execute();
            Surface.Present();
        }
    }

}
