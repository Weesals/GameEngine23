#pragma once

#include <memory>
#include <algorithm>
#include <vector>
#include <span>
#include <string>

#include "MathTypes.h"

// A pointing device (mouse, touch)
struct Pointer
{
	unsigned int mDeviceId;
	// Store current and previous states to compute deltas
	Vector2 mPositionCurrent;
	Vector2 mPositionPrevious;
	Vector2 mPositionDown;
	float mTotalDrag;
	unsigned int mCurrentButtonState;
	unsigned int mPreviousButtonState;
	int mMouseScroll;

	Pointer(unsigned int deviceId)
		: mDeviceId(deviceId)
		, mTotalDrag(0.0f)
		, mCurrentButtonState(0)
		, mPreviousButtonState(0)
		, mMouseScroll(0)
	{
	}

	Vector2 GetPositionDelta() const {
		return mPositionCurrent - mPositionPrevious;
	}
	// Is button currently down
	bool IsButtonDown(int id = -1) const {
		if (id == -1) return mCurrentButtonState != 0;
		return (mCurrentButtonState & (1 << id)) != 0;
	}
	// Was the button down last frame (might still be down this frame)
	bool WasButtonDown(int id = -1) const {
		if (id == -1) return mPreviousButtonState != 0;
		return (mPreviousButtonState & (1 << id)) != 0;
	}
	// Was button pressed this frame
	bool IsButtonPress(int id = -1) const {
		if (id == -1) return (mCurrentButtonState & ~mPreviousButtonState) != 0;
		return (mCurrentButtonState & ~mPreviousButtonState & (1 << id)) != 0;
	}
	// Was button released this frame
	bool IsButtonRelease(int id = -1) const {
		if (id == -1) return (~mCurrentButtonState & mPreviousButtonState) != 0;
		return (~mCurrentButtonState & mPreviousButtonState & (1 << id)) != 0;
	}

	// Called by the platform bridge code when mouse events are received
	void ReceiveMoveEvent(const Vector2& position) {
		mTotalDrag += Vector2::Distance(position, mPositionCurrent);
		mPositionCurrent = position;
	}
	void ReceiveMouseScroll(int delta) {
		mMouseScroll += delta;
	}
	void ReceiveButtonEvent(int buttonMask, bool state) {
		if (state) mCurrentButtonState |= buttonMask;
		else mCurrentButtonState &= ~buttonMask;
		if (state) { mPositionDown = mPositionCurrent; mTotalDrag = 0.0f; }
	}
	// Called once per frame to migrate current data into previous
	void ReceiveTickEvent() {
		mPositionPrevious = mPositionCurrent;
		mPreviousButtonState = mCurrentButtonState;
	}
};

class Input : public std::enable_shared_from_this<Input>
{
	struct KeyState
	{
		unsigned char KeyId;
		KeyState(unsigned char keyId) { KeyId = keyId; }
		bool operator ==(KeyState other) const { return other.KeyId == KeyId; }
		bool operator !=(KeyState other) const { return other.KeyId != KeyId; }
	};

	std::vector<std::shared_ptr<Pointer>> mPointers;

	std::vector<KeyState> mPressKeys;
	std::vector<KeyState> mDownKeys;
	std::vector<KeyState> mReleaseKeys;
	std::wstring mCharBuffer;

public:
	std::shared_ptr<Input> This() { return shared_from_this(); }
	// Called from something that receives input, to store and
	// pass pointer data to the application
	std::shared_ptr<Pointer> AllocatePointer(unsigned int deviceId)
	{
		auto pointer = std::make_shared<Pointer>(deviceId);
		mPointers.push_back(pointer);
		return pointer;
	}
	// Get all registered pointers
	std::span<const std::shared_ptr<Pointer>> GetPointers() const
	{
		return mPointers;
	}

	// If a key was pressed this frame
	bool IsKeyPressed(int keyId)
	{
		return std::any_of(mPressKeys.begin(), mPressKeys.end(), [=](auto key) { return key.KeyId == keyId; });
	}
	// If a key was released this frame
	bool IsKeyReleased(int keyId)
	{
		return std::any_of(mReleaseKeys.begin(), mReleaseKeys.end(), [=](auto key) { return key.KeyId == keyId; });
	}
	// If a key is currently down
	bool IsKeyDown(int keyId)
	{
		return std::any_of(mDownKeys.begin(), mDownKeys.end(), [=](auto key) { return key.KeyId == keyId; });
	}

	std::span<const KeyState> GetPressKeys() {
		return mPressKeys;
	}
	std::span<const KeyState> GetDownKeys() {
		return mDownKeys;
	}
	std::span<const KeyState> GetReleaseKeys() {
		return mReleaseKeys;
	}
	const std::wstring& GetCharBuffer() {
		return mCharBuffer;
	}

	// Hiding these mutator events inside a nested
	// class to keep the API clean
	// NOTE: Pointers can currently be mutated directly
	// TODO: Pointer mutation only via InputMutator
	struct InputMutator {
		Input* mInput;
		// Process a key event (press or release)
		void ReceiveKeyEvent(int keyId, bool down) {
			KeyState key((unsigned char)keyId);
			if (down) {
				if (mInput->IsKeyDown(keyId)) return;
				// Does this break typing quickly into input field?
				//if (std::find(mInput->mPressKeys.begin(), mInput->mPressKeys.end(), key) == mInput->mPressKeys.end())
				{
					mInput->mPressKeys.push_back(key);
				}
				mInput->mDownKeys.push_back(key);
			} else {
				// Key is no longer 'down'
				RemoveKey(mInput->mDownKeys, keyId);
				mInput->mReleaseKeys.push_back(key);
			}
		}
		void ReceiveCharEvent(wchar_t chr) {
			// Ignore system keys
			if (chr == '\b') return;
			mInput->mCharBuffer.push_back(chr);
		}
		// Notify of frame end; clear per-frame buffers
		void ReceiveTickEvent() {
			mInput->mPressKeys.clear();
			mInput->mReleaseKeys.clear();
			mInput->mCharBuffer.clear();
			for (auto pointer : mInput->mPointers) {
				pointer->ReceiveTickEvent();
			}
		}

		// Remove a key from a list
		static void RemoveKey(std::vector<KeyState>& keys, int keyId) {
			std::erase_if(keys, [=](auto& key) {
				return key.KeyId == keyId;
			});
		}
	};

	// All input mutations should occur through this
	// (but currently dont)
	InputMutator GetMutator() {
		return InputMutator(this);
	}

};

