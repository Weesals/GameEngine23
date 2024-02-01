using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using UnityEngine;
using Weesals.ECS;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Weesals.Game;
using Weesals.Landscape;
using Weesals.UI;
using Weesals.Utility;

class Program {

    public static void TestDynamicBitField() {
        var rnd1 = new Random(0);
        var rnd2 = new Random(0);
        int count1 = 0, count2 = 0, count3 = 0;

        const int Iterations = 30000000;
        const int MinValue = 10000;
        const int MaxValue = 20000;

        var bitField1 = new DynamicBitField();
        var timer1 = new Stopwatch();
        timer1.Start();
        //for (int z = 0; z < 500; z++) {
            for (int i = 0; i < 20; i++) {
                bitField1.Add(rnd1.Next(0, MaxValue));
            }
            for (int i = 0; i < Iterations; i++) {
                if (bitField1.Contains(rnd1.Next(MinValue, MaxValue))) ++count1;
            }
            var set1 = new HashSet<int>();
            foreach (var item in bitField1) set1.Add(item);
        //}
        timer1.Stop();
        Trace.WriteLine($"Run1 {timer1.ElapsedMilliseconds} ms {count1}");

        var bitField2 = new DynamicBitField2();
        var timer2 = new Stopwatch();
        timer2.Start();
        //for (int z = 0; z < 500; z++) {
            for (int i = 0; i < 20; i++) {
                bitField2.Add(rnd2.Next(0, MaxValue));
            }
            for (int i = 0; i < Iterations; i++) {
                if (bitField2.Contains(rnd2.Next(MinValue, MaxValue))) ++count2;
            }
            var set2 = new HashSet<int>();
            foreach (var item in bitField2) set2.Add(item);
        //}
        timer2.Stop();
        Trace.WriteLine($"Run2 {timer2.ElapsedMilliseconds} ms {count2}");

        var rnd3 = new Random(0);
        var set = new HashSet<int>();
        for (int i = 0; i < Iterations; i++) {
            set.Add(rnd3.Next(0, MaxValue));
            if (set.Contains(rnd3.Next(0, MaxValue))) ++count3;
        }
    }
    public static void Main() {
        MainIntl();
    }
    unsafe static void MainIntl() {
        var core = new Core();
        Core.ActiveInstance = core;
        Resources.LoadDefaultUIAssets();

        var editorWindow = new EditorWindow();

        var scene = new Scene();

        var play = new Play(scene);

        var eventSystem = new EventSystem(play.Canvas);
        editorWindow.GameView.EventSystem = eventSystem;
        editorWindow.GameView.Camera = play.Camera;
        editorWindow.GameView.Scene = play.ScenePasses;

        play.SelectionManager.OnSelectionChanged += (selection) => {
            foreach (var entity in selection) {
                if (entity.Owner is LandscapeRenderer landscape) {
                    editorWindow.ActivateLandscapeTools(landscape);
                    return;
                }
                if (entity.IsValid) {
                    editorWindow.ActivateEntityInspector(entity);
                    return;
                }
            }
            editorWindow.Inspector.SetInspector(default);
        };

        Stopwatch timer = new();
        timer.Start();

        float timeSinceRender = 0f;
        int renderHash = 0;

        // Loop while the window is valid
        while (core.MessagePump() == 0) {
            var graphics = core.GetGraphics();
            var windowRes = core.GetWindow().GetResolution();
            if (graphics.IsTombstoned() || windowRes.Y == 0) {
                Thread.Sleep(10);
                continue;
            }
            if (windowRes != graphics.GetResolution()) {
                graphics.SetResolution(windowRes);
                continue;
            }

            if (Input.GetKeyPressed(KeyCode.Escape)) {
                play.SelectionManager.ClearSelected();
            }

            var dt = (float)timer.Elapsed.TotalSeconds;
            timer.Restart();
            Time.Update(dt);
            timeSinceRender += dt;

            // Require editor UI layout to be valid
            editorWindow.Update(dt, windowRes);
            var gameViewport = editorWindow.GameView.GetGameViewportRect();
            // Setup for game viewport rendering
            play.SetViewport(gameViewport);

            //eventSystem.SetPointerOffset(-(Vector2)gameViewport.Min);
            eventSystem.Update(dt);
            play.Update(dt);
            play.PreRender(graphics);

            // If the frame hasnt changed, dont render anything
            var newRenderHash = play.Canvas.Revision + play.RenderRevision + editorWindow.Canvas.Revision + play.ScenePasses.GetRenderHash() + Handles.RenderHash;
            bool requireRender = renderHash != newRenderHash;
            if (!requireRender && timeSinceRender < 10.25f) {
                Thread.Sleep(6);
            } else {
                graphics.Reset();

                // Render the game world and UI
                play.Render(graphics);

                // Render the editor chrome
                graphics.SetViewport(new RectI(0, 0, windowRes.X, windowRes.Y));
                editorWindow.Render(graphics);

                // Flush render command buffer
                graphics.Execute();
                core.Present();
                timeSinceRender = 0f;
                renderHash = newRenderHash;
            }

            play.PostRender();
        }

        // Clean up
        JobScheduler.Instance.Dispose();
        editorWindow.Dispose();
        play.Dispose();
        core.Dispose();
    }


}
