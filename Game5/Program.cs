using System.Diagnostics;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals;
using Game5.Game;
using Game5;

/*
 * Mouse scroll to zoom (and pinch?)
 * Selection reticle system
 * 
 * Steven Universe
 */

class Program {
    private static ProfilerMarker ProfileMarker_MainStartup = new("Startup");
    private static ProfilerMarker ProfileMarker_Readbacks = new("Readbacks");

    public static void Main() {
        var scope = ProfileMarker_MainStartup.Auto();
        var coreJob = JobHandle.Schedule(static () => Core.ActiveInstance = new Core());
        var grapJob = coreJob.Then(static () => Core.ActiveInstance.InitializeGraphics());
        var loadJob = JobHandle.Schedule(Resources.LoadDefaultAssets);
        scope.Dispose();

        GameRoot root = new GameRoot();

        // Need UI assets for PlayUI
        var rootJob = root.Initialise(loadJob, out var playInitJob);

        // Require UI assets for Editor UI
        var editorWindowHandle = JobResult<EditorWindow>.Schedule(static () => {
            using (var marker = new ProfilerMarker("Creating Editor").Auto()) {
                return new EditorWindow();
            }
        }, JobHandle.CombineDependencies(loadJob, coreJob));

        // Auto configure quality level
        grapJob = JobHandle.CombineDependencies(rootJob, grapJob)
            .Then(() => {
                root.Play.SetAutoQuality(Core.ActiveInstance.GetGraphics());
            });

        // Need Core to create window
        coreJob.Complete();

        if (editorWindowHandle.IsValid) {
            // Window must be made on MainThread
            CSWindow editorWin = Core.ActiveInstance.CreateWindow("Weesals Engine");

            // Initialize editor
            EditorWindow? editorWindow = editorWindowHandle.Complete();
            var editorPref = editorWindow.ReadPreferences();
            editorWindow.ApplyPreferences(editorWin, editorPref);

            // Wait for game root before binding editor and showing window
            rootJob.Complete();
            grapJob.Complete();
            editorWindow.RegisterRootWindow(editorWin);
            using (var marker = new ProfilerMarker("Attach Editor").Auto()) {
                grapJob = grapJob.Then(() => {
                    editorWin.SetVisible(true);
                });
                root.AttachToEditor(editorWindow);
                grapJob.Complete();
            }
        } else {
            rootJob.Complete();
            throw new NotImplementedException("No editor is not supported (yet)");
        }

        var timer = new FrameTimer(4);

        playInitJob.Complete();

        // Loop while the window is valid
        for (int f = 0; ; ++f) {
            if (Core.ActiveInstance.MessagePump() != 0) {
                for (int i = ApplicationWindow.ActiveWindows.Count - 1; i >= 0; i--) {
                    var window = ApplicationWindow.ActiveWindows[i];
                    if (window.Window.IsAlive()) continue;
                    window.Dispose();
                }

                if (ApplicationWindow.ActiveWindows.Count == 0) break;
            }

            // Ensure resolution is correct, and ignore if all windows minimized
            bool isActive = false;
            foreach (var window in ApplicationWindow.ActiveWindows) {
                isActive |= window.Validate();
            }
            if (!isActive) {
                Thread.Sleep(10);
                continue;
            }

            Tracy.FrameMarkStart(0);

            var dt = (float)timer.ConsumeDeltaTicks().TotalSeconds;

            // Send update to all active windows
            for (int i = 0; i < ApplicationWindow.ActiveWindows.Count; i++) {
                var window = ApplicationWindow.ActiveWindows[i];
                window.Update(dt);
            }

            // Run anything that needs to run on main thread
            JobScheduler.Instance.RunMainThreadTasks();

            var graphics = Core.ActiveInstance.GetGraphics();

            using (ProfileMarker_Readbacks.Auto()) {
                CSGraphics.AsyncReadback.Awaiter.InvokeCallbacks(graphics);
            }

            // Allow windows to throttle rendering
            int renderingWindows = 0;
            foreach (var window in ApplicationWindow.ActiveWindows) {
                if (!window.IsRenderable) continue;
                var renDT = window.AdjustRenderDT(dt, 0,
                    FrameThrottler.IsOnBattery ? 1f : 0.05f);
                if (renDT == -1f) continue;
                window.Render(renDT, graphics);
                renderingWindows++;
            }

            RenderTargetPool.Instance.PruneOldFromPool();

            if (renderingWindows == 0) {
                Thread.Sleep(6);
            }

            foreach (var window in ApplicationWindow.ActiveWindows) {
                window?.Input.ReceiveTickEvent();
            }

            root.ResetFrame();
            Tracy.FrameMarkEnd(0);
        }

        // Wait for all inflight operations
        Core.ActiveInstance.GetGraphics().Wait();

        // Clean up
        JobScheduler.Instance.Dispose();
        foreach (var window in ApplicationWindow.ActiveWindows) {
            window?.Dispose();
        }
        root.Dispose();
        Core.ActiveInstance.Dispose();
    }

}
