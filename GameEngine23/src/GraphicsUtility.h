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
static T* GetOrCreate(std::unordered_map<K, std::unique_ptr<T>>& map, const K key, bool& wasCreated)
{
    wasCreated = false;
    auto i = map.find(key);
    if (i != map.end()) return i->second.get();
    auto newItem = new T();
    map.insert(std::make_pair(key, newItem));
    wasCreated = true;
    return newItem;
}
template<class K, class T>
static T* GetOrCreate(std::unordered_map<K, std::unique_ptr<T>>& map, const K key)
{
    bool wasCreated;
    return GetOrCreate(map, key, wasCreated);
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
    return AppendHash((const uint8_t*)&value, sizeof(T), 0);
}
static size_t GenericHash(const void* data, size_t size)
{
    return AppendHash((const uint8_t*)data, size, 0);
}
static size_t GenericHash(std::initializer_list<size_t> values)
{
    size_t hash = 0;
    for (auto& value : values) { hash *= 0x9E3779B97F4A7C15uL; hash ^= hash >> 16; hash += value; }
    return hash;
}
template<typename T>
static size_t ArrayHash(std::span<T> values) {
    return AppendHash((const uint8_t*)values.data(), values.size() * sizeof(T), 0);
}
static size_t VariadicHash() { return 0; }
template<class First, class... Args>
static size_t VariadicHash(First first, Args... args)
{
    return AppendHash(first, VariadicHash(args...));
}


// Stores a cache of items allowing efficient reuse where possible
// but avoiding overwriting until they have been consumed by the GPU
template<class T, int FrameDelay = 0>
class PerFrameItemStoreNoHash {
    static const int BlockShift = 4;
    static const int BlockSize = 1 << BlockShift;
    static const int BlockMask = BlockSize - 1;
protected:
    struct Item {
        size_t mLayoutHash;
        T mData;
        int mLockId = 0;
    };
    struct Block {
        std::unique_ptr<std::array<Item, BlockSize> > mItems;
        int mFirstEmpty;
        Block() : mItems(std::make_unique< std::array<Item, BlockSize> >()), mFirstEmpty(0) { }
        Item& operator [](int index) { return (*mItems)[index]; }
    };
    struct LockBundle {
        size_t mHandles;
        int mItemCount;
    };

private:
    // Item storage
    std::vector<Block> mBlocks;
    // Number of items allocated
    int mItemCount = 0;
    // Ranges within mItems which are locked
    std::vector<LockBundle> mLocks;

    int FindLock(size_t mask) {
        for (int i = 1; i < (int)mLocks.size(); ++i) {
            if (mLocks[i].mHandles == mask) return i;
        }
        return -1;
    }
    int RequireLock(size_t mask) {
        int index = FindLock(mask);
        if (index == -1) {
            for (index = 1; index < (int)mLocks.size(); ++index) if (mLocks[index].mItemCount == 0) break;
            if (index >= (int)mLocks.size()) mLocks.push_back({ .mItemCount = 0, });
            mLocks[index].mHandles = mask;
        }
        return index;
    }
    void SetLock(Item& item, int lockI) {
        mLocks[item.mLockId].mItemCount--;
        item.mLockId = lockI;
        mLocks[item.mLockId].mItemCount++;
    }

    template<class Allocate>
    Item& AllocateItem(uint64_t layoutHash, Allocate&& alloc) {
        // Try to reuse an existing one (based on age) of the same size
        for (int blockI = 0; blockI < (int)mBlocks.size(); blockI++) {
            Block& block = mBlocks[blockI];
            if (block.mFirstEmpty == -1) {
                block.mFirstEmpty = BlockSize;
                for (int i = BlockSize - 1; i >= 0; --i) {
                    auto& item = block[i];
                    if (item.mLockId != 0 && mLocks[item.mLockId].mHandles == 0)
                        SetLock(item, 0);
                    if (item.mLockId == 0) block.mFirstEmpty = i;
                }
            }
            for (int index = block.mFirstEmpty; index < BlockSize; ++index) {
                auto& item = block[index];
                if (item.mLockId != 0 || item.mLayoutHash != layoutHash) continue;
                return item;
            }
        }
        if (mItemCount >> BlockShift >= (int)mBlocks.size()) {
            mBlocks.emplace_back();
        }
        Item& item = mBlocks[mItemCount >> BlockShift][mItemCount & BlockMask];
        ++mItemCount;
        item = Item{ .mLayoutHash = layoutHash, };
        alloc(item);
        return item;
    }

public:
    PerFrameItemStoreNoHash() {
        mLocks.push_back({ .mItemCount = -1, });
    }
    ~PerFrameItemStoreNoHash() { }

    Item& InsertItem(const T& data, size_t layoutHash, size_t lockBits) {
        Item& item = AllocateItem(layoutHash, [&](Item& item) { });
        item.mData = data;
        SetLock(item, RequireLock(lockBits));
        return item;
    }
    template<class Allocate, class DataFill>
    Item& RequireItem(uint64_t layoutHash, size_t lockBits, Allocate&& alloc, DataFill&& dataFill) {
        Item& item = AllocateItem(layoutHash, alloc);
        // Setup item state
        SetLock(item, RequireLock(lockBits));
        dataFill(item);
        return item;
    }
    void Unlock(size_t mask) {
        int changeCount = 0;
        for (int i = 0; i < (int)mLocks.size(); ++i) {
            if ((mLocks[i].mHandles & mask) != 0) {
                mLocks[i].mHandles &= ~mask;
                ++changeCount;
            }
        }
        if (changeCount > 0) {
            for (auto& block : mBlocks) block.mFirstEmpty = -1;
        }
    }
    void Clear() {
        for (int b = 0; b < (int)mBlocks.size(); ++b) {
            auto& block = mBlocks[b];
            for (int i = 0; i < (int)(*block.mItems).size(); ++i) {
                (*block.mItems)[i] = { };
            }
        }
        for (int i = 0; i < (int)mLocks.size(); ++i) mLocks[i] = { };
        mLocks[0].mItemCount = -1;
        mItemCount = 0;
    }
};

// Stores a cache of items allowing efficient reuse where possible
// but avoiding overwriting until they have been consumed by the GPU
template<class T>
class PerFrameItemStore {
    static const int BlockShift = 4;
    static const int BlockSize = 1 << BlockShift;
    static const int BlockMask = BlockSize - 1;
protected:
    struct Item {
        size_t mDataHash;
        size_t mLayoutHash;
        T mData;
        int mLockId = 0;
    };
    struct Block {
        std::unique_ptr<std::array<Item, BlockSize> > mItems;
        int mFirstEmpty;
        Block() : mItems(std::make_unique< std::array<Item, BlockSize> >()), mFirstEmpty(0) { }
        Item& operator [](int index) { return (*mItems)[index]; }
    };
    struct LockBundle {
        size_t mHandles;
        int mItemCount;
    };

private:
    // Item storage
    std::vector<Block> mBlocks;
    // Number of items allocated
    int mItemCount = 0;
    // All items, organised by the hash of their data
    std::unordered_map<size_t, Item*> mItemsByHash;
    // Ranges within mItems which are locked
    std::vector<LockBundle> mLocks;

    int FindLock(size_t mask) {
        for (int i = 1; i < (int)mLocks.size(); ++i) {
            if (mLocks[i].mHandles == mask) return i;
        }
        return -1;
    }
    int RequireLock(size_t mask) {
        int index = FindLock(mask);
        if (index == -1) {
            for (index = 1; index < (int)mLocks.size(); ++index) if (mLocks[index].mItemCount == 0) break;
            if (index >= (int)mLocks.size()) mLocks.push_back({ .mItemCount = 0, });
            mLocks[index].mHandles = mask;
        }
        return index;
    }
    void SetLock(Item& item, int lockI) {
        mLocks[item.mLockId].mItemCount--;
        item.mLockId = lockI;
        mLocks[item.mLockId].mItemCount++;
    }

    template<class Allocate>
    Item& AllocateItem(uint64_t layoutHash, Allocate&& alloc) {
        // Try to reuse an existing one (based on age) of the same size
        for (int blockI = 0; blockI < (int)mBlocks.size(); blockI++) {
            Block& block = mBlocks[blockI];
            if (block.mFirstEmpty == -1) {
                block.mFirstEmpty = BlockSize;
                for (int i = BlockSize - 1; i >= 0; --i) {
                    auto& item = block[i];
                    if (item.mLockId != 0 && mLocks[item.mLockId].mHandles == 0)
                        SetLock(item, 0);
                    if (item.mLockId == 0) block.mFirstEmpty = i;
                }
            }
            for (int index = block.mFirstEmpty; index < BlockSize; ++index) {
                auto& item = block[index];
                if (item.mLockId != 0 || item.mLayoutHash != layoutHash) continue;
                mItemsByHash.erase(item.mDataHash);
                return item;
            }
        }
        if (mItemCount >> BlockShift >= (int)mBlocks.size()) {
            mBlocks.emplace_back();
        }
        Item& item = mBlocks[mItemCount >> BlockShift][mItemCount & BlockMask];
        ++mItemCount;
        item = Item{ .mLayoutHash = layoutHash, };
        alloc(item);
        return item;
    }

public:
    PerFrameItemStore()
        //: mLockFrameId(0), mCurrentFrameId(0)
    {
        mLocks.push_back({ .mItemCount = -1, });
    }
    ~PerFrameItemStore() { }

    // Find or allocate a constant buffer for the specified material and CB layout
    template<class Allocate, class DataFill, class Found>
    Item& RequireItem(uint64_t dataHash, uint64_t layoutHash, size_t lockBits, Allocate&& alloc, DataFill&& dataFill, Found&& found) {
        // Find if a buffer matching this hash already exists
        auto itemKV = mItemsByHash.find(dataHash);
        // Matching buffer was found, move it to end of queue
        if (itemKV != mItemsByHash.end()) {
            // If this is the first time we're using it this frame
            // update its last used frame
            auto& item = *itemKV->second;
            if ((mLocks[item.mLockId].mHandles & lockBits) != lockBits) {
                auto newMask = mLocks[item.mLockId].mHandles | lockBits;
                int lockId = RequireLock(newMask);
                SetLock(item, lockId);
            }
            found(item);
            return item;
        }

        return AllocateItem(dataHash, layoutHash, lockBits, alloc, dataFill);
    }
    template<class Allocate, class DataFill>
    Item& AllocateItem(uint64_t dataHash, uint64_t layoutHash, size_t lockBits, Allocate&& alloc, DataFill&& dataFill) {
        Item& item = AllocateItem(layoutHash, alloc);
        // Setup item state
        SetLock(item, RequireLock(lockBits));
        item.mDataHash = dataHash;
        mItemsByHash.insert({ dataHash, &item, });
        dataFill(item);
        return item;
    }

    void Unlock(size_t mask) {
        int changeCount = 0;
        for (int i = 0; i < (int)mLocks.size(); ++i) {
            if ((mLocks[i].mHandles & mask) != 0) {
                mLocks[i].mHandles &= ~mask;
                ++changeCount;
            }
        }
        if (changeCount > 0) {
            for (auto& block : mBlocks) block.mFirstEmpty = -1;
        }
    }
    void Clear() {
        for (int b = 0; b < (int)mBlocks.size(); ++b) {
            auto& block = mBlocks[b];
            for (int i = 0; i < (int)(*block.mItems).size(); ++i) {
                (*block.mItems)[i] = { };
            }
        }
        mItemsByHash.clear();
        for (int i = 0; i < (int)mLocks.size(); ++i) mLocks[i] = { };
        mLocks[0].mItemCount = -1;
        mItemCount = 0;
    }
};
