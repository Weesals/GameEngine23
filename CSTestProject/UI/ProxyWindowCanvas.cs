using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.UI {
    public interface IProxyWindowContainer {
        CSWindow Window { get; }
    }
    public class ProxyWindowCanvas : Canvas {

        public CSWindow Window;
        public CSGraphicsSurface Surface;

        protected int lastRenderRevision;
        public new int Revision => base.Revision + (GetRenderHash?.Invoke() ?? 0);
        public bool RequireRender => lastRenderRevision != Revision;

        public Func<int>? GetRenderHash;

        public struct InitHold : IDisposable {
            public ProxyWindowCanvas Proxy;
            public InitHold(ProxyWindowCanvas proxy) {
                Proxy = proxy;
                Proxy.initHolds++;
            }
            public void Dispose() {
                if (--Proxy.initHolds == 0 && Proxy.IsInCanvas != Proxy.Window.IsValid) {
                    if (Proxy.IsInCanvas) Proxy.CreateNestedWindow();
                    else Proxy.DestroyNestedWindow();
                }
            }
        }
        private int initHolds = 0;

        public ProxyWindowCanvas() : base(false) {
        }
        public ProxyWindowCanvas(CanvasRenderable child) : base(false) {
            AppendChild(child);
        }

        public override void Initialise(CanvasBinding binding) {
            var parentCanvas = binding.mParent?.Canvas;
            if (parentCanvas != null) {
                parentCanvas.OnPreUpdate += PreUpdate;
                parentCanvas.OnUpdate += Update;
                parentCanvas.OnRender += Render;
            }
            base.Initialise(binding);
            MarkComposeDirty();
            if (initHolds == 0) CreateNestedWindow();
        }
        public override void Uninitialise(CanvasBinding binding) {
            var parentCanvas = binding.mParent?.Canvas;
            if (parentCanvas != null) {
                parentCanvas.OnPreUpdate -= PreUpdate;
                parentCanvas.OnUpdate -= Update;
                parentCanvas.OnRender -= Render;
            }
            base.Uninitialise(binding);
            if (initHolds == 0) DestroyNestedWindow();
        }

        private RectI GetWindowRect() {
            var layout = GetComputedLayout();
            var minPnt = layout.Position.toxy();
            var maxPnt = layout.Position.toxy() + layout.GetSize();
            return RectI.FromMinMax(minPnt, maxPnt);
        }

        public void CreateNestedWindow() {
            var proxyContainer = FindParent<IProxyWindowContainer>();
            if (proxyContainer.Window.IsValid)
                CreateNestedWindow(proxyContainer.Window);
        }
        public void CreateNestedWindow(CSWindow parent) {
            Debug.Assert(!Surface.IsValid);
            Debug.Assert(!Window.IsValid);
            var layoutRect = GetWindowRect();
            Window = parent.CreateChildWindow(layoutRect);
            UpdateSizing();
            //MarkComposeDirty();
        }
        public void DestroyNestedWindow() {
            if (Surface.IsValid) { Surface.Dispose(); Surface = default; }
            if (Window.IsValid) { Window.Dispose(); Window = default; }
        }

        protected override void NotifyLayoutChanged() {
            UpdateSizing();
            base.NotifyLayoutChanged();
            MarkComposeDirty();
        }

        public void UpdateSizing(bool animate = false) {
            var layoutRect = GetWindowRect();
            if (!Window.IsValid) return;
            if (animate || Canvas.Tweens.GetIsTweening(frameTween)) {
                frameTween ??= new(this);
                Canvas.Tweens.RegisterTweenable(frameTween, 0f);
            } else {
                Window.SetWindowFrame(layoutRect, false);
            }
            SetSize(layoutRect.Size);
        }
        public class WindowFrameTween : ITweenable {
            public ProxyWindowCanvas Proxy;
            private static Random gRand = new();
            public WindowFrameTween(ProxyWindowCanvas proxy) {
                Proxy = proxy;
            }
            public bool UpdateTween(Tween tween) {
                var layoutRect = Proxy.GetWindowRect();
                var currentFrame = Proxy.Window.GetWindowFrame();
                var ease = Easing.StatefulPowerInOut(0.2f, 2f);
                var lerps = ease.Evaluate(tween);
                var min = Vector2.Lerp(currentFrame.Position.TopLeft, layoutRect.Min, lerps);
                var max = Vector2.Lerp(currentFrame.Position.BottomRight, layoutRect.Max, lerps);
                min += Vector2.One * gRand.NextSingle();
                max += Vector2.One * gRand.NextSingle();
                var blendedRect = RectI.FromMinMax(Int2.FloorToInt(min), Int2.FloorToInt(max));
                Proxy.Window.SetWindowFrame(blendedRect, false);
                // Doesn't work because layout is updated immediately
                //if (tween.FramePasses(0.15f)) Proxy.SetSize(layoutRect.Size);
                return ease.GetIsComplete(tween);
            }
        }
        private WindowFrameTween frameTween;

        public new void SetSize(Int2 size) {
            base.SetSize(size);
            if (!Window.IsValid) return;
            if (Surface.IsValid && Surface.GetResolution() != size) {
                Surface.SetResolution(size);
            }
        }

        public void RequireSurface(CSGraphics graphics) {
            var layoutRect = GetWindowRect();
            if (!Surface.IsValid && layoutRect.Width > 0) {
                Surface = Core.ActiveInstance.GetGraphics().CreateSurface(Window);
                Surface.SetResolution(layoutRect.Size);
            }
        }

        public override void Compose(ref CanvasCompositor.Context composer) {
            if (composer.GetCompositor() == Compositor) {
                base.Compose(ref composer);
            } else {
                RequireComposed();
            }
        }

        public void NotifyRendered() {
            lastRenderRevision = Revision;
        }

    }
}
