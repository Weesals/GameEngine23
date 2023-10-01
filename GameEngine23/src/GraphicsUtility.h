#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>
#include <numeric>
#include <span>
#include <vector>

#include "MathTypes.h"


// memcpy but with a stride
template<class T>
static void CopyElements(void* dest, std::span<T> source, int offset, int stride)
{
    *(__int8**)&dest += offset;
    for (int i = 0; i < source.size(); ++i) {
        std::memcpy((__int8*)dest + i * stride, &source[i], sizeof(source[i]));
    }
}
// Find an item in a map, or create a new one using default constructor
template<class K, class T>
static T* GetOrCreate(std::unordered_map<K, std::unique_ptr<T>>& map, const K key)
{
    auto i = map.find(key);
    if (i != map.end()) return i->second.get();
    auto newItem = new T();
    map.insert(std::make_pair(key, newItem));
    return newItem;
}
// Increment a number by an amount and return the original number
template<typename T>
static T PostIncrement(T& v, T a) { int t = v; v += a; return t; }

static size_t AppendHash(const uint8_t* ptr, size_t size, size_t hash)
{
    auto ApplyHash = [&]<typename Z>()
    {
        hash += *(Z*)ptr;
        hash *= 0x9E3779B97F4A7C15uL;
        hash ^= hash >> 16;
        ptr += sizeof(Z);
        size -= sizeof(Z);
    };
    while (size >= sizeof(uint64_t)) ApplyHash.operator()<uint64_t>();
    if (size >= sizeof(uint32_t)) ApplyHash.operator()<uint32_t>();
    if (size >= sizeof(uint16_t)) ApplyHash.operator()<uint16_t>();
    if (size >= sizeof(uint8_t)) ApplyHash.operator()<uint8_t>();
    return hash;
}
template<typename T>
static size_t AppendHash(const T& value, size_t hash)
{
    return AppendHash((const uint8_t*)&value, sizeof(T), hash);
}
template<typename T>
static size_t GenericHash(const T& value)
{
    return AppendHash((uint8_t*)&value, sizeof(T), 0);
}
static size_t GenericHash(const void* data, size_t size)
{
    return AppendHash((uint8_t*)data, size, 0);
}
static size_t GenericHash(std::initializer_list<size_t> values)
{
    size_t hash = 0;
    for (auto& value : values) { hash *= 0x9E3779B97F4A7C15uL; hash ^= hash >> 16; hash += value; }
    return hash;
}
static size_t VariadicHash() { return 0; }
template<class First, class... Args>
static size_t VariadicHash(First first, Args... args)
{
    return AppendHash(first, VariadicHash(args...));
}

// Stores a cache of items allowing efficient reuse where possible
// but avoiding overwriting until they have been consumed by the GPU
template<class T>
class PerFrameItemStore
{
    const uint64_t InvalidUsedFrame = 0xfffffffffffffffful;
protected:
    struct Item
    {
        T mData;
        size_t mDataHash;
        size_t mLayoutHash;
        uint64_t mLastUsedFrame;
    };

private:
    // All items, organised by the hash of their data
    // NOTE: Deal with hash collisions (or is it not important?)
    std::unordered_map<size_t, Item*> mItemsByHash;
    // All constant buffers, roughly ordered by their last usage
    std::deque<Item*> mUsageQueue;

    // No CBs should be written to from this frame ID
    uint64_t mLockFrameId;
    // When used, CBs get assigned this frame ID
    uint64_t mCurrentFrameId;

public:
    PerFrameItemStore()
        : mLockFrameId(0), mCurrentFrameId(0) { }
    ~PerFrameItemStore()
    {
        for (auto kv : mItemsByHash) delete kv.second;
        mItemsByHash.clear();
    }

    // Generate a hash for the specified binary data
    /*size_t ComputeHash(const std::vector<uint8_t>& data)
    {
        int wsize = (int)(data.size() / sizeof(size_t));
        return std::accumulate((size_t*)data.data(), (size_t*)data.data() + wsize, data.size(),
            [](size_t i, auto d) { return (i * 0x9E3779B97F4A7C15L + 0x0123456789ABCDEFL) ^ d; });
    }*/
    static size_t ComputeHash(const std::vector<uint8_t>& data)
    {
        size_t hash = 0;
        int size = (int)data.size();
        const uint8_t* ptr = (const uint8_t*)&data.front();
        auto ApplyHash = [&]<typename Z>()
        {
            hash += *(Z*)ptr;
            hash *= 0x9E3779B97F4A7C15uL;
            hash ^= hash >> 16;
            ptr += sizeof(Z);
            size -= sizeof(Z);
        };
        while (size >= sizeof(uint64_t)) ApplyHash.operator()<uint64_t>();
        if (size >= sizeof(uint32_t)) ApplyHash.operator()<uint32_t>();
        if (size >= sizeof(uint16_t)) ApplyHash.operator()<uint16_t>();
        if (size >= sizeof(uint8_t)) ApplyHash.operator()<uint8_t>();
        return hash;
    }


    // Find or allocate a constant buffer for the specified material and CB layout
    template<class Allocate, class DataFill, class Found>
    Item& RequireItem(uint64_t dataHash, uint64_t layoutHash, Allocate&& alloc, DataFill&& dataFill, Found&& found)
    {
        // Find if a buffer matching this hash already exists
        auto itemKV = mItemsByHash.find(dataHash);
        // Matching buffer was found, move it to end of queue
        if (itemKV != mItemsByHash.end())
        {
            // If this is the first time we're using it this frame
            // update its last used frame
            if (itemKV->second->mLastUsedFrame != mCurrentFrameId && itemKV->second->mLastUsedFrame != InvalidUsedFrame)
            {
                auto q = std::find(mUsageQueue.begin(), mUsageQueue.end(), itemKV->second);
                if (q != mUsageQueue.end()) mUsageQueue.erase(q);
                itemKV->second->mLastUsedFrame = mCurrentFrameId;
                mUsageQueue.push_back(itemKV->second);
            }
            found(*itemKV->second);
            return *itemKV->second;
        }

        Item* item = nullptr;
        // Try to reuse an existing one (based on age) of the same size
        if (mUsageQueue.size() > 100)
        {
            auto reuseItem = mUsageQueue.begin();
            while (reuseItem != mUsageQueue.end() && (*reuseItem)->mLayoutHash != layoutHash)
                ++reuseItem;
            if (reuseItem != mUsageQueue.end() && (*reuseItem)->mLastUsedFrame >= mLockFrameId) reuseItem = mUsageQueue.end();
            if (reuseItem != mUsageQueue.end())
            {
                item = *reuseItem;
                mUsageQueue.erase(reuseItem);
                mItemsByHash.erase(mItemsByHash.find(item->mDataHash));
            }
        }

        // If none are available to reuse, create a new one
        // TODO: Delete old ones if they get too old
        if (item == nullptr)
        {
            item = new Item();
            item->mLayoutHash = layoutHash;
            alloc(*item);
        }
        item->mDataHash = dataHash;
        item->mLastUsedFrame = mCurrentFrameId;

        mUsageQueue.push_back(item);
        mItemsByHash.insert({ dataHash, item, });
        dataFill(*item);

        return *item;
    }

    void SetDetatched(Item& item)
    {
        assert(item->mLastUsedFrame != InvalidUsedFrame);
        auto q = std::find(mUsageQueue.begin(), mUsageQueue.end(), &item);
        if (q != mUsageQueue.end()) mUsageQueue.erase(q);
        item->mLastUsedFrame = InvalidUsedFrame;
    }
    void SetAttached(Item& item)
    {
        assert(item->mLastUsedFrame == InvalidUsedFrame);
        item->mLastUsedFrame = mCurrentFrameId;
        mUsageQueue.push_back(&item);
    }

public:
    // Update the lock/current frames
    void SetResourceLockIds(uint64_t lockFrameId, uint64_t writeFrameId)
    {
        mLockFrameId = lockFrameId;
        mCurrentFrameId = writeFrameId;
    }
};

// Stores a cache of items allowing efficient reuse where possible
// but avoiding overwriting until they have been consumed by the GPU
template<class T>
class PerFrameItemStoreNoHash
{
protected:
    struct Item
    {
        T mData;
        size_t mLayoutHash;
        uint64_t mLastUsedFrame;
    };

private:
    // All constant buffers, roughly ordered by their last usage
    std::deque<Item> mUsageQueue;

    // No CBs should be written to from this frame ID
    uint64_t mLockFrameId;
    // When used, CBs get assigned this frame ID
    uint64_t mCurrentFrameId;

public:
    PerFrameItemStoreNoHash()
        : mLockFrameId(0), mCurrentFrameId(0) { }

    void InsertItem(const T& data)
    {
        Item item;
        item.mData = data;
        item.mLastUsedFrame = mCurrentFrameId;
        mUsageQueue.push_back(item);
    }

    // Find or allocate a constant buffer for the specified material and CB layout
    template<class Allocate, class DataFill>
    Item& RequireItem(uint64_t layoutHash, Allocate&& alloc, DataFill&& dataFill)
    {
        mUsageQueue.push_back({ });
        auto& item = mUsageQueue.back();
        item.mLayoutHash = layoutHash;
        item.mLastUsedFrame = mCurrentFrameId;
        alloc(item);
        dataFill(item);

        return item;
    }

    // Update the lock/current frames
    void SetResourceLockIds(uint64_t lockFrameId, uint64_t writeFrameId)
    {
        mLockFrameId = lockFrameId;
        mCurrentFrameId = writeFrameId;
        while (!mUsageQueue.empty() && mUsageQueue.front().mLastUsedFrame < lockFrameId)
        {
            mUsageQueue.pop_front();
        }
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
