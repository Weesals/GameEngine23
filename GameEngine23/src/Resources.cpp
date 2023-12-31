#define _SILENCE_CXX17_CODECVT_HEADER_DEPRECATION_WARNING

#include "Resources.h"
#include <codecvt>

std::unordered_map<std::string, Identifier, Identifier::string_hash, std::equal_to<>> Identifier::gStringToId{ { "invalid", 0 } };
std::unordered_map<Identifier, std::string> Identifier::gIdToString{ { 0, "invalid" } };
std::unordered_map<Identifier, std::wstring> Identifier::gIdToWString{ };

Identifier::Identifier(const std::string_view& name) : mId(Identifier::RequireStringId(name)) { }
Identifier::Identifier(const std::wstring_view& name) : mId(Identifier::RequireStringId(name)) { }

const IdentifierWithName IdentifierWithName::None;

Identifier Identifier::RequireStringId(const std::string_view& name)
{
    auto i = gStringToId.find(name);
    if (i == gStringToId.end()) {
        i = gStringToId.insert({ std::string(name), (int)gStringToId.size() }).first;
        gIdToString.insert({ (Identifier)i->second, std::string(name) });
    }
    return i->second;
}
Identifier Identifier::RequireStringId(const std::wstring_view& wname)
{
    std::wstring_convert<std::codecvt_utf8<wchar_t>, wchar_t> converter;
    std::string name = converter.to_bytes(wname.data());
    auto identifier = RequireStringId(name);
    gIdToWString[identifier] = wname;
    return identifier;
}
void Identifier::Purge()
{
    gStringToId.clear();
}
const std::string& Identifier::GetName(Identifier identifier) {
    static std::string unknown = "unknown";
    auto find = gIdToString.find(identifier);
    if (find != gIdToString.end()) return find->second;
    return unknown;
}
const std::wstring& Identifier::GetWName(Identifier identifier) {
    auto find = gIdToWString.find(identifier);
    if (find != gIdToWString.end()) return find->second;
    auto nameA = GetName(identifier);
    std::wstring_convert<std::codecvt_utf8<wchar_t>, wchar_t> converter;
    gIdToWString[identifier] = converter.from_bytes(nameA.data());
    return gIdToWString[identifier];
}
