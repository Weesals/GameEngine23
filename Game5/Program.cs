using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Game5.Game;
using Game5;
using Weesals.Engine.Profiling;
using Weesals.ECS;
using Weesals.Utility;

class Program {

    private static List<ApplicationWindow> windows = new();

    public static void Sparse2() {
        var rnd = new Random(0);
        var sparse2 = new SparseRanges();
        var alloc = new List<RangeInt>();
        for (int i = 0; i < 10000; i++) {
            if (i == 2581) {
                i = i;
            }
            if (rnd.Next(0, 10) < 6 && alloc.Count > 0) {
                var rangeI = rnd.Next(0, alloc.Count);
                var range = alloc[rangeI];
                alloc[rangeI] = alloc[^1];
                alloc.RemoveAt(alloc.Count - 1);
                sparse2.SetRange(range.Start, range.Length, false);
            } else {
                var size = rnd.Next(1, 100);
                var start = sparse2.FindAndSetRange(size);
                if (start == -1) continue;
                alloc.Add(new(start, size));
            }
        }
    }

    public static void Main() {
        {
            var asf = new SparseStorage();
            var rnd = new Random(0);
            var pages = new SparseStorage.PageRange();
            for (int i = 0; i < 100; i++) {
                var index = rnd.Next(0, 1024);
                ref var page1 = ref asf.RequirePage(ref pages, index);
                page1.Offset = i;
                Debug.Assert(asf.RequirePage(ref pages, index).Offset == i);
            }
        }
        {
            //Sparse2();

            /*var sparseColumn = new SparseColumnMap<int>();
            sparseColumn.Require(new(0, 0)) = 0;
            sparseColumn.Require(new(1, 0)) = 1;
            sparseColumn.Require(new(62, 0)) = 62;
            sparseColumn.Require(new(100, 0)) = 100;
            sparseColumn.Require(new(500, 0)) = 500;
            sparseColumn.Remove(new(1, 0));
            sparseColumn.Remove(new(0, 0));
            sparseColumn.Remove(new(62, 0));
            sparseColumn.Require(new(32, 0)) = 32;
            sparseColumn.Require(new(30, 0)) = 30;
            sparseColumn.Require(new(98, 0)) = 98;
            Debug.Assert(sparseColumn.Get(new(30, 0)) == 30);
            Debug.Assert(sparseColumn.Get(new(32, 0)) == 32);
            Debug.Assert(sparseColumn.Get(new(100, 0)) == 100);
            Debug.Assert(sparseColumn.Get(new(500, 0)) == 500);
            Debug.Assert(sparseColumn.Get(new(98, 0)) == 98);*/

            var sparse = new SparseRanges();
            sparse.SetRange(5, 10, true);
            int newStart00 = sparse.TryExtend(0, 5, 10);
            int newStart01 = sparse.TryExtend(15, 5, 7);
            int newStart02 = sparse.TryExtend(15, 3, 10);
            //int newStart1_fail1 = sparse.TryExtend(6, 5, 10);
            //int newStart1_fail2 = sparse.TryExtend(4, 5, 10);
            int newStart1_fail3 = sparse.TryExtend(5, 4, 10);
            //int newStart1_fail4 = sparse.TryExtend(5, 6, 10);
            int newStart1 = sparse.TryExtend(5, 5, 10);
            int newStart2 = sparse.TryExtend(10, 5, 10);
            int newStart3_fail1 = sparse.TryExtend(15, 6, 10);
            int newStart3_fail2 = sparse.TryExtend(15, 4, 10);
            int newStart3_fail3 = sparse.TryExtend(14, 5, 10);
            int newStart3 = sparse.TryExtend(15, 5, 10);

            sparse.SetRange(0, 100, false);

            sparse.SetRange(0, 5, false);
            sparse.SetRange(0, 5, true);
            sparse.SetRange(2, 3, false);
            sparse.SetRange(3, 10, true);
            sparse.SetRange(15, 5, true);
            Debug.Assert(sparse.GetValueAt(-1) == false);
            Debug.Assert(sparse.GetValueAt(0) == true);
            Debug.Assert(sparse.GetValueAt(1) == true);
            Debug.Assert(sparse.GetValueAt(2) == false);
            Debug.Assert(sparse.GetValueAt(3) == true);
            Debug.Assert(sparse.GetValueAt(14) == false);
            Debug.Assert(sparse.GetValueAt(15) == true);
            Debug.Assert(sparse.GetValueAt(30) == false);
            var rnd = new Random(0);
            for (int i = 0; i < 100000; i++) {
                sparse.SetRange(rnd.Next(0, 512), rnd.Next(1, 512), rnd.Next(0, 2) == 0);
                //int len = rnd.Next(1, 512);
                //sparse.TryExtend(rnd.Next(0, 512), len, len + rnd.Next(1, 512));
            }
            string s = "";
            foreach (var i in sparse) {
                s += i + ",";
            }
            while (sparse.FindAndSetRange(60, false) >= 0) ;
            while (sparse.FindAndSetRange(16, false) >= 0) ;
            while (sparse.FindAndSetRange(5, false) >= 0) ;
            while (sparse.FindAndSetRange(2, false) >= 0) ;
            while (sparse.FindAndSetRange(1, false) >= 0) ;
        }

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
