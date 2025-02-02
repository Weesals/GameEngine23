using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Profiling;

namespace Weesals.Engine {
    public class Core : IDisposable {
        private Platform platform;
        private CSGraphics graphics;

        unsafe public Core() {
            using (var marker = new ProfilerMarker("Creating Core").Auto()) {
                platform = new Platform(Platform.Create());
            }
        }
        unsafe public void Dispose() {
            graphics.Dispose();
            platform.Dispose();
        }

        public void InitializeGraphics() {
            using (var marker = new ProfilerMarker("Init Graphics").Auto()) {
                platform.InitializeGraphics();
                graphics = platform.CreateGraphics();
            }
        }

        public CSWindow CreateWindow(string name) {
            using (var marker = new ProfilerMarker("Create Window").Auto()) {
                return platform.CreateWindow(name);
            }
        }
        public CSGraphics GetGraphics() { return graphics; }
        public CSResources GetResources() { return platform.GetResources(); }
        public CSInput CreateInput() { return platform.CreateInput(); }

        public CSGraphics CreateGraphics() { return platform.CreateGraphics(); }

        public int MessagePump() {
            return platform.MessagePump();
        }

        public static Core ActiveInstance;

    }

    public struct FrameThrottler {

        float timeSinceRender = float.MaxValue;
        int renderHash = 0;

        public bool IsThrottled { get; private set; }

        static bool onBattery = false;
        public static bool IsOnBattery => onBattery;

        // Define constants and structures required for P/Invoke
        const int SYSTEM_POWER_STATUS = 0x0015;
        [StructLayout(LayoutKind.Sequential)]
        public struct SystemPowerStatus {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }
        [DllImport("Kernel32.dll")]
        public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        public FrameThrottler() { }

        static FrameThrottler() {
            UpdateBatteryStatus();
        }

        public static void UpdateBatteryStatus() {
            SystemPowerStatus status;
            if (!GetSystemPowerStatus(out status)) return;
            onBattery = (status.ACLineStatus == 0);
        }

        public float Step(float dt, int newRenderHash, float targetDT) {
            timeSinceRender += dt;
            IsThrottled = renderHash == newRenderHash && timeSinceRender <= targetDT;
            renderHash = newRenderHash;
            if (IsThrottled) return -1f;

            float elapsed = timeSinceRender;
            NotifyRendered();
            return elapsed;
        }
        private void NotifyRendered() {
            timeSinceRender = 0f;
        }
    }

    public class ApplicationWindow : IDisposable {
        public CSWindow Window;
        public CSGraphicsSurface Surface;
        public CSInput Input;
        public Int2 WindowSize => Window.GetSize();
        public bool IsRenderable => WindowSize.Y > 0;

        protected FrameThrottler throttler = new();

        public ApplicationWindow() {
            var core = Core.ActiveInstance;
            if (core == null) throw new Exception("Core must be initialised before creating windows");
            Input = core.CreateInput();
        }

        public virtual void RegisterRootWindow(CSWindow window) {
            Window = window;
            Window.SetInput(Input);
            CreateSurface();
            ApplicationWindow.ActiveWindows.Add(this);
        }

        protected virtual void CreateSurface() {
            var core = Core.ActiveInstance;
            using (var marker = new ProfilerMarker("Create Surface").Auto()) {
                Surface = core.GetGraphics().CreateSurface(Window);
            }
        }

        public virtual bool Validate() {
            if (!Window.IsValid) return false;
            var size = WindowSize;
            if (size.Y <= 0) return false;
            if (Surface.IsValid && size != Surface.GetResolution())
                Surface.SetResolution(size);
            return true;
        }

        public virtual void Update(float dt) {
        }

        public virtual float AdjustRenderDT(float dt, int renderHash, float targetDT) {
            return throttler.Step(dt, renderHash, targetDT);
        }

        public virtual void Render(float dt, CSGraphics graphics) {
        }

        public virtual void Dispose() {
            ApplicationWindow.ActiveWindows.Remove(this);
            if (Surface.IsValid) Surface.Dispose();
        }

        public static List<ApplicationWindow> ActiveWindows = new();
    }

    public interface IGameRoot {
        int RenderHash { get; }
        public Camera Camera { get; }
        public ScenePassManager Scene { get; }
    }

    public static class Graphics {

        public struct Context {
            public CSGraphics Graphics;
            public MaterialStack MaterialStack;
            public RenderQueue? RenderQueue;
        }
        [ThreadStatic]
        private static Context context;
        [ThreadStatic]
        private static Material intermediateMaterial;

        public static ref MaterialStack MaterialStack => ref context.MaterialStack;

        public struct Scoped : IDisposable {
            Context previousContext;
            public Scoped(CSGraphics _graphics, Material? rootMaterial = null, RenderQueue? queue = null) {
                previousContext = context;
                context = new() {
                    Graphics = _graphics,
                    MaterialStack = new(rootMaterial),
                    RenderQueue = queue,
                };
            }
            public void Dispose() {
                context.MaterialStack.Dispose();
                context = previousContext;
            }
        }

        public unsafe static void DrawMesh(Mesh mesh, Material material) {
            var graphics = context.Graphics;
            ref var materialStack = ref context.MaterialStack;

            using var meshPush = materialStack.Push(mesh.Material);
            using var matPush = materialStack.Push(material);

            var bindingsPtr = stackalloc CSBufferLayout[2] { mesh.IndexBuffer, mesh.VertexBuffer };
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);

            var pipeline = MaterialEvaluator.ResolvePipeline(graphics, bindings, materialStack);
            var resources = MaterialEvaluator.ResolveResources(graphics, pipeline, materialStack);
            context.RenderQueue.AppendMesh(mesh.Name, pipeline, bindings, resources);
        }

        public unsafe static void DrawMesh(Mesh mesh, Matrix4x4 matrix) {
            var graphics = context.Graphics;
            ref var materialStack = ref context.MaterialStack;

            intermediateMaterial ??= new();
            intermediateMaterial.SetValue(RootMaterial.iMMat, matrix);

            using var meshPush = materialStack.Push(mesh.Material);
            using var matPush = materialStack.Push(intermediateMaterial);

            var bindingsPtr = stackalloc CSBufferLayout[2] { mesh.IndexBuffer, mesh.VertexBuffer };
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);

            var pipeline = MaterialEvaluator.ResolvePipeline(graphics, bindings, materialStack);
            var resources = MaterialEvaluator.ResolveResources(graphics, pipeline, materialStack);
            context.RenderQueue.AppendMesh(mesh.Name, pipeline, bindings, resources);
        }

        public unsafe static void DrawMeshNow(Mesh mesh, Material material) {
            var graphics = context.Graphics;
            ref var materialStack = ref context.MaterialStack;

            using var meshPush = materialStack.Push(mesh.Material);
            using var matPush = materialStack.Push(material);

            var bindingsPtr = stackalloc CSBufferLayout[2] { mesh.IndexBuffer, mesh.VertexBuffer };
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);

            var pipeline = MaterialEvaluator.ResolvePipeline(graphics, bindings, materialStack);
            var resources = MaterialEvaluator.ResolveResources(graphics, pipeline, materialStack);
            graphics.Draw(pipeline, bindings.AsCSSpan(), resources.AsCSSpan(), CSDrawConfig.Default);
        }

        public unsafe static void DrawMeshInstanced(Mesh mesh, Material material, CSBufferLayout instanceData) {
            var graphics = context.Graphics;
            ref var materialStack = ref context.MaterialStack;

            intermediateMaterial ??= new();
            intermediateMaterial.SetValue("InstanceData", instanceData);

            using var meshPush = materialStack.Push(mesh.Material);
            using var matPush = materialStack.Push(intermediateMaterial);

            var bindingsPtr = stackalloc CSBufferLayout[2] { mesh.IndexBuffer, mesh.VertexBuffer };
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);

            var pipeline = MaterialEvaluator.ResolvePipeline(graphics, bindings, materialStack);
            var resources = MaterialEvaluator.ResolveResources(graphics, pipeline, materialStack);
            context.RenderQueue.AppendMesh(mesh.Name, pipeline, bindings, resources, instanceData.mCount);
        }
    }

}
