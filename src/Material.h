#pragma once

#include "Shader.h"
#include "Math.h"
#include "Resources.h"
#include <typeindex>
#include <typeinfo>
#include <span>

// Utility class for determining the size of types dynamically
class TypeCache {
public:
	struct TypeInfo {
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
	static const TypeInfo& Require() {
		if (Single<T>::info.mType == nullptr) {
			Single<T>::info = { &typeid(T), sizeof(T) };
			auto& instance = Instance<>::instance;
			//auto i = instance.mTypeCaches.find(typeid(T));
			//if (i != instance.mTypeCaches.end()) return i->second;
			instance.mTypeCaches[typeid(T)] = &Single<T>::info;
		}
		return Single<T>::info;
	}
	static const TypeInfo& Get(const std::type_info* type) {
		auto& instance = Instance<>::instance;
		auto i = instance.mTypeCaches.find(*type);
		if (i != instance.mTypeCaches.end()) return *i->second;
		return { type, -1 };
	}
};
template<class T> TypeCache::TypeInfo TypeCache::Single<T>::info;
template<class T> TypeCache TypeCache::Instance<T>::instance;

// A set of uniform values set on a material
class ParameterSet {
	struct Item {
		const TypeCache::TypeInfo* mType;
		int mByteOffset;
		int mCount;
	};
	std::unordered_map<Identifier, Item> mItems;
	std::vector<unsigned char> mData;

public:
	// Set the data for a value in this property set
	template<class T>
	std::span<const unsigned char> SetValue(Identifier name, const T* data, int count) {
		static_assert(std::is_same<int, T>::value || std::is_same<float, T>::value,
			"Types must be int or float based");

		auto& typeCache = TypeCache::Require<T>();
		Item newParam = { &typeCache, 0, count};
		auto newSize = typeCache.mSize * count;
		auto i = mItems.find(name);
		if (i == mItems.end()) {
			newParam.mByteOffset = (int)mData.size();
			mData.resize(mData.size() + newSize);
			mItems[name] = newParam;
		}
		else {
			newParam.mByteOffset = i->second.mByteOffset;
			auto oldSize = i->second.mType->mSize * i->second.mCount;
			if (newSize != oldSize) ResizeData(newParam.mByteOffset, newSize, oldSize);
			i->second = newParam;
		}
		auto begin = mData.data() + newParam.mByteOffset;
		std::memcpy(begin, data, newSize);
		return std::span<const unsigned char>(begin, newSize);
	}
	// Get the binary data for a value in this set
	std::span<const unsigned char> GetValueData(Identifier name) const {
		auto i = mItems.find(name);
		if (i == mItems.end()) return std::span<const unsigned char>();
		auto size = i->second.mType->mSize * i->second.mCount;
		return std::span<const unsigned char>(mData.data() + i->second.mByteOffset, size);
	}
private:
	// Resize the binary data allocated to an item, and
	// move the ByteOffset of other relevant other types
	void ResizeData(int at, int newSize, int oldSize) {
		throw "Not yet implemented";
	}
};

// Stores a binding of shaders and uniform parameter values
class Material {
public:
	// Used to compute computed parameters
	class ParameterContext
	{
		const Material* mMaterial;
	public:
		ParameterContext(const Material* mat) : mMaterial(mat) { }
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
		virtual std::span<const unsigned char> WriteValue(Material* dest, ParameterContext& context) const = 0;
	};
	// The typed version of the above class
	template<class T>
	class ComputedParameter : public ComputedParameterBase
	{
		std::function<T(ParameterContext)> mComputer;
	public:
		ComputedParameter(Identifier name, const std::function<T(ParameterContext)>& computer)
			: ComputedParameterBase(name), mComputer(computer) { }
		void OverwriteComputer(std::function<T(ParameterContext)> computer) { mComputer = computer; }
		std::span<const unsigned char> WriteValue(Material* dest, ParameterContext& context) const override
		{
			auto value = mComputer(context);
			return dest->SetUniform(mName, value);
		}
	};

	// Shaders bound
	Shader mVertexShader;
	Shader mPixelShader;

	// Parameters to be set
	ParameterSet mParameters;

	// Parameters (and eventually shaders?) are inherited from parent materials
	std::vector<std::shared_ptr<Material>> mIneritParameters;

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
	template<typename D> void Unpack(const Matrix& m, D&& del) { del(m.m[0], 16); }

public:

	Material() : mVertexShader(L""), mPixelShader(L""), mRevision(0) { }
	Material(Shader vertexShader, Shader pixelShader)
		: mVertexShader(vertexShader), mPixelShader(pixelShader), mRevision(0)
	{ }

	// Set shaders bound to this material
	void SetVertexShader(const Shader& shader) { mVertexShader = shader; }
	void SetPixelShader(const Shader& shader) { mPixelShader = shader; }

	// Get shaders bound to this material
	const Shader& GetVertexShader() const { return mVertexShader; }
	const Shader& GetPixelShader() const { return mPixelShader; }

	// Set various uniform values
	template<typename T>
	std::span<const unsigned char> SetUniform(Identifier name, T v) {
		std::span<const unsigned char> r;
		Unpack(v, [&]<typename F>(const F* data, int count) {
			r = mParameters.SetValue<F>(name, data, count);
		});
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
	std::span<const unsigned char> GetUniformBinaryData(Identifier name, const Material* context = nullptr) const {
		for (auto& par : mComputedParameters) {
			if (par->GetName() == name) {
				ParameterContext pcontext(context);
				// CONST CAST! Required so that the returned data can stored
				// (until an external cache exists that it can write to)
				return par->WriteValue(const_cast<Material*>(this), pcontext);
			}
		}

		auto data = mParameters.GetValueData(name);
		if (!data.empty()) return data;
		for (auto& mat : mIneritParameters)
		{
			data = mat->GetUniformBinaryData(name, context != nullptr ? context : this);
			if (!data.empty()) return data;
		}
		return data;
	}

	// Add a parent material that this material will inherit
	// properties from
	void InheritProperties(std::shared_ptr<Material> other)
	{
		mIneritParameters.push_back(other);
	}

};

