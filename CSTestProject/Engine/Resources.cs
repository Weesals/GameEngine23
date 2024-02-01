using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.UI;

namespace Weesals.Engine {
    public class Resources {
        static Dictionary<ValueTuple<string, string>, ShaderBase> loadedShaders = new();
        static Dictionary<string, Model> loadedModels = new();
        static Dictionary<string, Sprite> loadedSprites = new();

        public static void LoadDefaultUIAssets() {
            var spriteRenderer = new SpriteRenderer();
            var atlas = spriteRenderer.Generate(new[] {
                Resources.LoadTexture("./assets/ui/T_ButtonBG.png"),
                Resources.LoadTexture("./assets/ui/T_ButtonFrame.png"),
                Resources.LoadTexture("./assets/ui/T_FileIcon.png"),
                Resources.LoadTexture("./assets/ui/T_FolderIcon.png"),
                Resources.LoadTexture("./assets/ui/T_FileShader.png"),
                Resources.LoadTexture("./assets/ui/T_FileTxt.png"),
                Resources.LoadTexture("./assets/ui/T_FileModel.png"),
                Resources.LoadTexture("./assets/ui/T_FileImage.png"),
                Resources.LoadTexture("./assets/ui/T_Tick.png"),
            });
            atlas.Sprites[0].Borders = RectF.Unit01.Inset(0.1f);
            atlas.Sprites[1].Borders = RectF.Unit01.Inset(0.2f);
            loadedSprites.Add("ButtonBG", atlas.Sprites[0]);
            loadedSprites.Add("ButtonFrame", atlas.Sprites[1]);
            loadedSprites.Add("FileIcon", atlas.Sprites[2]);
            loadedSprites.Add("FolderIcon", atlas.Sprites[3]);
            loadedSprites.Add("FileShader", atlas.Sprites[4]);
            loadedSprites.Add("FileText", atlas.Sprites[5]);
            loadedSprites.Add("FileModel", atlas.Sprites[6]);
            loadedSprites.Add("FileImage", atlas.Sprites[7]);
            loadedSprites.Add("Tick", atlas.Sprites[8]);
        }

        public static ShaderBase LoadShader(string path, string entry) {
            var key = new ValueTuple<string, string>(path, entry);
            if (!loadedShaders.TryGetValue(key, out var shader)) {
                shader = ShaderBase.FromPath(path, entry);
                loadedShaders.Add(key, shader);
            }
            return shader;
        }

        public static Model LoadModel(string path) {
            if (!loadedModels.TryGetValue(path, out var model)) {
                model = Model.CreateFrom(Path.GetFileName(path), CSResources.LoadModel(path));
                loadedModels.Add(path, model);
            }
            return model;
        }

        public static Sprite? TryLoadSprite(string path) {
            if (!loadedSprites.TryGetValue(path, out var sprite)) {
                return default;
            }
            return sprite;
        }

        public static CSFont LoadFont(string path) {
            return CSResources.LoadFont(path);
        }

        public static CSTexture LoadTexture(string path) {
            return CSResources.LoadTexture(path);
        }
    }
}
