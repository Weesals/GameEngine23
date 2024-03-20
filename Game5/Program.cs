using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Game5.Game;
using Game5;

class Program {

    private static List<ApplicationWindow> windows = new();

    public static void Main() {
        var core = new Core();
        Core.ActiveInstance = core;
        Resources.LoadDefaultUIAssets();

        EditorWindow? editorWindow = new();
        ParticleDebugWindow? previewWindow = null;// new();
        ProfilerWindow? profilerWindow = null;
        editorWindow?.RegisterRootWindow(core.CreateWindow("Weesals Engine"));
        previewWindow?.RegisterRootWindow(core.CreateWindow("Preview"));

        if (editorWindow != null) windows.Add(editorWindow);
        if (previewWindow != null) windows.Add(previewWindow);

        var root = new GameRoot();

        if (editorWindow != null) {
            root.AttachToEditor(editorWindow);
        } else {
            //root.EventSystem.SetInput(gameWindow.Input);
            throw new NotImplementedException("No editor is not supported (yet)");
        }

        ApplicationWindow primaryWindow = editorWindow ?? throw new NotImplementedException();
        Input.Initialise(primaryWindow.Input);

        var timer = new FrameTimer(4);
        var throttler = new FrameThrottler();

        // Loop while the window is valid
        for (int f = 0; ; ++f) {
            if (core.MessagePump() != 0) {
                if (primaryWindow != null && !primaryWindow.Window.IsAlive()) break;
                if (profilerWindow != null && !profilerWindow.Window.IsAlive()) {
                    windows.Remove(profilerWindow);
                    profilerWindow = default;
                }
            }

            bool isActive = false;

            foreach (var window in windows) isActive |= window.Validate();

            if (!isActive) {
                Thread.Sleep(10);
                continue;
            }

            var graphics = core.GetGraphics();

            var dt = (float)timer.ConsumeDeltaTicks().TotalSeconds;
            throttler.Update(dt);

            if (Input.GetKeyPressed(KeyCode.F4) && profilerWindow == null) {
                profilerWindow = new();
                profilerWindow?.RegisterRootWindow(core.CreateWindow("Profiler"));
                windows.Add(profilerWindow);
            }

            float renDT;
            {
                // Let the editor update its UI layout
                editorWindow?.Update(dt, primaryWindow.Size);
                profilerWindow?.Update(dt);

                // Get the game viewport
                var gameViewport = editorWindow != null
                    ? editorWindow.GameView.GetGameViewportRect()
                    : new RectI(0, primaryWindow.Size);

                // Setup for game viewport rendering
                root.SetViewport(gameViewport);
                root.Update(dt);

                // Run anything that needs to run on main thread
                JobScheduler.Instance.RunMainThreadTasks();

                // If the frame hasnt changed, dont render anything
                var newRenderHash = root.RenderHash
                    + (editorWindow != null ? editorWindow.Canvas.Revision : 0)
                    + (profilerWindow != null ? profilerWindow .RenderRevision : 0);
                renDT = throttler.Step(newRenderHash, editorWindow != null && editorWindow.GameView.EnableRealtime);
            }
            if (throttler.IsThrottled) {
                Thread.Sleep(6);
                //core.Present();
            } else {
                if (primaryWindow?.IsRenderable ?? false) {
                    graphics.Reset();
                    graphics.SetSurface(primaryWindow.Surface);

                    // Render the game world and UI
                    root.Render(graphics, renDT);

                    // Render the editor chrome
                    graphics.SetViewport(new RectI(0, primaryWindow.Size));
                    editorWindow?.Render(graphics);

                    // Flush render command buffer
                    graphics.Execute();
                    primaryWindow.Surface.Present();
                }
                if (previewWindow?.IsRenderable ?? false) {
                    previewWindow?.UpdateFrom(root.Play.ParticleManager);
                    previewWindow?.Render(dt, graphics);
                }
                if (profilerWindow?.IsRenderable ?? false) {
                    profilerWindow.Render(dt, graphics);
                }
            }
            root.ResetFrame();

            foreach (var window in windows) window?.Input.ReceiveTickEvent();
        }

        // Clean up
        JobScheduler.Instance.Dispose();
        foreach (var window in windows) window?.Dispose();
        root.Dispose();
        core.Dispose();
    }

}
