using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.Utility;

namespace Weesals.UI {
    public struct Tween : ITimedEvent {
        public float Time0;
        public float Time1;
        public float Time => Time1;
        public float DeltaTime => Time1 - Time0;
        float ITimedEvent.TimeSinceEvent => Time;

        public static readonly Tween Complete = new Tween() { Time0 = 1000000f, Time1 = float.MaxValue, };
    }
    public interface ITweenable {
        // Return true while the tween is complete
        bool UpdateTween(Tween tween);
    }
    public class TweenManager {
        public struct Instance {
            public ITweenable Tweenable;
            public float Timer;
        }
        private List<Instance> instances = new();
        public void RegisterTweenable(ITweenable tweenable, float delay = 0f) {
            int i = 0;
            for (; i < instances.Count; i++) {
                if (instances[i].Tweenable == tweenable) break;
            }
            if (i >= instances.Count) instances.Add(new Instance() { Tweenable = tweenable, });
            var instance = instances[i];
            instance.Timer = -delay;
            instances[i] = instance;
        }
        public void Update(float dt) {
            for (int i = 0; i < instances.Count; i++) {
                var instance = instances[i];
                var tween = new Tween() { Time0 = Math.Max(0f, instance.Timer), };
                instance.Timer += dt;
                instances[i] = instance;
                if (tween.Time0 == tween.Time1) continue;
                tween.Time1 = Math.Max(0f, instance.Timer);
                if (!instance.Tweenable.UpdateTween(tween)) continue;
                instances.RemoveAt(i--);
            }
        }
    }
    public class Canvas : CanvasRenderable, IDisposable {
        private static ProfilerMarker ProfileMarker_Layout = new("Canvas Layout");
        private static ProfilerMarker ProfileMarker_Compose = new("Canvas Compose");
        private static ProfilerMarker ProfileMarker_ComposePartial = new("Canvas Compose Partial");
        private static ProfilerMarker ProfileMarker_Tweens = new("Canvas Tweens");
        private static ProfilerMarker ProfileMarker_Update = new("Canvas Update");
        public delegate void CanvasRepaintDelegate(ref CanvasCompositor.Context context);

        public CanvasMeshBuffer Builder { get; private set; }
        public CanvasCompositor Compositor { get; private set; }
        public HittestGrid HitTestGrid { get; private set; }
        public TweenManager Tweens { get; private set; }
        public Updatables Updatables { get; private set; }
        public StyleDictionary StyleDictionary { get; private set; }
        public Font DefaultFont { get; private set; }
        public Material Material;
        private Int2 mSize;
        public ISelectionGroup? SelectionGroup;
        public KeyboardFilter KeyboardFilter;
        public Action<float>? OnPreUpdate;
        public Action<float>? OnUpdate;
        public Action<CSGraphics>? OnRender;
        public CanvasRepaintDelegate OnRepaint;

        public int Revision => Builder.VertexRevision + Compositor.GetIndices().BufferLayout.revision;

        private List<CanvasRenderable> partialUpdates = new();

        public Canvas(bool initialize = true) {
            Builder = new();
            Compositor = new(Builder);
            HitTestGrid = new(new(8, 8));
            Tweens = new();
            Updatables = new();
            StyleDictionary = new();
            DefaultFont = Resources.LoadFont("./Assets/Roboto-Regular.ttf");
            Material = new Material("./Assets/ui.hlsl");
            Material.SetBlendMode(BlendMode.MakeAlphaBlend());
            Material.SetRasterMode(RasterMode.MakeNoCull());
            Material.SetDepthMode(DepthMode.MakeOff());
            var iModel = (CSIdentifier)"Model";
            var iView = (CSIdentifier)"View";
            var iProj = (CSIdentifier)"Projection";
            var iMVP = (CSIdentifier)"ModelViewProjection";
            Material.SetValue("CullRect", new Vector4(0f, 0f, 10000f, 10000f));
            Material.SetValue(iModel, Matrix4x4.Identity);
            Material.SetValue(iView, Matrix4x4.Identity);
            Material.SetValue(iProj, Matrix4x4.Identity);
            Material.SetComputedUniform<Matrix4x4>(iMVP, (ref ComputedContext context) => {
                return context.GetUniform<Matrix4x4>(iModel)
                    * context.GetUniform<Matrix4x4>(iView)
                    * context.GetUniform<Matrix4x4>(iProj);
            });
            SetHitTestEnabled(false);
            if (initialize) {
                Initialise(new CanvasBinding(this));
            }
        }
        public void Dispose() {
            Builder.Dispose();
            Compositor.Dispose();
        }
        public override void Initialise(CanvasBinding binding) {
            var parentCanvas = binding.mParent?.Canvas;
            if (parentCanvas != null) {
                StyleDictionary = parentCanvas.StyleDictionary;
                HitTestGrid = parentCanvas.HitTestGrid;
                binding.mCanvas = this;
            }
            base.Initialise(binding);
        }
        public override void Uninitialise(CanvasBinding binding) {
            var parentCanvas = binding.mParent?.Canvas;
            if (parentCanvas != null) {
                binding.mCanvas = this;
            }
            base.Uninitialise(binding);
            if (parentCanvas != null) {
                StyleDictionary = new();
                HitTestGrid = new(new(8, 8));
            }
        }
        public void SetSize(Int2 size) {
            if (mSize != size) {
                mSize = size;
                if (Parent == null) {
                    UpdateLayout(CanvasLayout.MakeBox(size));
                }
            }
        }
        protected override void NotifyLayoutChanged() {
            var layout = GetComputedLayout();
            var layoutMin = layout.Position;
            var layoutMax = layout.TransformPosition2DN(new(1f, 1f));
            //layoutMax -= layoutMin * 2f;
            //layoutMin -= layoutMin;
            if (Parent == null) {
                HitTestGrid.SetResolution(layout.GetSize());
            }
            Material.SetValue("Projection", Matrix4x4.CreateOrthographicOffCenter(layoutMin.X, layoutMax.X, layoutMax.Y, layoutMin.Y, 0.0f, 500.0f));
            base.NotifyLayoutChanged();
        }
        public new void MarkComposeDirty() { base.MarkComposeDirty(); }
        public bool GetIsComposeDirty() { return base.HasDirtyFlag(DirtyFlags.Compose); }
        public void RequireComposed() {
            if (HasDirtyFlag(DirtyFlags.Children)) {
                using var marker = ProfileMarker_Layout.Auto();
                RequireLayout();
            }
            if (HasDirtyFlag(DirtyFlags.Compose)) {
                using var marker = ProfileMarker_Compose.Auto();
                partialUpdates.Clear();
                ClearDirtyFlag(DirtyFlags.Compose);
                var builder = Compositor.CreateBuilder(this);
                var compositor = Compositor.CreateRoot(ref builder);
                Compose(ref compositor);
                if (OnRepaint != null) OnRepaint(ref compositor);
                Compositor.EndBuild(builder);
            }
            if (partialUpdates.Count > 0) {
                using var marker = ProfileMarker_ComposePartial.Auto();
                foreach (var partial in partialUpdates) {
                    partial.ComposePartial();
                }
                Builder.MarkVerticesChanged(default);
            }
        }
        public Int2 GetSize() {
	        return mSize;
        }

        public void PreUpdate(float dt) {
            using var marker = ProfileMarker_Tweens.Auto();
            OnPreUpdate?.Invoke(dt);
            Tweens.Update(dt);
        }
        public void Update(float dt) {
            using var marker = ProfileMarker_Update.Auto();
            OnUpdate?.Invoke(dt);
            Updatables.Invoke(dt);
        }

        public void Render(CSGraphics graphics) {
            OnRender?.Invoke(graphics);
            graphics.SetViewport(new(default, GetSize()));
            using (new GPUMarker(graphics, "Canvas Render")) {
                Compositor.Render(graphics, Material);
            }
        }

        public void AppendPartialDirty(CanvasRenderable renderable) {
            partialUpdates.Add(renderable);
        }
    }

}
