using System;
using System.Collections.Generic;
using System.Linq;
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
            platform.InitializeGraphics();
            graphics = platform.CreateGraphics();
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

        public static Core? ActiveInstance;

    }

    public struct FrameThrottler {

        float timeSinceRender = 0f;
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
        public Int2 Size => Window.GetSize();
        public bool IsRenderable => Size.Y > 0;

        protected FrameThrottler throttler = new();

        public ApplicationWindow() {
            var core = Core.ActiveInstance;
            if (core == null) throw new Exception("Core must be initialised before creating windows");
            Input = core.CreateInput();
        }

        public virtual void RegisterRootWindow(CSWindow window) {
            var core = Core.ActiveInstance;
            Window = window;
            using (var marker = new ProfilerMarker("Create Surface").Auto()) {
                Surface = core.GetGraphics().CreateSurface(window);
            }
            Window.SetInput(Input);
        }

        public virtual bool Validate() {
            if (!Window.IsValid) return false;
            var size = Size;
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
            if (Surface.IsValid) Surface.Dispose();
        }

        public static List<ApplicationWindow> ActiveWindows = new();
    }

    public interface IGameRoot {
        int RenderHash { get; }
        public Camera Camera { get; }
        public ScenePassManager Scene { get; }
    }

}
