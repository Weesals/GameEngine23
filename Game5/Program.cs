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
using Weesals.Engine.Serialization;
using Weesals.Impostors;
using Weesals.UI;

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

    private static void Serialize(TSONNode test) {
        using (var d = test.CreateChild("Derp1")) {
            int v = 1;
            d.Serialize(ref v);
            int v2 = 2;
            d.Serialize(ref v2);
        }
        using (var d = test.CreateChild("Derp2")) {
            int v = 10;
            d.Serialize(ref v);
            using (var n = d.CreateBinary()) {
                int vn = 10;
                n.Serialize(ref vn);
                n.Serialize(ref vn);
            }
            d.Serialize(ref v);
        }
        using (var d = test.CreateChild("Derp3")) {
            int v = 6;
            d.Serialize(ref v);
        }
    }

    public static void Main() {
        /*{
            var b64 = new Base64Map<int>();
            b64.Insert(new Vector2(0f, 0f), 1);
            b64.Insert(new Vector2(-5f, -5f), 2);
            b64.Insert(new Vector2(0f, 5f), 3);
            b64.Insert(new Vector2(2f, 2f), 4);
            b64.Insert(new Vector2(3f, 1f), 5);
            b64.Remove(new Vector2(0f, 0f), 1);
        }*/
        {
            var buffer = new DataBuffer();
            using (var docroot = TSONNode.CreateWrite(buffer)) {
                using (var test = docroot.CreateChild("Test")) {
                    Serialize(test);
                }
            }
            using (var docroot = TSONNode.CreateRead(buffer)) {
                using (var test = docroot.CreateChild("Test")) {
                    Serialize(test);
                }
            }
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

        ImpostorGenerator impostorGenerator = null;

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
            if (Input.GetKeyPressed(KeyCode.F6)) {
                if (impostorGenerator == null) {
                    graphics.Reset();

                    var hull = Resources.LoadModel("./Assets/B_Granary1_Hull.fbx", new() { }).Meshes[0];
                    var hull2 = Resources.LoadModel("./Assets/B_House_Hull.fbx", new() { }).Meshes[0];
                    var play = root.Play;

                    void AppendImpostor(Mesh mesh, Mesh hull) {
                        impostorGenerator = new ImpostorGenerator();
                        impostorGenerator.CreateImpostor(graphics, mesh).ContinueWith(async (task) => {
                            var material = await task;
                            var meshInstance = play.Scene.CreateInstance(mesh.BoundingBox);
                            play.Scene.SetTransform(meshInstance, Matrix4x4.CreateRotationY(3.14f / 4.0f));
                            play.ScenePasses.SetMeshLOD(mesh, hull, material);
                        });
                    }
                    AppendImpostor(Resources.LoadModel("./Assets/B_Granary1.fbx").Meshes[0], hull);
                    AppendImpostor(Resources.LoadModel("./Assets/B_Granary2.fbx").Meshes[0], hull2);
                    AppendImpostor(Resources.LoadModel("./Assets/B_Granary3.fbx").Meshes[0], hull2);
                    AppendImpostor(Resources.LoadModel("./Assets/SM_House.fbx").Meshes[0], hull2);
                    AppendImpostor(Resources.LoadModel("./Assets/B_House2.fbx").Meshes[0], hull2);
                    AppendImpostor(Resources.LoadModel("./Assets/B_House3.fbx").Meshes[0], hull2);

                    //play.ScenePasses.AddInstance(meshInstance, hull, material, RenderTags.Default);
                    /*var img = new Image();
                    img.SetTransform(CanvasTransform.MakeDefault().WithAnchors(0.5f, 0f, 1f, 1.0f));
                    img.AspectMode = Image.AspectModes.PreserveAspectContain;
                    img.Texture = impostorGenerator.AlbedoTarget;
                    img.HitTestEnabled = false;
                    root.Canvas.AppendChild(img);*/

                    graphics.Execute();
                }
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

                    CSGraphics.AsyncReadback.Awaiter.InvokeCallbacks(graphics);
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
