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

        var primaryWindow = new ApplicationWindow(core.CreateWindow("Weesals Engine"));
        var previewWindow = new ApplicationWindow(core.CreateWindow("Preview"));
        previewWindow.Window.SetSize(new Int2(400, 200));

        Input.Initialise(primaryWindow.Input);
        Resources.LoadDefaultUIAssets();

        var root = new GameRoot();
        editorWindow = new();

        var eventSystem = root.EventSystem;
        if (editorWindow != null) {
            editorWindow.GameView.EventSystem = eventSystem;
            editorWindow.GameView.Camera = root.Play.Camera;
            editorWindow.GameView.Scene = root.ScenePasses;
            editorWindow.Inspector.AppendEditables(root.Editables);
            root.Play.SelectionManager.OnSelectionChanged += (selection) => {
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
            editorWindow.EventSystem.SetInput(primaryWindow.Input);
        } else {
            eventSystem.SetInput(primaryWindow.Input);
        }

        var previewCanvas = new Canvas();
        var posImage = new Image() { AspectMode = Image.AspectModes.PreserveAspectContain, };
        posImage.AppendChild(new TextBlock("Position"));
        var velImage = new Image() { AspectMode = Image.AspectModes.PreserveAspectContain, };
        velImage.AppendChild(new TextBlock("Velocity"));
        var row = new ListLayout() { Axis = ListLayout.Axes.Horizontal, ScaleMode = ListLayout.ScaleModes.StretchOrClamp, };
        row.AppendChild(posImage);
        row.AppendChild(velImage);
        previewCanvas.AppendChild(row);

        var timer = new FrameTimer(4);
        var throttler = new FrameThrottler();

        // Loop while the window is valid
        while (core.MessagePump() == 0) {
            bool isActive = false;

            isActive |= primaryWindow.Validate();
            isActive |= previewWindow.Validate();

            if (!isActive) {
                Thread.Sleep(10);
                continue;
            }

            var graphics = core.GetGraphics();

            var dt = (float)timer.ConsumeDeltaTicks().TotalSeconds;
            Time.Update(dt);
            throttler.Update(dt);

            if (primaryWindow.IsRenderable) {
                var gameViewport = new RectI(0, primaryWindow.Size);
                // Require editor UI layout to be valid
                if (editorWindow != null) {
                    editorWindow.Update(dt, primaryWindow.Size);
                    gameViewport = editorWindow.GameView.GetGameViewportRect();
                }

                // Setup for game viewport rendering
                root.SetViewport(gameViewport);
                root.Update(dt);

                // If the frame hasnt changed, dont render anything
                var newRenderHash = root.RenderHash
                    + (editorWindow != null ? editorWindow.Canvas.Revision : 0);
                var wasThrottled = throttler.IsThrottled;
                throttler.Step(newRenderHash, editorWindow != null && editorWindow.GameView.EnableRealtime);
                if (throttler.IsThrottled) {
                    Thread.Sleep(6);
                    //core.Present();
                } else {
                    graphics.Reset();
                    graphics.SetSurface(primaryWindow.Surface);

                    // Render the game world and UI
                    root.Render(graphics, dt);

                    // Render the editor chrome
                    graphics.SetViewport(new RectI(0, primaryWindow.Size));
                    editorWindow?.Render(graphics);

                    // Flush render command buffer
                    graphics.Execute();
                    primaryWindow.Surface.Present();
                }
                root.ResetFrame();
            }
            if (previewWindow.IsRenderable) {
                posImage.Texture = root.Play.ParticleManager.PositionBuffer;
                velImage.Texture = root.Play.ParticleManager.VelocityBuffer;

                graphics.Reset();
                graphics.SetSurface(previewWindow.Surface);
                graphics.Clear();
                previewCanvas.SetSize(previewWindow.Size);
                previewCanvas.Update(dt);
                previewCanvas.RequireComposed();
                previewCanvas.Render(graphics);
                graphics.Execute();
                previewWindow.Surface.Present();
            }

            primaryWindow.Input.ReceiveTickEvent();
            previewWindow.Input.ReceiveTickEvent();
        }

        // Clean up
        JobScheduler.Instance.Dispose();
        editorWindow?.Dispose();
        primaryWindow.Dispose();
        previewWindow.Dispose();
        root.Dispose();
        core.Dispose();
    }


}
