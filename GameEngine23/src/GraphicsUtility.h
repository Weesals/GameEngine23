#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>
#include <numeric>
#include <span>
#include <vector>

#include "MathTypes.h"

typedef uint64_t LockMask;

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

/*static size_t AppendHash(const uint8_t* ptr, size_t size, size_t hash)
{
    while (size >= sizeof(__m128)) {
        __m128 dat
        memcpy(&dat, ptr, sizeof(__m128));
        ApplyHash256(z);
        ptr += sizeof(__m128);
        size -= sizeof(__m128);
    }
    if (size > 0) {
        uint64_t z[4] = { 0, 0, 0, 0 };
        memcpy(z, ptr, size);
        ApplyHash256(z);
    }
}*/
#pragma optimize("t", on)
template<int Count = 4>
static size_t AppendHashT(const uint8_t* ptr, size_t size, size_t hash) {
    typedef uint64_t Base;
    Base z[Count] = { 0 };
    while ((int64_t)size > 0) {
        memcpy(z, ptr, size < sizeof(z) ? size : sizeof(z));

        constexpr uint64_t prime1 = 0x9E3779B97F4A7C15uLL;
        constexpr uint64_t prime2 = 0xC2B2AE3D27D4EB4FuLL;
#if defined(_MSC_VER)
        hash = _rotl64(hash, 15);
#else
        hash ^= hash >> 15;
#endif
        hash *= prime1;
        hash += z[0] * (Base)(prime2);
        if constexpr (Count >= 2) hash += z[1] * (Base)(prime2 * prime2);
        if constexpr (Count >= 3) hash += z[2] * (Base)(prime2 * prime2 * prime2);
        if constexpr (Count >= 4) hash += z[3] * (Base)(prime2 * prime2 * prime2 * prime2);

        ptr += sizeof(z);
        size -= sizeof(z);
    }
    return hash;
}
#pragma optimize("t", off)
static size_t AppendHash(const uint8_t* ptr, size_t size, size_t hash) {
    return AppendHashT<4>(ptr, size, hash);
}
template<typename T>
static size_t AppendHash(const T& value, size_t hash) {
    if (sizeof(T) < sizeof(uint64_t)) return AppendHashT<1>((const uint8_t*)&value, sizeof(T), hash);
    if (sizeof(T) < sizeof(uint64_t) * 2) return AppendHashT<2>((const uint8_t*)&value, sizeof(T), hash);
    return AppendHash((const uint8_t*)&value, sizeof(T), hash);
}
template<typename T>
static size_t GenericHash(const T& value) {
    if (sizeof(T) < sizeof(uint64_t)) return AppendHashT<1>((const uint8_t*)&value, sizeof(T), 0);
    if (sizeof(T) < sizeof(uint64_t) * 2) return AppendHashT<2>((const uint8_t*)&value, sizeof(T), 0);
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
        const T& operator * () const { return mData; }
        const T* operator -> () const { return &mData; }
        T& operator * () { return mData; }
        T* operator -> () { return &mData; }
    };
    struct Block {
        std::unique_ptr<std::array<Item, BlockSize> > mItems;
        int mFirstEmpty;
        Block() : mItems(std::make_unique< std::array<Item, BlockSize> >()), mFirstEmpty(0) { }
        Item& operator [](int index) const { return (*mItems)[index]; }
    };
    struct LockBundle {
        LockMask mHandles;
        int mItemCount;
    };

private:
    // Item storage
    std::vector<Block> mBlocks;
    // Number of items allocated
    int mItemCount = 0;
    // Ranges within mItems which are locked
    std::vector<LockBundle> mLocks;

    int FindLock(LockMask mask) {
        for (int i = 1; i < (int)mLocks.size(); ++i) {
            if (mLocks[i].mHandles == mask) return i;
        }
        return -1;
    }
    int RequireLock(LockMask mask) {
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

    Item& InsertItem(const T& data, size_t layoutHash, LockMask lockBits) {
        Item& item = AllocateItem(layoutHash, [&](Item& item) { });
        item.mData = data;
        SetLock(item, RequireLock(lockBits));
        return item;
    }
    template<class Allocate, class DataFill>
    Item& RequireItem(uint64_t layoutHash, LockMask lockBits, Allocate&& alloc, DataFill&& dataFill) {
        Item& item = AllocateItem(layoutHash, alloc);
        // Setup item state
        SetLock(item, RequireLock(lockBits));
        dataFill(item);
        return item;
    }
    bool GetHasAny(LockMask mask, LockMask value) {
        for (int i = 0; i < (int)mLocks.size(); ++i) {
            if ((mLocks[i].mHandles & mask) == value && mLocks[i].mItemCount > 0) return true;
        }
        return false;
    }
    uint64_t Unlock(LockMask mask) {
        int changeCount = 0;
        uint64_t lockMask = 0ull;
        for (int i = 0; i < (int)mLocks.size(); ++i) {
            if ((mLocks[i].mHandles & mask) != 0) {
                lockMask |= 1ull << i;
                mLocks[i].mHandles &= ~mask;
                ++changeCount;
            }
        }
        if (changeCount > 0) {
            for (auto& block : mBlocks) block.mFirstEmpty = -1;
        }
        return lockMask;
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
    void PurgeUnlocked() {
        for (auto& block : mBlocks) {
            block.mFirstEmpty = BlockSize;
            for (auto i = 0; i < BlockSize; ++i) {
                auto& item = block[i];
                if (item.mLockId != 0 && mLocks[item.mLockId].mHandles == 0)
                    SetLock(item, 0);
                if (item.mLockId == 0) {
                    item = { };
                    block.mFirstEmpty = std::min(block.mFirstEmpty, i);
                }
            }
        }
    }
    struct MaskedCollection {
        PerFrameItemStoreNoHash<T, FrameDelay>& mItemStore;
        uint64_t mLockMask;
        struct Iterator {
            MaskedCollection mCollection;
            int mItemId;
            Iterator(const MaskedCollection& collection, int itemId)
                : mCollection(collection), mItemId(itemId) { }
            Iterator& operator ++() {
                auto& blocks = mCollection.mItemStore.mBlocks;
                for (++mItemId; mItemId < mCollection.mItemStore.mItemCount; ++mItemId) {
                    int blockId = mItemId >> BlockShift;
                    int itemId = mItemId & BlockMask;
                    auto& item = blocks[blockId][itemId];
                    if (((1ull << item.mLockId) & mCollection.mLockMask) != 0) return *this;
                }
                mItemId = -1;
                return *this;
            }
            Item& GetItem() const { return mCollection.mItemStore.mBlocks[mItemId >> BlockShift][mItemId & BlockMask]; }
            uint64_t GetLockHandle() const { return mCollection.mItemStore.mLocks[GetItem().mLockId].mHandles; }
            void Delete() { mCollection.mItemStore.SetLock(GetItem(), 0); }
            bool operator ==(const Iterator& other) const { return mItemId == other.mItemId; }
            T* operator -> () const { return &*this; }
            T& operator *() const { return GetItem().mData; }
        };
        MaskedCollection(PerFrameItemStoreNoHash<T, FrameDelay>& itemStore, uint64_t mask)
            : mItemStore(itemStore), mLockMask(mask)
        { }
        Iterator begin() const {
            auto it = Iterator(*this, -1);
            ++it;
            return it;
        }
        Iterator end() const {
            return Iterator(*this, -1);
        }
    };
    MaskedCollection GetMaskItemIterator(uint64_t mask) {
        return MaskedCollection(*this, mask);
    }
    MaskedCollection GetAllActive() {
        uint64_t lockMask = 0ull;
        for (int i = 0; i < mLocks.size(); ++i) {
            if (mLocks[i].mHandles != 0) lockMask |= 1ull << i;
        }
        return MaskedCollection(*this, lockMask);
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
        LockMask mHandles;
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

    int FindLock(LockMask mask) {
        for (int i = 1; i < (int)mLocks.size(); ++i) {
            if (mLocks[i].mHandles == mask) return i;
        }
        return -1;
    }
    int RequireLock(LockMask mask) {
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
    Item& RequireItem(uint64_t dataHash, uint64_t layoutHash, LockMask lockBits, Allocate&& alloc, DataFill&& dataFill, Found&& found) {
        // Find if a buffer matching this hash already exists
        auto itemKV = mItemsByHash.find(dataHash);
        // Matching buffer was found, move it to end of queue
        if (itemKV != mItemsByHash.end()) {
            // If this is the first time we're using it this frame
            // update its last used frame
            Item& item = *itemKV->second;
            assert(item.mLayoutHash == layoutHash);
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
    Item& AllocateItem(uint64_t dataHash, uint64_t layoutHash, LockMask lockBits, Allocate&& alloc, DataFill&& dataFill) {
        Item& item = AllocateItem(layoutHash, alloc);
        // Setup item state
        SetLock(item, RequireLock(lockBits));
        item.mDataHash = dataHash;
        mItemsByHash.insert({ dataHash, &item, });
        dataFill(item);
        return item;
    }

    void Unlock(LockMask mask) {
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
