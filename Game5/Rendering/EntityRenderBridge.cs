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

namespace Weesals.Rendering {
    [SparseComponent, NoCloneComponent]
    public struct SceneRenderable {
        public CSInstance[] SceneIndex;
        public Material? AnimMaterial;

        public override string ToString() { return SceneIndex.Select(i => i.ToString()).Aggregate((i1, i2) => $"{i1}, {i2}").ToString(); }
    }

    public class RenderWorldBinding : IHighlightListener {
        public readonly World World;
        //public readonly World RenderWorld;
        public readonly Scene Scene;
        public readonly ScenePassManager ScenePasses;
        public class TableBindings {
            public Entity[] SceneEntities = Array.Empty<Entity>();
            public ArchetypeComponentLookup<CModel> ModelLookup;
            public ArchetypeComponentLookup<ECTransform> TransformLookup;
            public ArchetypeComponentLookup<CSelectable> SelectedLookup;
            public ArchetypeComponentLookup<CAnimation> AnimationLookup;
            public DynamicBitField ChangedModels = new();
            public DynamicBitField ChangedTransforms = new();
            public DynamicBitField ChangedSelected = new();
            public DynamicBitField ValidEntities = new();
            public TableBindings(StageContext context, Archetype table) {
                ModelLookup = new(context, table);
                ModelLookup.GetColumn(table).AddModificationListener(ChangedModels);
                TransformLookup = new(context, table);
                TransformLookup.GetColumn(table).AddModificationListener(ChangedTransforms);
                SelectedLookup = new(context, table);
                if (SelectedLookup.IsValid)
                    SelectedLookup.GetColumn(table).AddModificationListener(ChangedSelected);
                AnimationLookup = new(context, table);
            }
        }
        public TableBindings[] Bindings = Array.Empty<TableBindings>();
        public RenderWorldBinding(World world, World renderWorld, Scene scene, ScenePassManager scenePasses) {
            World = world;
            //RenderWorld = renderWorld;
            Scene = scene;
            ScenePasses = scenePasses;

            var renderables = World.BeginQuery().With<ECTransform>().With<CModel>().Build();
            World.Stage.AddListener(renderables, new ArchetypeListener() {
                OnCreate = (entityAddr) => {
                    //var renEntity = renderWorld.CreateEntity();
                    var entity = World.Stage.GetEntity(entityAddr);
                    var binding = RequireBinding(entityAddr);
                    binding.SceneEntities[entityAddr.Row] = entity;
                    binding.ChangedModels.Add(entityAddr.Row);
                    binding.ChangedTransforms.Add(entityAddr.Row);
                    binding.ValidEntities.Add(entityAddr.Row);
                    World.Stage.AddComponent<SceneRenderable>(entity) = new() {
                        SceneIndex = Array.Empty<CSInstance>(),
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
            if (fromBining.ChangedModels.TryRemove(from.Row)) toBinding.ChangedModels.Add(to.Row);
            if (fromBining.ChangedTransforms.TryRemove(from.Row)) toBinding.ChangedTransforms.Add(to.Row);
            if (fromBining.ChangedSelected.TryRemove(from.Row)) toBinding.ChangedSelected.Add(to.Row);
            if (fromBining.ValidEntities.TryRemove(from.Row)) toBinding.ValidEntities.Add(to.Row);
        }
        private void RemoveEntityFlags(EntityAddress entityAddr) {
            ref var binding = ref Bindings[entityAddr.ArchetypeId];
            binding.ChangedModels.TryRemove(entityAddr.Row);
            binding.ChangedTransforms.TryRemove(entityAddr.Row);
            binding.ChangedSelected.TryRemove(entityAddr.Row);
            binding.ValidEntities.TryRemove(entityAddr.Row);
        }
        private void RemoveEntityScene(EntityAddress entityAddr) {
            ref var sceneProxy = ref World.Stage.GetComponentRef<SceneRenderable>(entityAddr);
            foreach (var index in sceneProxy.SceneIndex) {
                ScenePasses.RemoveInstance(index);
                Scene.RemoveInstance(index);
            }
            sceneProxy.SceneIndex = Array.Empty<CSInstance>();
        }
        public TableBindings RequireBinding(EntityAddress entityAddr) {
            if (entityAddr.ArchetypeId >= Bindings.Length)
                Array.Resize(ref Bindings, entityAddr.ArchetypeId + 16);
            ref var binding = ref Bindings[entityAddr.ArchetypeId];
            if (binding == null)
                binding = new(World.Context, World.Stage.GetArchetype(entityAddr.ArchetypeId));
            if (entityAddr.Row >= binding.SceneEntities.Length)
                Array.Resize(ref binding.SceneEntities, entityAddr.Row + 256);
            return binding;
        }
        public ref Entity RequireEntitySlot(EntityAddress entityAddr) {
            return ref RequireBinding(entityAddr).SceneEntities[entityAddr.Row];
        }
        public void UpdateModel(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var emodel = binding.ModelLookup.GetValue(World.Stage, entityAddr);
            var model = emodel.Model;
            ref var sceneProxy = ref World//RenderWorld
                .GetComponentRef<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            foreach (var index in sceneProxy.SceneIndex) {
                ScenePasses.RemoveInstance(index);
                Scene.RemoveInstance(index);
            }
            sceneProxy.SceneIndex = new CSInstance[model != null ? model.Meshes.Count : 0];
            if (binding.AnimationLookup.IsValid) {
                sceneProxy.AnimMaterial ??= new();
                UpdateAnimation(entityAddr);
            }
            for (int i = 0; i < sceneProxy.SceneIndex.Length; i++) {
                var mesh = model.Meshes[i];
                var instance = Scene.CreateInstance(mesh.BoundingBox);
                ScenePasses.AddInstance(instance, mesh, sceneProxy.AnimMaterial, RenderTags.Default);
                sceneProxy.SceneIndex[i] = instance;
            }
            binding.ChangedSelected.TryRemove(entityAddr.Row);
            if (binding.TransformLookup.IsValid) UpdateTransform(entityAddr);
            if (binding.SelectedLookup.GetHasSparseComponent(World.Stage, entityAddr)) UpdateSelected(entityAddr);
        }
        public void UpdateTransform(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var epos = binding.TransformLookup.GetValue(World.Stage, entityAddr);
            var emodel = binding.ModelLookup.GetValue(World.Stage, entityAddr);
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            var tform = epos.AsMatrix();
            for (int i = 0; i < sceneProxy.SceneIndex.Length; i++) {
                var mesh = emodel.Model.Meshes[i];
                Scene.SetTransform(sceneProxy.SceneIndex[i], mesh.Transform * tform);
            }
        }
        unsafe public void UpdateSelected(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var archetype = World.Stage.GetArchetype(entityAddr.ArchetypeId);
            var selected = binding.SelectedLookup.GetValue(archetype, entityAddr.Row);
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            foreach (var index in sceneProxy.SceneIndex)
                Scene.UpdateInstanceData(index, sizeof(float) * (16 + 16 + 4),
                    selected.Selected ? 1.0f : 0.0f);
        }
        private void UpdateAnimation(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var emodel = binding.ModelLookup.GetValue(World.Stage, entityAddr);
            var model = emodel.Model;
            var archetype = World.Stage.GetArchetype(entityAddr.ArchetypeId);
            var eanim = binding.AnimationLookup.GetValueRef(archetype, entityAddr.Row);
            ref var sceneProxy = ref World//RenderWorld
                .GetComponentRef<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            if (sceneProxy.AnimMaterial == null) return;
            var skinnedMesh = model.Meshes[0] as SkinnedMesh;
            Span<Matrix4x4> bones = stackalloc Matrix4x4[32];
            var animation = eanim.Animation;
            var time = UnityEngine.Time.time % (float)animation.Duration.TotalSeconds;
            var animPlayback = new AnimationPlayback(skinnedMesh);
            animPlayback.SetAnimation(animation.GetAs<Animation>());
            animPlayback.UpdateClip(time);
            var boneTransforms = animPlayback.ApplyBindPose(skinnedMesh);
            sceneProxy.AnimMaterial!.SetArrayValue("BoneTransforms", boneTransforms.AsSpan());
        }

        public void UpdateChanged() {
            for (int i = 0; i < Bindings.Length; i++) {
                var binding = Bindings[i];
                if (binding == null) continue;
                foreach (var row in binding.ChangedModels) {
                    UpdateModel(new EntityAddress(new ArchetypeId(i), row));
                }
                foreach (var row in binding.ChangedTransforms) {
                    UpdateTransform(new EntityAddress(new ArchetypeId(i), row));
                }
                foreach (var row in binding.ChangedSelected) {
                    UpdateSelected(new EntityAddress(new ArchetypeId(i), row));
                }
                if (binding.AnimationLookup.IsValid) {
                    foreach (var row in binding.ValidEntities) {
                        UpdateAnimation(new EntityAddress(new ArchetypeId(i), row));
                    }
                }
                binding.ChangedModels.Clear();
                binding.ChangedTransforms.Clear();
                binding.ChangedSelected.Clear();
            }
        }

        public void NotifyHighlightChanged(ItemReference target, Color color) {
            if (!World.IsValid(target.GetEntity())) return;
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(target.GetEntity());
            foreach (var index in sceneProxy.SceneIndex)
                Scene.UpdateInstanceData(index, sizeof(float) * (16 + 16),
                    (Vector4)color);
        }

    }
}
