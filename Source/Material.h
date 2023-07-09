#pragma once

#include "Shader.h"
#include "Math.h"
#include <typeindex>
#include <typeinfo>
#include <span>

// Utility class for determining the size of types dynamically
class TypeCache {
	template<class T = int>
	struct Instance { static TypeCache instance; };

public:
	struct TypeInfo {
		const std::type_info* mType;
		int mSize;
	};
	std::unordered_map<std::type_index, TypeInfo> mTypeCaches;
	template<class T>
	static TypeInfo Require() {
		auto& instance = Instance<>::instance;
		auto i = instance.mTypeCaches.find(typeid(T));
		if (i != instance.mTypeCaches.end()) return i->second;
		TypeInfo cache = { &typeid(T), sizeof(T) };
		instance.mTypeCaches[typeid(T)] = cache;
		return cache;
	}
	static TypeInfo Get(const std::type_info* type) {
		auto& instance = Instance<>::instance;
		auto i = instance.mTypeCaches.find(*type);
		if (i != instance.mTypeCaches.end()) return i->second;
		return { type, -1 };
	}
};
template<class T>
TypeCache TypeCache::Instance<T>::instance;

// A set of uniform values set on a material
class ParameterSet {
	struct Item {
		const std::type_info* mType;
		int mByteOffset;
		int mCount;
	};
	std::unordered_map<std::string, Item> mItems;
	std::vector<byte> mData;

public:
	// Set the data for a value in this property set
	template<class T>
	void SetValue(const std::string& name, T* data, int count) {
		static_assert(std::is_same<int, T>::value || std::is_same<float, T>::value,
			"Types must be int or float based");

		Item newParam = { &typeid(T), 0, count};
		auto newSize = TypeCache::Require<T>().mSize * count;
		auto i = mItems.find(name);
		if (i == mItems.end()) {
			newParam.mByteOffset = (int)mData.size();
			mData.resize(mData.size() + newSize);
			mItems[name] = newParam;
		}
		else {
			newParam.mByteOffset = i->second.mByteOffset;
			auto oldSize = TypeCache::Get(i->second.mType).mSize * i->second.mCount;
			if (newSize != oldSize) ResizeData(newParam.mByteOffset, newSize, oldSize);
			i->second = newParam;
		}
		std::memcpy(mData.data() + newParam.mByteOffset, data, newSize);
	}
	// Get the binary data for a value in this set
	std::span<const byte> GetValueData(const std::string& name) const {
		auto i = mItems.find(name);
		if (i == mItems.end()) return std::span<const byte>();
		auto size = TypeCache::Get(i->second.mType).mSize * i->second.mCount;
		return std::span<const byte>(mData.data() + i->second.mByteOffset, size);
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
private:
	// Shaders bound
	Shader mVertexShader;
	Shader mPixelShader;

	// Parameters to be set
	ParameterSet mParameters;

	// Parameters (and eventually shaders?) are inherited from parent materials
	std::vector<std::shared_ptr<Material>> mInerhitParameters;

public:
	Material() : mVertexShader(L""), mPixelShader(L"") { }
	Material(Shader vertexShader, Shader pixelShader)
		: mVertexShader(vertexShader), mPixelShader(pixelShader)
	{ }

	// Set shaders bound to this material
	void SetVertexShader(const Shader& shader) { mVertexShader = shader; }
	void SetPixelShader(const Shader& shader) { mPixelShader = shader; }

	// Get shaders bound to this material
	Shader GetVertexShader() const { return mVertexShader; }
	Shader GetPixelShader() const { return mPixelShader; }

	// Set various uniform values
	void SetUniform(const std::string& name, float v) {
		mParameters.SetValue(name, &v, 1);
	}
	void SetUniform(const std::string& name, Vector2 vec) {
		mParameters.SetValue(name, &vec.x, 2);
	}
	void SetUniform(const std::string& name, Vector3 vec) {
		mParameters.SetValue(name, &vec.x, 3);
	}
	void SetUniform(const std::string& name, Vector4 vec) {
		mParameters.SetValue(name, &vec.x, 4);
	}
	void SetUniform(const std::string& name, Matrix mat) {
		mParameters.SetValue(name, mat.m[0], 16);
	}
	void SetUniform(const std::string& name, int i) {
		mParameters.SetValue(name, &i, 1);
	}

	// Get the binary data for a specific parameter
	std::span<const byte> GetUniformBinaryData(const std::string& name) const {
		auto data = mParameters.GetValueData(name);
		if (!data.empty()) return data;
		for (auto mat : mInerhitParameters)
		{
			data = mat->GetUniformBinaryData(name);
			if (!data.empty()) return data;
		}
		return data;
	}

	// Add a parent material that this material will inherit
	// properties from
	void InheritProperties(std::shared_ptr<Material> other)
	{
		mInerhitParameters.push_back(other);
	}

};
