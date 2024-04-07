using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Editor.Assets;
using Weesals.Engine.Importers;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Geometry;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Engine {
    public struct ResourceKey {
        public string Key;
        public string? SourcePath;
        public ulong SourceHash;
        public static ResourceKey CreateFileKey(string sourcePath, string category, ulong hash = 0) {
            var key = $"{category}${GeneratePathHash(sourcePath)}";
            if (hash != 0) key = $"{key}{hash:X}";
            return new() {
                Key = key,
                SourcePath = sourcePath,
                SourceHash = (ulong)File.GetLastWriteTimeUtc(sourcePath).Ticks,
            };
        }
        public static ResourceKey CreateResourceKey(string name) {
            return new() {
                Key = name,
                SourcePath = null,
                SourceHash = 0,
            };
        }
        public static ulong GeneratePathHash(string path) {
            ulong hash = 0;
            int i = 0;
            for (; i < path.Length - 4; i += 4) {
                var chars = (path[i] << 24) + (path[i + 1] << 16) +
                    (path[i + 2] << 8) + (path[i + 3] << 0);
                hash = hash * 51 + (ulong)chars;
            }
            for (; i < path.Length; ++i) {
                hash = hash * 51 + path[i];
            }
            return hash;
        }
    }
    // Use FileSystemWatcher
    public class ResourceCacheManager {
        public struct CacheEntry : IDisposable {
            public ulong SourceHash;
            public Stream FileStream;
            public bool IsValid => FileStream != null;
            public static readonly CacheEntry NotFound = new();
            public void Dispose() {
                if (FileStream != null) {
                    FileStream.Dispose();
                }
            }
        }
        private static string GetPath(ResourceKey key) {
            return $"DataCache/{key.Key}";
        }
        public static CacheEntry TryLoad(ResourceKey key) {
            var path = GetPath(key);
            if (!Path.Exists(path)) return CacheEntry.NotFound;
            var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            stream.Read(header);
            var cache = new CacheEntry() {
                SourceHash = MemoryMarshal.Read<ulong>(header),
                FileStream = stream,
            };
            if (cache.SourceHash != key.SourceHash) {
                stream.Close();
                return default;
            }
            return cache;
        }
        public static CacheEntry TrySave(ResourceKey key) {
            var stream = File.OpenWrite(GetPath(key));
            stream.Write(MemoryMarshal.Cast<ulong, byte>(new Span<ulong>(ref key.SourceHash)));
            var cache = new CacheEntry() {
                SourceHash = key.SourceHash,
                FileStream = stream,
            };
            return cache;
        }
    }

    public class Resources {
        public struct LoadedResource {
            public IAssetImporter Importer;
            public ulong ResourceId;
        }
        static Dictionary<ValueTuple<string, string>, Shader> shaders = new();
        static Dictionary<string, Sprite> loadedSprites = new();

        static Dictionary<ulong, Model> loadedModels = new();
        static Dictionary<ulong, PreprocessedShader> preprocessedShader = new();
        static Dictionary<ulong, Material> loadedMaterials = new();
        static Dictionary<ulong, CompiledShader> loadedShaders = new();
        static Dictionary<ulong, CompiledShader> uniqueShaders = new();
        static Dictionary<ulong, Font> loadedFonts = new();
        static Dictionary<ulong, CSTexture> loadedTextures = new();

        static Dictionary<ResourceKey, LoadedResource> loadedResources = new();

        private static FontImporter fontImporter = new();
        private static TextureImporter textureImporter = new();
        private static ShaderImporter shaderImporter = new();

        public static int LoadedModelCount => loadedModels.Count;
        public static int LoadedSpriteCount => loadedSprites.Count;
        public static int LoadedShaderCount => uniqueShaders.Count;
        public static int LoadedFontCount => loadedFonts.Count;
        public static int LoadedTextureCount => loadedTextures.Count;
        public static int Generation { get; private set; }

        public static void LoadDefaultUIAssets() {
            var spriteRenderer = new SpriteRenderer();

            var atlas = spriteRenderer.Generate(new[] {
                Resources.LoadTexture("./Assets/ui/T_ButtonBG.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_ButtonFrame.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_TextBox.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_FileIcon.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_FolderIcon.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_FileShader.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_FileTxt.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_FileModel.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_FileImage.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_Tick.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_HeaderBG.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
                Resources.LoadTexture("./Assets/ui/T_PanelBG.png", BufferFormat.FORMAT_R8G8B8A8_UNORM),
            });
            atlas.Sprites[0].Borders = RectF.Unit01.Inset(0.3f);
            atlas.Sprites[1].Borders = RectF.Unit01.Inset(0.3f);
            atlas.Sprites[2].Borders = RectF.Unit01.Inset(0.3f);
            atlas.Sprites[2].Scale = 0.5f;
            atlas.Sprites[10].Borders = RectF.Unit01.Inset(0.2f);
            atlas.Sprites[11].Borders = RectF.Unit01.Inset(0.2f);
            atlas.Sprites[10].Scale = 0.5f;
            atlas.Sprites[11].Scale = 0.5f;
            loadedSprites.Add("ButtonBG", atlas.Sprites[0]);
            loadedSprites.Add("ButtonFrame", atlas.Sprites[1]);
            loadedSprites.Add("TextBox", atlas.Sprites[2]);
            loadedSprites.Add("FileIcon", atlas.Sprites[3]);
            loadedSprites.Add("FolderIcon", atlas.Sprites[4]);
            loadedSprites.Add("FileShader", atlas.Sprites[5]);
            loadedSprites.Add("FileText", atlas.Sprites[6]);
            loadedSprites.Add("FileModel", atlas.Sprites[7]);
            loadedSprites.Add("FileImage", atlas.Sprites[8]);
            loadedSprites.Add("Tick", atlas.Sprites[9]);
            loadedSprites.Add("HeaderBG", atlas.Sprites[10]);
            loadedSprites.Add("PanelBG", atlas.Sprites[11]);
        }

        public static Shader LoadShader(string path, string entry) {
            var key = new ValueTuple<string, string>(path, entry);
            lock (shaders) {
                if (!shaders.TryGetValue(key, out var shader)) {
                    shader = Shader.FromPath(path, entry);
                    shaders.Add(key, shader);
                }
                return shader;
            }
        }

        public static Model LoadModel(string path) {
            var model = LoadModel(path, out var handle);
            handle.Complete();
            return model;
        }
        public static Model LoadModel(string path, out JobHandle handle) {
            handle = default;
            var pathHash = ResourceKey.GeneratePathHash(path);
            if (loadedModels.TryGetValue(pathHash, out var model)) return model;
            lock (loadedModels) {
                if (loadedModels.TryGetValue(pathHash, out model)) return model;
                model = FBXImporter.Import(path, out handle);
                loadedModels.Add(pathHash, model);
            }
            return model;
        }

        public static Sprite? TryLoadSprite(string path) {
            if (!loadedSprites.TryGetValue(path, out var sprite)) {
                return default;
            }
            return sprite;
        }

        unsafe public static Font LoadFont(string path) {
            var pathHash = ResourceKey.GeneratePathHash(path);
            if (loadedFonts.TryGetValue(pathHash, out var font)) return font;
            lock (loadedFonts) {
                if (loadedFonts.TryGetValue(pathHash, out font)) return font;
                var key = ResourceKey.CreateFileKey(path, "font");
                font = fontImporter.LoadAsset(key);
                loadedFonts.Add(pathHash, font);
                RegisterLoadedAsset(key, fontImporter, 0);
            }
            return font;
        }

        public static CSTexture LoadTexture(string path, BufferFormat format = BufferFormat.FORMAT_BC1_UNORM) {
            var pathHash = ResourceKey.GeneratePathHash(path);
            if (loadedTextures.TryGetValue(pathHash, out var texture)) return texture;
            lock (loadedTextures) {
                if (loadedTextures.TryGetValue(pathHash, out texture)) return texture;
                var key = ResourceKey.CreateFileKey(path, "tex");
                texture = textureImporter.LoadAsset(key, format);
                loadedTextures.Add(pathHash, texture);
                RegisterLoadedAsset(key, textureImporter, 0);
            }
            return texture;
        }

        public static CSTexture TryLoadTexture(string name) {
            var nameHash = ResourceKey.GeneratePathHash(name);
            if (loadedTextures.TryGetValue(nameHash, out var texture)) return texture;
            var key = ResourceKey.CreateResourceKey(name);
            using var entry = ResourceCacheManager.TryLoad(key);
            if (!entry.IsValid) return default;
            texture = CSTexture.Create(name);
            using (var reader = new BinaryReader(entry.FileStream)) {
                TextureImporter.Serialize(reader, texture);
            }
            loadedTextures.Add(nameHash, texture);
            return texture;
        }
        public static bool TryPutTexture(string name, CSTexture texture) {
            if (!texture.IsValid) return false;
            var nameHash = ResourceKey.GeneratePathHash(name);
            var key = ResourceKey.CreateResourceKey(name);
            using var entry = ResourceCacheManager.TrySave(key);
            if (!entry.IsValid) return false;
            using (var writer = new BinaryWriter(entry.FileStream)) {
                TextureImporter.Serialize(writer, texture);
            }
            loadedTextures.Add(nameHash, texture);
            RegisterLoadedAsset(key, textureImporter, 0);
            return true;
        }

        public static Material LoadMaterial(string shaderPath) {
            ulong hash = shaderPath.ComputeStringHash();
            if (loadedMaterials.TryGetValue(hash, out var material)) return material;
            lock (loadedMaterials) {
                if (loadedMaterials.TryGetValue(hash, out material)) return material;
                var source = RequirePreprocessedShader(shaderPath, default);
                material = new Material();
                material.SetVertexShader(LoadShader(shaderPath, "VSMain"));
                material.SetPixelShader(LoadShader(shaderPath, "PSMain"));
                loadedMaterials.Add(hash, material);
                return material;
            }
        }
        unsafe public static PreprocessedShader RequirePreprocessedShader(string shaderPath, Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros) {
            // TODO: Cache in 'shader'
            ulong hash = shaderPath.ComputeStringHash();
            foreach (var macro in macros) {
                var macroHash = (((ulong)macro.Key.GetStableHash() * 1254739) ^ ((ulong)macro.Value.GetStableHash() * 37139213));
                macroHash ^= macroHash >> 13;
                hash += macroHash;
            }
            if (preprocessedShader.TryGetValue(hash, out var preprocessed)) return preprocessed;
            lock (preprocessedShader) {
                if (preprocessedShader.TryGetValue(hash, out preprocessed)) return preprocessed;
                var key = ResourceKey.CreateFileKey(shaderPath, "preprocess", hash);
                preprocessed = shaderImporter.PreprocessShader(shaderPath, macros);
                preprocessedShader.Add(hash, preprocessed);
            }
            return preprocessed;
        }
        unsafe public static CompiledShader RequireShader(CSGraphics graphics, Shader shader, string profile, Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros, CSIdentifier renderPass) {
            if (shader == null) return null;

            // TODO: Cache in 'shader'
            ulong hash = shader.Path.ComputeStringHash();
            hash += renderPass.IsValid ? (ulong)renderPass.GetStableHash() : shader.Entry.ComputeStringHash();
            foreach (var macro in macros) {
                var macroHash = (((ulong)macro.Key.GetStableHash() * 1254739) ^ ((ulong)macro.Value.GetStableHash() * 37139213));
                macroHash ^= macroHash >> 13;
                hash += macroHash;
            }
            hash += profile.ComputeStringHash();
            if (loadedShaders.TryGetValue(hash, out var compiledshader)) return compiledshader;
            lock (loadedShaders) {
                if (loadedShaders.TryGetValue(hash, out compiledshader)) return compiledshader;
                var key = ResourceKey.CreateFileKey(shader.Path, "shader", hash);
                compiledshader = shaderImporter.LoadAsset(key, graphics, shader, profile, renderPass, macros);

                // Deduplicate
                var compiledHash = (ulong)compiledshader.GetHashCode();
                if (!uniqueShaders.TryAdd(compiledHash, compiledshader)) {
                    compiledshader = uniqueShaders[compiledHash];
                }
                compiledshader.ReferenceCount++;

                // Register loaded asset
                loadedShaders.Add(hash, compiledshader);
                RegisterLoadedAsset(key, shaderImporter, hash);
            }
            return compiledshader;
        }


        private static void RegisterLoadedAsset(ResourceKey key, IAssetImporter importer, ulong hash) {
            Trace.Assert(loadedResources.TryAdd(key, new LoadedResource() {
                Importer = importer,
                ResourceId = hash,
            }), "Failed to add resource, might be hash collision");
        }
        public static void ReloadAssets() {
            var invalidated = new PooledList<ResourceKey>();
            foreach (var kv in loadedResources) {
                var key = kv.Key;
                var resource = kv.Value;
                if (string.IsNullOrEmpty(key.SourcePath)) continue;
                if ((ulong)File.GetLastWriteTimeUtc(key.SourcePath).Ticks == key.SourceHash) continue;
                if (resource.Importer is ShaderImporter) {
                    var compiledshader = loadedShaders[resource.ResourceId];
                    if (--compiledshader.ReferenceCount == 0)
                        uniqueShaders.Remove((ulong)compiledshader.GetHashCode());

                    loadedShaders.Remove(resource.ResourceId);
                    invalidated.Add(key);
                }
                if (resource.Importer is TextureImporter) {
                    //loadedTextures.Remove(resource.ResourceId);
                    //invalidated.Add(key);
                }
            }
            foreach (var item in invalidated) {
                loadedResources.Remove(item);
            }
            Trace.WriteLine("Reloading " + invalidated.Count + " items");
            if (invalidated.Count > 0) {
                ++Generation;
            }
            invalidated.Dispose();
        }
    }
}
