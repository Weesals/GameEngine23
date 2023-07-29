#pragma once

#include <string>
#include <string_view>
#include <map>
#include <unordered_map>

struct Identifier
{
    short mId;
    Identifier() : mId(-1) { }
    Identifier(int id) : mId((short)id) { }
    Identifier(const std::string_view& name);
    Identifier(const std::wstring_view& name);
    Identifier(const char* name) : Identifier(std::string_view(name)) { }
    Identifier(const wchar_t* name) : Identifier(std::wstring_view(name)) { }
    bool operator <(const Identifier& o) const { return mId < o.mId; }
    bool operator >(const Identifier& o) const { return mId > o.mId; }
    bool operator ==(const Identifier& o) const { return mId == o.mId; }
    bool operator !=(const Identifier& o) const { return mId != o.mId; }
    operator int() const { return mId; }
};

template <> struct std::hash<Identifier>
{
    std::size_t operator()(const Identifier& k) const { return hash<short>()(k.mId); }
};

// TODO: Use hat trie instead of map
class Resources
{

public:
    struct comp
    {
        template <class _Ty1, class _Ty2>
        _NODISCARD constexpr auto operator()(_Ty1&& _Left, _Ty2&& _Right) const
            noexcept(noexcept(static_cast<_Ty1&&>(_Left) < static_cast<_Ty2&&>(_Right))) // strengthened
            -> decltype(static_cast<_Ty1&&>(_Left) < static_cast<_Ty2&&>(_Right)) {
            auto ll = static_cast<_Ty1&&>(_Left).length();
            auto rl = static_cast<_Ty2&&>(_Right).length();
            if (ll != rl) return ll < rl;
            return static_cast<_Ty1&&>(_Left) < static_cast<_Ty2&&>(_Right);
        }
        using is_transparent = int;
    };
private:

    static std::map<std::string, Identifier, comp> mStringToId;
    static std::map<std::wstring, Identifier, comp> mWStringToId;

public:
    // Get a persistent id for the any string
    // (to more efficiently track via resource paths or other attributes)
    static Identifier RequireStringId(const std::string_view& name)
    {
        auto i = mStringToId.find(name);
        if (i == mStringToId.end())
            i = mStringToId.insert({ std::string(name), (int)mStringToId.size() }).first;
        return i->second;
    }
    static Identifier RequireStringId(const std::wstring_view& name)
    {
        auto i = mWStringToId.find(name);
        if (i == mWStringToId.end())
            i = mWStringToId.insert({ std::wstring(name), (int)mWStringToId.size() }).first;
        return i->second;
    }

};

