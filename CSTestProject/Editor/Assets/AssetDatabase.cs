using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.Editor.Assets {
    public class AssetImporter {
        public virtual object? TryImport(AssetReference asset) {
            return null;
        }
    }
    public class TextureImporter : AssetImporter {
        public BufferFormat Format = BufferFormat.FORMAT_BC1_UNORM;
        public override object? TryImport(AssetReference asset) {
            var texture = Resources.LoadTexture(asset.FilePath, Format);
            return texture;
        }
        public static readonly TextureImporter Default = new();
    }
    public class ModelImporter : AssetImporter {
        public override object? TryImport(AssetReference asset) {
            return Resources.LoadModel(asset.FilePath);
        }
        public static readonly ModelImporter Default = new();
    }
    public class AssetReference {
        public string FilePath;
        public AssetImporter Importer;
        public AssetReference(string filePath) {
            FilePath = filePath;
        }
    }
    public class AssetDatabase {

        private Dictionary<string, AssetImporter> assetImporters = new();
        private Dictionary<string, AssetReference> assetReferences = new();

        public AssetDatabase() {
            assetImporters.Add("jpg", TextureImporter.Default);
            assetImporters.Add("png", TextureImporter.Default);
            assetImporters.Add("fbx", ModelImporter.Default);
        }

        public AssetReference AllocateMetadata() {
            string identifier;
            while (true) {
                identifier = Random.Shared.Next().ToString("X");
                if (!assetReferences.ContainsKey(identifier)) break;
            }
            var meta = new AssetReference(identifier);
            assetReferences.Add(identifier, meta);
            return meta;
        }

        public AssetReference RequireMetadataByPath(string path) {
            if (assetReferences.TryGetValue(path, out var asset)) return asset;
            asset = new AssetReference(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (assetImporters.TryGetValue(ext, out var importer)) {
                asset.Importer = importer;
            }
            assetReferences.Add(path, asset);
            return asset;
        }
        public AssetReference? GetMetadata(string identifier) {
            return assetReferences.TryGetValue(identifier, out var meta) ? meta : default;
        }

    }
}
