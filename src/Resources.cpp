#include "Resources.h"

std::map<std::string, Identifier, Resources::comp> Resources::mStringToId;
std::map<std::wstring, Identifier, Resources::comp> Resources::mWStringToId;

Identifier::Identifier(const std::string_view& name) : mId(Resources::RequireStringId(name)) { }
Identifier::Identifier(const std::wstring_view& name) : mId(Resources::RequireStringId(name)) { }
