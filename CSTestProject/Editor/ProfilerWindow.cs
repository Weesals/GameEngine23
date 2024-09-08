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
            private List<int> threadDepths = new();
            public ProfilerContent(ProfilerWindow window) {
                Window = window;
            }
            private float TickToPX(long tick) {
                return 2f * tick / TimeSpan.TicksPerMillisecond - offset.X;
            }
            private long PXToTick(float px) {
                return (long)((px + offset.X) * TimeSpan.TicksPerMillisecond / 2f);
            }
            private int GetSnapshotAt(long tick) {
                var snapshots = ProfilerMarker.AllSnapshots;
                int min = 0, max = snapshots.Count - 1;
                while (min < max) {
                    int mid = (min + max) / 2;
                    if (tick <= snapshots[mid].BeginTick) max = mid;
                    else min = mid + 1;
                }
                return min;
            }
            private int SkipToDepth0Tick(long tick, int index) {
                var snapshots = ProfilerMarker.AllSnapshots;
                for (; index < snapshots.Count; ++index)
                    if (snapshots[index].ThreadDepth == 0 &&
                        snapshots[index].BeginTick <= tick) break;
                return index;
            }
            public override void Compose(ref CanvasCompositor.Context composer) {
                const float ThreadHeight = 20f;
                const float ThreadSeparation = 2f;
                const float RowHeight = ThreadHeight + ThreadSeparation;
                base.Compose(ref composer);
                var windowSize = Window.Size;
                var snapshots = ProfilerMarker.AllSnapshots;
                var bgSprite = Resources.TryLoadSprite("PanelBG");
                var minSnapshot = GetSnapshotAt(PXToTick(0f));
                var maxSnapshot = GetSnapshotAt(PXToTick(windowSize.X));
                maxSnapshot = SkipToDepth0Tick(PXToTick(windowSize.X), maxSnapshot);
                if (maxSnapshot < snapshots.Count) ++maxSnapshot;
                for (int s = minSnapshot; s < maxSnapshot; s++) {
                    var snapshot = snapshots[s];
                    if (threads.Contains(snapshot.ThreadName)) continue;
                    threads.Add(snapshot.ThreadName);
                    threadDepths.Add(0);
                }
                for (int i = 0; i < threads.Count; i++) threadDepths[i] = 0;
                for (int s = minSnapshot; s < maxSnapshot; s++) {
                    var snapshot = snapshots[s];
                    var threadId = threads.IndexOf(snapshot.ThreadName);
                    threadDepths[threadId] = Math.Max(threadDepths[threadId], snapshot.ThreadDepth);
                }
                var threadOffset = new Vector2(0f, offset.Y);
                for (int i = 0; i <= threads.Count; i++) {
                    var img = composer.CreateTransient<CanvasImage>(Canvas);
                    var layout = CanvasLayout.MakeBox(
                        new Vector2(windowSize.X, ThreadSeparation),
                        new Vector2(0f, i * RowHeight) - threadOffset
                    );
                    layout.Position += mLayoutCache.Position;
                    if (img.HasDirtyFlags)
                        img.UpdateLayout(Canvas, layout);
                    img.Append(ref composer);
                }
                for (int s = minSnapshot; s < maxSnapshot; s++) {
                    var snapshot = snapshots[s];
                    var threadId = threads.IndexOf(snapshot.ThreadName);
                    var sx = TickToPX(snapshot.BeginTick);
                    var ex = TickToPX(snapshot.EndTick) + 1;
                    var img = composer.CreateTransient<CanvasImage>(Canvas);
                    var txt = composer.CreateTransient<CanvasText>(Canvas);
                    var layout = CanvasLayout.MakeBox(
                        new Vector2(ex - sx, ThreadHeight),
                        new Vector2(sx, threadId * RowHeight + ThreadSeparation) - threadOffset
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

        public override void Update(float dt) {
            EventSystem.Update(dt);
        }

        public override void Render(float dt, CSGraphics graphics) {
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
