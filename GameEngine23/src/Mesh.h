#pragma once

#include <span>
#include <vector>
#include <memory>
#include <array>

#include "Material.h"
#include "Buffer.h"

// Store data related to drawing a mesh
class Mesh
{
	// TODO: Attributes should be dynamic and packed
	// into a single storage container
	//std::vector<Vector3> mPositionData;
	//std::vector<Vector3> mNormalData;
	//std::vector<Vector2> mUVData;
	//std::vector<ColorB4> mColors;

	// TODO: Indicies should also get packed, so that
	// they can be 32 or 16 bit
	//std::vector<int> mIndices;

	// Material used to render this mesh
	// TODO: Some meshes are multi-material
	std::shared_ptr<Material> mMaterial;

	// Is incremented whenever mesh data is changed
	// (so that graphics buffers can be updated accordingly)
	int mRevision;

	BoundingBox mBoundingBox;

	int8_t mVertexPositionId;
	int8_t mVertexNormalId;
	int8_t mVertexColorId;
	std::array<int8_t, 8> mVertexTexCoordId;
	mutable BufferLayoutPersistent mVertexBinds;
	mutable BufferLayoutPersistent mIndexBinds;

	std::string mName;

	int CreateVertexBind(int8_t& id, const char* name, BufferFormat fmt) {
		assert(id == -1);
		auto type = BufferFormatType::GetType(fmt);
		auto bsize = type.GetByteSize();
		id = mVertexBinds.AppendElement(BufferLayout::Element(name, fmt, bsize, bsize, nullptr));
		mVertexBinds.mBuffer.mSize = -1;
		RequireVertexAlloc(id);
		return id;
	}
	void RequireVertexAlloc(int8_t id) {
		auto& vbind = mVertexBinds.mElements[id];
		if (vbind.mData == nullptr) {
			int size = vbind.mBufferStride * GetVertexCount();
			if (size > 0) vbind.mData = malloc(size);
		}
	}
	void Realloc(BufferLayout::Element& el, int count) {
		int size = el.mBufferStride * count;
		if (size == 0) return;
		auto* newData = realloc(el.mData, size);
		if (newData == nullptr && el.mData != nullptr) free(el.mData);
		el.mData = newData;
	}

	void RequireVertexElementFormat(int8_t& elId, BufferFormat fmt, const char* name) {
		if (elId == -1) { CreateVertexBind(elId, name, fmt); return; }
		auto& el = mVertexBinds.mElements[elId];
		if (el.mFormat == fmt) return;
		el.mFormat = fmt;
		el.mItemSize = el.mBufferStride = BufferFormatType::GetType(el.mFormat).GetByteSize();
		if (el.mData != nullptr) Realloc(el, GetVertexCount());
		mVertexBinds.mBuffer.mSize = -1;
	}

public:
	Mesh(const std::string& name)
		: mRevision(0), mName(name),
		mVertexBinds(BufferLayoutPersistent((size_t)this, 0, BufferLayout::Usage::Vertex, 0, 4)),
		mIndexBinds(BufferLayoutPersistent((size_t)this + 1, 0, BufferLayout::Usage::Index, 0, 1)),
		mVertexPositionId(0),
		mVertexNormalId(-1),
		mVertexColorId(-1)
		//mVertexTexCoordId(-1)
	{
		for (auto i = mVertexTexCoordId.begin(); i != mVertexTexCoordId.end(); ++i) *i = -1;
		mVertexPositionId = mVertexBinds.AppendElement(BufferLayout::Element{ "POSITION", BufferFormat::FORMAT_R32G32B32_FLOAT, sizeof(Vector3), sizeof(Vector3), nullptr, });
		mIndexBinds.AppendElement(BufferLayout::Element{ "INDEX", BufferFormat::FORMAT_R32_UINT, sizeof(int), sizeof(int), nullptr, });
	}

	const std::string& GetName() const { return mName; }

	void Reset()
	{
		SetVertexCount(0);
		SetIndexCount(0);
		MarkChanged();
	}

	int GetRevision() const { return mRevision; }
	const BoundingBox& GetBoundingBox() const { return mBoundingBox; }
	void CalculateBoundingBox() {
		mBoundingBox.mMin = std::numeric_limits<float>::max();
		mBoundingBox.mMax = std::numeric_limits<float>::min();
		for (auto& vec : GetPositionsV()) {
			Vector3 pos = vec;
			mBoundingBox.mMin = Vector3::Min(mBoundingBox.mMin, pos);
			mBoundingBox.mMax = Vector3::Max(mBoundingBox.mMax, pos);
		}
	}

	int GetVertexCount() const { return mVertexBinds.mCount; }
	int GetIndexCount() const { return mIndexBinds.mCount; }

	void RequireVertexPositions(BufferFormat fmt = BufferFormat::FORMAT_R32G32B32_FLOAT) {
		RequireVertexElementFormat(mVertexPositionId, fmt, "POSITION");
	}
	void RequireVertexNormals(BufferFormat fmt = BufferFormat::FORMAT_R32G32B32_FLOAT) {
		RequireVertexElementFormat(mVertexNormalId, fmt, "NORMAL");
	}
	void RequireVertexTexCoords(int coord, BufferFormat fmt = BufferFormat::FORMAT_R32G32_FLOAT) {
		RequireVertexElementFormat(mVertexTexCoordId[coord], fmt, "TEXCOORD");
	}
	void RequireVertexColors(BufferFormat fmt = BufferFormat::FORMAT_R8G8B8A8_UNORM) {
		RequireVertexElementFormat(mVertexColorId, fmt, "COLOR");
	}
	void SetIndexFormat(bool _32bit) {
		SetIndexCount(0);
		auto& el = mIndexBinds.mElements[0];
		el.mFormat = _32bit ? BufferFormat::FORMAT_R32_UINT : BufferFormat::FORMAT_R16_UINT;
		el.mItemSize = el.mBufferStride = BufferFormatType::GetType(el.mFormat).GetByteSize();
		mIndexBinds.CalculateImplicitSize();
	}

	void SetVertexCount(int count)
	{
		if (mVertexBinds.mCount == count) return;
		for (auto& binding : mVertexBinds.GetElements())
			Realloc(binding, count);
		mVertexBinds.mCount = count;
		mVertexBinds.mBuffer.mRevision++;
		mVertexBinds.CalculateImplicitSize();
		MarkChanged();
	}
	void SetIndexCount(int count)
	{
		if (mIndexBinds.mCount == count) return;
		for (auto& binding : mIndexBinds.GetElements())
			Realloc(binding, count);
		mIndexBinds.mCount = count;
		mIndexBinds.mBuffer.mRevision++;
		mIndexBinds.CalculateImplicitSize();
		MarkChanged();
	}

	void SetIndices(std::span<const int> indices)
	{
		SetIndexCount((int)indices.size());
		GetIndicesV().Set(indices);
	}

	TypedBufferView<Vector3> GetPositionsV() {
		return TypedBufferView<Vector3>(&mVertexBinds.GetElements()[mVertexPositionId], mVertexBinds.mCount);
	}
	TypedBufferView<Vector3> GetNormalsV(bool require = false) {
		if (mVertexNormalId == -1) { if (require) RequireVertexNormals(); else return { }; }
		return TypedBufferView<Vector3>(&mVertexBinds.GetElements()[mVertexNormalId], mVertexBinds.mCount);
	}
	TypedBufferView<Vector2> GetTexCoordsV(int channel = 0, bool require = false) {
		if (mVertexTexCoordId[channel] == -1) { if (require) RequireVertexTexCoords(channel); else return { }; }
		return TypedBufferView<Vector2>(&mVertexBinds.GetElements()[mVertexTexCoordId[channel]], mVertexBinds.mCount);
	}
	TypedBufferView<ColorB4> GetColorsV(bool require = false) {
		if (mVertexColorId == -1) { if (require) RequireVertexColors(); else return { }; }
		return TypedBufferView<ColorB4>(&mVertexBinds.GetElements()[mVertexColorId], mVertexBinds.mCount);
	}
	TypedBufferView<int> GetIndicesV(bool require = false) {
		return TypedBufferView<int>(&mIndexBinds.GetElements()[0], mIndexBinds.mCount);
	}

	BufferLayoutPersistent& GetVertexBuffer() const { return mVertexBinds; }

	void CreateMeshLayout(std::vector<const BufferLayout*>& bindings) const {
		if (mVertexBinds.mBuffer.mSize == -1) mVertexBinds.CalculateImplicitSize();
		bindings.insert(bindings.end(), { &mIndexBinds, &mVertexBinds });
	}

	const std::shared_ptr<Material>& GetMaterial(bool require = false) { if (mMaterial == nullptr && require) mMaterial = std::make_shared<Material>(); return mMaterial; }
	void SetMaterial(const std::shared_ptr<Material>& mat) { mMaterial = mat; }

	// Notify graphics and other dependents that the mesh data has changed
	void MarkChanged() {
		mRevision++;
		mVertexBinds.mBuffer.mRevision++;
		mIndexBinds.mBuffer.mRevision++;
	}

private:
	// Utility functions to aid in managing attribute vectors
	template<class T>
	void ResizeIfValid(std::vector<T>& data, int count)
	{
		if (data.empty()) return;
		data.resize(count);
	}
	template<class T>
	void Resize(std::vector<T>& data, int count)
	{
		if (data.size() == count)  return;
		data.resize(count);
	}
	template<class T>
	std::vector<T>& GetOrAllocateVertex(std::vector<T>& data, bool required)
	{
		if (required && data.empty()) data.resize(GetVertexCount());
		return data;
	}

};
