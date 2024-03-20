#define _SILENCE_CXX17_CODECVT_HEADER_DEPRECATION_WARNING

#include "Resources.h"
#include <codecvt>
#include <mutex>

std::unordered_map<std::string, Identifier, Identifier::string_hash, std::equal_to<>> Identifier::gStringToId{ { "invalid", 0 } };
std::vector<std::string> Identifier::gIdToString{ "invalid" };
std::vector<std::wstring> Identifier::gIdToWString{ L"invalid" };

Identifier::Identifier(const std::string_view& name) : mId(Identifier::RequireStringId(name)) { }
Identifier::Identifier(const std::wstring_view& name) : mId(Identifier::RequireStringId(name)) { }

const IdentifierWithName IdentifierWithName::None;

std::mutex gInsertMutex;

Identifier Identifier::RequireStringId(const std::string_view& name) {
    std::lock_guard<std::mutex> lock(gInsertMutex);
    auto i = gStringToId.find(name);
    if (i == gStringToId.end()) {
        i = gStringToId.insert({ std::string(name), (int)gIdToString.size() }).first;
        gIdToString.push_back(std::string(name));
    }
    return i->second;
}
Identifier Identifier::RequireStringId(const std::wstring_view& wname) {
    std::wstring_convert<std::codecvt_utf8<wchar_t>, wchar_t> converter;
    std::string name = converter.to_bytes(wname.data());
    auto identifier = RequireStringId(name);
    std::lock_guard<std::mutex> lock(gInsertMutex);
    if (identifier.mId >= gIdToWString.size()) gIdToWString.resize(gIdToString.size());
    gIdToWString[identifier] = wname;
    return identifier;
}
void Identifier::Purge() {
    gStringToId.clear();
}
const std::string& Identifier::GetName(Identifier identifier) {
    static std::string unknown = "unknown";
    if ((uint16_t)identifier.mId >= gIdToString.size()) return unknown;
    std::lock_guard<std::mutex> lock(gInsertMutex);
    return gIdToString[identifier.mId];
}
const std::wstring& Identifier::GetWName(Identifier identifier) {
    static std::wstring unknown = L"unknown";
    if ((uint16_t)identifier.mId >= gIdToString.size()) return unknown;
    if (identifier.mId >= gIdToWString.size()) {
        std::lock_guard<std::mutex> lock(gInsertMutex);
        gIdToWString.resize(gIdToString.size());
    }
    if (gIdToWString[identifier.mId].empty()) {
        auto nameA = GetName(identifier);
        std::lock_guard<std::mutex> lock(gInsertMutex);
        std::wstring_convert<std::codecvt_utf8<wchar_t>, wchar_t> converter;
        gIdToWString[identifier.mId] = converter.from_bytes(nameA.data());
    }
    std::lock_guard<std::mutex> lock(gInsertMutex);
    return gIdToWString[identifier.mId];
}
