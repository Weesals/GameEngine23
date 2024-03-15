#pragma once

#include <string>
#include <unordered_map>

class ShaderCache {
	struct VariantEntry {
		std::string mMacros;
		std::string mData;
	};
	struct ShaderEntry {
		size_t mModifiedDate;
		std::vector<VariantEntry> mEntries;
	};
	std::unordered_map<std::string, ShaderEntry> mShaders;
public:

	//std::string TryGetVariant

};
