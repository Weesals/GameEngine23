#pragma once

#include "Shader.h"
#include "MathTypes.h"
#include "Texture.h"
#include "Resources.h"
#include <typeindex>
#include <typeinfo>
#include <span>
#include <memory>

// Utility class for determining the size of types dynamically
class TypeCache
{
public:
	struct TypeInfo
	{
		const std::type_info* mType;
		int mSize;
	};

private:
	template<class T = int>
	struct Single { static TypeInfo info; };
	template<class T = int>
	struct Instance { static TypeCache instance; };

public:
	std::unordered_map<std::type_index, TypeInfo*> mTypeCaches;
	template<class T>
	static const TypeInfo& Require()
	{
		if (Single<T>::info.mType == nullptr)
		{
			Single<T>::info = { &typeid(T), sizeof(T) };
			auto& instance = Instance<>::instance;
			//auto i = instance.mTypeCaches.find(typeid(T));
			//if (i != instance.mTypeCaches.end()) return i->second;
			instance.mTypeCaches[typeid(T)] = &Single<T>::info;
		}
		return Single<T>::info;
	}
	static const TypeInfo* Get(const std::type_info* type);
};
template<class T> TypeCache::TypeInfo TypeCache::Single<T>::info;
template<class T> TypeCache TypeCache::Instance<T>::instance;


// A set of uniform values set on a material
class ParameterSet
{
	struct Item
	{
		const TypeCache::TypeInfo* mType;
		int mByteOffset;
		int mCount;
	};
	std::unordered_map<Identifier, Item> mItems;
	std::vector<uint8_t> mData;

public:
	// Set the data for a value in this property set
	template<class T>
	std::span<const uint8_t> SetValue(Identifier name, const T* data, int count)
	{
		static_assert(std::is_same<int, T>::value || std::is_same<float, T>::value
			|| std::is_same<std::shared_ptr<Texture>, T>::value,
			"Types must be int or float based");

		return SetValue(name, data, count, TypeCache::Require<T>());
	}
	std::span<const uint8_t> SetValue(Identifier name, const void* data, int count, const TypeCache::TypeInfo& typeInfo);
	// Get the binary data for a value in this set
	std::span<const uint8_t> GetValueData(Identifier name) const;
private:
	// Resize the binary data allocated to an item, and
	// move the ByteOffset of other relevant other types
	void ResizeData(int at, int newSize, int oldSize);
};

struct BlendMode
{
	enum BlendArg : uint8_t { Zero, One, SrcColor, SrcInvColor, SrcAlpha, SrcInvAlpha, DestColor, DestInvColor, DestAlpha, DestInvAlpha, };
	enum BlendOp : uint8_t { Add, Sub, RevSub, Min, Max, };
	BlendArg mSrcAlphaBlend = BlendArg::One;
	BlendArg mDestAlphaBlend = BlendArg::Zero;
	BlendArg mSrcColorBlend = BlendArg::One;
	BlendArg mDestColorBlend = BlendArg::Zero;
	BlendOp mBlendAlphaOp = BlendOp::Add;
	BlendOp mBlendColorOp = BlendOp::Add;
	static BlendMode Opaque() { return { One, Zero, One, Zero, Add, Add, }; }
	static BlendMode AlphaBlend() { return { SrcAlpha, SrcInvAlpha, SrcAlpha, SrcInvAlpha, Add, Add, }; }
};
struct RasterMode
{
	enum CullModes : uint8_t { None = 1, Front = 2, Back = 3, };
	CullModes mCullMode;
	RasterMode() : mCullMode(CullModes::Back) { }
	RasterMode& SetCull(CullModes mode) { mCullMode = mode; return *this; }
	static RasterMode MakeDefault() { return RasterMode(); }
};
struct DepthMode
{
	enum Comparisons : uint8_t { Never = 1, Less, Equal, LEqual, Greater, NEqual, GEqual, Always, };
	Comparisons mComparison;
	bool mWriteEnable;
	DepthMode(Comparisons c = Comparisons::Less, bool write = true) : mComparison(c), mWriteEnable(write) { }
	static DepthMode MakeOff() { return DepthMode(Comparisons::Always, false);}
};

// Stores a binding of shaders and uniform parameter values
class Material
{
public:
	// Used to compute computed parameters
	class ParameterContext
	{
		const Material* mMaterial;
		int mMinDepth;
	public:
		ParameterContext(const Material* mat) : mMaterial(mat), mMinDepth(0){ }
		int GetMinDepth() const { return mMinDepth; }
		template<class T>
		T& GetUniform(Identifier name) {
			return *(T*)mMaterial->GetUniformBinaryData(name).data();
		}
	};

private:
	// A parameter that is calculated based on other parameters
	class ComputedParameterBase
	{
	protected:
		Identifier mName;
		ComputedParameterBase(Identifier name) : mName(name) { }
	public:
		virtual ~ComputedParameterBase() { }
		Identifier GetName() const { return mName; }
		virtual std::span<const uint8_t> WriteValue(Identifier name, Material* dest, ParameterContext& context) const = 0;
	};
	// The typed version of the above class
	template<class T>
	class ComputedParameter : public ComputedParameterBase
	{
		std::function<T(ParameterContext&)> mComputer;
	public:
		ComputedParameter(Identifier name, const std::function<T(ParameterContext&)>& computer)
			: ComputedParameterBase(name), mComputer(computer) { }
		void OverwriteComputer(std::function<T(ParameterContext&)> computer) { mComputer = computer; }
		std::span<const uint8_t> WriteValue(Identifier name, Material* dest, ParameterContext& context) const override
		{
			auto value = mComputer(context);
			return dest->SetUniformNoNotify(name, value);
		}
	};
	struct ComputeCacheItem {
		Identifier mIdentifier;
		int mRevisionHash;
	};

	// Shaders bound
	std::shared_ptr<Shader> mVertexShader;
	std::shared_ptr<Shader> mPixelShader;

	// How to blend/raster/clip
	BlendMode mBlendMode;
	RasterMode mRasterMode;
	DepthMode mDepthMode;

	// Parameters to be set
	ParameterSet mParameters;

	// Per-instance data is all contained within the material
	// so why not the instance count itself?
	int mInstanceCount;

	// Parameters (and eventually shaders?) are inherited from parent materials
	std::vector<std::shared_ptr<Material>> mInheritParameters;

	// These parameters can automatically compute themselves
	std::vector<ComputedParameterBase*> mComputedParameters;

	// Incremented whenever data within this material changes
	int mRevision;

	// Utility functions to unpack floats/ints from complex types
	template<typename D> void Unpack(int v, D&& del) { del(&v, 1); }
	template<typename D> void Unpack(float v, D&& del) { del(&v, 1); }
	template<typename D> void Unpack(const Vector2& v, D&& del) { del(&v.x, 2); }
	template<typename D> void Unpack(const Vector3& v, D&& del) { del(&v.x, 3); }
	template<typename D> void Unpack(const Vector4& v, D&& del) { del(&v.x, 4); }
	template<typename D> void Unpack(const Color& v, D&& del) { del(&v.x, 4); }
	template<typename D> void Unpack(const Matrix& m, D&& del) { del(m.m[0], 16); }
	template<typename V, typename D>
	void Unpack(const std::span<const V> v, D&& del)
	{
		Unpack(*v.begin(), [&]<typename F>(const F * data, int count) {
			del(data, count * (int)v.size());
		});
	}
	template<typename V, typename D>
	void Unpack(const std::vector<V>& v, D&& del) { Unpack(std::span<const V>(v.begin(), v.end()), del); }

	// Set a uniform value, without marking this material as changed
	// (used by computed parameters, as they dont logically change anything)
	template<typename T>
	std::span<const uint8_t> SetUniformNoNotify(Identifier name, T v) {
		std::span<const uint8_t> r;
		Unpack(v, [&]<typename F>(const F * data, int count) {
			r = mParameters.SetValue<F>(name, data, count);
		});
		return r;
	}

	// Whenever a change is made that requires this material to be re-uploaded
	// (or computed parameters to recompute)
	void MarkChanged()
	{
		mRevision++;
	}

public:

	Material() : Material(nullptr, nullptr) { }
	Material(const std::shared_ptr<Shader>& vertexShader, const std::shared_ptr<Shader>& pixelShader)
		: mVertexShader(vertexShader), mPixelShader(pixelShader), mInstanceCount(0), mRevision(0)
	{ }

	// Set shaders bound to this material
	void SetVertexShader(const std::shared_ptr<Shader>& shader);
	void SetPixelShader(const std::shared_ptr<Shader>& shader);

	// Get shaders bound to this material
	const std::shared_ptr<Shader>& GetVertexShader(bool inherit = true) const;
	const std::shared_ptr<Shader>& GetPixelShader(bool inherit = true) const;

	// How to blend with the backbuffer
	void SetBlendMode(BlendMode mode);
	const BlendMode& GetBlendMode() const;

	// How rasterize
	void SetRasterMode(RasterMode mode);
	const RasterMode& GetRasterMode() const;

	// How to clip
	void SetDepthMode(DepthMode mode);
	const DepthMode& GetDepthMode() const;

	// Materials handle instancing
	void SetInstanceCount(int count);
	int GetInstanceCount(bool inherit = true) const;

	// Set various uniform values
	template<typename T>
	std::span<const uint8_t> SetUniform(Identifier name, T v) {
		auto r = SetUniformNoNotify(name, v);
		MarkChanged();
		return r;
	}
	std::span<const uint8_t> SetUniform(Identifier name, const std::shared_ptr<Texture>& tex) {
		auto r = mParameters.SetValue<std::shared_ptr<Texture>>(name, &tex, 1);
		MarkChanged();
		return r;
	}
	template<typename Container>
	std::span<const uint8_t> SetInstancedUniform(Identifier name, const Container& v) {
		std::span<const typename Container::value_type> span(v.begin(), v.end());
		auto r = SetUniformNoNotify(name, span);
		MarkChanged();
		return r;
	}

	// These uniforms are computed based on other parameters
	// TODO: Once a matching computed parameter is found, the execution context
	// should still be the child material, not the parent.
	// TODO: Results should be cached in some way, probably based on a hash of
	// mRevision of all materials involved
	template<class T>
	void SetComputedUniform(Identifier name, const std::function<T(ParameterContext)>& lambda)
	{
		auto i = std::find_if(mComputedParameters.begin(), mComputedParameters.end(), [=](auto item) { return item->GetName() == name; });
		if (i != mComputedParameters.end())
			dynamic_cast<ComputedParameter<T>*>(*i)->OverwriteComputer(lambda);
		else
			mComputedParameters.push_back(new ComputedParameter<T>(name, lambda));
	}

	// Get the binary data for a specific parameter
	std::span<const uint8_t> GetUniformBinaryData(Identifier name) const;
	std::span<const uint8_t> IntlGetUniformBinaryData(Identifier name, const Material* context) const;

	const std::shared_ptr<Texture>& GetUniformTexture(Identifier name) const;

	// Add a parent material that this material will inherit
	// properties from
	void InheritProperties(std::shared_ptr<Material> other);
	void RemoveInheritance(std::shared_ptr<Material> other);

	// Returns a value that will change if anything changes in this material
	// or any inherited material
	// Use to determine if a value cache is still current
	int ComputeHeirarchicalRevisionHash() const;

};

