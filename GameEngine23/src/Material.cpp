#include  "Material.h"

#include "MaterialEvaluator.h"
#include "GraphicsDeviceBase.h"

const TypeCache::TypeInfo* TypeCache::Get(const std::type_info* type)
{
	auto& instance = Instance<>::instance;
	auto i = instance.mTypeCaches.find(*type);
	if (i != instance.mTypeCaches.end()) return i->second;
	return nullptr;
}

ParameterSet::~ParameterSet()
{
	for (auto item : mItems)
	{
		// TODO: Should items be addref/deref?
		/*if (item.second.mType == &TypeCache::Require<std::shared_ptr<Texture>>())
		{
			auto data = std::span<const uint8_t>(mData.data() + item.second.mByteOffset, item.second.mType->mSize);
			std::shared_ptr<Texture>& ptr = *(std::shared_ptr<Texture>*)&data;
			ptr.~shared_ptr();
		}*/
	}
}

// Set the data for a value in this property set
std::span<const uint8_t> ParameterSet::SetValue(Identifier name, const void* data, int count, const TypeCache::TypeInfo& typeInfo)
{
	Item newParam = { &typeInfo, 0, count };
	auto newSize = typeInfo.mSize * count;
	auto i = mItems.find(name);
	if (i == mItems.end()) {
		newParam.mByteOffset = (int)mData.size();
		mData.resize(mData.size() + newSize);
		mItems[name] = newParam;
	} else {
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
const TypeCache::TypeInfo* ParameterSet::GetValueType(Identifier name) const
{
	auto i = mItems.find(name);
	if (i == mItems.end()) return { };
	return i->second.mType;
}
int ParameterSet::GetItemIdentifiers(Identifier* outlist, int capacity) const {
	int count = 0;
	for (auto& item : mItems) {
		if (count > capacity) break;
		outlist[count] = item.first;
		++count;
	}
	return count;
}
const uint8_t* ParameterSet::GetDataRaw() const {
	return mData.data();
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


void* MaterialEvaluatorContext::GetAndIterateParameter(Identifier name)
{
	auto parId = mCache.GetParameters()[mIterator++];
	return &mOutput[mCache.GetValues()[parId].mOutputOffset];
}

std::span<const uint8_t> MaterialCollectorContext::GetUniformSource(Identifier name, MaterialCollectorContext& context) const {
	std::span<const uint8_t> data;
	for (auto* mat : mMaterials) {
		data = mCollector.GetUniformSource(mat, name, context);
		if (!data.empty()) return data;
	}
	return mCollector.GetUniformSourceNull(name, context);
}


// Set shaders bound to this material
void Material::SetVertexShader(const std::shared_ptr<Shader>& shader) { mVertexShader = shader; }
void Material::SetPixelShader(const std::shared_ptr<Shader>& shader) { mPixelShader = shader; }
void Material::SetRenderPassOverride(const IdentifierWithName& pass) { mRenderPassOverride = pass; }
const IdentifierWithName& Material::GetRenderPassOverride() const {
	return mRenderPassOverride;
}

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
void Material::SetBlendMode(BlendMode mode) { mMaterialState.mBlendMode = mode; }
const BlendMode& Material::GetBlendMode() const { return mMaterialState.mBlendMode; }

// How rasterize
void Material::SetRasterMode(RasterMode mode) { mMaterialState.mRasterMode = mode; }
const RasterMode& Material::GetRasterMode() const { return mMaterialState.mRasterMode; }

// How to clip
void Material::SetDepthMode(DepthMode mode) { mMaterialState.mDepthMode = mode; }
const DepthMode& Material::GetDepthMode() const { return mMaterialState.mDepthMode; }

const MaterialState& Material::GetMaterialState() const { return mMaterialState; }

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
	const Material* self = this;
	ParameterContext context(std::span<const Material*>(&self, 1));
	return GetUniformBinaryData(name, context);
}
std::span<const uint8_t> Material::GetUniformBinaryData(Identifier name, ParameterContext& context) const
{
	auto par = FindComputed(name);
	if (par != nullptr)
	{
		// CONST CAST! Required so that the returned data can stored
		// (until an external cache exists that it can write to)
		auto owner = const_cast<Material*>(this);
		// TODO: Cache result in top-most dependency
		return par->WriteValue(name, owner, context);
	}

	// Check if the value has been set explicitly
	auto data = mParameters.GetValueData(name);
	if (!data.empty()) { return data; }

	// Check if it exists in inherited material properties
	for (auto& mat : mInheritParameters)
	{
		data = mat->GetUniformBinaryData(name, context);
		if (!data.empty()) return data;
	}
	return data;
}

const std::shared_ptr<TextureBase>* Material::GetUniformTexture(Identifier name) const
{
	auto data = mParameters.GetValueData(name);
	if (data.empty()) return nullptr;
	return (std::shared_ptr<TextureBase>*)data.data();
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

void Material::ResolveResources(CommandBuffer& cmdBuffer, std::vector<const void*>& resources, const PipelineLayout* pipeline) const
{
	// Get constant buffer data for this batch
	for (auto* cb : pipeline->mConstantBuffers) {
		uint8_t tmpData[512];
		for (auto& val : cb->GetValues()) {
			auto data = GetUniformBinaryData(val.mName);
			std::memcpy(tmpData + val.mOffset, data.data(), data.size());
		}
		resources.push_back(cmdBuffer.RequireConstantBuffer(std::span<uint8_t>(tmpData, cb->mSize)));
	}
	// Get other resource data for this batch
	{
		for (auto* rb : pipeline->mResources) {
			auto* data = GetUniformTexture(rb->mName);
			resources.push_back(data == nullptr ? nullptr : data->get());
		}
	}
}

Material MakeNullMaterial() {
	Material mat;
	mat.SetUniform("NullMat", Matrix::Identity);
	mat.SetUniform("NullVec", Vector4::Zero);
	return mat;
}
Material Material::NullInstance = MakeNullMaterial();

RootMaterial::RootMaterial()
	: Material()
{
	InitialiseDefaults();
}
RootMaterial::RootMaterial(const std::wstring& shaderPath)
	: Material(shaderPath)
{
	InitialiseDefaults();
}
RootMaterial::RootMaterial(const std::shared_ptr<Shader>& vertexShader, const std::shared_ptr<Shader>& pixelShader)
	: Material(vertexShader, pixelShader)
{
	InitialiseDefaults();
}


void RootMaterial::InitialiseDefaults() {
	static Identifier iMMat = "Model";
	static Identifier iVMat = "View";
	static Identifier iPMat = "Projection";
	static Identifier iMVMat = "ModelView";
	static Identifier iMVPMat = "ModelViewProjection";
	static Identifier iLightDir = "_WorldSpaceLightDir0";
	SetUniform("Model", Matrix::Identity);
	SetView(Matrix::CreateLookAt(Vector3(0, 5, -10), Vector3(0, 0, 0), Vector3(0, 1, 0)));
	SetProjection(Matrix::CreatePerspectiveFieldOfView(1.0f, 1.0f, 1.0f, 500.0f));
	SetComputedUniform<Matrix>("ModelView", [=](auto& context) {
        auto m = context.GetUniform<Matrix>(iMMat);
        auto v = context.GetUniform<Matrix>(iVMat);
        return (m * v);
    });
    SetComputedUniform<Matrix>("ViewProjection", [=](auto& context) {
        auto v = context.GetUniform<Matrix>(iVMat);
        auto p = context.GetUniform<Matrix>(iPMat);
        return (v * p);
    });
    SetComputedUniform<Matrix>("ModelViewProjection", [=](auto& context) {
        auto mv = context.GetUniform<Matrix>(iMVMat);
        auto p = context.GetUniform<Matrix>(iPMat);
        return (mv * p);
    });
    SetComputedUniform<Matrix>("InvModelViewProjection", [=](auto& context) {
        auto mvp = context.GetUniform<Matrix>(iMVPMat);
        return mvp.Invert();
    });
    SetComputedUniform<Vector3>("_ViewSpaceLightDir0", [=](auto& context) {
        auto lightDir = context.GetUniform<Vector3>(iLightDir);
        auto view = context.GetUniform<Matrix>(iVMat);
        return Vector3::TransformNormal(lightDir, view);
    });
    SetComputedUniform<Vector3>("_ViewSpaceUpVector", [=](auto& context) {
        return context.GetUniform<Matrix>(iVMat).Up();
    });
}
void RootMaterial::SetResolution(Vector2 res) {
	static Identifier iRes = "Resolution";
	SetUniform(iRes, res);
}
void RootMaterial::SetView(const Matrix& view) {
	static Identifier iView = "View";
	SetUniform(iView, view);
}
void RootMaterial::SetProjection(const Matrix& proj) {
	static Identifier iProj = "Projection";
	SetUniform(iProj, proj);
}
