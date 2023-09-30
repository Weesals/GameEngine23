#include "Resources.h"

std::map<std::string, Identifier, Identifier::comp> Identifier::gStringToId;
std::map<std::wstring, Identifier, Identifier::comp> Identifier::gWStringToId;

Identifier::Identifier(const std::string_view& name) : mId(Identifier::RequireStringId(name)) { }
Identifier::Identifier(const std::wstring_view& name) : mId(Identifier::RequireStringId(name)) { }
