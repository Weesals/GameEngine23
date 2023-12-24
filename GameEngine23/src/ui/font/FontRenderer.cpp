#include "FontRenderer.h"

#include "../../utility/DistanceFieldGenerator.h"

#include "freetype/freetype.h"

#include <array>
#include <algorithm>
#include <chrono>
#include <cassert>
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>

#pragma comment(lib, "freetype.lib")

FontRenderer::FontRenderer() { }
FontRenderer::~FontRenderer() { }

int FontInstance::GetGlyphId(wchar_t chr) const {
    auto pnt = std::partition_point(mGlyphs.begin(), mGlyphs.end(), [=](auto& glyph) {
        return glyph.mGlyph < chr;
    });
    if (pnt != mGlyphs.end()) return (int)std::distance(mGlyphs.begin(), pnt);
    return 0;
}
const Glyph& FontInstance::GetGlyph(int id) const {
    return mGlyphs[id];
}

class FontRendererFT : public FontRenderer {
    FT_Library mLibrary;
public:
    FontRendererFT() {
        FT_Init_FreeType(&mLibrary);
    }
    ~FontRendererFT() {
        FT_Done_FreeType(mLibrary);
    }
    virtual std::shared_ptr<FontInstance> CreateInstance() override;
    const FT_Library& GetLibrary() const { return mLibrary; }
};

class FontInstanceFT : public FontInstance {
    FontRendererFT* mRenderer;
public:
    FontInstanceFT(FontRendererFT* renderer)
        : mRenderer(renderer)
    { }
    ~FontInstanceFT() {
    }
    virtual bool Load(const std::string& path, std::string_view glyphs) override {
        FT_Face mFace;
        if (FT_New_Face(mRenderer->GetLibrary(), path.c_str(), 0, &mFace)) return false;
        mLineHeight = 27;
        FT_Set_Pixel_Sizes(mFace, 0, mLineHeight);

        struct EntryMeta {
            Glyph mGlyph;
            int mDataOffset;
            uint16_t mDataSize;
            uint16_t mX;
            uint16_t mY;
        };
        std::vector<EntryMeta> entries;
        std::vector<uint8_t> pxdata;
        entries.reserve(glyphs.size());
        pxdata.reserve(1024);

        for (auto& c1 : glyphs) {
            auto ci1 = FT_Get_Char_Index(mFace, c1);
            FT_Load_Glyph(mFace, ci1, FT_LOAD_RENDER);

            // Read pixel data
            auto& bitmap = mFace->glyph->bitmap;
            unsigned char* pixels = bitmap.buffer;
            EntryMeta meta{
                .mGlyph = Glyph {
                    .mGlyph = (uint8_t)c1,
                    .mSize = Int2((int)bitmap.width, (int)bitmap.rows),
                    .mOffset = Int2((int)mFace->glyph->bitmap_left, (int)((mFace->ascender >> 6) - mFace->glyph->bitmap_top)),
                    .mAdvance = (int)(mFace->glyph->advance.x >> 6),
                },
                .mDataOffset = (int)pxdata.size(),
                .mDataSize = (uint16_t)(bitmap.rows * bitmap.width),
            };
            pxdata.resize(meta.mDataOffset + meta.mDataSize);
            for (int y = 0; y < (int)bitmap.rows; ++y) {
                for (int x = 0; x < (int)bitmap.width; ++x) {
                    pxdata[meta.mDataOffset + x + y * meta.mGlyph.mSize.x] = pixels[x];
                }
                pixels += bitmap.pitch;
            }
            entries.push_back(meta);
            
            // Read kerning data
            for (auto& c2 : glyphs) {
                auto ci2 = FT_Get_Char_Index(mFace, c2);

                FT_Vector kerning;
                FT_Get_Kerning(mFace, ci1, ci2, FT_KERNING_DEFAULT, &kerning);
                if (kerning.x != 0) {
                    mKernings.insert(std::make_pair(std::make_tuple(c1, c2), (int)kerning.x));
                }
            }
        }

        // Blit glyphs into texture (starting with largest)
        std::sort(entries.begin(), entries.end(), [](auto& g1, auto& g2) {
            return g1.mGlyph.mSize.y > g2.mGlyph.mSize.y;
        });

        mTexture = std::make_shared<Texture>();
        mTexture->SetSize(256);
        mTexture->SetMipCount(1);

        auto datavec = mTexture->GetRawData();
        auto texdata = std::span<ColorB4>((ColorB4*)datavec.data(), (int)datavec.size() / 4);
        for (auto& px : texdata) px = ColorB4::Clear;

        int lineHeight = 0;
        int padding = 9;
        Int2 pos = Int2(padding, padding);
        for (auto& entry : entries) {
            int endX = pos.x + entry.mGlyph.mSize.x + padding;
            if (endX > mTexture->GetSize().x) {
                pos.x = padding;
                pos.y += lineHeight;
                lineHeight = 0;
                if (entry.mGlyph.mSize.x > mTexture->GetSize().x) break;
            }
            lineHeight = std::max((int)lineHeight, (int)entry.mGlyph.mSize.y + padding);
            if (pos.y + lineHeight > mTexture->GetSize().y) break;
            entry.mX = (uint16_t)pos.x;
            entry.mY = (uint16_t)pos.y;
            for (int y = 0; y < entry.mGlyph.mSize.y; ++y) {
                for (int x = 0; x < entry.mGlyph.mSize.x; ++x) {
                    texdata[pos.x + x + (pos.y + y) * mTexture->GetSize().x]
                        = ColorB4::MakeWhite(pxdata[entry.mDataOffset + x + y * entry.mGlyph.mSize.x]);
                }
            }
            pos.x += entry.mGlyph.mSize.x + padding;
        }
        
        for (int m = 1; m < mTexture->GetMipCount(); ++m) {
            FT_Set_Pixel_Sizes(mFace, 0, mLineHeight >> m);
            auto mipDataRaw = mTexture->GetData(m, 0);
            std::span<ColorB4> mipDataPX((ColorB4*)mipDataRaw.data(), (int)mipDataRaw.size() / 4);
            Int2 mipSize = Texture::GetMipResolution(mTexture->GetSize(), mTexture->GetBufferFormat(), m);
            // Generate mips
            for (auto& entry : entries) {
                auto ci1 = FT_Get_Char_Index(mFace, entry.mGlyph.mGlyph);
                FT_Load_Glyph(mFace, ci1, FT_LOAD_RENDER);
                // Read pixel data
                auto& bitmap = mFace->glyph->bitmap;
                unsigned char* pixels = bitmap.buffer;
                Int2 pos = Int2(
                    (entry.mX * 2 + entry.mGlyph.mSize.x - (bitmap.width << m)) >> (m + 1),
                    (entry.mY * 2 + entry.mGlyph.mSize.y - (bitmap.rows << m)) >> (m + 1)
                );
                for (int y = 0; y < (int)bitmap.rows; ++y) {
                    for (int x = 0; x < (int)bitmap.width; ++x) {
                        Int2 pxp(pos.x + x, pos.y + y);
                        if ((uint32_t)pxp.x >= (uint32_t)mipSize.x || (uint32_t)pxp.y >= (uint32_t)mipSize.y) continue;
                        auto px = pixels[x + y * bitmap.pitch];
                        mipDataPX[pxp.x + pxp.y * mipSize.x] = ColorB4::MakeWhite(px);
                    }
                }
            }
        }
        FT_Done_Face(mFace);

        for (int m = 0; m < std::min(1, mTexture->GetMipCount()); ++m) {
            auto mipDataRaw = mTexture->GetData(m, 0);
            std::span<ColorB4> mipDataPX((ColorB4*)mipDataRaw.data(), (int)mipDataRaw.size() / 4);
            Int2 mipSize = Texture::GetMipResolution(mTexture->GetSize(), mTexture->GetBufferFormat(), m);
            // Generate distance field
            for (int t = 0; t < 1; ++t) {
                DistanceFieldGenerator dfgen;
                dfgen.Generate(mipDataPX, mipSize);
                dfgen.ApplyDistances(mipDataPX, mipSize, 7.0f / (1 << m));
            }
        }
        mTexture->MarkChanged();

        std::sort(entries.begin(), entries.end(), [](auto& g1, auto& g2) {
            return g1.mGlyph.mGlyph < g2.mGlyph.mGlyph;
        });
        mGlyphs.reserve(entries.size());
        for (auto& entry : entries) {
            auto glyph = entry.mGlyph;
            glyph.mAtlasOffset = Int2(entry.mX, entry.mY);
            mGlyphs.push_back(glyph);
        }

        return true;
    }
};

std::shared_ptr<FontInstance> FontRendererFT::CreateInstance() {
    return std::make_shared<FontInstanceFT>(this);
}
std::shared_ptr<FontRenderer> FontRenderer::Create() {
    return std::make_shared<FontRendererFT>();
}
