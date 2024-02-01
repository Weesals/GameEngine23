using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Game;
using Weesals.Game.Gameplay;

namespace Weesals.Rendering {
    [SparseComponent, NoCopyComponent]
    public struct SceneRenderable {
        public CSInstance[] SceneIndex;
        public override string ToString() { return SceneIndex.ToString(); }
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
            public ArchetypeComponentLookup<CPosition> PositionLookup;
            public ArchetypeComponentLookup<CSelectable> SelectedLookup;
            public DynamicBitField ChangedModels = new();
            public DynamicBitField ChangedTransforms = new();
            public DynamicBitField ChangedPositions = new();
            public DynamicBitField ChangedSelected = new();
            public TableBindings(StageContext context, Archetype table) {
                ModelLookup = new(context, table);
                ModelLookup.GetColumn(table).AddModificationListener(ChangedModels);
                TransformLookup = new(context, table);
                TransformLookup.GetColumn(table).AddModificationListener(ChangedTransforms);
                PositionLookup = new(context, table);
                PositionLookup.GetColumn(table).AddModificationListener(ChangedPositions);
                SelectedLookup = new(context, table);
                if (SelectedLookup.IsValid)
                    SelectedLookup.GetColumn(table).AddModificationListener(ChangedSelected);
            }
        }
        public TableBindings[] Bindings = Array.Empty<TableBindings>();
        public RenderWorldBinding(World world, World renderWorld, Scene scene, ScenePassManager scenePasses) {
            World = world;
            //RenderWorld = renderWorld;
            Scene = scene;
            ScenePasses = scenePasses;

            var renderables = World.BeginQuery().With<CPosition>().With<CModel>().Build();
            World.Stage.AddListener(renderables, new ArchetypeListener() {
                OnCreate = (entityAddr) => {
                    //var renEntity = renderWorld.CreateEntity();
                    var entity = World.Stage.GetEntity(entityAddr);
                    RequireEntitySlot(entityAddr) = entity;
                    World.Stage.AddComponent<SceneRenderable>(entity) = new() {
                        SceneIndex = Array.Empty<CSInstance>(),
                    };
                    //UpdateTransform(entityAddr);
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
            if (fromBining.ChangedPositions.TryRemove(from.Row)) toBinding.ChangedPositions.Add(to.Row);
            if (fromBining.ChangedSelected.TryRemove(from.Row)) toBinding.ChangedSelected.Add(to.Row);
        }
        private void RemoveEntityFlags(EntityAddress entityAddr) {
            ref var binding = ref Bindings[entityAddr.ArchetypeId];
            binding.ChangedModels.TryRemove(entityAddr.Row);
            binding.ChangedTransforms.TryRemove(entityAddr.Row);
            binding.ChangedPositions.TryRemove(entityAddr.Row);
            binding.ChangedSelected.TryRemove(entityAddr.Row);
        }
        private void RemoveEntityScene(EntityAddress entityAddr) {
            ref var sceneProxy = ref World.Stage.GetComponentRef<SceneRenderable>(entityAddr);
            foreach (var index in sceneProxy.SceneIndex) Scene.RemoveInstance(index);
            sceneProxy.SceneIndex = Array.Empty<CSInstance>();
        }
        public ref Entity RequireEntitySlot(EntityAddress entityAddr) {
            if (entityAddr.ArchetypeId >= Bindings.Length)
                Array.Resize(ref Bindings, entityAddr.ArchetypeId + 16);
            ref var binding = ref Bindings[entityAddr.ArchetypeId];
            if (binding == null)
                binding = new(World.Context, World.Stage.GetArchetype(entityAddr.ArchetypeId));
            if (entityAddr.Row >= binding.SceneEntities.Length)
                Array.Resize(ref binding.SceneEntities, entityAddr.Row + 256);
            return ref binding.SceneEntities[entityAddr.Row];
        }
        public void UpdateModel(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var emodel = binding.ModelLookup.GetValue(World.Stage, entityAddr);
            var model = emodel.Model;
            ref var sceneProxy = ref World//RenderWorld
                .GetComponentRef<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            foreach (var index in sceneProxy.SceneIndex) Scene.RemoveInstance(index);
            sceneProxy.SceneIndex = new CSInstance[model != null ? model.Meshes.Count : 0];
            for (int i = 0; i < sceneProxy.SceneIndex.Length; i++) {
                var instance = Scene.CreateInstance();
                ScenePasses.AddInstance(instance, model.Meshes[i]);
                sceneProxy.SceneIndex[i] = instance;
            }
            binding.ChangedPositions.TryRemove(entityAddr.Row);
            binding.ChangedSelected.TryRemove(entityAddr.Row);
            if (binding.TransformLookup.IsValid) UpdateTransform(entityAddr);
            if (binding.PositionLookup.IsValid) UpdatePosition(entityAddr);
            if (binding.SelectedLookup.IsValid) UpdateSelected(entityAddr);
        }
        public void UpdateTransform(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var epos = binding.TransformLookup.GetValue(World.Stage, entityAddr);
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            foreach (var index in sceneProxy.SceneIndex)
                Scene.SetTransform(index, epos.AsMatrix());
        }
        public void UpdatePosition(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var epos = binding.PositionLookup.GetValue(World.Stage, entityAddr);
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            foreach (var index in sceneProxy.SceneIndex)
                Scene.SetTransform(index,
                    Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(epos.Value));
        }
        unsafe public void UpdateSelected(EntityAddress entityAddr) {
            var binding = Bindings[entityAddr.ArchetypeId];
            var selected = binding.SelectedLookup.GetValue(World.Stage, entityAddr);
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(binding.SceneEntities[entityAddr.Row]);
            foreach (var index in sceneProxy.SceneIndex)
                Scene.UpdateInstanceData(index, sizeof(float) * (16 + 16 + 4),
                    selected.Selected ? 1.0f : 0.0f);
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
                foreach (var row in binding.ChangedPositions) {
                    UpdatePosition(new EntityAddress(new ArchetypeId(i), row));
                }
                foreach (var row in binding.ChangedSelected) {
                    UpdateSelected(new EntityAddress(new ArchetypeId(i), row));
                }
                binding.ChangedModels.Clear();
                binding.ChangedTransforms.Clear();
                binding.ChangedPositions.Clear();
                binding.ChangedSelected.Clear();
            }
        }

        public void NotifyHighlightChanged(GenericTarget target, Color color) {
            var sceneProxy = World//RenderWorld
                .GetComponent<SceneRenderable>(target.GetEntity());
            foreach (var index in sceneProxy.SceneIndex)
                Scene.UpdateInstanceData(index, sizeof(float) * (16 + 16),
                    (Vector4)color);
        }

    }
}
