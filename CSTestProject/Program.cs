using GameEngine23.Interop;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Game;
using Weesals.Landscape;
using Weesals.UI;
using Weesals.Utility;

class Program {


    /*public struct TestInt2 {
        public int X, Y;
    }*/

    [DllImport("CSBindings", CallingConvention = CallingConvention.Cdecl, EntryPoint = "InvokeTest", ExactSpelling = true)]
    public static extern Int2 InvokeTest();

    unsafe static void Main() {
        /*var value = InvokeTest();
        Stopwatch watch = new Stopwatch();

        watch.Restart();
        float v0 = PerformanceTest.DoNothing();
        watch.Stop();
        var ms0 = watch.ElapsedMilliseconds;

        watch.Restart();
        float v1 = 0;
        for (int i = 0; i < 200000000; ++i)
            v1 = PerformanceTest.CSDLLInvoke(v1, i);
        watch.Stop();
        var ms1 = watch.ElapsedMilliseconds;

        watch.Restart();
        float v2 = PerformanceTest.CPPVirtual();
        watch.Stop();
        var ms2 = watch.ElapsedMilliseconds;

        watch.Restart();
        float v3 = 0;
        for (int i = 0; i < 200000000; ++i)
            v3 = v3 + i;
        watch.Stop();
        var ms3 = watch.ElapsedMilliseconds;

        watch.Restart();
        float v4 = PerformanceTest.CPPDirect();
        watch.Stop();
        var ms4 = watch.ElapsedMilliseconds;

        Debug.Assert(v1 == v2);
        Debug.Assert(v1 == v3);
        Console.WriteLine(
            $"Adding numbers from 0 to 200000000\n"
            + $" BaseLine: {ms0}ms\n"
            + $" C# DllInvoke: {ms1}ms, Result {v1}\n"
            + $" C++ Virtual : {ms2}ms, Result {v2}\n"
            + $" C# Direct   : {ms3}ms, Result {v3}\n"
            + $" C++ Direct  : {ms4}ms, Result {v4}\n");
        */


        var core = new Core();
        Core.ActiveInstance = core;

        var editorWindow = new EditorWindow();

        var world = new Play();
        var scene = new Scene(core.GetScene());
        var shadowPass = new ShadowPass(scene, "Shadows");
        var basePass = new BasePass(scene, "BasePass");
        basePass.UpdateShadowParameters(shadowPass);
        basePass.AddDependency("ShadowMap", shadowPass);

        var model = Resources.LoadModel("./assets/SM_Barracks.fbx");
        var camera = new Camera() {
            FOV = 3.14f * 0.25f,
            Position = new Vector3(0, 20f, -10f),
            Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 3.14f * 0.3f),
        };

        var instances = new List<CSInstance>();
        foreach (var mesh in model.Meshes) {
            var instance = scene.CSScene.CreateInstance();
            instances.Add(instance);
            using var materials = new PooledList<Material>();
            materials.Add(mesh.Material);
            materials.Add(basePass.OverrideMaterial);
            materials.Add(scene.RootMaterial);
            basePass.AddInstance(instance, mesh, materials);
            materials[1] = shadowPass.OverrideMaterial;
            shadowPass.AddInstance(instance, mesh, materials);
        };


        var canvas = new Canvas();
        canvas.AppendChild(new UIPlay());

        Stopwatch timer = new();
        timer.Start();

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

            var dt = (float)timer.Elapsed.TotalSeconds;
            timer.Restart();

            var res = graphics.GetResolution();
            graphics.Reset();

            // Move camera with WASD/UDLR
            var move = new Vector2(
                Input.GetSignedAxis(KeyCode.LeftArrow, KeyCode.RightArrow) + Input.GetSignedAxis(KeyCode.A, KeyCode.D),
                Input.GetSignedAxis(KeyCode.DownArrow, KeyCode.UpArrow) + Input.GetSignedAxis(KeyCode.S, KeyCode.W)
            );
            camera.Position += move.AppendY(0f) * (dt * camera.Position.Y);

            // Require editor UI layout to be valid
            editorWindow.UpdateLayout(res);
            var gameViewport = editorWindow.GameView.GetGameViewportRect();

            // Setup for game viewport rendering
            camera.Aspect = (float)gameViewport.Size.X / gameViewport.Size.Y;
            basePass.SetViewProjection(camera.GetViewMatrix(), camera.GetProjectionMatrix());

            // Control visibility with Spacebar
            basePass.SetVisible(instances[0], !Input.GetKeyDown(KeyCode.Space));

            // Control position with mouse cursor
            var mpos = Input.GetMousePosition();
            var mray = camera.ViewportToRay(((RectF)gameViewport).Unlerp(mpos));
            var pos = mray.ProjectTo(new Plane(Vector3.UnitY, 0f));
            var mat = Matrix4x4.CreateRotationY(3.14f) * Matrix4x4.CreateTranslation(pos);
            foreach (var instance in instances) {
                scene.CSScene.UpdateInstanceData(instance, &mat, sizeof(Matrix4x4));
            }

            // Render the shadow pass
            shadowPass.UpdateShadowFrustum(basePass);
            shadowPass.Bind(graphics);
            graphics.Clear();
            shadowPass.Render(graphics);

            // Render the base pass
            basePass.UpdateShadowParameters(shadowPass);
            basePass.Bind(graphics);
            graphics.Clear();
            graphics.SetViewport(gameViewport);
            world.Render(graphics, basePass);
            basePass.Render(graphics);
            canvas.SetSize(gameViewport.Size);
            canvas.Render(graphics, canvas.Material);

            // Render the editor chrome
            graphics.SetViewport(new RectI(0, 0, res.X, res.Y));
            editorWindow.Render(graphics);

            // Flush render command buffer
            graphics.Execute();
            core.Present();
        }

        // Clean up
        editorWindow.Dispose();
        canvas.Dispose();
        core.Dispose();
    }


}
