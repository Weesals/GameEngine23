#include  "Material.h"

const TypeCache::TypeInfo* TypeCache::Get(const std::type_info* type)
{
	auto& instance = Instance<>::instance;
	auto i = instance.mTypeCaches.find(*type);
	if (i != instance.mTypeCaches.end()) return i->second;
	return nullptr;
}

// Set the data for a value in this property set
std::span<const uint8_t> ParameterSet::SetValue(Identifier name, const void* data, int count, const TypeCache::TypeInfo& typeInfo)
{
	Item newParam = { &typeInfo, 0, count };
	auto newSize = typeInfo.mSize * count;
	auto i = mItems.find(name);
	if (i == mItems.end())
	{
		newParam.mByteOffset = (int)mData.size();
		mData.resize(mData.size() + newSize);
		mItems[name] = newParam;
	}
	else
	{
		newParam.mByteOffset = i->second.mByteOffset;
		auto oldSize = i->second.mType->mSize * i->second.mCount;
		if (newSize != oldSize) ResizeData(newParam.mByteOffset, newSize, oldSize);
		i->second = newParam;
	}
	auto begin = mData.data() + newParam.mByteOffset;
	std::memcpy(begin, data, newSize);
	return std::span<const uint8_t>(begin, newSize);
}

// Get the binary data for a value in this set
std::span<const uint8_t> ParameterSet::GetValueData(Identifier name) const
{
	auto i = mItems.find(name);
	if (i == mItems.end()) return { };
	auto size = i->second.mType->mSize * i->second.mCount;
	return std::span<const uint8_t>(mData.data() + i->second.mByteOffset, size);
}

// Resize the binary data allocated to an item, and
// move the ByteOffset of other relevant other types
void ParameterSet::ResizeData(int at, int newSize, int oldSize)
{
	int delta = newSize - oldSize;
	if (delta > 0) mData.insert(mData.begin() + at + oldSize, delta, 0);
	else mData.erase(mData.begin() + at + newSize, mData.begin() + at + oldSize);
	for (auto& item : mItems)
	{
		if (item.second.mByteOffset > at) item.second.mByteOffset += delta;
	}
}


// Set shaders bound to this material
void Material::SetVertexShader(const std::shared_ptr<Shader>& shader) { mVertexShader = shader; }
void Material::SetPixelShader(const std::shared_ptr<Shader>& shader) { mPixelShader = shader; }

// Get shaders bound to this material
const std::shared_ptr<Shader>& Material::GetVertexShader(bool inherit) const
{
	if (!inherit || mVertexShader != nullptr) return mVertexShader;
	for (auto& inherit : mInheritParameters)
	{
		const auto& result = inherit->GetVertexShader(true);
		if (result != nullptr) return result;
	}
	return mVertexShader;
}
const std::shared_ptr<Shader>& Material::GetPixelShader(bool inherit) const
{
	if (!inherit || mPixelShader != nullptr) return mPixelShader;
	for (auto& inherit : mInheritParameters)
	{
		const auto& result = inherit->GetPixelShader(true);
		if (result != nullptr) return result;
	}
	return mPixelShader;
}

// How to blend with the backbuffer
void Material::SetBlendMode(BlendMode mode) { mBlendMode = mode; }
const BlendMode& Material::GetBlendMode() const { return mBlendMode; }

// How rasterize
void Material::SetRasterMode(RasterMode mode) { mRasterMode = mode; }
const RasterMode& Material::GetRasterMode() const { return mRasterMode; }

// How to clip
void Material::SetDepthMode(DepthMode mode) { mDepthMode = mode; }
const DepthMode& Material::GetDepthMode() const { return mDepthMode; }

// Materials handle instancing
void Material::SetInstanceCount(int count) { mInstanceCount = count; }
int Material::GetInstanceCount(bool inherit) const {
	if (!inherit || mInstanceCount != 0) return mInstanceCount;
	for (auto& inherit : mInheritParameters)
	{
		const auto& result = inherit->GetInstanceCount(true);
		if (result != 0) return result;
	}
	return mInstanceCount;
}


// Get the binary data for a specific parameter
std::span<const uint8_t> Material::GetUniformBinaryData(Identifier name) const
{
	// TODO: An efficient way to cache computed values
	return IntlGetUniformBinaryData(name, this);
}
std::span<const uint8_t> Material::IntlGetUniformBinaryData(Identifier name, const Material* context) const
{
	for (auto& par : mComputedParameters)
	{
		if (par->GetName() != name) continue;
		ParameterContext pcontext(context);
		// CONST CAST! Required so that the returned data can stored
		// (until an external cache exists that it can write to)
		auto owner = const_cast<Material*>(this);
		// TODO: Cache result in top-most dependency
		return par->WriteValue(name, owner, pcontext);
	}

	// Check if the value has been set explicitly
	auto data = mParameters.GetValueData(name);
	if (!data.empty()) { return data; }

	// Check if it exists in inherited material properties
	for (auto& mat : mInheritParameters)
	{
		data = mat->IntlGetUniformBinaryData(name, context != nullptr ? context : this);
		if (!data.empty()) return data;
	}
	return data;
}

const std::shared_ptr<Texture>& Material::GetUniformTexture(Identifier name) const
{
	return *(std::shared_ptr<Texture>*)mParameters.GetValueData(name).data();
}

// Add a parent material that this material will inherit
// properties from
void Material::InheritProperties(std::shared_ptr<Material> other)
{
	mInheritParameters.push_back(other);
}
void Material::RemoveInheritance(std::shared_ptr<Material> other)
{
	auto i = std::find(mInheritParameters.begin(), mInheritParameters.end(), other);
	if (i != mInheritParameters.end()) mInheritParameters.erase(i);
}

// Returns a value that will change if anything changes in this material
// or any inherited material
// Use to determine if a value cache is still current
int Material::ComputeHeirarchicalRevisionHash() const
{
	int hash = mRevision;
	for (auto& item : mInheritParameters)
	{
		hash = 0xdeece66d * hash + item->ComputeHeirarchicalRevisionHash();
	}
	return hash;
}
