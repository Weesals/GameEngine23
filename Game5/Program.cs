using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using UnityEngine;
using Weesals.ECS;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Engine.Jobs;
using Game5.Game;
using Weesals.Landscape;
using Weesals.UI;
using Weesals.Utility;
using Game5;
using Weesals.Engine.Rendering;

class Program {

    static EditorWindow? editorWindow;

    public static void Main() {
        var core = new Core();
        Core.ActiveInstance = core;
        Resources.LoadDefaultUIAssets();

        editorWindow = new();

        var scene = new Scene();

        var root = new GameRoot(scene);
        var play = root.Play;

        var eventSystem = root.EventSystem;
        if (editorWindow != null) {
            editorWindow.GameView.EventSystem = eventSystem;
            editorWindow.GameView.Camera = play.Camera;
            editorWindow.GameView.Scene = root.ScenePasses;
            editorWindow.Inspector.AppendEditables(root.Editables);
            play.SelectionManager.OnSelectionChanged += (selection) => {
                foreach (var selected in selection) {
                    if (selected.Owner is LandscapeRenderer landscape) {
                        editorWindow.ActivateLandscapeTools(landscape);
                        return;
                    }
                    var entity = selected;
                    if (entity.Owner is IEntityRedirect redirect)
                        entity = redirect.GetOwner(entity.Data);
                    if (entity.Owner is World world) {
                        editorWindow.ActivateEntityInspector(world, entity.GetEntity());
                        return;
                    }
                }
                editorWindow.Inspector.SetInspector(default);
            };
            editorWindow.EventSystem.KeyboardFilter.Insert(0, eventSystem);
            editorWindow.EventSystem.SetInput(core.GetInput());
        } else {
            eventSystem.SetInput(core.GetInput());
        }

        var posImage = new Image() { AspectMode = Image.AspectModes.PreserveAspectContain, };
        var velImage = new Image() { AspectMode = Image.AspectModes.PreserveAspectContain, };
        var row = new ListLayout() { Axis = ListLayout.Axes.Horizontal, ScaleMode = ListLayout.ScaleModes.StretchOrClamp, };
        row.AppendChild(posImage);
        row.AppendChild(velImage);
        editorWindow.ProjectView.AppendChild(row);

        var timer = new FrameTimer(4);
        var throttler = new FrameThrottler();

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

            var dt = (float)timer.ConsumeDeltaTicks().TotalSeconds;
            Time.Update(dt);
            throttler.Update(dt);

            var gameViewport = new RectI(0, windowRes);
            // Require editor UI layout to be valid
            if (editorWindow != null) {
                editorWindow.Update(dt, windowRes);
                gameViewport = editorWindow.GameView.GetGameViewportRect();
            }
            // Setup for game viewport rendering
            root.SetViewport(gameViewport);

            root.Update(dt);

            graphics.Reset();

            root.PreRender(graphics, dt);

            // If the frame hasnt changed, dont render anything
            var newRenderHash = root.RenderHash
                + (editorWindow != null ? editorWindow.Canvas.Revision : 0)
                + Handles.RenderHash;
            var wasThrottled = throttler.IsThrottled;
            throttler.Step(newRenderHash, true);
            //Console.WriteLine($"{throttler.IsThrottled} {((int)(1000 * dt))}");
            if (throttler.IsThrottled) {
                graphics.Execute();
                Thread.Sleep(6);
                //core.Present();
            } else {
                posImage.Texture = root.Play.ParticleManager.PositionBuffer;
                velImage.Texture = root.Play.ParticleManager.VelocityBuffer;

                // Render the game world and UI
                root.Render(graphics);

                // Render the editor chrome
                graphics.SetViewport(new RectI(0, windowRes));
                editorWindow?.Render(graphics);

                // Flush render command buffer
                graphics.Execute();
                core.Present();
            }
            core.GetInput().ReceiveTickEvent();

            root.PostRender();
        }

        // Clean up
        JobScheduler.Instance.Dispose();
        editorWindow?.Dispose();
        root.Dispose();
        core.Dispose();
    }


}
