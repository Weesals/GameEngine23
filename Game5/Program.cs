using System.Diagnostics;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Impostors;
using Weesals;
using Game5.Game;
using Game5;

class Program {

    public static void Main() {
        var coreHandle = JobResult<Core>.Schedule(() => new Core());

        var coreJob = coreHandle.Then((core) => Core.ActiveInstance = core);
        var grapJob = coreHandle.Then((core) => core.InitializeGraphics());

        var loadJob = JobHandle.Schedule(Resources.LoadDefaultUIAssets);

        GameRoot root = new GameRoot();

        // Need UI assets for PlayUI
        var rootJob = root.Initialise(loadJob);

        // Require UI assets for Editor UI
        EditorWindow? editorWindow = null;
        loadJob = JobHandle.CombineDependencies(loadJob, coreJob).Then(() => {
            using (var marker = new ProfilerMarker("Creating Editor").Auto()) {
                editorWindow = new();
            }
        });

        grapJob = JobHandle.CombineDependencies(rootJob, grapJob)
            .Then(() => {
                using (var marker = new ProfilerMarker("Waiting for Root").Auto()) {
                    // Need a valid Play to bind the editor to
                    root.Play.SetAutoQuality(Core.ActiveInstance.GetGraphics());
                }
            });

        coreJob.Complete();

        CSWindow editorWin = Core.ActiveInstance.CreateWindow("Weesals Engine");

        loadJob.Complete();
        var editorPref = editorWindow.ReadPreferences();
        editorWindow.ApplyPreferences(editorWin, editorPref);

        rootJob.Complete();

        using (var marker = new ProfilerMarker("Attach Editor").Auto()) {
            if (editorWindow != null) {
                grapJob = grapJob.Then(() => {
                    editorWindow.RegisterRootWindow(editorWin);
                    editorWin.SetVisible(true);
                });
                root.AttachToEditor(editorWindow);
                grapJob.Complete();
            } else {
                throw new NotImplementedException("No editor is not supported (yet)");
            }
        }

        var timer = new FrameTimer(4);

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

            // Allow windows to throttle rendering
            int renderingWindows = 0;
            var graphics = Core.ActiveInstance.GetGraphics();
            foreach (var window in ApplicationWindow.ActiveWindows) {
                if (!window.IsRenderable) continue;
                var renDT = window.AdjustRenderDT(dt, 0,
                    FrameThrottler.IsOnBattery ? 1f : 0.05f);
                if (renDT == -1f) continue;
                window.Render(renDT, graphics);
                renderingWindows++;
            }
            if (renderingWindows == 0) {
                Thread.Sleep(6);
            }

            foreach (var window in ApplicationWindow.ActiveWindows) {
                window?.Input.ReceiveTickEvent();
            }

            root.ResetFrame();
            Tracy.FrameMarkEnd(0);
        }

        // Clean up
        JobScheduler.Instance.Dispose();
        foreach (var window in ApplicationWindow.ActiveWindows) {
            window?.Dispose();
        }
        root.Dispose();
        Core.ActiveInstance.Dispose();
    }

}
