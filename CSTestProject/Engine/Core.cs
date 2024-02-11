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

        public CSModel LoadModel(string path) {
            return CSResources.LoadModel(path);
        }

        public int MessagePump() {
            return platform.MessagePump();
        }

        public static Core? ActiveInstance;

    }

    public class ApplicationWindow : IDisposable {
        public CSWindow Window;
        public CSGraphicsSurface Surface;
        public CSInput Input;
        public Int2 Size;
        public bool IsRenderable => Size.Y > 0;
        public ApplicationWindow(CSWindow window) {
            var core = Core.ActiveInstance;
            if (core == null) throw new Exception("Core must be initialised before creating windows");
            Window = window;
            Surface = core.GetGraphics().CreateSurface(window);
            Input = core.CreateInput();
            Window.SetInput(Input);
        }
        public bool Validate() {
            Size = Window.GetSize();
            if (Size.Y <= 0) return false;
            if (Size != Surface.GetResolution()) Surface.SetResolution(Size);
            return true;
        }
        public void Dispose() {
            Surface.Dispose();
        }
    }

}
