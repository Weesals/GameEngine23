#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>
#include <numeric>
#include <span>
#include <vector>
#include <atomic>
#include <mutex>

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

class PerFrameItemStoreBase {
protected:
    struct LockBundle {
        LockMask mHandles;
        int mItemCount;
    };
    // Ranges within mItems which are locked
    std::vector<LockBundle> mLocks;

    int RequireLock(LockMask mask) {
        assert(mask != 0);  // mask of 0 should use lock 0
        while (true) {
            int index = -1;
            for (int i = 1; i < (int)mLocks.size(); ++i) {
                auto& lock = mLocks[i];
                // TODO: mHandle could be zeroed immediately after this
                // Should also add an item
                if (lock.mHandles == mask) return i;
                if (lock.mHandles == 0 && lock.mItemCount == 0 && index == -1) index = i;
            }
            if (index == -1) { index = (int)mLocks.size(); mLocks.push_back({ .mHandles = 0, .mItemCount = 0, }); }
            auto zeroLock = (LockMask)0;
            if (std::atomic_ref<LockMask>(mLocks[index].mHandles).compare_exchange_weak(zeroLock, mask)) return index;
        }
    }
    void ChangeLockRef(int oldLockI, int lockI) {
        std::atomic_ref<int>(mLocks[lockI].mItemCount)++;
        auto& oldLock = mLocks[oldLockI];
        if (std::atomic_ref<int>(oldLock.mItemCount)-- == 0) oldLock.mHandles = 0;
    }

    bool TrySetLock(int& lockId, int oldLockI, int newLockI) {
        if (!std::atomic_ref<int>(lockId).compare_exchange_weak(oldLockI, newLockI)) return false;
        ChangeLockRef(oldLockI, newLockI);
        return true;
    }
    void SetLock(int& lockId, int newLockI) {
        std::atomic_ref<int> newLockItemCountRef(mLocks[newLockI].mItemCount);
        newLockItemCountRef++;
        assert(newLockI == 0 || mLocks[newLockI].mHandles != 0);
        auto oldLockI = std::atomic_ref<int>(lockId).exchange(newLockI);
        auto& oldLock = mLocks[oldLockI];
        // NOTE: This is unsafe - mItemCount could be 0 but another thread may be adding an item to it
        if (std::atomic_ref<int>(oldLock.mItemCount)-- == 0) oldLock.mHandles = 0;
    }

    PerFrameItemStoreBase() {
        mLocks.reserve(8);
        mLocks.push_back({ .mItemCount = -1, });
    }

public:
    bool GetHasAny(LockMask mask, LockMask value) {
        for (int i = 0; i < (int)mLocks.size(); ++i) {
            if ((mLocks[i].mHandles & mask) == value && mLocks[i].mItemCount > 0) return true;
        }
        return false;
    }
    uint64_t Unlock(LockMask mask, bool& anyNewEmpty) {
        int changeCount = 0;
        uint64_t lockMask = 0ull;
        for (int i = 0; i < (int)mLocks.size(); ++i) {
            if ((mLocks[i].mHandles & mask) != 0) {
                lockMask |= 1ull << i;
                mLocks[i].mHandles &= ~mask;
                anyNewEmpty |= mLocks[i].mHandles == 0;
                ++changeCount;
            }
        }
        return lockMask;
    }
};

// Stores a cache of items allowing efficient reuse where possible
// but avoiding overwriting until they have been consumed by the GPU
template<class T>
class PerFrameItemStoreNoHash : public PerFrameItemStoreBase {
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

private:
    // Item storage
    std::vector<Block> mBlocks;
    // Number of items allocated
    int mItemCount = 0;

    void SetLock(Item& item, int lockI) {
        PerFrameItemStoreBase::SetLock(item.mLockId, lockI);
    }

    template<class Allocate, class ReceiveIndex>
    Item& AllocateItem(uint64_t layoutHash, Allocate&& alloc, ReceiveIndex&& receiveIndex) {
        // Try to reuse an existing one (based on age) of the same size
        for (int blockI = 0; blockI < (int)mBlocks.size(); blockI++) {
            Block& block = mBlocks[blockI];
            int firstEmpty = block.mFirstEmpty;
            if (firstEmpty == -1) {
                firstEmpty = BlockSize;
                for (int i = BlockSize - 1; i >= 0; --i) {
                    auto& item = block[i];
                    auto oldLock = item.mLockId;
                    if (oldLock != 0 && mLocks[oldLock].mHandles == 0)
                        TrySetLock(item.mLockId, oldLock, 0);
                    if (item.mLockId == 0) firstEmpty = i;
                }
                block.mFirstEmpty = firstEmpty;
            }
            int endIndex = std::min(BlockSize, mItemCount - blockI * BlockSize);
            for (int index = firstEmpty; index < endIndex; ++index) {
                auto& item = block[index];
                if (item.mLockId != 0 || item.mLayoutHash != layoutHash) continue;
                receiveIndex((blockI << BlockShift) + index);
                return item;
            }
        }
        std::atomic_ref<int> itemCount(mItemCount);
        int itemIndex = itemCount++;
        if (itemIndex >> BlockShift >= (int)mBlocks.size()) {
            mBlocks.emplace_back();
        }
        Item& item = mBlocks[itemIndex >> BlockShift][itemIndex & BlockMask];
        item = Item{ .mLayoutHash = layoutHash, };
        alloc(item);
        receiveIndex(itemIndex);
        return item;
    }

public:
    PerFrameItemStoreNoHash() {
        mBlocks.reserve(32);
    }
    ~PerFrameItemStoreNoHash() { }

    Item& GetItem(int index) {
        return mBlocks[index >> BlockShift][index & BlockMask];
    }

    template<class Allocate, class ReceiveIndex>
    Item& RequireLockedItem(size_t layoutHash, LockMask lockBits, Allocate&& alloc, ReceiveIndex&& receiveIndex) {
        auto lockId = RequireLock(lockBits);
        while (true) {
            int itemIndex = -1;
            auto& item = AllocateItem(layoutHash, alloc, [&](int index) { itemIndex = index; });
            // The lock failed to set, probably taken by another thread
            if (!TrySetLock(item.mLockId, 0, lockId)) continue;
            while (mLocks[lockId].mHandles != lockBits) {
                // The lock is incorrect - probably changed while we were assigning it
                lockId = RequireLock(lockBits);
                SetLock(item, lockId);
            }
            receiveIndex(itemIndex);
            return item;
        }
    }
    Item& InsertItem(const T& data, size_t layoutHash, LockMask lockBits) {
        Item& item = RequireLockedItem(layoutHash, lockBits, [&](Item& item) {}, [](int index) {});
        item.mData = data;
        return item;
    }
    template<class Allocate, class DataFill>
    Item& RequireItem(uint64_t layoutHash, LockMask lockBits, Allocate&& alloc, DataFill&& dataFill) {
        return RequireItem(layoutHash, lockBits, alloc, dataFill, [](int index) {});
    }
    template<class Allocate, class DataFill, class ReceiveIndex>
    Item& RequireItem(uint64_t layoutHash, LockMask lockBits, Allocate&& alloc, DataFill&& dataFill, ReceiveIndex&& receiveIndex) {
        Item& item = RequireLockedItem(layoutHash, lockBits, alloc, receiveIndex);
        dataFill(item);
        return item;
    }
    uint64_t Unlock(LockMask mask) {
        bool anyNewEmpty;
        uint64_t lockMask = PerFrameItemStoreBase::Unlock(mask, anyNewEmpty);
        if (anyNewEmpty) {
            for (auto& block : mBlocks) block.mFirstEmpty = -1;
        }
        return lockMask;
    }
    void RequireItemLock(Item& item, LockMask mask) {
        auto oldLock = mLocks[item.mLockId];
        auto newMask = oldLock.mHandles | mask;
        if (oldLock.mHandles == newMask) return;
        auto lockId = newMask == 0 ? 0 : RequireLock(newMask);
        SetLock(item, lockId);
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
        PerFrameItemStoreNoHash<T>& mItemStore;
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
        MaskedCollection(PerFrameItemStoreNoHash<T>& itemStore, uint64_t mask)
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

#if 0
// Stores a cache of items allowing efficient reuse where possible
// but avoiding overwriting until they have been consumed by the GPU
template<class T>
struct ItemWithDataHash { T mData; size_t mDataHash; };
template<class T>
class PerFrameItemStore : PerFrameItemStoreNoHash< ItemWithDataHash<T> > {

    // All items, organised by the hash of their data
    std::unordered_map<size_t, Item*> mItemsByHash;

};
#else
// Stores a cache of items allowing efficient reuse where possible
// but avoiding overwriting until they have been consumed by the GPU
template<class T>
class PerFrameItemStore : public PerFrameItemStoreBase {
    static const int BlockShift = 4;
    static const int BlockSize = 1 << BlockShift;
    static const int BlockMask = BlockSize - 1;
public:
    struct Item {
        size_t mDataHash;
        size_t mLayoutHash;
        T mData;
        int mLockId = 0;
    };
protected:
    struct Block {
        std::unique_ptr<std::array<Item, BlockSize> > mItems;
        int mFirstEmpty;
        Block() : mItems(std::make_unique< std::array<Item, BlockSize> >()), mFirstEmpty(0) { }
        Item& operator [](int index) { return (*mItems)[index]; }
    };

private:
    // Item storage
    std::vector<Block> mBlocks;
    // Number of items allocated
    int mItemCount = 0;
    std::mutex mItemsHashMutex;
    // All items, organised by the hash of their data
    std::unordered_map<size_t, Item*> mItemsByHash;

    void SetLock(Item& item, int lockI) {
        PerFrameItemStoreBase::SetLock(item.mLockId, lockI);
    }

    template<class Allocate, class ReceiveIndex>
    Item& AllocateItem(uint64_t layoutHash, Allocate&& alloc, ReceiveIndex&& receiveIndex) {
        // Try to reuse an existing one (based on age) of the same size
        for (int blockI = 0; blockI < (int)mBlocks.size(); blockI++) {
            Block& block = mBlocks[blockI];
            int firstEmpty = block.mFirstEmpty;
            if (firstEmpty == -1) {
                firstEmpty = BlockSize;
                for (int i = BlockSize - 1; i >= 0; --i) {
                    auto& item = block[i];
                    auto oldLock = item.mLockId;
                    if (oldLock != 0 && mLocks[oldLock].mHandles == 0)
                        TrySetLock(item.mLockId, oldLock, 0);
                    if (item.mLockId == 0) firstEmpty = i;
                }
                block.mFirstEmpty = firstEmpty;
            }
            int endIndex = std::min(BlockSize, mItemCount - blockI * BlockSize);
            for (int index = firstEmpty; index < endIndex; ++index) {
                auto& item = block[index];
                if (item.mLockId != 0 || item.mLayoutHash != layoutHash) continue;
                std::scoped_lock lock(mItemsHashMutex);
                mItemsByHash.erase(item.mDataHash);
                receiveIndex(index);
                return item;
            }
        }
        std::atomic_ref<int> itemCount(mItemCount);
        int itemIndex = itemCount++;
        if (itemIndex >> BlockShift >= (int)mBlocks.size()) {
            mBlocks.emplace_back();
        }
        Item& item = mBlocks[itemIndex >> BlockShift][itemIndex & BlockMask];
        item = Item{ .mLayoutHash = layoutHash, };
        alloc(item);
        receiveIndex(itemIndex);
        return item;
    }

public:
    PerFrameItemStore()
        //: mLockFrameId(0), mCurrentFrameId(0)
    {
        mBlocks.reserve(128);
        mLocks.reserve(16);
        mItemsByHash.reserve(256);
        mLocks.push_back({ .mItemCount = -1, });
    }
    ~PerFrameItemStore() { }

    // Find or allocate a constant buffer for the specified material and CB layout
    template<class Allocate, class DataFill, class Found>
    Item& RequireItem(uint64_t dataHash, uint64_t layoutHash, LockMask lockBits, Allocate&& alloc, DataFill&& dataFill, Found&& found) {
        assert(lockBits != 0);
        while (true) {
            // Find if a buffer matching this hash already exists
            auto itemKV = mItemsByHash.find(dataHash);
            // Matching buffer was found, move it to end of queue
            if (itemKV == mItemsByHash.end()) break;
            // If this is the first time we're using it this frame
            // update its last used frame
            Item& item = *itemKV->second;
            assert(item.mDataHash == dataHash);
            assert(item.mLayoutHash == layoutHash);
            auto oldLockI = item.mLockId;
            if ((mLocks[oldLockI].mHandles & lockBits) != lockBits) {
                auto newMask = mLocks[oldLockI].mHandles | lockBits;
                int lockId = RequireLock(newMask);
                if (!TrySetLock(item.mLockId, oldLockI, lockId)) continue;
            }
            found(item);
            return item;
        }

        return AllocateItem(dataHash, layoutHash, lockBits, alloc, dataFill);
    }
    template<class Allocate, class DataFill>
    Item& AllocateItem(uint64_t dataHash, uint64_t layoutHash, LockMask lockBits, Allocate&& alloc, DataFill&& dataFill) {
        while (true) {
            Item& item = AllocateItem(layoutHash, alloc, [](int index) {});
            // Setup item state
            auto oldLockId = item.mLockId;
            if (!TrySetLock(item.mLockId, 0, RequireLock(lockBits))) continue;
            dataFill(item);
            item.mDataHash = dataHash;
            std::scoped_lock lock(mItemsHashMutex);
            mItemsByHash.insert({ dataHash, &item, });
            return item;
        }
    }

    Item& GetItem(int index) {
        return mBlocks[index >> BlockShift][index & BlockMask];
    }

    void Substitute(LockMask mask, LockMask newMask) {
        for (int i = 0; i < (int)mLocks.size(); ++i) {
            if ((mLocks[i].mHandles & mask) == 0) continue;
            std::atomic_ref<size_t> handles(mLocks[i].mHandles);
            handles |= newMask;
            handles &= (~mask | newMask);   // In case any bits are common in both mask and newMask
        }
    }
    void Substitute(Item& item, LockMask mask, LockMask newMask) {
        while (true) {
            auto oldLockId = item.mLockId;
            auto oldLockHandles = mLocks[oldLockId].mHandles;
            if ((oldLockHandles & mask) == 0) return;
            auto newHandles = (oldLockHandles & ~mask) | newMask;
            auto newLockId = newHandles == 0 ? 0 : RequireLock(newHandles);
            if (TrySetLock(item.mLockId, oldLockId, newLockId)) return;
        }
    }
    void Unlock(LockMask mask) {
        bool anyNewEmpty;
        uint64_t lockMask = PerFrameItemStoreBase::Unlock(mask, anyNewEmpty);
        if (anyNewEmpty) {
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
        std::scoped_lock lock(mItemsHashMutex);
        mItemsByHash.clear();
        for (int i = 0; i < (int)mLocks.size(); ++i) mLocks[i] = { };
        mLocks[0].mItemCount = -1;
        mItemCount = 0;
    }
    template<class Callback>
    int Find(Callback&& callback) {
        for (int b = 0; b < (int)mBlocks.size(); ++b) {
            auto& block = mBlocks[b];
            for (int i = 0; i < (int)(*block.mItems).size(); ++i) {
                auto& item = (*block.mItems)[i];
                if (callback(item)) return (b << BlockShift) + i;
            }
        }
        throw "Not found";
    }
    template<class Callback>
    void ForAll(Callback&& callback) {
        for (int b = 0; b < (int)mBlocks.size(); ++b) {
            auto& block = mBlocks[b];
            for (int i = 0; i < (int)(*block.mItems).size(); ++i) {
                callback((*block.mItems)[i]);
            }
        }
    }
    template<class Callback>
    void RemoveIf(Callback&& callback, LockMask newMask) {
        for (int b = 0; b < (int)mBlocks.size(); ++b) {
            auto& block = mBlocks[b];
            for (int i = 0; i < (int)(*block.mItems).size(); ++i) {
                auto& item = (*block.mItems)[i];
                if (callback(item)) {
                    mItemsByHash.erase(item.mDataHash);
                    TrySetLock(item.mLockId, item.mLockId, RequireLock(newMask));
                }
            }
        }
    }
    void DetachAll() {
        /*ForAll([&](auto& item) {
            item.mDataHash = 0;
        });*/
        std::scoped_lock lock(mItemsHashMutex);
        mItemsByHash.clear();
    }
    void RequireItemLock(Item& item, LockMask mask) {
        auto oldLock = mLocks[item.mLockId];
        auto newMask = oldLock.mHandles | mask;
        if (oldLock.mHandles == newMask) return;
        auto lockId = newMask == 0 ? 0 : RequireLock(newMask);
        SetLock(item, lockId);
    }
    void RemoveLock(int index, LockMask mask) {
        int b = (index >> BlockShift);
        int i = (index & BlockMask);
        auto& block = mBlocks[b];
        auto& item = block[i];
        auto oldLock = mLocks[item.mLockId];
        auto newMask = oldLock.mHandles & ~mask;
        if (oldLock.mHandles == newMask) return;
        auto lockId = newMask == 0 ? 0 : RequireLock(newMask);
        SetLock(item, lockId);
        if (item.mLockId == 0) {
            block.mFirstEmpty = std::min(block.mFirstEmpty, i);
        }
    }
};

#endif