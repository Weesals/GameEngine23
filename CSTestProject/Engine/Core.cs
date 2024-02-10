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

        public CSGraphics GetGraphics() { return graphics; }
        public CSWindow GetWindow() { return platform.GetWindow(); }
        public CSResources GetResources() { return platform.GetResources(); }
        public CSInput GetInput() { return platform.GetInput(); }

        public CSModel LoadModel(string path) {
            return CSResources.LoadModel(path);
        }

        public int MessagePump() {
            return platform.MessagePump();
        }
        public void Present() {
            platform.Present();
        }

        public static Core? ActiveInstance;

    }
}
