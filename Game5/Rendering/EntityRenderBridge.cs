using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Game5.Game;
using Game5.Game.Gameplay;
using System.Xml.Linq;
using Weesals.Utility;
using System.Collections;
using Weesals.Engine.Jobs;
using System.Diagnostics;
using Weesals.Engine.Profiling;

namespace Weesals.Rendering {
    [SparseComponent, NoCloneComponent]
    public struct SceneRenderable {
        public VisualInstance Instance;

        //public override string ToString() { return SceneIndex.Select(i => i.ToString()).Aggregate((i1, i2) => $"{i1}, {i2}").ToString(); }
        public override string ToString() { return $"{Instance}"; }
    }

    public class VisualPrefab {
        public struct ModelInstance {
            public int IdOffset;
            public Model Model;
            public IReadOnlyList<Mesh> Meshes => Model.Meshes;
            public ModelInstance(Model model) {
                Model = model;
            }
        }
        public struct ParticleInstance {
            public ParticleSystem Particle;
            public Vector3 LocalPosition;
            public ParticleInstance(ParticleSystem particle) {
                Particle = particle;
            }
        }

        public string Name;
        public ModelInstance[] Models;
        public ParticleInstance[] Particles;
        public int MeshCount;

        public void FinalizeLoad() {
            int offset = 0;
            for (int i = 0; i < Models.Length; i++) {
                Models[i].IdOffset = offset;
                offset += Models[i].Model.Meshes.Count;
            }
            MeshCount = offset;
        }
    }
    public class VisualInstance {
        public VisualPrefab Prefab;
        public SceneInstance[] Meshes;
        public ParticleSystem.Emitter[] Particles;
        public Material? AnimMaterial;
        public VisualInstance(VisualPrefab prefab) {
            Prefab = prefab;
            Meshes = new SceneInstance[Prefab.MeshCount];
            Particles = new ParticleSystem.Emitter[Prefab.Particles.Length];
        }
    }

    public class VisualsCollection {
        public readonly Play Play;
        public class VisualCollection {
            public string Name;
            public VisualPrefab[] Variants;
            public VisualCollection(string name, VisualPrefab[] variants) {
                Name = name;
                Variants = variants;
            }
        }
        public Dictionary<string, VisualCollection> VisualsByName = new();
        public List<VisualCollection> Visuals = new();

        public VisualsCollection(Play play) {
            Play = play;
        }
        struct VariantBuilder : IDisposable {
            public PooledList<VisualPrefab.ModelInstance> Models = new();
            public PooledList<VisualPrefab.ParticleInstance> Particles = new();
            public bool IsValid => Models.IsCreated;
            public VariantBuilder() { }
            public VisualPrefab Build() {
                return new VisualPrefab() {
                    Models = Models.ToArray(),
                    Particles = Particles.ToArray(),
                };
            }
            public void Dispose() {
                Models.Dispose();
                Particles.Dispose();
            }
        }
        struct LoadContext {
            public PooledList<VariantBuilder> Variants;
            public JobHandle LoadHandle;
            public void AppendModel(SJson jModel) {
                if (jModel.IsArray) {
                    foreach (var jChild in jModel) AppendModel(jChild);
                    return;
                }
                var path = (string)jModel["Path"];
                var model = new VisualPrefab.ModelInstance(Resources.LoadModel(path, out var handle));
                LoadHandle = JobHandle.CombineDependencies(LoadHandle, handle);
                var jVariantMask = jModel["VariantMask"];
                foreach (var id in new IntEnumerator(jVariantMask.IsValid ? jVariantMask.ToString() : null, 0, true)) {
                    RequireVariant(id).Models.Add(model);
                }
            }
            public void AppendParticle(SJson jParticle, Play play) {
                if (jParticle.IsArray) {
                    foreach (var jChild in jParticle) AppendParticle(jChild, play);
                    return;
                }
                var path = (string)jParticle["Path"];
                var particleType = play.ParticleManager.RequireSystemFromJSON(play.Scene, path);
                var particle = new VisualPrefab.ParticleInstance(particleType);
                var xLocalPosition = jParticle["LocalPosition"];
                if (xLocalPosition.IsValid) {
                    var floatEn = new FloatEnumerator(xLocalPosition.ToString());
                    for (int i = 0; i < 3; ++i) {
                        if (!floatEn.MoveNext()) break;
                        particle.LocalPosition[i] = floatEn.Current;
                    }
                }
                var jVariantMask = jParticle["VariantMask"];
                foreach (var id in new IntEnumerator(jVariantMask.IsValid ? jVariantMask.ToString() : null, 0, true)) {
                    RequireVariant(id).Particles.Add(particle);
                }
            }
            private ref VariantBuilder RequireVariant(int id) {
                while (id >= Variants.Count) Variants.Add(default);
                if (!Variants[id].IsValid) Variants[id] = new();
                return ref Variants[id];
            }
            public void Dispose() {
                Variants.Dispose();
            }
        }
        public JobHandle Load(string doc) {
            var json = new SJson(doc);
            var jVisuals = json["Visuals"];
            LoadContext loadContext = new();
            foreach (var jVisual in jVisuals) {
                string? name = null;
                foreach (var jField in jVisual.GetFields()) {
                    if (jField.Key == "Name") {
                        name = (string)jField.Value;
                    } else if (jField.Key == "Model") {
                        loadContext.AppendModel(jField.Value);
                    } else if (jField.Key == "Particle") {
                        loadContext.AppendParticle(jField.Value, Play);
                    }
                }
                ref var variants = ref loadContext.Variants;
                if (loadContext.Variants[0].IsValid) {
                    for (int i = 1; i < variants.Count; i++) {
                        if (!variants[i].IsValid) continue;
                        variants[i].Models.AddRange(variants[0].Models);
                        variants[i].Particles.AddRange(variants[0].Particles);
                    }
                }
                // If we have variants, ignore the common variant (0)
                int var0 = variants.Count > 1 ? 1 : 0;
                var visual = new VisualCollection(
                    name ?? "",
                    new VisualPrefab[variants.Count - var0]
                );
                for (int i = var0; i < variants.Count; i++) {
                    if (!variants[i].IsValid) continue;
                    visual.Variants[i - var0] = variants[i].Build();
                    visual.Variants[i - var0].Name = i == 0 ? visual.Name : $"{visual.Name} ({i})";
                    variants[i].Dispose();
                }
                Visuals.Add(visual);
                VisualsByName.Add(visual.Name, visual);
                variants.Clear();
            }
            var loadHandle = loadContext.LoadHandle;
            loadContext.Dispose();
            loadHandle = JobHandle.Schedule(() => {
                using var marker = new ProfilerMarker("Vis Finalize").Auto();
                foreach (var context in Visuals) {
                    foreach (var variant in context.Variants) {
                        if (variant == null) continue;
                        variant.FinalizeLoad();
                    }
                }
            }, loadHandle);
            return loadHandle;
        }

        public VisualPrefab GetVisuals(string name) {
            if (name != null && VisualsByName.TryGetValue(name, out var visual)) {
                return visual.Variants[0];
            }
            return default!;
        }

        public struct IntEnumerator : IEnumerator<int> {
            public readonly string? Source;
            public int Index;
            public int Current { get; private set; }
            object IEnumerator.Current => Current;
            public IntEnumerator(string? str, int index = 0, bool forceDefault = false) {
                Source = str;
                Index = index;
                Current = forceDefault ? -1 : 0;
            }
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                if (Source == null) {
                    if (Current == 0) return false;
                    Current = 0;
                    return true;
                }
                Current = 0;
                while (Index < Source.Length && char.IsWhiteSpace(Source[Index])) ++Index;
                int begin = Index;
                for (; Index < Source.Length && char.IsNumber(Source[Index]); ++Index) {
                    Current = Current * 10 + (int)(Source[Index] - '0');
                }
                if (begin == Index) return false;
                while (Index < Source.Length && char.IsWhiteSpace(Source[Index])) ++Index;
                if (Index < Source.Length && Source[Index] == ',') ++Index;
                return true;
            }
            public IntEnumerator GetEnumerator() { return this; }
        }
        public struct FloatEnumerator : IEnumerator<float> {
            public readonly string? Source;
            public int Index;
            public float Current { get; private set; }
            object IEnumerator.Current => Current;
            public FloatEnumerator(string? str, int index = 0) {
                Source = str;
                Index = index;
                Current = -1;
            }
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                if (Source == null) return false;
                Current = 0;
                while (Index < Source.Length && char.IsWhiteSpace(Source[Index])) ++Index;
                int begin = Index;
                bool neg = Index < Source.Length && Source[Index] == '-';
                if (neg) ++Index;
                for (; Index < Source.Length && char.IsNumber(Source[Index]); ++Index) {
                    Current = Current * 10 + (int)(Source[Index] - '0');
                }
                if (Index < Source.Length && Source[Index] == '.') {
                    ++Index;
                    float Div = 0.1f;
                    for (; Index < Source.Length && char.IsNumber(Source[Index]); ++Index) {
                        Current += Div * (int)(Source[Index] - '0');
                        Div /= 10.0f;
                    }
                }
                if (begin == Index) return false;
                if (neg) Current = -Current;
                while (Index < Source.Length && char.IsWhiteSpace(Source[Index])) ++Index;
                if (Index < Source.Length && Source[Index] == ',') ++Index;
                return true;
            }
            public FloatEnumerator GetEnumerator() { return this; }
        }
    }

    public class RenderWorldBinding : IHighlightListener {
        public readonly World World;
        //public readonly World RenderWorld;
        public readonly Scene Scene;
        public readonly ScenePassManager ScenePasses;
        public readonly ParticleSystemManager ParticleSystem;
        public readonly VisualsCollection EntityVisuals;
        public class TableBindings {
            public Entity[] SceneEntities = Array.Empty<Entity>();
            public ArchetypeComponentLookup<CModel> ModelLookup;
            public ArchetypeComponentLookup<ECTransform> TransformLookup;
            public ArchetypeComponentLookup<CSelectable> SelectedLookup;
            public ArchetypeComponentLookup<CAnimation> AnimationLookup;
            public RevisionMonitor ChangedModels;
            public RevisionMonitor ChangedTransforms;
            public RevisionMonitor ChangedSelected;
            //public RevisionMonitor ValidEntities;
            //public DynamicBitField ChangedModels = new();
            //public DynamicBitField ChangedTransforms = new();
            //public DynamicBitField ChangedSelected = new();
            public DynamicBitField ValidEntities = new();
            public TableBindings(EntityManager manager, ref Archetype archetype) {
                ModelLookup = new(manager, ref archetype);
                //ModelLookup.AddModificationListener(manager, archetype, ChangedModels);
                ChangedModels = ModelLookup.CreateRevisionMonitor(manager, true);
                TransformLookup = new(manager, ref archetype);
                //TransformLookup.AddModificationListener(manager, archetype, ChangedTransforms);
                ChangedTransforms = TransformLookup.CreateRevisionMonitor(manager, true);
                SelectedLookup = new(manager, ref archetype);
                if (SelectedLookup.IsValid)
                    ChangedSelected = SelectedLookup.CreateRevisionMonitor(manager);
                    //SelectedLookup.AddModificationListener(manager, archetype, ChangedSelected);
                AnimationLookup = new(manager, ref archetype);
            }
        }
        public TableBindings[] Bindings = Array.Empty<TableBindings>();
        public RenderWorldBinding(World world, Scene scene, ScenePassManager scenePasses, ParticleSystemManager particleSystem, VisualsCollection entityVisuals) {
            World = world;
            //RenderWorld = renderWorld;
            Scene = scene;
            ScenePasses = scenePasses;
            ParticleSystem = particleSystem;
            EntityVisuals = entityVisuals;

            var renderables = World.BeginQuery().With<ECTransform>().With<CModel>().Build();
            World.Manager.AddListener(renderables, new ArchetypeListener() {
                OnCreate = (entityAddr) => {
                    //var renEntity = renderWorld.CreateEntity();
                    var entity = World.Manager.GetEntity(entityAddr);
                    var binding = RequireBinding(entityAddr);
                    binding.SceneEntities[entityAddr.Row] = entity;
                    /*binding.ChangedModels.TryAdd(entityAddr.Row);
                    binding.ChangedTransforms.TryAdd(entityAddr.Row);*/
                    binding.ValidEntities.TryAdd(entityAddr.Row);
                    World.Manager.AddComponent<SceneRenderable>(entity) = new() {
                        //SceneIndex = Array.Empty<SceneInstance>(),
                    };
                },
                OnMove = (move) => {
                    RequireEntitySlot(move.To) = RequireEntitySlot(move.From);
                    MoveEntityFlags(move.From, move.To);
                },
                OnDelete = (entityAddr) => {
                    RemoveEntityFlags(entityAddr);
                    RemoveEntityScene(entityAddr);
                    //renderWorld.DeleteEntity(RequireEntitySlot(entity));
                }
            });
        }

        private void MoveEntityFlags(EntityAddress from, EntityAddress to) {
            ref var fromBining = ref Bindings[from.ArchetypeId];
            ref var toBinding = ref Bindings[to.ArchetypeId];
            /*if (fromBining.ChangedModels.TryRemove(from.Row)) toBinding.ChangedModels.Add(to.Row);
            if (fromBining.ChangedTransforms.TryRemove(from.Row)) toBinding.ChangedTransforms.Add(to.Row);
            if (fromBining.ChangedSelected.TryRemove(from.Row)) toBinding.ChangedSelected.Add(to.Row);*/
            if (fromBining.ValidEntities.TryRemove(from.Row)) toBinding.ValidEntities.Add(to.Row);
        }
        private void RemoveEntityFlags(EntityAddress entityAddr) {
            ref var binding = ref Bindings[entityAddr.ArchetypeId];
            /*binding.ChangedModels.TryRemove(entityAddr.Row);
            binding.ChangedTransforms.TryRemove(entityAddr.Row);
            binding.ChangedSelected.TryRemove(entityAddr.Row);*/
            binding.ValidEntities.TryRemove(entityAddr.Row);
        }
        private void RemoveEntityScene(EntityAddress entityAddr) {
            ref var sceneProxy = ref World.Manager.GetComponentRef<SceneRenderable>(entityAddr);
            DestroyInstance(sceneProxy.Instance);
            sceneProxy.Instance = default;
        }

        private VisualInstance CreateInstance(VisualPrefab prefab, bool animated = false) {
            var visuals = new VisualInstance(prefab);
            if (animated) visuals.AnimMaterial ??= new();
            for (int i = 0; i < prefab.Models.Length; i++) {
                var model = prefab.Models[i];
                for (int m = 0; m < model.Meshes.Count; m++) {
                    var mesh = model.Meshes[m];
                    var instance = Scene.CreateInstance(mesh.BoundingBox);
                    ScenePasses.AddInstance(instance, mesh, visuals.AnimMaterial, RenderTags.Default);
                    visuals.Meshes[model.IdOffset + m] = instance;
                }
            }
            for (int i = 0; i < prefab.Particles.Length; i++) {
                var particle = prefab.Particles[i];
                visuals.Particles[i] = particle.Particle.CreateEmitter(Vector3.Zero);
            }
            return visuals;
        }
        private void DestroyInstance(VisualInstance instance) {
            if (instance == null) return;
            foreach (var index in instance.Meshes) {
                ScenePasses.RemoveInstance(index);
                Scene.RemoveInstance(index);
            }
            foreach (var emitter in instance.Particles) {
                emitter.MarkDead();
            }
            instance.Meshes = Array.Empty<SceneInstance>();
            instance.Particles = Array.Empty<ParticleSystem.Emitter>();
        }

        public TableBindings RequireBinding(EntityAddress entityAddr) {
            if (entityAddr.ArchetypeId >= Bindings.Length)
                Array.Resize(ref Bindings, entityAddr.ArchetypeId + 16);
            ref var binding = ref Bindings[entityAddr.ArchetypeId];
            if (binding == null)
                binding = new(World.Manager, ref World.Manager.GetArchetype(entityAddr.ArchetypeId));
            if (entityAddr.Row >= binding.SceneEntities.Length)
                Array.Resize(ref binding.SceneEntities, (int)BitOperations.RoundUpToPowerOf2((uint)entityAddr.Row + 512));
            return binding;
        }
        public ref Entity RequireEntitySlot(EntityAddress entityAddr) {
            return ref RequireBinding(entityAddr).SceneEntities[entityAddr.Row];
        }
        public void UpdateModel(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            ref var sceneProxy = ref World//RenderWorld
                .GetComponentRef<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            if (sceneProxy.Instance != null) {
                DestroyInstance(sceneProxy.Instance);
            }
            var emodel = binding.ModelLookup.GetValueRO(World.Manager, entityAddr);
            var prefab = EntityVisuals.GetVisuals(emodel.PrefabName);
            if (prefab == null) return;
            sceneProxy.Instance = CreateInstance(prefab, binding.AnimationLookup.IsValid);
            if (binding.AnimationLookup.IsValid) {
                UpdateAnimation(entityAddr);
            }
            //binding.ChangedSelected.TryRemove(entityAddr.Row);
            if (binding.TransformLookup.IsValid) UpdateTransform(entityAddr);
            if (binding.SelectedLookup.GetHasSparseComponent(World.Manager, entityAddr)) UpdateSelected(entityAddr);
        }
        public void UpdateTransform(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            if (sceneProxy.Instance == null) return;
            var transform = binding.TransformLookup.GetValueRO(World.Manager, entityAddr);
            var tform = transform.AsMatrix();
            var prefab = sceneProxy.Instance.Prefab;
            foreach (var model in prefab.Models) {
                for(int m = 0; m < model.Meshes.Count; m++) {
                    var mesh = model.Model.Meshes[m];
                    int i = model.IdOffset + m;
                    Scene.SetTransform(sceneProxy.Instance.Meshes[i], mesh.Transform * tform);
                }
            }
            for (int i = 0; i < sceneProxy.Instance.Particles.Length; i++) {
                var particle = sceneProxy.Instance.Particles[i];
                var ogparticle = prefab.Particles[i];
                particle.Position = Vector3.Transform(ogparticle.LocalPosition, tform);
            }
        }
        unsafe public void UpdateSelected(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var selected = binding.SelectedLookup.GetValueRO(World.Manager, entityAddr);
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            if (sceneProxy.Instance == null) return;
            foreach (var index in sceneProxy.Instance.Meshes)
                Scene.UpdateInstanceData(index, sizeof(float) * (16 + 16 + 4),
                    selected.Selected ? 1.0f : 0.0f);
        }
        private void UpdateAnimation(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var emodel = binding.ModelLookup.GetValueRO(World.Manager, entityAddr);
            ref var archetype = ref World.Manager.GetArchetype(entityAddr.ArchetypeId);
            var eanim = binding.AnimationLookup.GetValueRO(ref World.Manager.ColumnStorage, ref archetype, entityAddr.Row);
            ref var sceneProxy = ref World//RenderWorld
                .GetComponentRef<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            if (sceneProxy.Instance == null) return;
            if (sceneProxy.Instance.AnimMaterial == null) return;
            var model = sceneProxy.Instance.Prefab.Models[0];
            var skinnedMesh = model.Meshes[0] as SkinnedMesh;
            Debug.Assert(skinnedMesh != null, "Attempting to skin a non-skinnable mesh. Probaby remove CAnimation");
            Span<Matrix4x4> bones = stackalloc Matrix4x4[32];
            var animation = eanim.Animation;
            var time = UnityEngine.Time.time % (float)animation.Duration.TotalSeconds;
            var animPlayback = new AnimationPlayback(skinnedMesh);
            animPlayback.SetAnimation(animation.GetAs<Animation>());
            animPlayback.UpdateClip(time);
            var boneTransforms = animPlayback.ApplyBindPose(skinnedMesh);
            sceneProxy.Instance.AnimMaterial!.SetArrayValue("BoneTransforms", boneTransforms.AsSpan());
        }

        public void UpdateChanged() {
            ref var columnStorage = ref World.Manager.ColumnStorage;
            for (int i = 0; i < Bindings.Length; i++) {
                var binding = Bindings[i];
                if (binding == null) continue;
                ref var archetype = ref World.Manager.GetArchetype(new ArchetypeId(i));
                var changedModels = columnStorage.GetChanges(binding.ChangedModels, ref archetype);
                var changedTransforms = columnStorage.GetChanges(binding.ChangedTransforms, ref archetype);
                var changedSelected = columnStorage.GetChanges(binding.ChangedSelected, ref archetype);
                foreach (var row in changedModels) {
                    UpdateModel(new EntityAddress(new ArchetypeId(i), row));
                }
                foreach (var row in changedTransforms) {
                    UpdateTransform(new EntityAddress(new ArchetypeId(i), row));
                }
                foreach (var row in changedSelected) {
                    UpdateSelected(new EntityAddress(new ArchetypeId(i), row));
                }
                if (binding.AnimationLookup.IsValid) {
                    foreach (var row in binding.ValidEntities) {
                        UpdateAnimation(new EntityAddress(new ArchetypeId(i), row));
                    }
                }
                columnStorage.Reset(ref binding.ChangedModels, ref archetype);
                columnStorage.Reset(ref binding.ChangedTransforms, ref archetype);
                columnStorage.Reset(ref binding.ChangedSelected, ref archetype);
                //binding.ChangedModels.Clear();
                //binding.ChangedTransforms.Clear();
                //binding.ChangedSelected.Clear();
            }
        }

        public void NotifyHighlightChanged(ItemReference target, Color color) {
            if (!World.IsValid(target.GetEntity())) return;
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(target.GetEntity());
            if (sceneProxy.Instance == null) return;
            foreach (var index in sceneProxy.Instance.Meshes)
                Scene.UpdateInstanceData(index, sizeof(float) * (16 + 16),
                    (Vector4)color);
        }

    }
}
