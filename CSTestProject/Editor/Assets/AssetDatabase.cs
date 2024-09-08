using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.Geometry;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Editor.Assets {
    public class AssetImporter {
        public virtual object? TryImport(AssetReference asset) {
            return null;
        }
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
            //assetImporters.Add("jpg", TextureImporter.Default);
            //assetImporters.Add("png", TextureImporter.Default);
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

    public interface IAssetImporter {
    }
    public interface IAssetImporter<T> : IAssetImporter {
        T LoadAsset(ResourceKey key);
    }

    public class FontImporter : IAssetImporter<Font> {
        unsafe public Font LoadAsset(ResourceKey key) {
            var path = key.SourcePath;
            Font font = default;
            using (var entry = ResourceCacheManager.TryLoad(key)) {
                if (entry.IsValid) {
                    font = new();
                    using (var reader = new BinaryReader(entry.FileStream)) {
                        font.Serialize(reader);
                    }
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
            using (var entry = ResourceCacheManager.TrySave(key)) {
                if (entry.IsValid) {
                    using (var writer = new BinaryWriter(entry.FileStream)) {
                        font.Serialize(writer);
                    }
                }
            }
            return font;
        }
    }
    public class TextureImporter : IAssetImporter<CSTexture> {
        private static ProfilerMarker ProfileMarker_Serialize = new("Texture Load");
        unsafe public CSTexture LoadAsset(ResourceKey key) {
            return LoadAsset(key, BufferFormat.FORMAT_BC1_UNORM);
        }
        unsafe public CSTexture LoadAsset(ResourceKey key, BufferFormat format) {
            var path = key.SourcePath;
            CSTexture texture = default;
            using (var entry = ResourceCacheManager.TryLoad(key)) {
                if (entry.IsValid) {
                    using var cachedMarker = new ProfilerMarker("Texture Cached").Auto();
                    texture = CSTexture.Create(path);
                    using (var reader = new BinaryReader(entry.FileStream)) {
                        Serialize(reader, texture);
                    }
                    return texture;
                }
            }
            using var marker = ProfileMarker_Serialize.Auto();
            texture = CSResources.LoadTexture(path);
            if (texture.IsValid) {
                if (texture.Format != format) {
                    bool isMul4 = (texture.Size.X & 3) == 0 && (texture.Size.Y & 3) == 0;
                    if (isMul4 && format >= BufferFormat.FORMAT_BC1_TYPELESS) {
                        var texData = texture.GetTextureData().Reinterpret<Color>();
                        bool hasTrans = false;
                        foreach (var c in texData) if (c.A < 255) { hasTrans = true; break; }
                        if (hasTrans && format == BufferFormat.FORMAT_BC1_UNORM) {
                            format = BufferFormat.FORMAT_BC3_UNORM;
                        }
                        texture.GenerateMips();
                        texture.CompressTexture(format);
                    }
                }
                using (var entry = ResourceCacheManager.TrySave(key)) {
                    if (entry.IsValid) {
                        using (var writer = new BinaryWriter(entry.FileStream)) {
                            Serialize(writer, texture);
                        }
                    }
                }
            }
            return texture;
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
    }
    public class ShaderImporter : IAssetImporter<CompiledShader> {
        unsafe public CompiledShader LoadAsset(ResourceKey key) {
            throw new NotImplementedException();
        }
        unsafe public PreprocessedShader PreprocessShader(string shaderPath, Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros) {
            return new PreprocessedShader(CSGraphics.PreprocessShader(shaderPath, macros));
        }
        unsafe public CompiledShader LoadAsset(ResourceKey key, CSGraphics graphics, Shader shader,
            string profile, CSIdentifier renderPass, Span<KeyValuePair<CSIdentifier, CSIdentifier>> macros
        ) {
            using var marker = new ProfilerMarker("Load Shader").Auto();
            CompiledShader compiledshader = default;
            using (var entry = ResourceCacheManager.TryLoad(key)) {
                if (entry.IsValid) {
                    using (var reader = new BinaryReader(entry.FileStream)) {
                        compiledshader = new();
                        compiledshader.Deserialize(reader);
                        compiledshader.NativeShader = CSCompiledShader.Create(
                            Path.GetFileNameWithoutExtension(key.SourcePath),
                            compiledshader.CompiledBlob.Length,
                            compiledshader.Reflection.ConstantBuffers.Length,
                            compiledshader.Reflection.ResourceBindings.Length,
                            compiledshader.Reflection.InputParameters.Length
                        );
                        var nativeCBs = compiledshader.NativeShader.GetConstantBuffers();
                        var nativeRBs = compiledshader.NativeShader.GetResources();
                        var nativeIPs = compiledshader.NativeShader.GetInputParameters();
                        for (int i = 0; i < nativeCBs.Length; i++) {
                            ref var nativeCB = ref nativeCBs[i];
                            var cb = compiledshader.Reflection.ConstantBuffers[i];
                            nativeCB.Data.mName = cb.Name;
                            nativeCB.Data.mBindPoint = cb.BindPoint;
                            nativeCB.Data.mSize = cb.Size;
                            compiledshader.NativeShader.InitializeValues(i, cb.Values.Length);
                            var nativeValues = compiledshader.NativeShader.GetValues(i);
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
                        for (int i = 0; i < nativeIPs.Length; i++) {
                            ref var nativeIP = ref nativeIPs[i];
                            var rb = compiledshader.Reflection.InputParameters[i];
                            nativeIP.mName = rb.Name;
                            nativeIP.mSemantic = rb.Semantic;
                            nativeIP.mSemanticIndex = rb.SemanticIndex;
                            nativeIP.mRegister = rb.Register;
                            nativeIP.mMask = rb.Mask;
                            nativeIP.mType = (byte)rb.Type;
                        }
                        Trace.Assert(compiledshader.NativeShader.GetBinaryData().Length
                            == compiledshader.CompiledBlob.Length);
                        compiledshader.CompiledBlob.CopyTo(
                            compiledshader.NativeShader.GetBinaryData()
                        );
                    }
                }
            }
            if (compiledshader == null) {
                using var compilemarker = new ProfilerMarker("Compile Shader").Auto();
                compiledshader = new();
                var entryFn = renderPass.IsValid ? renderPass.GetName() + "_" + shader.Entry : shader.Entry;
                Debug.WriteLine($"Compiling Shader {shader} : {entryFn}");

                CSCompiledShader nativeshader = default;
                while (true) {
                    var source = CSGraphics.PreprocessShader(shader.Path, macros);
                    nativeshader = graphics.CompileShader(source.GetSourceRaw(), entryFn, new CSIdentifier(profile));
                    compiledshader.IncludeFiles = new string[source.GetIncludeCount()];
                    for (int i = 0; i < compiledshader.IncludeFiles.Length; i++) {
                        compiledshader.IncludeFiles[i] = source.GetInclude(i);
                    }
                    compiledshader.IncludeHash = GetIncludeHash(compiledshader);
                    source.Dispose();
                    if (nativeshader.IsValid) break;
                }
                compiledshader.CompiledBlob = nativeshader.GetBinaryData().ToArray();
                compiledshader.Reflection = new ShaderReflection();
                var nativeCBs = nativeshader.GetConstantBuffers();
                var nativeRBs = nativeshader.GetResources();
                var nativeIPs = nativeshader.GetInputParameters();
                compiledshader.Reflection.ConstantBuffers = new ShaderReflection.ConstantBuffer[nativeCBs.Length];
                compiledshader.Reflection.ResourceBindings = new ShaderReflection.ResourceBinding[nativeRBs.Length];
                compiledshader.Reflection.InputParameters = new ShaderReflection.InputParameter[nativeIPs.Length];
                for (int i = 0; i < nativeCBs.Length; i++) {
                    var nativeCB = nativeCBs[i];
                    var nativeValues = nativeshader.GetValues(i);
                    var values = new ShaderReflection.UniformValue[nativeValues.Length];
                    for (int v = 0; v < values.Length; v++) {
                        values[v] = new ShaderReflection.UniformValue() {
                            Name = nativeValues[v].mName,
                            Type = nativeValues[v].mType,
                            Offset = nativeValues[v].mOffset,
                            Size = nativeValues[v].mSize,
                            Rows = nativeValues[v].mRows,
                            Columns = nativeValues[v].mColumns,
                            Flags = nativeValues[v].mFlags,
                        };
                    }
                    compiledshader.Reflection.ConstantBuffers[i] = new() {
                        Name = nativeCB.mName,
                        BindPoint = nativeCB.mBindPoint,
                        Size = nativeCB.mSize,
                        Values = values,
                    };
                }
                for (int i = 0; i < nativeRBs.Length; i++) {
                    var nativeRB = nativeRBs[i];
                    compiledshader.Reflection.ResourceBindings[i] = new() {
                        Name = nativeRB.mName,
                        BindPoint = nativeRB.mBindPoint,
                        Stride = nativeRB.mStride,
                        Type = (ShaderReflection.ResourceTypes)nativeRB.mType,
                    };
                }
                for (int i = 0; i < nativeIPs.Length; i++) {
                    var nativeIP = nativeIPs[i];
                    compiledshader.Reflection.InputParameters[i] = new() {
                        Name = nativeIP.mName,
                        Semantic = nativeIP.mSemantic,
                        SemanticIndex = nativeIP.mSemanticIndex,
                        Mask = (byte)nativeIP.mMask,
                        Register = nativeIP.mRegister,
                        Type = (ShaderReflection.InputParameter.Types)nativeIP.mType,
                    };
                }
                compiledshader.NativeShader = nativeshader;
                compiledshader.RecomputeHash();
                using (var entry = ResourceCacheManager.TrySave(key)) {
                    using (var writer = new BinaryWriter(entry.FileStream)) {
                        compiledshader.Serialize(writer);
                    }
                }
                Debug.WriteLine($"=> Size {compiledshader.CompiledBlob.Length} bytes");
                var stats = compiledshader.NativeShader.GetStatistics();
                Debug.WriteLine($"=> Istr: {stats.mInstructionCount} Tex: {stats.mTexIC}");
            }
            return compiledshader;
        }

        public ulong GetIncludeHash(CompiledShader compiledshader) {
            ulong hash = 0;
            for (int i = 0; i < compiledshader.IncludeFiles.Length; i++) {
                hash += (ulong)File.GetLastWriteTimeUtc(compiledshader.IncludeFiles[i]).Ticks;
            }
            return hash;
        }
    }

}
