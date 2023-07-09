#pragma once

#include <span>
#include <vector>
#include "Math.h"
#include <memory>

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

	// Is incremented whenever mesh data is changed
	// (so that graphics buffers can be updated accordingly)
	int mRevision;

public:
	Mesh()
		: mRevision(0)
	{
	}

	void Reset()
	{
		SetVertexCount(0);
		SetIndexCount(0);
		MarkChanged();
	}

	int GetRevision() const { return mRevision; }

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
