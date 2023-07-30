#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>
#include <numeric>


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

template<typename T>
static size_t GenericHash(const T& value)
{
    size_t hash = 0;
    int size = sizeof(T);
    const uint8_t* ptr = (const uint8_t*)&value;
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


// Stores a cache of items allowing efficient reuse where possible
// but avoiding overwriting until they have been consumed by the GPU
template<class T>
class PerFrameItemStore
{
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
    UINT64 mLockFrameId;
    // When used, CBs get assigned this frame ID
    UINT64 mCurrentFrameId;

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
            if (itemKV->second->mLastUsedFrame != mCurrentFrameId)
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

public:
    // Update the lock/current frames
    void SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId)
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
    UINT64 mLockFrameId;
    // When used, CBs get assigned this frame ID
    UINT64 mCurrentFrameId;

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
    void SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId)
    {
        mLockFrameId = lockFrameId;
        mCurrentFrameId = writeFrameId;
        while (!mUsageQueue.empty() && mUsageQueue.front().mLastUsedFrame < lockFrameId)
        {
            mUsageQueue.pop_front();
        }
    }
};