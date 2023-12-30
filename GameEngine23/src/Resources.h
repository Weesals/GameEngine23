#pragma once

#include <string>
#include <string_view>
#include <unordered_map>

struct Identifier
{
    short mId;
    Identifier() : mId(0) { }
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
    const std::string& GetName() const { return GetName(*this); }
    bool IsValid() const { return mId != 0; }

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
    static std::unordered_map<Identifier, std::string> gIdToString;
    static std::unordered_map<Identifier, std::wstring> gIdToWString;

public:
    // Get a persistent id for the any string
    // (to more efficiently track via resource paths or other attributes)
    static Identifier RequireStringId(const std::string_view& name);
    static Identifier RequireStringId(const std::wstring_view& name);
    static const std::string& GetName(Identifier identifier);
    static const std::wstring& GetWName(Identifier identifier);
    static void Purge();

};

struct IdentifierWithName : Identifier
{
    std::string mName;
    IdentifierWithName() : Identifier() { }
    IdentifierWithName(const std::string_view& name) : Identifier(name), mName(name) { }
    IdentifierWithName(const char* name) : IdentifierWithName(std::string_view(name)) { }
    IdentifierWithName(const Identifier& other) : Identifier(other), mName(other.GetName()) { }
    IdentifierWithName(const IdentifierWithName& other) = default;
    IdentifierWithName(IdentifierWithName&& other) = default;
    ~IdentifierWithName() { }
    IdentifierWithName& operator =(const IdentifierWithName& other) = default;
    IdentifierWithName& operator =(IdentifierWithName&& other) = default;
    operator const std::string& () const { return mName; }
    operator short() const { return mId; }
    const std::string& GetName() const { return mName; }
    bool operator ==(const std::string& other) const { return mName == other; }
    bool operator !=(const std::string& other) const { return mName != other; }
    static const IdentifierWithName None;
};

template <> struct std::hash<Identifier>
{
    std::size_t operator()(const Identifier& k) const { return hash<short>()(k.mId); }
};
