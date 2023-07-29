#include "InputDispatcher.h"

#include <algorithm>

bool Performance::SetInteraction(const std::shared_ptr<InteractionBase>& interaction, bool cancel)
{
    if (mInteraction != nullptr)
    {
        if (cancel) mInteraction->OnCancel(*this); else mInteraction->OnEnd(*this);
    }
    mInteraction = interaction;
    if (mInteraction != nullptr && !mInteraction->OnBegin(*this)) mInteraction = nullptr;
    //Debug.Log("Setting interaction to " + Interaction);
    return mInteraction != nullptr;
}

Vector2 Performance::GetPositionPrevious() const { return GetPositionPrevious(StateMask::Anything); }
Vector2 Performance::GetPositionCurrent() const { return GetPositionCurrent(StateMask::Anything); }
Vector2 Performance::GetPositionDelta() const { return GetPositionDelta(StateMask::Anything); }
Vector2 Performance::GetPositionDown() const { return GetPositionDown(StateMask::AnyButton); }
float Performance::GetDistanceCurrent() const { return GetDistanceCurrent(StateMask::AnyButton); }
float Performance::GetDistancePrevious() const { return GetDistancePrevious(StateMask::AnyButton); }
float Performance::GetAverageRoll() const { return GetAverageRoll(StateMask::AnyButton); }
float Performance::GetAverageDrag() const { return GetAverageDrag(StateMask::AnyButton); }
int Performance::GetDownCount() const { return GetCount(StateMask::AnyButtonDown); }
//float Performance::GetDownTime() const { return GetTimeSince(StateMask::AnyButtonDown); }
bool Performance::GetIsDrag() const { return GetAverageDrag() >= 16.f; }

Vector2 Performance::GetPositionPrevious(StateMask mask) const
{
    auto pos = Vector2::Zero;
    auto count = 0.0f;
    for (int i = First(mask); i < mPointers.size(); Next(i, mask)) { pos += mPointers[i]->mPositionPrevious; ++count; }
    return count > 0 ? pos / count : Vector2::Zero;
}
Vector2 Performance::GetPositionCurrent(StateMask mask) const
{
    auto pos = Vector2::Zero;
    auto count = 0.0f;
    for (int i = First(mask); i < mPointers.size(); Next(i, mask)) { pos += mPointers[i]->mPositionCurrent; ++count; }
    return count > 0 ? pos / count : Vector2::Zero;
}
Vector2 Performance::GetPositionDelta(StateMask mask) const
{
    return GetPositionCurrent(mask) - GetPositionPrevious(mask);
}
Vector2 Performance::GetPositionDown(StateMask mask) const
{
    auto pos = Vector2::Zero;
    auto count = 0.0f;
    for (int i = First(mask); i < mPointers.size(); Next(i, mask)) { pos += mPointers[i]->mPositionDown; ++count; }
    return count > 0 ? pos / count : Vector2::Zero;
}
float Performance::GetDistanceCurrent(StateMask mask) const
{
    Vector2 ave = GetPositionCurrent(mask);
    float distance = 0, count = 0;
    for (int i = First(mask); i < mPointers.size(); Next(i, mask)) { distance += Vector2::Distance(ave, mPointers[i]->mPositionCurrent); ++count; }
    return count > 0 ? distance / count : 0.0f;
}

bool Performance::FramePressed() const { return !WasDown() && IsDown(); }
bool Performance::FrameRelease() const { return WasDown() && !IsDown(); }
bool Performance::FramePressed(int button) const { return !WasDown(button) && IsDown(button); }
bool Performance::FrameRelease(int button) const { return WasDown(button) && !IsDown(button); }

float Performance::GetDistancePrevious(StateMask mask) const
{
    Vector2 ave = GetPositionPrevious(mask);
    float distance = 0, count = 0;
    for (int i = First(mask); i < mPointers.size(); Next(i, mask)) { distance += Vector2::Distance(ave, mPointers[i]->mPositionPrevious); ++count; }
    return count > 0 ? distance / count : 0.0f;
}
float Performance::GetAverageRoll(StateMask mask) const
{
    Vector2 prevAve = Vector2::Zero, curAve = Vector2::Zero;
    float count = 0;
    for (int i = First(mask); i < mPointers.size(); Next(i, mask))
    {
        prevAve += mPointers[i]->mPositionPrevious;
        curAve += mPointers[i]->mPositionCurrent;
        count++;
    }
    if (count > 0) { prevAve /= count; curAve /= count; }
    float rot = 0.0f;
    for (int i = First(mask); i < mPointers.size(); Next(i, mask))
    {
        auto dPrev = (mPointers[i]->mPositionPrevious - prevAve).Normalize();
        auto dCur = (mPointers[i]->mPositionCurrent - curAve).Normalize();
        rot += std::asin(std::clamp(Vector2::Cross(dCur, dPrev), -1.0f, 1.0f));
    }
    if (count > 0) rot /= count;
    return rot;
}
float Performance::GetAverageDrag(StateMask mask) const
{
    float ave = 0.0f, count = 0.0f;
    for (int i = First(mask); i < mPointers.size(); Next(i, mask)) { ave += mPointers[i]->mTotalDrag; ++count; }
    return count > 0 ? ave / count : 0.0f;
}
bool Performance::IsDown() const
{
    for (int i = 0; i < mPointers.size(); i++) if (mPointers[i]->IsButtonDown()) return true;
    return false;
}
bool Performance::WasDown() const
{
    for (int i = 0; i < mPointers.size(); i++) if (mPointers[i]->WasButtonDown()) return true;
    return false;
}
bool Performance::IsDown(int button) const
{
    for (int i = 0; i < mPointers.size(); i++) if (mPointers[i]->IsButtonDown(button)) return true;
    return false;
}
bool Performance::WasDown(int button) const
{
    for (int i = 0; i < mPointers.size(); i++) if (mPointers[i]->WasButtonDown(button)) return true;
    return false;
}
bool Performance::HasButton(int button) const
{
    for (int i = 0; i < mPointers.size(); i++) if (mPointers[i]->IsButtonDown(button) || mPointers[i]->WasButtonDown(button)) return true;
    return false;
}

int Performance::GetCount(StateMask mask) const
{
    int count = 0;
    for (int i = First(mask); i < mPointers.size(); Next(i, mask)) ++count;
    return count;
}

int Performance::First(StateMask mask) const
{
    int i = -1;
    Next(i, mask);
    return i;
}
void Performance::Next(int& i, StateMask mask) const
{
    ++i;
    while (i < mPointers.size() && (GetStateMask(*mPointers[i]) & mask) == 0) ++i;
}
Performance::StateMask Performance::GetStateMask(const Pointer& pointer) const
{
    auto mask = (StateMask)(pointer.mCurrentButtonState | (pointer.mPreviousButtonState << 4));
    if (mask == 0) mask = (StateMask)((uint16_t)mask | (uint16_t)StateMask::Hover);
    return mask;
}


void InputDispatcher::Initialise(const std::shared_ptr<Input>& input)
{
    mInput = input;
    mPerformance = std::make_shared<Performance>();
}
void InputDispatcher::RegisterInteraction(const std::shared_ptr<InteractionBase>& interaction, bool enable)
{
    if (enable) mInteractions.push_back(interaction);
    else std::erase(mInteractions, interaction);
}
void InputDispatcher::Update(bool allowInput)
{
    // TODO: There should be more than 1 performance; at the least
    // one for touch and one for mouse
    auto pointers = mInput->GetPointers();
    // Remove invalid pointers
    std::erase_if(mPerformance->mPointers,
        [&](auto& item)
        {
            return (std::find(pointers.begin(), pointers.end(), item) != pointers.end());
        });
    // Add missing pointers
    for (auto& pointer : pointers)
    {
        if (std::find(mPerformance->mPointers.begin(), mPerformance->mPointers.end(), pointer) == mPerformance->mPointers.end())
        {
            mPerformance->mPointers.push_back(pointer);
        }
    }

    // Try to find the best interaction for the current state
    if (mPerformance->mInteraction == nullptr && allowInput)
    {
        auto state = GetBestInteraction(*mPerformance);
        if (state.Interaction != nullptr) {
            bool forceResolve =
                // Any ACTIVE interactions are forced to activate
                state.Score.GetIsActive()
                // If an interaction is ready and nothing else is valid
                || (state.Score.GetIsReady() && state.PotentialCount == 1)
                // If an interaction is the only one satisfied
                || (state.Score.GetIsSatisfied() && state.Contest == 1)
                // On mouse up: Always resolve to an interaction
                || mPerformance->FrameRelease()
                // Could force drag to begin interaction?
                //|| (state.Score.IsSatisfied && performance.IsDrag)
                ;
            if (forceResolve) mPerformance->SetInteraction(state.Interaction, true);
        }
    }
    // Update the current interaction, if one is bound
    if (mPerformance->mInteraction != nullptr)
    {
        mPerformance->mInteraction->OnUpdate(*mPerformance);
    }
}

// Find the best interaction for a specific performance
InputDispatcher::ActivationState InputDispatcher::GetBestInteraction(const Performance& performance) {
    ActivationState state{ };
    for (auto& interaction : mInteractions)
    {
        auto score = interaction->GetActivation(performance);
        // Choose the item with the best score
        if (score > state.Score) {
            state.Contest = 1;
            state.Score = score;
            state.Interaction = interaction;
            // If this item is marked as Active, always use it
            if (state.Score.GetIsActive()) break;
        }
        else if (score == state.Score) {
            // If we match scores, increment the contest counter
            state.Contest++;
        }
        // Count how many items are potentially valid
        if (score.GetIsPotential()) state.PotentialCount++;
    }
    return state;
}
