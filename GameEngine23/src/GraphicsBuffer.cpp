#include "GraphicsBuffer.h"

#include <algorithm>

void GraphicsBufferDelta::AppendRegion(RangeInt destRegion) {
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
