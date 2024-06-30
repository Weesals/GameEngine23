﻿using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Game5.Game;
using Game5;
using Weesals.Engine.Profiling;

class Program {

    private static List<ApplicationWindow> windows = new();

    public static void Main() {
        var loadJob = JobHandle.Schedule(() => {
            using var marker = new ProfilerMarker("Load UI Assets").Auto();
            Resources.LoadDefaultUIAssets();
        });

        GameRoot root;
        using (var marker = new ProfilerMarker("Creating Root").Auto()) {
            root = new GameRoot();
        }

        JobHandle rootJob;
        using (var marker = new ProfilerMarker("Initialising Root").Auto()) {
            loadJob.Complete();     // Need UI assets for PlayUI
            rootJob = root.Initialise();
        }

        Core core;
        using (var marker = new ProfilerMarker("Creating Core").Auto()) {
            core = new Core();
            Core.ActiveInstance = core;
        }

        EditorWindow? editorWindow = new();
        ParticleDebugWindow? previewWindow = null;
        ProfilerWindow? profilerWindow = null;
        ApplicationWindow primaryWindow;
        using (var marker = new ProfilerMarker("Create Windows").Auto()) {
            var rootWindow = core.CreateWindow("Weesals Engine");
            try {
                var json = new SJson(File.ReadAllText("./Config/window.txt"));
                var frame = new RectI(json["x"], json["y"], json["w"], json["h"]);
                rootWindow.SetWindowFrame(frame, json["max"]);
            } catch { }

            editorWindow?.RegisterRootWindow(rootWindow);

            if (editorWindow != null) windows.Add(editorWindow);

            primaryWindow = editorWindow ?? throw new NotImplementedException();
            Input.Initialise(primaryWindow.Input);
        }

        using (var marker = new ProfilerMarker("Waiting for Root").Auto()) {
            // Need a valid Play to bind the editor to
            rootJob.Complete();
            root.Play.SetAutoQuality(core.GetGraphics());
        }

        using (var marker = new ProfilerMarker("Attach Editor").Auto()) {
            if (editorWindow != null) {
                root.AttachToEditor(editorWindow);
            } else {
                //root.EventSystem.SetInput(gameWindow.Input);
                throw new NotImplementedException("No editor is not supported (yet)");
            }
        }

        var timer = new FrameTimer(4);
        var throttler = new FrameThrottler();

        bool windowMoved = false;
        primaryWindow.Window.RegisterMovedCallback(() => { windowMoved = true; }, true);

        // Loop while the window is valid
        for (int f = 0; ; ++f) {
            if (core.MessagePump() != 0) {
                if (primaryWindow != null && !primaryWindow.Window.IsAlive()) break;
                if (profilerWindow != null && !profilerWindow.Window.IsAlive()) {
                    windows.Remove(profilerWindow);
                    profilerWindow = default;
                }
            }

            if (windowMoved) {
                var frame = primaryWindow.Window.GetWindowFrame();
                Debug.WriteLine("Window moved");
                Directory.CreateDirectory("./Config/");
                File.WriteAllText("./Config/window.txt", $@"{{ x:{frame.Position.X}, y:{frame.Position.Y}, w:{frame.Position.Width}, h:{frame.Position.Height}, max:{frame.Maximized != 0} }}");
                windowMoved = false;
            }

            bool isActive = false;

            foreach (var window in windows) isActive |= window.Validate();

            if (!isActive) {
                Thread.Sleep(10);
                continue;
            }

            Tracy.FrameMarkStart(0);

            var graphics = core.GetGraphics();

            var dt = (float)timer.ConsumeDeltaTicks().TotalSeconds;
            throttler.Update(dt);

            if (Input.GetKeyPressed(KeyCode.F4) && profilerWindow == null) {
                profilerWindow = new();
                profilerWindow?.RegisterRootWindow(core.CreateWindow("Profiler"));
                windows.Add(profilerWindow);
            }
            if (Input.GetKeyPressed(KeyCode.F3) && profilerWindow == null) {
                previewWindow = new();
                previewWindow?.RegisterRootWindow(core.CreateWindow("Particles"));
                windows.Add(previewWindow);
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
                    editorWindow?.Render(renDT, graphics);

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
            Tracy.FrameMarkEnd(0);
        }

        // Clean up
        JobScheduler.Instance.Dispose();
        foreach (var window in windows) window?.Dispose();
        root.Dispose();
        core.Dispose();
    }

}
