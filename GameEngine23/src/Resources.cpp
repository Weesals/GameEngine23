#include "Resources.h"

std::unordered_map<std::string, Identifier, Identifier::string_hash, std::equal_to<>> Identifier::gStringToId;
std::unordered_map<std::wstring, Identifier, Identifier::string_hash, std::equal_to<>> Identifier::gWStringToId;

Identifier::Identifier(const std::string_view& name) : mId(Identifier::RequireStringId(name)) { }
Identifier::Identifier(const std::wstring_view& name) : mId(Identifier::RequireStringId(name)) { }

const IdentifierWithName IdentifierWithName::None;
