#pragma once

#include "Material.h"
#include "Containers.h"
#include "GraphicsUtility.h"
#include "GraphicsDeviceBase.h"
#include <cassert>

// Extracts material parameters from precalculated offsets
// Must be generated from a MaterialCollector
// Used for efficient construction of constant buffers
// and determining which materials can cause cached value invalidation
class MaterialEvaluator {
	static const uint16_t InvalidSize = -1;
	std::unique_ptr<uint8_t[]> mBuffer = nullptr;
	int mBufferSize = 0;
public:
	struct Source {
		const Material* mMaterial;
	};
	struct Value {
		uint16_t mOutputOffset;	// Offset in the output data array
		uint16_t mValueOffset;	// Offset within the material
		uint8_t mDataSize;		// How big the data type is
		int8_t mSourceId;
	};
	uint16_t mValueOffset;
	uint16_t mComputedOffset;
	uint16_t mParameterOffset;
	uint16_t mDataSize = InvalidSize;
	bool IsValid() { return mDataSize != InvalidSize; }
	void RequireBuffer(int size) {
		if (size <= mBufferSize) return;
		mBuffer.reset(new uint8_t[mBufferSize = size]);
	}
	Source* GetSources() const { return (Source*)mBuffer.get(); }
	Value* GetValues() const { return (Value*)(mBuffer.get() + mValueOffset); }
	uint8_t* GetParameters() const { return mBuffer.get() + mParameterOffset; }
	std::span<Value> GetValueArray() const {
		Value* begin = (Value*)(mBuffer.get() + mValueOffset);
		Value* end = (Value*)(mBuffer.get() + mComputedOffset);
		return std::span<Value>(begin, (size_t)(end - begin));
	}
	std::span<Value> GetComputedValueArray() const {
		Value* begin = (Value*)(mBuffer.get() + mComputedOffset);
		Value* end = (Value*)(mBuffer.get() + mParameterOffset);
		return std::span<Value>(begin, (size_t)(end - begin));
	}
	void Evaluate(std::span<uint8_t> data) const {
		auto* sources = GetSources();
		assert(data.size() >= mDataSize);
		for (auto& value : GetValueArray()) {
			auto srcData = sources[value.mSourceId].mMaterial->mParameters.GetDataRaw() + value.mValueOffset;
			std::memcpy(data.data() + value.mOutputOffset, srcData, value.mDataSize);
		}
		MaterialEvaluatorContext context(*this, 0, data);
		for (auto& value : GetComputedValueArray()) {
			auto par = sources[value.mSourceId].mMaterial->mComputedParameters.data() + value.mValueOffset;
			par->second->EvaluateValue(data.subspan(value.mOutputOffset, value.mDataSize), context);
		}
	}
	std::span<uint8_t> EvaluateAppend(std::vector<uint8_t>& data, int finalSize) const {
		int begin = (int)data.size();
		data.resize(begin + mDataSize);
		std::memset(data.data(), 0, finalSize);
		Evaluate(std::span<uint8_t>(data.begin() + begin, mDataSize));
		data.resize(begin + finalSize);
		return std::span<uint8_t>(data.begin() + begin, finalSize);
	}
	void EvaluateSafe(std::span<uint8_t> data) const {
		if (mDataSize == data.size()) {
			Evaluate(data);
		}
		else {
			std::array<uint8_t, 512> tmpData;
			Evaluate(tmpData);
			std::memcpy(data.data(), tmpData.data(), data.size());
		}
	}
	static void ResolveConstantBuffer(ShaderBase::ConstantBuffer* cb, std::span<const Material*> materialStack, uint8_t* buffer) {
		for (auto& val : cb->mValues) {
			for (auto* mat : materialStack) {
				Material::ParameterContext context(materialStack);
				auto data = context.GetUniform(val.mName);
				std::memcpy(buffer + val.mOffset, data.data(), data.size());
			}
		}
	}
	static std::span<const void*> ResolveResources(CommandBuffer& cmdBuffer, const PipelineLayout* pipeline, std::span<const Material*> materialStack) {
		auto resources = cmdBuffer.RequireFrameData<const void*>(pipeline->GetResourceCount());
		ResolveResources(cmdBuffer, pipeline, materialStack, resources);
		return resources;
	}
	static void ResolveResources(CommandBuffer& cmdBuffer, const PipelineLayout* pipeline, std::span<const Material*> materialStack, std::span<const void*> outResources) {
		int r = 0;
		Material::ParameterContext context(materialStack);
		// Get constant buffer data for this batch
		for (auto* cb : pipeline->mConstantBuffers) {
			uint8_t tmpData[512];
			auto count = (int)(uint32_t)(cb->mSize + sizeof(uint64_t)) / sizeof(uint64_t);
			for (int i = 0; i < count; ++i) ((uint64_t*)tmpData)[i] = 0;
			for (auto& val : cb->mValues) {
				auto data = context.GetUniform(val.mName);
				std::memcpy(tmpData + val.mOffset, data.data(), data.size());
			}
			outResources[r++] = cmdBuffer.RequireConstantBuffer(std::span<uint8_t>(tmpData, cb->mSize));
		}
		// Get other resource data for this batch
		{
			for (auto* rb : pipeline->mResources) {
				auto data = context.GetUniform(rb->mName);
				outResources[r++] = data.empty() ? nullptr : ((std::shared_ptr<void*>*)data.data())->get();
			}
		}
	}
};

// Calculates material parameter offsets and source materials within the material stack
class MaterialCollector {
	static const uint16_t InvalidOffset = (uint16_t)(-1);
	struct Value : public MaterialEvaluator::Value {
		Identifier mName;
		int8_t mParamOffset;
		int8_t mParamCount;
	};
	typedef MaterialEvaluator::Source Source;
	HybridVector<Source, 16> mSources;
	HybridVector<Value, 32> mValues;
	HybridVector<uint8_t, 16> mParameterIds;
	InplaceVector<uint8_t> mParameterStack;
	std::vector<uint8_t> mOutputData;
	int mValueCount = 0;
	int mDataSize = 0;
public:
	void Clear() {
		mValueCount = 0;
		mSources.clear();
		mValues.clear();
		mParameterIds.clear();
		mOutputData.clear();
	}
	std::span<const uint8_t> GetUniformSource(const Material* material, Identifier name, MaterialCollectorContext& context) {
		auto* valuesData = mValues.data();
		for (int i = 0; i < mValues.size(); ++i) {
			auto& value = valuesData[i];
			if (value.mName != name) continue;
			if (!mParameterStack.empty()) mParameterIds.push_back((uint8_t)i);
			const uint8_t* srcData = value.mParamOffset >= 0
				? mOutputData.data() + value.mOutputOffset
				: mSources[value.mSourceId].mMaterial->mParameters.GetDataRaw() + value.mValueOffset;
			return std::span<const uint8_t>(srcData, value.mDataSize);
		}
		return GetUniformSourceIntl(material, name, context);
	}
	std::span<const uint8_t> GetUniformSourceNull(Identifier name, MaterialCollectorContext& context) {
		auto material = &Material::NullInstance;
		auto valueData = material->mParameters.GetValueData("NullVec");
		ObserveValue(material, name, valueData);
		return valueData;
	}

	// Ensure all values come before computed
	// Retain relative order of computed (not of value)
	void Finalize() {
		auto* valuesData = mValues.data();
		assert(mParameterStack.empty());
		int max = (int)mValues.size() - 1;
		while (max >= 0 && valuesData[max].mParamOffset >= 0) --max;
		int nxt = max;
		while (nxt >= 0) {
			while (nxt >= 0 && valuesData[nxt].mParamOffset < 0) --nxt;
			if (nxt < 0) break;
			for (auto& param : mParameterIds) {
				if (param == nxt) param = max;
				else if (param == max) param = nxt;
			}
			std::swap(valuesData[nxt], valuesData[max]);
			--max;
			--nxt;
		}
		mValueCount = max + 1;
	}

	void FinalizeAndClearOutputOffsets() {
		Finalize();
		for (auto& value : mValues) value.mOutputOffset = InvalidOffset;
	}
	void SetItemOutputOffset(Identifier name, int offset, int byteSize = -1) {
		for (auto& value : mValues) {
			if (value.mName != name) continue;
			value.mOutputOffset = offset;
			if (byteSize >= 0) value.mDataSize = byteSize;
			break;
		}
	}
	void RepairOutputOffsets(bool allowCompacting = true) {
		if (allowCompacting) {
			for (int i = (int)mValues.size() - 1; i >= mValueCount; --i) {
				auto& value = mValues[i];
				if (value.mOutputOffset == InvalidOffset) continue;
				auto poff = value.mParamOffset;
				auto pcnt = value.mParamCount;
				int bestId = -1;
				for (int p = 0; p < pcnt; ++p) {
					int parId = mParameterIds[poff + p];
					auto& other = mValues[parId];
					if (other.mOutputOffset != InvalidOffset) continue;
					if (other.mDataSize > value.mDataSize) continue;
					bestId = std::max(bestId, parId);
				}
				if (bestId >= 0)
					mValues[bestId].mOutputOffset = value.mOutputOffset;
			}
		}
		int maxAlloc = 0;
		for (auto& value : mValues) {
			if (value.mOutputOffset != InvalidOffset) maxAlloc = std::max(maxAlloc, value.mOutputOffset + value.mDataSize);
		}
		for (auto& value : mValues) {
			if (value.mOutputOffset == InvalidOffset) { value.mOutputOffset = maxAlloc; maxAlloc += value.mDataSize; }
		}
		mDataSize = maxAlloc;
	}
	size_t GenerateSourceHash() {
		size_t hash = 0;
		for (auto& source : mSources) hash += GenericHash((size_t)source.mMaterial);
		return hash;
	}
	size_t GenerateLayoutHash() {
		size_t hash = 0;
		for (auto& value : mValues) hash += GenericHash((value.mName.mId << 16) ^ value.mOutputOffset);
		return hash;
	}
	void BuildEvaluator(MaterialEvaluator& cache) {
		int dataOffset = 0;
		if (!mValues.empty()) { auto& last = mValues.back(); dataOffset = last.mOutputOffset + last.mDataSize; }
		cache.mValueOffset = (uint16_t)(sizeof(MaterialEvaluator::Source) * mSources.size());
		cache.mComputedOffset = (uint16_t)(cache.mValueOffset + sizeof(MaterialEvaluator::Value) * mValueCount);
		cache.mParameterOffset = (uint16_t)(cache.mComputedOffset + sizeof(MaterialEvaluator::Value) * (mValues.size() - mValueCount));
		int size = (int)(cache.mParameterOffset + mParameterIds.size());
		cache.RequireBuffer(size);
		cache.mDataSize = (uint16_t)mDataSize;

		std::copy(mSources.begin(), mSources.end(), cache.GetSources());
		std::copy(mParameterIds.begin(), mParameterIds.end(), cache.GetParameters());
		auto destValues = cache.GetValues();
		for (auto& value : mValues) *(destValues++) = value;
		mSources.clear();
		mValues.clear();
		mParameterIds.clear();
		Clear();
	}
private:
	// At this point, the parameter definitely does not yet exist in our list
	std::span<const uint8_t> GetUniformSourceIntl(const Material* material, Identifier name, MaterialCollectorContext& context) {
		auto par = std::partition_point(material->mComputedParameters.begin(), material->mComputedParameters.end(),
			[=](const auto& kv) { return kv.first < name; });
		if (par != material->mComputedParameters.end() && par->first == name) {
			BeginComputed(material, name);
			Material::ComputedParameterBase* parameter = par->second.get();
			auto outData = ConsumeTempData(parameter->GetDataSize());
			parameter->SourceValue(outData, context);
			EndComputed(material, par, outData);
			return outData;
		}
		// Check if the value has been set explicitly
		auto data = material->mParameters.GetValueData(name);
		if (!data.empty()) {
			ObserveValue(material, name);
			return data;
		}
		// Check if it exists in inherited material properties
		for (auto& mat : material->mInheritParameters) {
			data = GetUniformSourceIntl(mat.get(), name, context);
			if (!data.empty()) return data;
		}
		return data;
	}
	int RequireSource(const Material* material) {
		for (int i = 0; i < (int)mSources.size(); ++i) if (mSources[i].mMaterial == material) return i;
		//if (mSources.capacity() < 4) mSources.reserve(4);
		int id = (int)mSources.size();
		mSources.emplace_back(Source{ .mMaterial = material });
		return id;
	}
	void ObserveValue(const Material* material, Identifier name) {
		//if (mValues.capacity() < 8) mValues.reserve(8);
		auto valueData = material->mParameters.GetValueData(name);
		ObserveValue(material, name, valueData);
	}
	void ObserveValue(const Material* material, Identifier name, std::span<const uint8_t> valueData) {
		Value v;
		v.mOutputOffset = InvalidOffset;
		v.mValueOffset = (uint16_t)(valueData.data() - material->mParameters.GetDataRaw());
		v.mDataSize = (uint8_t)(valueData.size());
		v.mSourceId = RequireSource(material);
		v.mName = name;
		v.mParamOffset = -1;
		v.mParamCount = -1;
		mValues.emplace_back(v);
		if (!mParameterStack.empty()) mParameterIds.push_back((uint8_t)(mValues.size() - 1));
	}
	std::span<uint8_t> ConsumeTempData(int dataSize) {
		mOutputData.resize(mOutputData.size() + dataSize);
		return std::span<uint8_t>(mOutputData.end() - dataSize, mOutputData.end());
	}
	void BeginComputed(const Material* material, Identifier name) {
		//if (mParameterIds.capacity() < 8) mParameterIds.reserve(8);
		mParameterStack.push_back((uint8_t)mParameterIds.size());
	}
	void EndComputed(const Material* material, Material::ComputedParameterCollection::const_iterator parameter, std::span<uint8_t> valueData) {
		int from = mParameterStack.pop_back();
		Value v;
		v.mOutputOffset = (uint8_t)(valueData.data() - mOutputData.data());
		v.mValueOffset = (uint16_t)(&*parameter - material->mComputedParameters.data());
		v.mDataSize = parameter->second->GetDataSize();
		v.mSourceId = RequireSource(material);
		v.mName = parameter->first;
		v.mParamOffset = from;
		v.mParamCount = (int)(mParameterIds.size() - from);
		mValues.emplace_back(v);
		if (!mParameterStack.empty()) mParameterIds.push_back((uint8_t)(mValues.size() - 1));
	}
};
