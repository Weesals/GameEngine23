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
using Weesals.Engine.Serialization;
using Weesals.Geometry;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Engine {
    public enum DefaultTexture { None, White, Black, Clear, }

    public struct ReadLockScope : IDisposable {
        public ReaderWriterLockSlim mutex;
        public ReadLockScope(ReaderWriterLockSlim _mutex) { mutex = _mutex; mutex.EnterReadLock(); }
        public void Dispose() { mutex.ExitReadLock(); }
    }
    public struct WriteLockScope : IDisposable {
        public ReaderWriterLockSlim mutex;
        public WriteLockScope(ReaderWriterLockSlim _mutex) { mutex = _mutex; mutex.EnterWriteLock(); }
        public void Dispose() { mutex.ExitWriteLock(); }
    }

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
        public override int GetHashCode() {
            return Key == null ? 0 : Key.GetHashCode();
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
        public static void DeleteCache(ResourceKey key) {
            File.Delete(GetPath(key));
        }
    }

    public class ResourceLoader<TResource> {
        public class Item {
            public bool Loaded;
            public TResource Resource;
            public JobHandle LoadHandle;
            public ulong ConfigHash;

            public void SetResource(TResource resource) {
                Resource = resource;
                MarkLoaded();
            }
            public void MarkLoaded() {
                Loaded = true;
                LoadHandle = default;
            }
        }

        private ReaderWriterLockSlim dictionaryLock = new();
        public Dictionary<ulong, Item> loadedItems = new();
        public int Count {
            get {
                using var read = new ReadLockScope(dictionaryLock);
                return loadedItems.Count;
            }
        }
        public void Dispose() {
            dictionaryLock.Dispose();
        }
        // Might fail and return null if contested
        public Item? TryGetItem(ulong id) {
            if (dictionaryLock.TryEnterReadLock(0)) {
                loadedItems.TryGetValue(id, out var item);
                dictionaryLock.ExitReadLock();
                return item;
            }
            return default;
        }
        // Will always succeed
        public Item RequireItem(ulong id) {
            var item = TryGetItem(id);
            if (item != null) return item;
            try {
                dictionaryLock.EnterWriteLock();
                if (!loadedItems.TryGetValue(id, out item)) {
                    loadedItems.Add(id, item = new());
                }
                return item;
            } finally {
                dictionaryLock.ExitWriteLock();
            }
        }
    }

    public class Resources {
        public struct LoadedResource {
            public IAssetImporter Importer;
            public ulong ResourceId;
        }
        static Dictionary<ValueTuple<string, string>, Shader> shaders = new();
        static Dictionary<string, Sprite> loadedSprites = new();

        //static Dictionary<ulong, Model> loadedModels = new();
        static Dictionary<ulong, PreprocessedShader> preprocessedShader = new();
        static Dictionary<ulong, Material> loadedMaterials = new();
        //static Dictionary<ulong, CompiledShader> loadedShaders = new();
        static Dictionary<ulong, CompiledShader> uniqueShaders = new();
        static Dictionary<ulong, Font> loadedFonts = new();
        static ResourceLoader<CompiledShader> loadedShaders = new();
        static ResourceLoader<Model> loadedModels = new();
        static ResourceLoader<CSTexture> loadedTextures = new();

        static Dictionary<ResourceKey, LoadedResource> loadedResources = new();

        private static FontImporter fontImporter = new();
        private static TextureImporter textureImporter = new();
        private static ShaderImporter shaderImporter = new();

        private static CSTexture defaultTexWhite;
        private static CSTexture defaultTexBlack;
        private static CSTexture defaultTexClear;

        public static int LoadedModelCount => loadedModels.Count;
        public static int LoadedSpriteCount => loadedSprites.Count;
        public static int LoadedShaderCount => uniqueShaders.Count;
        public static int LoadedFontCount => loadedFonts.Count;
        public static int LoadedTextureCount => loadedTextures.Count;
        public static int Generation { get; private set; }

        public void Dispose() {
            defaultTexWhite.Dispose();
            defaultTexBlack.Dispose();
            defaultTexClear.Dispose();
        }

        public static void LoadDefaultUIAssets() {
            using var marker = new ProfilerMarker("Load Default Assets").Auto();

            Resources.LoadFont("./Assets/Roboto-Regular.ttf");

            var spriteRenderer = new SpriteRenderer();

            var spritePaths = new KeyValuePair<string, string>[] {
                new("ButtonBG", "./Assets/ui/T_ButtonBG.png"),
                new("ButtonFrame", "./Assets/ui/T_ButtonFrame.png"),
                new("TextBox", "./Assets/ui/T_TextBox.png"),
                new("FileIcon", "./Assets/ui/T_FileIcon.png"),
                new("FolderIcon", "./Assets/ui/T_FolderIcon.png"),
                new("FileShader", "./Assets/ui/T_FileShader.png"),
                new("FileText", "./Assets/ui/T_FileTxt.png"),
                new("FileModel", "./Assets/ui/T_FileModel.png"),
                new("FileImage", "./Assets/ui/T_FileImage.png"),
                new("Tick", "./Assets/ui/T_Tick.png"),
                new("HeaderBG", "./Assets/ui/T_HeaderBG.png"),
                new("PanelBG", "./Assets/ui/T_PanelBG.png"),
            };
            var originalSprites = new CSTexture[spritePaths.Length];
            for (int i = 0; i < spritePaths.Length; i++) {
                originalSprites[i] = Resources.LoadTexture(spritePaths[i].Value, BufferFormat.FORMAT_R8G8B8A8_UNORM);
            }

            var atlas = spriteRenderer.Generate(originalSprites);
            atlas.Sprites[0].Borders = RectF.Unit01.Inset(0.3f);
            atlas.Sprites[1].Borders = RectF.Unit01.Inset(0.3f);
            atlas.Sprites[2].Borders = RectF.Unit01.Inset(0.3f);
            atlas.Sprites[2].Scale = 0.5f;
            atlas.Sprites[10].Borders = RectF.Unit01.Inset(0.2f);
            atlas.Sprites[11].Borders = RectF.Unit01.Inset(0.2f);
            atlas.Sprites[10].Scale = 0.5f;
            atlas.Sprites[11].Scale = 0.5f;
            for (int i = 0; i < atlas.Sprites.Length; i++) {
                loadedSprites.Add(spritePaths[i].Key, atlas.Sprites[i]);
            }
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

        public static Model LoadModel(string path)
            => LoadModel(path, FBXImporter.LoadConfig.Default);
        public static Model LoadModel(string path, FBXImporter.LoadConfig config) {
            var model = LoadModel(path, config, out var handle);
            handle.Complete();
            return model;
        }
        public static Model LoadModel(string path, out JobHandle handle)
            => LoadModel(path, FBXImporter.LoadConfig.Default, out handle);
        public static Model LoadModel(string path, FBXImporter.LoadConfig config, out JobHandle handle) {
            handle = default;
            var pathHash = ResourceKey.GeneratePathHash(path);
            var item = loadedModels.RequireItem(pathHash);
            if (!item.Loaded) {
                if (item.Resource == null) {
                    lock (item) {
                        if (item.Resource == null) {
                            var key = ResourceKey.CreateFileKey(path, "model");
                            using (var entry = ResourceCacheManager.TryLoad(key)) {
                                if (entry.IsValid) {
                                    var data = new byte[entry.FileStream.Length];
                                    entry.FileStream.Read(data);
                                    item.Resource = new();
                                    item.LoadHandle = JobHandle.Schedule(() => {
                                        var buffer = new DataBuffer(data);
                                        using (var serializer = TSONNode.CreateRead(buffer)) {
                                            item.Resource.Serialize(serializer);
                                        }
                                        item.MarkLoaded();
                                    });
                                }
                            }
                            if (item.Resource == null) {
                                item.Resource = FBXImporter.Import(path, config, out item.LoadHandle);
                                item.LoadHandle.Then((object? modelObj) => {
                                    lock (loadedModels) {
                                        using var entry = ResourceCacheManager.TrySave(key);
                                        if (entry.IsValid) {
                                            var buffer = new DataBuffer();
                                            using (var serializer = TSONNode.CreateWrite(buffer)) {
                                                ((Model)modelObj).Serialize(serializer);
                                            }
                                            using (var writer = new BinaryWriter(entry.FileStream)) {
                                                writer.Write(buffer.AsSpan());
                                            }
                                        }
                                    }
                                    item.MarkLoaded();
                                }, item.Resource);
                            }
                        }
                    }
                }
            }
            handle = item.LoadHandle;
            return item.Resource;
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
            var pathHash = ResourceKey.GeneratePathHash(path) + (ulong)format;
            var item = loadedTextures.RequireItem(pathHash);
            if (item.Loaded) return item.Resource;
            lock (item) {
                if (item.Loaded) return item.Resource;
                item.ConfigHash = (ulong)format;
                var key = ResourceKey.CreateFileKey(path, $"tex@{format}");
                item.SetResource(textureImporter.LoadAsset(key, format));
                RegisterLoadedAsset(key, textureImporter, pathHash);
                return item.Resource;
            }
        }
        /*public static JobResult<CSTexture> LoadTextureAsync(string path, BufferFormat format = BufferFormat.FORMAT_BC1_UNORM) {
            var pathHash = ResourceKey.GeneratePathHash(path);
            if (loadedTextures.TryGetValue(pathHash, out var texture)) return texture.Resource;
            lock (loadedTextures) {
                if (loadedTextures.TryGetValue(pathHash, out texture)) return texture.Resource;
                texture = new();
                var key = ResourceKey.CreateFileKey(path, "tex");
                texture.LoadHandle = JobHandle.Schedule(() => {
                    texture.Resource = textureImporter.LoadAsset(key, format);
                    texture.LoadHandle = default;
                });
                loadedTextures.Add(pathHash, texture);
                RegisterLoadedAsset(key, textureImporter, 0);
            }
            return new JobResult<CSTexture>(texture.LoadHandle, texture.Resource);
        }*/

        public static CSTexture TryLoadTexture(string name) {
            var nameHash = ResourceKey.GeneratePathHash(name);
            var item = loadedTextures.RequireItem(nameHash);
            if (item.Loaded) return item.Resource;
            var key = ResourceKey.CreateResourceKey(name);
            lock (item) {
                if (item.Loaded) return item.Resource;
                using var entry = ResourceCacheManager.TryLoad(key);
                if (!entry.IsValid) {
                    item.SetResource(default);
                    return default;
                }
                var texture = CSTexture.Create(name);
                using (var reader = new BinaryReader(entry.FileStream)) {
                    TextureImporter.Serialize(reader, texture);
                }
                item.SetResource(texture);
                return item.Resource;
            }
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
            var item = loadedTextures.RequireItem(nameHash);
            item.SetResource(texture);
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
        public static string GetResourceType(string filePath) {
            using (var reader = File.OpenRead(filePath)) {
                Span<byte> header = stackalloc byte[64];
                int len = reader.Read(header);
                if (header[0] == '/' && header[1] == '*') {
                    int end = 2;
                    while (end < len && header[end] != '*' && header[end] != ':') ++end;
                    var typeName = header.Slice(2, end - 2);
                    return Encoding.UTF8.GetString(typeName);
                }
            }
            return null;
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
        unsafe public static CompiledShader RequireShader(CSGraphics graphics, Shader shader, string profile, Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros, CSIdentifier renderPass, out JobHandle handle) {
            handle = default;
            if (shader == null) return null;

            // TODO: Cache in 'shader'
            ulong hash = shader.Path.ComputeStringHash();
            hash += renderPass.IsValid ? (ulong)renderPass.GetStableHash() : shader.Entry.ComputeStringHash();
            foreach (var macro in macros) {
                var macroHash = (((ulong)macro.Key.GetStableHash() * 1128889) ^ ((ulong)macro.Value.GetStableHash()));
                macroHash ^= macroHash >> 13;
                macroHash *= 29499439;
                macroHash ^= macroHash >> 13;
                hash += macroHash;
            }
            hash += profile.ComputeStringHash();
            var item = loadedShaders.RequireItem(hash);
            if (item.Resource == null) {
                var key = ResourceKey.CreateFileKey(shader.Path, "shader", hash);
                lock (item) {
                    if (item.Resource == null) {
                        var compiledshader = new CompiledShader();
                        item.Resource = compiledshader;
                        var macrosArray = macros;//.ToArray();
                        //item.LoadHandle = JobHandle.Schedule(() => {
                            shaderImporter.LoadAsset(item.Resource, key, graphics, shader, profile, renderPass, macrosArray);
                            // Deduplicate
                            var compiledHash = (ulong)compiledshader.GetHashCode();
                            if (!uniqueShaders.TryAdd(compiledHash, compiledshader)) {
                                compiledshader = uniqueShaders[compiledHash];
                            }
                            compiledshader.ReferenceCount++;
                            item.Resource = compiledshader;
                            item.MarkLoaded();
                        //});

                        RegisterLoadedAsset(key, shaderImporter, hash);
                    }
                }
            }
            handle = item.LoadHandle;
            return item.Resource;
        }


        private static void RegisterLoadedAsset(ResourceKey key, IAssetImporter importer, ulong hash) {
            lock (loadedResources) {
                Trace.Assert(loadedResources.TryAdd(key, new LoadedResource() {
                    Importer = importer,
                    ResourceId = hash,
                }), "Failed to add resource, might be hash collision");
            }
        }
        public static void ReloadAssets() {
            var invalidated = new PooledList<ResourceKey>();
            foreach (var kv in loadedResources) {
                var key = kv.Key;
                var resource = kv.Value;
                if (string.IsNullOrEmpty(key.SourcePath)) continue;
                bool same = (ulong)File.GetLastWriteTimeUtc(key.SourcePath).Ticks == key.SourceHash;
                if (same && resource.Importer is ShaderImporter shaderImporter) {
                    var compiledshader = loadedShaders.loadedItems[resource.ResourceId].Resource;
                    if (shaderImporter.GetIncludeHash(compiledshader) != compiledshader.IncludeHash) {
                        same = false;
                    }
                }
                if (same) continue;
                if (resource.Importer is ShaderImporter) {
                    ResourceCacheManager.DeleteCache(key);
                    var compiledshader = loadedShaders.loadedItems[resource.ResourceId].Resource;
                    if (--compiledshader.ReferenceCount == 0)
                        uniqueShaders.Remove((ulong)compiledshader.GetHashCode());

                    //loadedShaders.Remove(resource.ResourceId);
                    loadedShaders.loadedItems[resource.ResourceId].Resource = null;
                    loadedShaders.loadedItems[resource.ResourceId].Loaded = false;
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

        private static CSTexture RequireSolidTex(ref CSTexture tex, string name, int size, Color color) {
            if (tex.IsValid) return tex;
            tex = CSTexture.Create(name, size, size, BufferFormat.FORMAT_R8G8B8A8_UNORM);
            tex.GetTextureData().Reinterpret<Color>().AsSpan().Fill(color);
            return tex;
        }
        public static CSTexture RequireDefaultTexture(DefaultTexture defaultTexture) {
            switch (defaultTexture) {
                case DefaultTexture.White: return RequireSolidTex(ref defaultTexWhite, "White", 4, Color.White);
                case DefaultTexture.Black: return RequireSolidTex(ref defaultTexBlack, "Black", 4, Color.Black);
                case DefaultTexture.Clear: return RequireSolidTex(ref defaultTexClear, "Clear", 4, Color.Clear);
            }
            return default;
        }

        public static string FindAssetPath(CSTexture texture) {
            foreach (var tex in loadedTextures.loadedItems) {
                if (tex.Value.Resource != texture) continue;
                foreach (var resource in loadedResources) {
                    if (resource.Value.Importer != textureImporter) continue;
                    if (resource.Value.ResourceId == tex.Key) {
                        return resource.Key.SourcePath;
                    }
                }
            }
            return default;
        }
    }
}
