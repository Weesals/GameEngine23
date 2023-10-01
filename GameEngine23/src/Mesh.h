#pragma once

#include <span>
#include <vector>
#include <memory>

#include "Material.h"
#include "MathTypes.h"

enum BufferFormat : uint8_t {
	FORMAT_UNKNOWN = 0,
	FORMAT_R32G32B32A32_TYPELESS = 1,
	FORMAT_R32G32B32A32_FLOAT = 2,
	FORMAT_R32G32B32A32_UINT = 3,
	FORMAT_R32G32B32A32_SINT = 4,
	FORMAT_R32G32B32_TYPELESS = 5,
	FORMAT_R32G32B32_FLOAT = 6,
	FORMAT_R32G32B32_UINT = 7,
	FORMAT_R32G32B32_SINT = 8,
	FORMAT_R16G16B16A16_TYPELESS = 9,
	FORMAT_R16G16B16A16_FLOAT = 10,
	FORMAT_R16G16B16A16_UNORM = 11,
	FORMAT_R16G16B16A16_UINT = 12,
	FORMAT_R16G16B16A16_SNORM = 13,
	FORMAT_R16G16B16A16_SINT = 14,
	FORMAT_R32G32_TYPELESS = 15,
	FORMAT_R32G32_FLOAT = 16,
	FORMAT_R32G32_UINT = 17,
	FORMAT_R32G32_SINT = 18,
	FORMAT_R32G8X24_TYPELESS = 19,
	FORMAT_D32_FLOAT_S8X24_UINT = 20,
	FORMAT_R32_FLOAT_X8X24_TYPELESS = 21,
	FORMAT_X32_TYPELESS_G8X24_UINT = 22,
	FORMAT_R10G10B10A2_TYPELESS = 23,
	FORMAT_R10G10B10A2_UNORM = 24,
	FORMAT_R10G10B10A2_UINT = 25,
	FORMAT_R11G11B10_FLOAT = 26,
	FORMAT_R8G8B8A8_TYPELESS = 27,
	FORMAT_R8G8B8A8_UNORM = 28,
	FORMAT_R8G8B8A8_UNORM_SRGB = 29,
	FORMAT_R8G8B8A8_UINT = 30,
	FORMAT_R8G8B8A8_SNORM = 31,
	FORMAT_R8G8B8A8_SINT = 32,
	FORMAT_R16G16_TYPELESS = 33,
	FORMAT_R16G16_FLOAT = 34,
	FORMAT_R16G16_UNORM = 35,
	FORMAT_R16G16_UINT = 36,
	FORMAT_R16G16_SNORM = 37,
	FORMAT_R16G16_SINT = 38,
	FORMAT_R32_TYPELESS = 39,
	FORMAT_D32_FLOAT = 40,
	FORMAT_R32_FLOAT = 41,
	FORMAT_R32_UINT = 42,
	FORMAT_R32_SINT = 43,
};

struct BufferLayout {
	enum Usage : uint8_t { Vertex, Index, Instance, Uniform, };
	struct Element {
		std::string mBindName;
		uint16_t mItemSize = 0;		// Size of each item
		uint16_t mBufferStride = 0;	// Separation between items in this buffer (>= mItemSize)
		BufferFormat mFormat = FORMAT_UNKNOWN;
		void* mData = nullptr;
		Element() { }
		Element(const std::string_view& name, BufferFormat format, int stride, int size, uint8_t* data)
			: mBindName(name), mFormat(format), mBufferStride(stride), mItemSize(size), mData(data)
		{ }
	};
	struct Buffer {
		size_t mIdentifier;
		int mRevision;
		int mSize;		// Size in bytes to allocate for the entire buffer
		Buffer(size_t identifier, int size, int revision = 0)
			: mIdentifier(identifier), mSize(size), mRevision(revision) { }
	};
	Buffer mBuffer;
	std::vector<Element> mElements;
	Usage mUsage = Usage::Vertex;
	int mOffset = 0;	// Offset in count when binding a view to this buffer
	int mCount = 0;		// How many elements to make current
	BufferLayout() : mBuffer(0, 0) { }
	BufferLayout(size_t identifier, int size, Usage usage, int count)
		: mBuffer(identifier, size), mUsage(usage), mCount(count) { }
	void CalculateImplicitSize() {
		mBuffer.mSize = 0;
		for (auto& el : mElements) mBuffer.mSize += el.mItemSize;
		mBuffer.mSize *= mCount;
	}
};
template<class T>
inline std::span<const T*> operator+(const std::vector<T*>& value) {
	return std::span<const T*>((const T**)value.data(), value.size());
}

// Store data related to drawing a mesh
class Mesh
{
	// TODO: Attributes should be dynamic and packed
	// into a single storage container
	std::vector<Vector3> mPositionData;
	std::vector<Vector3> mNormalData;
	std::vector<Vector2> mUVData;
	std::vector<Color> mColors;

	// TODO: Indicies should also get packed, so that
	// they can be 32 or 16 bit
	std::vector<int> mIndices;

	// Material used to render this mesh
	// TODO: Some meshes are multi-material
	std::shared_ptr<Material> mMaterial;

	// Is incremented whenever mesh data is changed
	// (so that graphics buffers can be updated accordingly)
	int mRevision;

	BoundingBox mBoundingBox;

	mutable BufferLayout mVertexBinds;
	mutable BufferLayout mIndexBinds;

	std::string mName;

public:
	Mesh(const std::string& name)
		: mRevision(0), mName(name)
	{
	}

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
		for (auto& vec : mPositionData) {
			mBoundingBox.mMin = Vector3::Min(mBoundingBox.mMin, vec);
			mBoundingBox.mMax = Vector3::Max(mBoundingBox.mMax, vec);
		}
	}

	int GetVertexCount() const { return (int)mPositionData.size(); }
	int GetIndexCount() const { return (int)mIndices.size(); }

	void SetVertexCount(int count)
	{
		mPositionData.resize(count);
		ResizeIfValid(mNormalData, count);
		ResizeIfValid(mUVData, count);
		ResizeIfValid(mColors, count);
		MarkChanged();
	}
	void SetIndexCount(int count)
	{
		mIndices.resize(count);
		MarkChanged();
	}

	void SetIndices(const std::vector<int>& indices)
	{
		SetIndexCount((int)indices.size());
		std::copy(indices.begin(), indices.end(), mIndices.begin());
	}

	// Get mutable versions of these attributes
	// NOTE: Must call MarkChanged if they get changed externally
	std::span<Vector3> GetPositions() { return mPositionData; }
	std::span<Vector3> GetNormals(bool require = false) { return GetOrAllocateVertex(mNormalData, require); }
	std::span<Vector2> GetUVs(bool require = false) { return GetOrAllocateVertex(mUVData, require); }
	std::span<Color> GetColors(bool require = false) { return GetOrAllocateVertex(mColors, require); }
	std::span<int> GetIndices() { return mIndices; }

	// Get immutable versions of these attributes
	std::span<const Vector3> GetPositions() const { return mPositionData; }
	std::span<const Vector3> GetNormals() const { return mNormalData; }
	std::span<const Vector2> GetUVs() const { return mUVData; }
	std::span<const Color> GetColors() const { return mColors; }
	std::span<const int> GetIndices() const { return mIndices; }

	void CreateMeshLayout(std::vector<const BufferLayout*>& bindings) const {
		if (mIndexBinds.mBuffer.mRevision != mRevision) {
			new(&mIndexBinds) BufferLayout((size_t)this + 1, 0, BufferLayout::Usage::Index, GetIndexCount());
			mIndexBinds.mBuffer.mRevision = mRevision;
			mIndexBinds.mElements.push_back(BufferLayout::Element{ "INDEX", BufferFormat::FORMAT_R32_UINT, sizeof(int), sizeof(int), (uint8_t*)mIndices.data(), });
			mIndexBinds.CalculateImplicitSize();
		}
		if (mVertexBinds.mBuffer.mRevision != mRevision) {
			new (&mVertexBinds) BufferLayout((size_t)this, 0, BufferLayout::Usage::Vertex, GetVertexCount());
			mVertexBinds.mBuffer.mRevision = mRevision;
			mVertexBinds.mElements.push_back(BufferLayout::Element{ "POSITION", BufferFormat::FORMAT_R32G32B32_FLOAT, sizeof(Vector3), sizeof(Vector3), (uint8_t*)mPositionData.data(), });
			if (!mNormalData.empty()) mVertexBinds.mElements.push_back(BufferLayout::Element{ "NORMAL", BufferFormat::FORMAT_R32G32B32_FLOAT, sizeof(Vector3), sizeof(Vector3), (uint8_t*)mNormalData.data(), });
			if (!mUVData.empty()) mVertexBinds.mElements.push_back(BufferLayout::Element{ "TEXCOORD", BufferFormat::FORMAT_R32G32_FLOAT, sizeof(Vector2), sizeof(Vector2), (uint8_t*)mUVData.data(), });
			if (!mColors.empty()) mVertexBinds.mElements.push_back(BufferLayout::Element{ "COLOR", BufferFormat::FORMAT_R32G32B32A32_FLOAT, sizeof(Color), sizeof(Color), (uint8_t*)mColors.data(), });
			mVertexBinds.CalculateImplicitSize();
		}
		bindings.insert(bindings.end(), { &mIndexBinds, &mVertexBinds });
	}

	const std::shared_ptr<Material>& GetMaterial(bool require = false) { if (mMaterial == nullptr && require) mMaterial = std::make_shared<Material>(); return mMaterial; }
	void SetMaterial(const std::shared_ptr<Material>& mat) { mMaterial = mat; }

	// Notify graphics and other dependents that the mesh data has changed
	void MarkChanged() {
		mRevision++;
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
