using GameEngine23.Interop;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using UnityEngine;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Game;
using Weesals.Landscape;
using Weesals.UI;
using Weesals.Utility;

class Program {

    [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "InvokeTest", ExactSpelling = true)]
    public static extern Int2 InvokeTest();

    unsafe static void Main() {
        var core = new Core();
        Core.ActiveInstance = core;
        Resources.LoadDefaultUIAssets();

        var editorWindow = new EditorWindow();

        var scene = new Scene(core.GetScene());

        var play = new Play(scene);

        var eventSystem = new EventSystem(play.Canvas);

        Stopwatch timer = new();
        timer.Start();

        float timeSinceRender = 0f;

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

            int renderHash = play.Canvas.Revision + play.RenderRevision + editorWindow.Canvas.Revision + play.Scene.CSScene.GetGPURevision();

            var dt = (float)timer.Elapsed.TotalSeconds;
            timer.Restart();
            Time.Update(dt);
            timeSinceRender += dt;

            // Require editor UI layout to be valid
            editorWindow.Update(dt, windowRes);
            var gameViewport = editorWindow.GameView.GetGameViewportRect();
            // Setup for game viewport rendering
            play.SetViewport(gameViewport);

            eventSystem.SetPointerOffset(-(Vector2)gameViewport.Min);
            eventSystem.Update(dt);
            play.Update(dt);
            play.PreRender(graphics);

            // If the frame hasnt changed, dont render anything
            bool requireRender = renderHash != play.Canvas.Revision + play.RenderRevision + editorWindow.Canvas.Revision + play.Scene.CSScene.GetGPURevision();
            if (!requireRender && timeSinceRender < 0.25f) {
                Thread.Sleep(6);
                continue;
            }

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
        }

        // Clean up
        editorWindow.Dispose();
        play.Dispose();
        core.Dispose();
    }


}
