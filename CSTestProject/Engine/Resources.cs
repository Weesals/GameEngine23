using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public class Resources {
        static Dictionary<Tuple<string, string>, ShaderBase> loadedShaders = new();
        static Dictionary<string, Model> loadedModels = new();

        public static ShaderBase LoadShader(string path, string entry) {
            var key = new Tuple<string, string>(path, entry);
            if (!loadedShaders.TryGetValue(key, out var shader)) {
                shader = ShaderBase.FromPath(path, entry);
                loadedShaders.Add(key, shader);
            }
            return shader;
        }

        public static Model LoadModel(string path) {
            if (!loadedModels.TryGetValue(path, out var model)) {
                var nativemodel = CSResources.LoadModel(path);
                model = Model.CreateFrom(nativemodel);
                loadedModels.Add(path, model);
            }
            return model;
        }

        public static CSFont LoadFont(string path) {
            return CSResources.LoadFont(path);
        }

        public static CSTexture LoadTexture(string path) {
            return CSResources.LoadTexture(path);
        }
    }
}
