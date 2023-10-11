#pragma once
#include <cassert>

template<class T, int Size = 7>
struct InplaceVector {
	T mValues[Size];
	uint8_t mSize = 0;
	InplaceVector() { }
	InplaceVector(T value) { for (int i = 0; i < Size; ++i) mValues[i] = value; }
	uint8_t size() const { return mSize; }
	bool empty() const { return mSize == 0; }
	T* begin() { return mValues; }
	T* end() { return mValues + mSize; }
	void push_back(uint8_t v) { assert(mSize < Size); mValues[mSize++] = v; }
	T& pop_back() { return mValues[--mSize]; }
	T& operator[](int i) { return mValues[i]; }
	const T& operator[](int i) const { return mValues[i]; }
};
template<class T, int StaticSize = 8>
class HybridVector {
	static inline constexpr int GetStaticOffset() { return 8 - 6 / alignof(T) * alignof(T); }
	struct Data {
		static inline constexpr int GetStaticPadding() { return std::max(1, (StaticSize - (8 - GetStaticOffset())) / (int)sizeof(T)); }
		uint8_t mSize = 0;
		uint8_t mCapacity;
		union {
			T* mPtr = nullptr;
			T _Padding[GetStaticPadding()];
		};
	} mData;
	static inline constexpr int GetStaticCapacity() { return (sizeof(Data) - 2) / sizeof(T); }
public:
	HybridVector() { mData.mCapacity = GetStaticCapacity(); }
	~HybridVector() { if (mData.mCapacity > GetStaticCapacity()) free(mData.mPtr); }
	uint8_t size() const { return mData.mSize; }
	bool empty() const { return mData.mSize == 0; }
	void clear() { mData.mSize = 0; }
	T& operator[](size_t i) { return data()[i]; }
	T* data() { return mData.mCapacity <= GetStaticCapacity() ? (T*)((uint8_t*)this + GetStaticOffset()) : mData.mPtr; }
	T* begin() { return data(); }
	T* end() { return data() + mData.mSize; }
	T& front() { return *data(); }
	T& back() { return end()[-1]; }
	void push_back(const T& v) {
		emplace_back(v);
	}
	template<class Args>
	void emplace_back(Args&& v) {
		if (mData.mSize == mData.mCapacity) {
			int newCap = mData.mCapacity * 2;
			mData.mPtr = mData.mCapacity == GetStaticCapacity()
				? (T*)std::memcpy(malloc(sizeof(T) * newCap), data(), size() * sizeof(T))
				: (T*)realloc(mData.mPtr, sizeof(T) * newCap);
			mData.mCapacity = newCap;
		}
		data()[mData.mSize++] = v;
	}
	T& pop_back() { return data()[--mData.mSize]; }
	std::span<T> span() { return std::span<T>(data(), mData.mSize); }
};

struct ExpandableMemoryArena {
    struct Page {
        int mSize;
        int mConsumed;
        void* mData;
        Page(int capacity)
            : mSize(capacity), mConsumed(0)
        {
            mData = malloc(capacity);
        }
        Page(Page&& o) : mData(o.mData) { o.mData = nullptr; }
        ~Page() {
            if (mData != nullptr) free(mData);
        }
        void* AttemptConsume(int size) {
            if (mConsumed + size > mSize) return nullptr;
            void* data = (uint8_t*)mData + mConsumed;
            mConsumed += size;
            return data;
        }
    };
    std::vector<Page> mPages;
    int mActivePage;
    ExpandableMemoryArena() {
        mActivePage = 0;
        mPages.reserve(4);
    }
    void Clear() {
        mActivePage = 0;
        for (auto& page : mPages) page.mConsumed = 0;
    }
    void* Require(int size) {
        if (size <= 0) return nullptr;
        const int DataAlignment = 8;
        size = (size + DataAlignment - 1) & ~(DataAlignment - 1);
        while (mActivePage < (int)mPages.size()) {
            void* data = mPages[mActivePage].AttemptConsume(size);
            if (data != nullptr) return data;
            ++mActivePage;
        }
        const int Alignment = 1024;
        const int MinimumPageSize = 1024 * 32;
        int allocSize = size * 2;
        allocSize = (allocSize + (Alignment - 1)) & (~Alignment);
        if (allocSize < MinimumPageSize) allocSize = MinimumPageSize;
        mPages.emplace_back(allocSize);
        void* data = mPages.back().AttemptConsume(size);
        return data;
    }
    int SumConsumedMemory() const {
        int mem = 0;
        for (auto& page : mPages) mem += page.mConsumed;
        return mem;
    }
};

struct SparseIndices
{
    std::vector<RangeInt> mRanges;
    int Allocate() { return Allocate(1).start; }
    RangeInt Allocate(int count)
    {
        for (auto block = mRanges.begin(); block != mRanges.end(); ++block)
        {
            if (block->length < count) continue;
            block->length -= count;
            block->start += count;
            RangeInt r(block->start - count, count);
            if (block->length <= 0) block = mRanges.erase(block);
            return r;
        }
        return RangeInt(-1, -1);
    }
    void Return(RangeInt& range) {
        Return(range.start, range.length);
        range = RangeInt(0, 0);
    }
    void Return(int start, int count)
    {
        if (mRanges.empty()) { mRanges.push_back(RangeInt(start, count)); return; }
        auto it = std::partition_point(mRanges.begin(), mRanges.end(), [&](auto& item) {
            return item.start < start;
            });
        if (it != mRanges.end()) {
            if (it->start == start + count) {
                it->start -= count;
                it->length += count;
                if (it != mRanges.begin()) AttemptMerge(it - 1);
                return;
            }
        }
        if (it != mRanges.begin()) {
            auto p = it - 1;
            if (p->end() == start) {
                p->length += count;
                if (it != mRanges.end()) AttemptMerge(p);
                return;
            }
        }
        /*int end = start + count;
        auto block = mRanges.rbegin();
        for (; block != mRanges.rend(); block++)
        {
            // Merge at end of block
            if (block->end() == start)
            {
                block->length += count;
                if (block + 1 != mRanges.rend()) AttemptMerge(block.base());
                return;
            }
            // Merge at start of block
            if (block->start == (start + count))
            {
                block->start -= count; block->length += count;
                if (block != mRanges.rbegin()) AttemptMerge((block - 1).base());
                return;
            }
            // Block is prior to new range, insert new range
            if (block->start <= end) break;
        }
        //--block;*/
        mRanges.insert(it, RangeInt(start, count));
    }
    int Find(int index)
    {
        int min = 0, max = (int)mRanges.size() - 1;
        while (max >= min)
        {
            int mid = (min + max) / 2;
            auto value = mRanges[mid];
            if (index < value.start) max = mid - 1;
            else if (index >= value.end()) min = mid + 1;
            else return mid;
        }
        return -1;
    }
    bool Contains(int index) { return Find(index) != -1; }
    struct Iterator
    {
        SparseIndices& mIndices;
        int mUnallocIndex;
        int mCurrent;
        Iterator(SparseIndices& inds)
            : mIndices(inds)
        {
            mUnallocIndex = 0;
            mCurrent = -1;
            ++*this;
        }
        Iterator& operator ++() {
            ++mCurrent;
            if (mUnallocIndex < mIndices.mRanges.size())
            {
                auto unused = mIndices.mRanges[mUnallocIndex];
                if (mCurrent >= unused.start)
                {
                    mCurrent += unused.length;
                    ++mUnallocIndex;
                }
            }
            return *this;
        }
        bool operator ==(const Iterator& other) const { return mCurrent == other.mCurrent; }
    };

    Iterator begin() { return Iterator(*this); }
    Iterator end() { Iterator it(*this); it.mCurrent = mRanges.back().end(); }
private:
    bool AttemptMerge(std::vector<RangeInt>::iterator it)
    {
        auto p0 = it;
        auto p1 = it + 1;
        if (p0->end() != p1->start) return false;
        p0->length += p1->length;
        mRanges.erase(p1);
        return true;
    }
};

template<class T>
struct SparseArray
{
    SparseIndices mUnused;
    std::vector<T> mItems;
    T& operator[](int i) { return mItems[i]; }
    const T& operator[](int i) const { return mItems[i]; }
    std::span<T> operator[](RangeInt i) { return std::span<T>(mItems.data() + i.start, mItems.data() + i.end()); }
    std::span<const T> operator[](RangeInt i) const { return std::span<T>(mItems.data() + i.start, mItems.data() + i.end()); }
    int Allocate() { return Allocate(1).start; }
    RangeInt Allocate(int count)
    {
        if (count == 0) return { };
        while (true)
        {
            auto range = mUnused.Allocate(count);
            if (range.start >= 0) return range;
            RequireCapacity((int)mItems.size() + count);
        }
    }

    void RequireCapacity(int newCapacity)
    {
        if (mItems.size() < newCapacity)
        {
            int oldSize = (int)mItems.size();
            int newSize = std::max(oldSize, 32);
            while (newSize < newCapacity) newSize *= 2;
            mItems.resize(newSize);
            mUnused.Return(oldSize, newSize - oldSize);
        }
    }
    int Add(const T& value) {
        int id = Allocate();
        mItems[id] = value;
        return id;
    }
    int Add(T&& value) {
        int id = Allocate();
        mItems[id] = std::move(value);
        return id;
    }
    template<class T>
    RangeInt AddRange(const T& arr)
    {
        auto range = Allocate((int)arr.size());
        int i = range.start;
        for (auto it = arr.begin(); it != arr.end(); ++it) mItems[i++] = *it;
        return range;
    }
    void Return(int id)
    {
        RangeInt range(id, 1);
        Return(range);
    }
    void Return(RangeInt& range)
    {
        mUnused.Return(range);
        range = { };
    }
    void Reallocate(RangeInt& range, int newCount)
    {
        if (newCount < range.length)
        {
            mUnused.Return(RangeInt(range.start + newCount, range.length - newCount));
            range.length = newCount;
            return;
        }
        // Attempt to consume free adjacent blocks if available
        int nextRangeId = mUnused.Find(range.end());
        if (nextRangeId >= 0 && range.length + mUnused.mRanges[nextRangeId].length >= newCount)
        {
            int takeCount = newCount - range.length;
            auto& nextRange = mUnused.mRanges[nextRangeId];
            nextRange.length -= takeCount;
            nextRange.start += takeCount;
            range.length = newCount;
            return;
        }
        // Otherwise reallocate; returning first is allowed because unused data is still copied during resize
        auto ogRange = range;
        mUnused.Return(range);
        range = Allocate(newCount);
        if (range.start != ogRange.start)
        {
            std::memcpy(mItems.begin() + range.start, mItems.begin() + ogRange.start, sizeof(T) * ogRange.length);
        }
        return range;
    }

    struct Iterator
    {
        SparseIndices::Iterator mIndices;
        SparseArray<T>& mArray;
        Iterator(SparseArray<T>& arr)
            : mArray(arr), mIndices(arr.mUnused.begin())
        {

        }
        int GetIndex() const { return mIndices.mCurrent; }
        T& operator *() const
        {
            return mArray.mItems[GetIndex()];
        }
        T* operator ->() const { return &**this; }
        Iterator& operator ++()
        {
            ++mIndices;
            return *this;
        }
        bool operator ==(const Iterator& other) const
        {
            return mIndices.mCurrent == other.mIndices.mCurrent;
        }
    };
    Iterator begin()
    {
        return Iterator(*this);
    }
    Iterator end()
    {
        auto it = Iterator(*this);
        it.mIndices.mCurrent = (int)mItems.size();
        return it;
    }
};
