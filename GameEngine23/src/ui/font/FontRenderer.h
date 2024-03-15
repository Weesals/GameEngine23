#pragma once

#include <string>
#include <unordered_map>
#include <memory>

#include "../../MathTypes.h"
#include "../../Texture.h"

struct Glyph {
    wchar_t mGlyph;
	Int2 mAtlasOffset;
	Int2 mSize;
    Int2 mOffset;
    int mAdvance;
};

class FontInstance;

class FontRenderer {
protected:
	FontRenderer();
public:
	virtual ~FontRenderer();
	virtual std::shared_ptr<FontInstance> CreateInstance() = 0;
	static std::shared_ptr<FontRenderer> Create();
};

class FontInstance {
    class hash_tuple {
        // Recursive template code derived from Matthieu M.
        template <class Tuple, size_t Index = std::tuple_size<Tuple>::value - 1>
        struct HashValueImpl {
            static void apply(size_t& seed, Tuple const& tuple) {
                HashValueImpl<Tuple, Index - 1>::apply(seed, tuple);
                seed ^= (std::get<Index>(tuple));
            }
        };
        template <class Tuple>
        struct HashValueImpl<Tuple, 0> {
            static void apply(size_t& seed, Tuple const& tuple) {
                seed ^= (std::get<0>(tuple));
            }
        };
    public:
        template <typename ... TT>
        size_t operator()(std::tuple<TT...> const& tt) const {
            size_t seed = 0;
            HashValueImpl<std::tuple<TT...> >::apply(seed, tt);
            return seed;
        }
    };
protected:
    std::vector<Glyph> mGlyphs;
    std::unordered_map<std::tuple<char, char>, int, hash_tuple> mKernings;
	std::shared_ptr<Texture> mTexture;
    int mLineHeight;
public:
	const std::shared_ptr<Texture>& GetTexture() const { return mTexture; }
    int GetLineHeight() const { return mLineHeight; }
    int GetKerningCount() const { return (int)mKernings.size(); }
    const std::unordered_map<std::tuple<char, char>, int, hash_tuple>& GetKernings() const { return mKernings; }
    int GetKerning(wchar_t c1, wchar_t c2) const { auto k = mKernings.find(std::make_tuple((char)c1, (char)c2)); return k != mKernings.end() ? k->second : 0; }
	virtual bool Load(const std::string& path, std::string_view glyps) = 0;
    int GetGlyphCount() const { return (int)mGlyphs.size(); }
    int GetGlyphId(wchar_t chr) const;
    const Glyph& GetGlyph(int id) const;
};
