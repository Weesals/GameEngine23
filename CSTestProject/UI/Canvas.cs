using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
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
        public void RegisterTweenable(ITweenable tweenable) {
            int i = 0;
            for (; i < instances.Count; i++) {
                if (instances[i].Tweenable == tweenable) break;
            }
            if (i >= instances.Count) instances.Add(new Instance() { Tweenable = tweenable, });
            var instance = instances[i];
            instance.Timer = 0f;
            instances[i] = instance;
        }
        public void Update(float dt) {
            for (int i = 0; i < instances.Count; i++) {
                var instance = instances[i];
                var tween = new Tween() { Time0 = instance.Timer, };
                instance.Timer += dt;
                tween.Time1 = instance.Timer;
                instances[i] = instance;
                if (!instance.Tweenable.UpdateTween(tween)) continue;
                instances.RemoveAt(i--);
            }
        }
    }
    public class Canvas : CanvasRenderable, IDisposable {
        public delegate void CanvasRepaintDelegate(ref CanvasCompositor.Context context);

        public CanvasMeshBuffer Builder { get; private set; }
        public CanvasCompositor Compositor { get; private set; }
        public HittestGrid HitTestGrid { get; private set; }
        public TweenManager Tweens { get; private set; }
        public Font DefaultFont { get; private set; }
        public Material Material;
        private Int2 mSize;
        public ISelectionGroup? SelectionGroup;
        public Updatables Updatables = new();
        public KeyboardFilter KeyboardFilter;
        public CanvasRepaintDelegate OnRepaint;

        public int Revision => Builder.VertexRevision + Compositor.GetIndices().BufferLayout.revision;

        public Canvas() {
            Builder = new();
            Compositor = new(Builder);
            HitTestGrid = new(new Int2(8, 8));
            Tweens = new();
            DefaultFont = Resources.LoadFont("./Assets/Roboto-Regular.ttf");
            Initialise(new CanvasBinding(this));
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
        }
        public void Dispose() {
            Builder.Dispose();
            Compositor.Dispose();
        }
        public void SetSize(Int2 size) {
            HitTestGrid.SetResolution(size);
            if (mSize != size) {
                mSize = size;
                Material.SetValue("Projection", Matrix4x4.CreateOrthographicOffCenter(0.0f, (float)mSize.X, (float)mSize.Y, 0.0f, 0.0f, 500.0f));
                UpdateLayout(CanvasLayout.MakeBox(size));
            }
        }
        public new void MarkComposeDirty() { base.MarkComposeDirty(); }
        public bool GetIsComposeDirty() { return base.HasDirtyFlag(DirtyFlags.Compose); }
        public void RequireComposed() {
            if (HasDirtyFlag(DirtyFlags.Children)) {
                RequireLayout();
            }
            if (HasDirtyFlag(DirtyFlags.Compose)) {
                ClearDirtyFlag(DirtyFlags.Compose);
                var builder = Compositor.CreateBuilder(this);
                var compositor = Compositor.CreateRoot(ref builder);
                Compose(ref compositor);
                if (OnRepaint != null) OnRepaint(ref compositor);
                Compositor.EndBuild(builder);
            }
        }
        public Int2 GetSize() {
	        return mSize;
        }

        public void Update(float dt) {
            Tweens.Update(dt);
            Updatables.Invoke(dt);
        }

        public void Render(CSGraphics graphics) {
            Compositor.Render(graphics, Material);
        }

    }

}
