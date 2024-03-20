using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Importers;
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Geometry;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Engine {
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
        private static string GetPath(string key) {
            return $"DataCache/{key}";
        }
        public static CacheEntry TryLoad(string key) {
            var path = GetPath(key);
            if (!Path.Exists(path)) return CacheEntry.NotFound;
            var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            stream.Read(header);
            var cache = new CacheEntry() {
                SourceHash = MemoryMarshal.Read<ulong>(header),
                FileStream = stream,
            };
            return cache;
        }
        public static CacheEntry TrySave(string key, ulong sourceHash) {
            var stream = File.OpenWrite(GetPath(key));
            stream.Write(MemoryMarshal.Cast<ulong, byte>(new Span<ulong>(ref sourceHash)));
            var cache = new CacheEntry() {
                SourceHash = sourceHash,
                FileStream = stream,
            };
            return cache;
        }
    }

    public class Resources {
        public struct LoadedShader {
            public string FilePath;
            public Shader Shader;
        }
        static Dictionary<ValueTuple<string, string>, ShaderBase> loadedShaders = new();
        static Dictionary<string, Model> loadedModels = new();
        static Dictionary<string, Sprite> loadedSprites = new();
        static Dictionary<ulong, LoadedShader> loadedShaders2 = new();
        static Dictionary<ulong, Shader> uniqueShaders = new();
        static Dictionary<string, Font> loadedFonts = new();
        static Dictionary<string, CSTexture> loadedTextures = new();

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

        public static ShaderBase LoadShader(string path, string entry) {
            var key = new ValueTuple<string, string>(path, entry);
            lock (loadedShaders) {
                if (!loadedShaders.TryGetValue(key, out var shader)) {
                    shader = ShaderBase.FromPath(path, entry);
                    loadedShaders.Add(key, shader);
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
            if (!loadedModels.TryGetValue(path, out var model)) {
                //model = Model.CreateFrom(Path.GetFileName(path), CSResources.LoadModel(path));
                model = FBXImporter.Import(path, out handle);
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

        unsafe public static Font LoadFont(string path) {
            if (loadedFonts.TryGetValue(path, out var font)) return font;
            var sourceHash = (ulong)File.GetLastWriteTime(path).Ticks;
            var hash = GeneratePathHash(path);
            var key = $"font${hash:X}";
            using (var entry = ResourceCacheManager.TryLoad(key)) {
                if (entry.IsValid && entry.SourceHash == sourceHash) {
                    font = new();
                    using (var reader = new BinaryReader(entry.FileStream)) {
                        font.Serialize(reader);
                    }
                    loadedFonts.Add(path, font);
                    return font;
                }
            }
            var natFont = CSResources.LoadFont(path);
            font = new Font();
            font.Texture = natFont.GetTexture();
            font.LineHeight = natFont.GetLineHeight();
            font.Kernings = new();
            font.Glyphs = new CSGlyph[natFont.GetGlyphCount()];
            for (int i = 0; i < font.Glyphs.Length; i++) {
                font.Glyphs[i] = natFont.GetGlyph(i);
            }
            int kerningCount = natFont.GetKerningCount();
            var kernings = stackalloc char[kerningCount * 2];
            natFont.GetKernings(new MemoryBlock<ushort>((ushort*)kernings, kerningCount));
            for (int i = 0; i < kerningCount; i++) {
                char c1 = kernings[i * 2 + 0], c2 = kernings[i * 2 + 1];
                var kerning = font.GetKerning(c1, c2);
                font.Kernings.Add((c1, c2), kerning);
            }
            var gen = new DistanceFieldGenerator();
            gen.Generate(font.Texture.GetTextureData().Reinterpret<Color>(), font.Texture.GetSize());
            gen.ApplyDistances(font.Texture.GetTextureData().Reinterpret<Color>(), font.Texture.GetSize(), 7.0f);
            gen.Dispose();
            using (var entry = ResourceCacheManager.TrySave(key, sourceHash)) {
                if (entry.IsValid) {
                    using (var writer = new BinaryWriter(entry.FileStream)) {
                        font.Serialize(writer);
                    }
                }
            }
            loadedFonts.Add(path, font);
            return font;
        }

        public static CSTexture TryLoadTexture(string key) {
            if (loadedTextures.TryGetValue(key, out var texture)) return texture;
            using var entry = ResourceCacheManager.TryLoad(key);
            if (!entry.IsValid) return default;
            texture = CSTexture.Create(key);
            using (var reader = new BinaryReader(entry.FileStream)) {
                Serialize(reader, texture);
            }
            loadedTextures.Add(key, texture);
            return texture;
        }
        public static bool TryPutTexture(string key, CSTexture texture) {
            if (!texture.IsValid) return false;
            using var entry = ResourceCacheManager.TrySave(key, 0);
            if (!entry.IsValid) return false;
            using (var writer = new BinaryWriter(entry.FileStream)) {
                Serialize(writer, texture);
            }
            loadedTextures.Add(key, texture);
            return true;
        }

        public static CSTexture LoadTexture(string path, BufferFormat format = BufferFormat.FORMAT_BC1_UNORM) {
            if (loadedTextures.TryGetValue(path, out var texture)) return texture;
            lock (loadedTextures) {
                if (loadedTextures.TryGetValue(path, out texture)) return texture;
                var sourceHash = (ulong)File.GetLastWriteTime(path).Ticks;
                var key = $"tex${(GeneratePathHash(path) + (int)format):X}";
                using (var entry = ResourceCacheManager.TryLoad(key)) {
                    if (entry.IsValid && sourceHash == entry.SourceHash) {
                        using var cachedMarker = new ProfilerMarker("Texture Cached").Auto();
                        texture = CSTexture.Create(path);
                        using (var reader = new BinaryReader(entry.FileStream)) {
                            Serialize(reader, texture);
                        }
                        loadedTextures.Add(path, texture);
                        return texture;
                    }
                }
                using var marker = new ProfilerMarker("Texture Load").Auto();
                texture = CSResources.LoadTexture(path);
                if (texture.IsValid) {
                    if (texture.Format != format) {
                        bool isMul4 = (texture.Size.X & 3) == 0 && (texture.Size.Y & 3) == 0;
                        if (isMul4 && format >= BufferFormat.FORMAT_BC4_TYPELESS) {
                            texture.CompressTexture(format);
                        }
                    }
                    using (var entry = ResourceCacheManager.TrySave(key, sourceHash)) {
                        if (entry.IsValid) {
                            using (var writer = new BinaryWriter(entry.FileStream)) {
                                Serialize(writer, texture);
                            }
                        }
                    }
                }
                loadedTextures.Add(path, texture);
            }
            return texture;
        }

        private static int GeneratePathHash(string path) {
            int hash = 0;
            int i = 0;
            for (; i < path.Length - 4; i += 4) {
                var chars = (path[i] << 24) + (path[i + 1] << 16) +
                    (path[i + 2] << 8) + (path[i + 3] << 0);
                hash = hash * 51 + chars;
            }
            for (; i < path.Length; ++i) {
                hash = hash * 51 + path[i];
            }
            return hash;
        }

        public static void Serialize(BinaryWriter writer, CSTexture texture) {
            writer.Write(texture.GetSize3D().X);
            writer.Write(texture.GetSize3D().Y);
            writer.Write(texture.GetSize3D().Z);
            writer.Write(texture.MipCount);
            writer.Write(texture.ArrayCount);
            writer.Write((int)texture.Format);
            for (int i = 0; i < texture.ArrayCount; i++) {
                for (int m = 0; m < texture.MipCount; m++) {
                    var data = texture.GetTextureData(m, i);
                    writer.Write(data.AsSpan());
                }
            }
        }
        public static void Serialize(BinaryReader reader, CSTexture texture) {
            const int MaxTexSize = 0x10000;
            var size = new Int3(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()
            );
            if ((uint)size.X > MaxTexSize || (uint)size.Y > MaxTexSize || (uint)size.Z > MaxTexSize)
                return;
            texture.SetSize3D(size);
            texture.SetMipCount(reader.ReadInt32());
            texture.SetArrayCount(reader.ReadInt32());
            texture.SetFormat((BufferFormat)reader.ReadInt32());
            for (int i = 0; i < texture.ArrayCount; i++) {
                for (int m = 0; m < texture.MipCount; m++) {
                    var data = texture.GetTextureData(m, i);
                    reader.Read(data.AsSpan());
                }
            }
        }

        public static void ReloadShaders() {
            var invalidated = new PooledList<ulong>();
            foreach (var shader in loadedShaders2) {
                var key = $"shader${shader.Key:X}";
                var sourceHash = (ulong)File.GetLastWriteTime(shader.Value.FilePath).Ticks;
                using (var entry = ResourceCacheManager.TryLoad(key)) {
                    if (entry.IsValid && entry.SourceHash != sourceHash) {
                        invalidated.Add(shader.Key);
                    }
                }
            }
            foreach (var item in invalidated) {
                loadedShaders2.Remove(item);
            }
            Debug.WriteLine("Reloading " + invalidated.Count + " shaders");
            if (invalidated.Count > 0) {
                ++Generation;
            }
        }

        unsafe public static Shader RequireShader(CSGraphics graphics, ShaderBase shader, string profile, Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros, CSIdentifier renderPass) {
            if (shader == null) return null;

            // TODO: Cache in 'shader'
            ulong hash = shader.Path.ComputeStringHash();
            hash += renderPass.IsValid ? (ulong)renderPass.GetHashCode() : shader.Entry.ComputeStringHash();
            foreach (var macro in macros) {
                var macroHash = (((ulong)macro.Key.mId * 1254739) ^ ((ulong)macro.Value.mId * 37139213));
                macroHash ^= macroHash >> 13;
                hash += macroHash;
            }
            hash += profile.ComputeStringHash();
            if (!loadedShaders2.TryGetValue(hash, out var existing)) {
                using var marker = new ProfilerMarker("Compile Shader").Auto();
                existing.FilePath = shader.Path;
                existing.Shader = new Shader();
                var compiledshader = existing.Shader;
                var key = $"shader${hash:X}";
                var sourceHash = (ulong)File.GetLastWriteTime(shader.Path).Ticks;
                using (var entry = ResourceCacheManager.TryLoad(key)) {
                    if (entry.IsValid && entry.SourceHash == sourceHash) {
                        using (var reader = new BinaryReader(entry.FileStream)) {
                            compiledshader.Deserialize(reader);
                            compiledshader.CompiledShader = CSCompiledShader.Create(
                                Path.GetFileNameWithoutExtension(shader.Path),
                                compiledshader.CompiledBlob.Length,
                                compiledshader.Reflection.ConstantBuffers.Length,
                                compiledshader.Reflection.ResourceBindings.Length
                            );
                            var nativeCBs = compiledshader.CompiledShader.GetConstantBuffers();
                            var nativeRBs = compiledshader.CompiledShader.GetResources();
                            for (int i = 0; i < nativeCBs.Length; i++) {
                                ref var nativeCB = ref nativeCBs[i];
                                var cb = compiledshader.Reflection.ConstantBuffers[i];
                                nativeCB.Data.mName = cb.Name;
                                nativeCB.Data.mBindPoint = cb.BindPoint;
                                nativeCB.Data.mSize = cb.Size;
                                compiledshader.CompiledShader.InitializeValues(i, cb.Values.Length);
                                var nativeValues = compiledshader.CompiledShader.GetValues(i);
                                for (int v = 0; v < nativeValues.Length; v++) {
                                    ref var nativeValue = ref nativeValues[v];
                                    var cbValue = cb.Values[v];
                                    nativeValue.mName = cbValue.Name;
                                    nativeValue.mOffset = cbValue.Offset;
                                    nativeValue.mSize = cbValue.Size;
                                }
                            }
                            for (int i = 0; i < nativeRBs.Length; i++) {
                                ref var nativeRB = ref nativeRBs[i];
                                var rb = compiledshader.Reflection.ResourceBindings[i];
                                nativeRB.mName = rb.Name;
                                nativeRB.mBindPoint = rb.BindPoint;
                                nativeRB.mStride = rb.Stride;
                                nativeRB.mType = (byte)rb.Type;
                            }
                            Trace.Assert(compiledshader.CompiledShader.GetBinaryData().Length
                                == compiledshader.CompiledBlob.Length);
                            compiledshader.CompiledBlob.CopyTo(
                                compiledshader.CompiledShader.GetBinaryData()
                            );
                        }
                    }
                }
                if (compiledshader.CompiledBlob == null) {
                    var entryFn = renderPass.IsValid ? renderPass.GetName() + "_" + shader.Entry : shader.Entry;
                    Debug.WriteLine($"Compiling Shader {shader} : {entryFn}");
                    var nativeshader = graphics.CompileShader(shader.Path, entryFn, new CSIdentifier(profile), macros);
                    compiledshader.CompiledBlob = nativeshader.GetBinaryData().ToArray();
                    compiledshader.Reflection = new ShaderReflection();
                    var nativeCBs = nativeshader.GetConstantBuffers();
                    var nativeRBs = nativeshader.GetResources();
                    compiledshader.Reflection.ConstantBuffers = new ShaderReflection.ConstantBuffer[nativeCBs.Length];
                    compiledshader.Reflection.ResourceBindings = new ShaderReflection.ResourceBinding[nativeRBs.Length];
                    for (int i = 0; i < nativeCBs.Length; i++) {
                        var nativeCB = nativeCBs[i];
                        var nativeValues = nativeshader.GetValues(i);
                        var values = new ShaderReflection.UniformValue[nativeValues.Length];
                        for (int v = 0; v < values.Length; v++) {
                            values[v] = new ShaderReflection.UniformValue() {
                                Name = nativeValues[v].mName,
                                Offset = nativeValues[v].mOffset,
                                Size = nativeValues[v].mSize,
                            };
                        }
                        compiledshader.Reflection.ConstantBuffers[i] = new ShaderReflection.ConstantBuffer() {
                            Name = nativeCB.mName,
                            BindPoint = nativeCB.mBindPoint,
                            Size = nativeCB.mSize,
                            Values = values,
                        };
                    }
                    for (int i = 0; i < nativeRBs.Length; i++) {
                        var nativeRB = nativeRBs[i];
                        compiledshader.Reflection.ResourceBindings[i] = new ShaderReflection.ResourceBinding() {
                            Name = nativeRB.mName,
                            BindPoint = nativeRB.mBindPoint,
                            Stride = nativeRB.mStride,
                            Type = (ShaderReflection.ResourceTypes)nativeRB.mType,
                        };
                    }
                    compiledshader.CompiledShader = nativeshader;
                    compiledshader.RecomputeHash();
                    using (var entry = ResourceCacheManager.TrySave(key, sourceHash)) {
                        using (var writer = new BinaryWriter(entry.FileStream)) {
                            compiledshader.Serialize(writer);
                        }
                    }
                }
                uniqueShaders.TryAdd((ulong)compiledshader.GetHashCode(), compiledshader);
                loadedShaders2.Add(hash, existing);
            }
            return existing.Shader;
        }

    }
}
