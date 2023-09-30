#pragma once

template<class T, int Size = 7>
struct InplaceVector {
	T mValues[Size];
	uint8_t mSize = 0;
	uint8_t size() const { return mSize; }
	bool empty() const { return mSize == 0; }
	T* begin() { return mValues; }
	T* end() { return mValues + mSize; }
	void push_back(uint8_t v) { assert(mSize < Size); mValues[mSize++] = v; }
	T& pop_back() { return mValues[--mSize]; }
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
