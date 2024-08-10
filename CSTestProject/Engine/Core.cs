using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public class Core : IDisposable {
        private Platform platform;
        private CSGraphics graphics;

        unsafe public Core() {
            platform = new Platform(Platform.Create());
            graphics = platform.CreateGraphics();
        }
        unsafe public void Dispose() {
            graphics.Dispose();
            platform.Dispose();
        }

        public CSWindow CreateWindow(string name) { return platform.CreateWindow(name); }
        public CSGraphics GetGraphics() { return graphics; }
        public CSResources GetResources() { return platform.GetResources(); }
        public CSInput CreateInput() { return platform.CreateInput(); }

        public CSGraphics CreateGraphics() { return platform.CreateGraphics(); }

        public int MessagePump() {
            return platform.MessagePump();
        }

        public static Core? ActiveInstance;

    }

    public class ApplicationWindow : IDisposable {
        public CSWindow Window;
        public CSGraphicsSurface Surface;
        public CSInput Input;
        public Int2 Size => Window.GetSize();
        public bool IsRenderable => Size.Y > 0;
        public ApplicationWindow() { }
        public virtual void RegisterRootWindow(CSWindow window) {
            var core = Core.ActiveInstance;
            if (core == null) throw new Exception("Core must be initialised before creating windows");
            Window = window;
            Surface = core.GetGraphics().CreateSurface(window);
            Input = core.CreateInput();
            Window.SetInput(Input);
        }
        public bool Validate() {
            if (!Window.IsValid) return false;
            var size = Size;
            if (size.Y <= 0) return false;
            if (size != Surface.GetResolution())
                Surface.SetResolution(size);
            return true;
        }
        public void Dispose() {
            if (Surface.IsValid) Surface.Dispose();
        }
    }

}
