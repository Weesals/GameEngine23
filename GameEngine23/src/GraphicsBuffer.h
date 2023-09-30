#pragma once

#include <vector>
#include <span>
#include "MathTypes.h"
#include "Delegate.h"

class GraphicsBufferBase
{
protected:
	int mStride;
	int mCount;
	int mRevision;
	std::vector<uint8_t> mData;
public:
	GraphicsBufferBase(int stride, int count)
		: mStride(stride), mCount(count)
	{
		mData.resize(GetSize());
		++mRevision;
	}
	const uint8_t* GetRawData() const { return mData.data(); }
	int GetSize() const { return mCount * mStride; }
	int GetStride() const { return mStride; }
	int GetCount() const { return mCount; }
	int GetRevision() const { return mRevision; }
	int SetCount(int count)
	{
		int ocount = mCount;
		mCount = count;
		mData.resize(GetSize());
		++mRevision;
		return ocount;
	}
	void MarkChanged(RangeInt rangeCount)
	{
		mRevision++;
	}
};

template<typename T>
class GraphicsBuffer : public GraphicsBufferBase
{
	Delegate<Int2>::Container mOnDataUpdated;
public:
	GraphicsBuffer(int count = 32)
		: GraphicsBufferBase(sizeof(T), count)
	{
	}

	void SetValue(int index, T& data)
	{
		((T*)mData.data())[index] = data;
		++mRevision;
	}
	std::span<T> GetValues(RangeInt range)
	{
		return std::span<T>((T*)mData.data() + range.start, range.length);
	}

	Delegate<Int2>::Reference&& RegisterOnDataUpdated(Delegate<Int2>::Function& fn) {
		return mOnDataUpdated.Add(fn);
	}

};

class GraphicsBufferDelta
{
	std::vector<RangeInt> mCopyRegions;

public:
	void AppendRegion(RangeInt destRegion) {
		auto it = std::partition_point(mCopyRegions.begin(), mCopyRegions.end(), [&](auto& item) {
			return item.end() < destRegion.start;
		});
		if (it != mCopyRegions.end() && it->start <= destRegion.end()) {
			*it = RangeInt::FromBeginEnd(
				std::min(it->start, destRegion.start),
				std::max(it->end(), destRegion.end())
			);
			auto nxt = it + 1;
			for (; nxt != mCopyRegions.end(); ++nxt) {
				if (nxt->start > it->end()) break;
			}
			if (nxt != it + 1) {
				it->end(std::max(it->end(), (nxt - 1)->end()));
				mCopyRegions.erase(it + 1, nxt);
			}
			return;
		}
		mCopyRegions.insert(it, destRegion);
	}
	std::span<RangeInt> GetRegions() { return mCopyRegions; }
	void Clear() { mCopyRegions.clear(); }
};
