#pragma once

#include <string>
#include <string_view>
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
    const std::string& GetName() {
        static const std::string empty;
        for (auto& kv : gStringToId) if (kv.second == *this) return kv.first;
        return empty;
    }

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

    struct string_hash {
        using is_transparent = void;
        [[nodiscard]] size_t operator()(const char* txt) const {
            return std::hash<std::string_view>{}(txt);
        }
        [[nodiscard]] size_t operator()(std::string_view txt) const {
            return std::hash<std::string_view>{}(txt);
        }
        [[nodiscard]] size_t operator()(const std::string& txt) const {
            return std::hash<std::string>{}(txt);
        }
        [[nodiscard]] size_t operator()(std::wstring_view txt) const {
            return std::hash<std::wstring_view>{}(txt);
        }
        [[nodiscard]] size_t operator()(const std::wstring& txt) const {
            return std::hash<std::wstring>{}(txt);
        }
    };
    // TODO: Use hat trie instead of map
    static std::unordered_map<std::string, Identifier, string_hash, std::equal_to<>> gStringToId;
    static std::unordered_map<std::wstring, Identifier, string_hash, std::equal_to<>> gWStringToId;

public:
    // Get a persistent id for the any string
    // (to more efficiently track via resource paths or other attributes)
    static Identifier RequireStringId(const std::string_view& name)
    {
        auto i = gStringToId.find(name);
        if (i == gStringToId.end())
            i = gStringToId.insert({ std::string(name), (int)gStringToId.size() }).first;
        return i->second;
    }
    static Identifier RequireStringId(const std::wstring_view& name)
    {
        auto i = gWStringToId.find(name);
        if (i == gWStringToId.end())
            i = gWStringToId.insert({ std::wstring(name), (int)gWStringToId.size() }).first;
        return i->second;
    }
    static void Purge()
    {
        gStringToId.clear();
        gWStringToId.clear();
    }

};

template <> struct std::hash<Identifier>
{
    std::size_t operator()(const Identifier& k) const { return hash<short>()(k.mId); }
};
