using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.UI {
    public class ProxyWindowCanvas : Canvas {

        public CSWindow Window;
        public CSGraphicsSurface Surface;

        private int lastRenderRevision;
        public new int Revision => base.Revision + (GetRenderHash?.Invoke() ?? 0);
        public bool RequireRender => lastRenderRevision != Revision;

        public Func<int>? GetRenderHash;

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
        }
        public override void Uninitialise(CanvasBinding binding) {
            var parentCanvas = binding.mParent?.Canvas;
            if (parentCanvas != null) {
                parentCanvas.OnPreUpdate -= PreUpdate;
                parentCanvas.OnUpdate -= Update;
                parentCanvas.OnRender -= Render;
            }
            base.Uninitialise(binding);
            if (Surface.IsValid) Surface.Dispose();
            if (Window.IsValid) Window.Dispose();
        }

        public void CreateNestedWindow(CSWindow parent) {
            Debug.Assert(!Surface.IsValid);
            Debug.Assert(!Window.IsValid);
            var layout = GetComputedLayout();
            var layoutRect = new RectI((int)layout.Position.X, (int)layout.Position.Y,
                    (int)layout.GetWidth(), (int)layout.GetHeight());
            Window = parent.CreateChildWindow(layoutRect);
            SetSize(layoutRect.Size);
            MarkComposeDirty();
        }

        protected override void NotifyLayoutChanged() {
            UpdateSizing();
            base.NotifyLayoutChanged();
            MarkComposeDirty();
        }

        private void UpdateSizing() {
            var layout = GetComputedLayout();
            var layoutRect = new RectI((int)layout.Position.X, (int)layout.Position.Y,
                    (int)layout.GetWidth(), (int)layout.GetHeight());
            if (!Window.IsValid) return;
            Window.SetWindowFrame(layoutRect, false);
            SetSize(layoutRect.Size);
        }

        public new void SetSize(Int2 size) {
            base.SetSize(size);
            if (!Window.IsValid) return;
            if (Surface.IsValid) {
                Surface.SetResolution(size);
            } else if (size.X > 0) {
                Surface = Core.ActiveInstance.GetGraphics().CreateSurface(Window);
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
