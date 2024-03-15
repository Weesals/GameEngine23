using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.UI;

namespace Weesals.Editor {
    public class ProfilerWindow : ApplicationWindow {

        public Canvas Canvas = new();
        public EventSystem EventSystem;
        private ProfilerContent profileContent;

        public int RenderRevision = 0;

        public class ProfilerContent : CanvasRenderable, IBeginDragHandler, IDragHandler {
            private Vector2 offset;
            public readonly ProfilerWindow Window;
            private List<string> threads = new();
            public ProfilerContent(ProfilerWindow window) {
                Window = window;
            }
            public override void Compose(ref CanvasCompositor.Context composer) {
                const float ThreadHeight = 20f;
                const float ThreadSeparation = 2f;
                const float RowHeight = ThreadHeight + ThreadSeparation;
                base.Compose(ref composer);
                var windowSize = Window.Size;
                var snapshots = ProfilerMarker.AllSnapshots;
                var bgSprite = Resources.TryLoadSprite("PanelBG");
                foreach (var snapshot in snapshots) {
                    if (threads.Contains(snapshot.ThreadName)) continue;
                    threads.Add(snapshot.ThreadName);
                }
                for (int i = 0; i <= threads.Count; i++) {
                    var img = composer.CreateTransient<CanvasImage>(Canvas);
                    var threadOffset = new Vector2(0f, offset.Y);
                    var layout = CanvasLayout.MakeBox(
                        new Vector2(windowSize.X, ThreadSeparation),
                        new Vector2(0f, i * RowHeight) - threadOffset
                    );
                    layout.Position += mLayoutCache.Position;
                    if (img.HasDirtyFlags)
                        img.UpdateLayout(Canvas, layout);
                    img.Append(ref composer);
                }
                foreach (var snapshot in snapshots) {
                    var threadId = threads.IndexOf(snapshot.ThreadName);
                    var sx = 5f * snapshot.BeginTick / TimeSpan.TicksPerMillisecond;
                    var ex = 5f * snapshot.EndTick / TimeSpan.TicksPerMillisecond;
                    var img = composer.CreateTransient<CanvasImage>(Canvas);
                    var txt = composer.CreateTransient<CanvasText>(Canvas);
                    var layout = CanvasLayout.MakeBox(
                        new Vector2(ex - sx, ThreadHeight),
                        new Vector2(sx, threadId * RowHeight + ThreadSeparation) - offset
                    );
                    layout.Position += mLayoutCache.Position;
                    if (img.HasDirtyFlags) {
                        img.SetSprite(bgSprite);
                        img.UpdateLayout(Canvas, layout);
                    }
                    img.Append(ref composer);
                    if (txt.Text != snapshot.Name) {
                        txt.Text = snapshot.Name;
                        txt.FontSize = 8;
                    }
                    txt.Color = Color.Black;
                    txt.UpdateLayout(Canvas, layout);
                    txt.Append(ref composer);
                }
            }
            public void OnBeginDrag(PointerEvent events) {
            }
            public void OnDrag(PointerEvent events) {
                var delta = events.CurrentPosition - events.PreviousPosition;
                var newOffset = offset - delta;
                newOffset = Vector2.Clamp(newOffset, new Vector2(0f, 0f), new Vector2(100000.0f, 1000.0f));
                if (offset != newOffset) {
                    offset = newOffset;
                    Window.RenderRevision++;
                }
            }

            public void ForceRedraw() {
                MarkComposeDirty();
            }
        }

        public override void RegisterRootWindow(CSWindow window) {
            base.RegisterRootWindow(window);
            Canvas.AppendChild(profileContent = new(this) {
                Transform = CanvasTransform.MakeDefault().WithOffsets(0f, 100f, 0f, 0f),
            });
            EventSystem = new(Canvas);
            EventSystem.SetInput(Input);
        }

        public void Update(float dt) {
            EventSystem.Update(dt);
        }

        unsafe public void Render(float dt, CSGraphics graphics) {
            profileContent.ForceRedraw();
            graphics.Reset();
            graphics.SetSurface(Surface);
            var rt = Surface.GetBackBuffer();
            graphics.SetRenderTargets(new Span<CSRenderTarget>(ref rt), default);
            graphics.Clear();
            Canvas.SetSize(Size);
            Canvas.Update(dt);
            Canvas.RequireComposed();
            Canvas.Render(graphics);
            graphics.Execute();
            Surface.Present();
        }

    }
}
