#pragma once

#include "Input.h"
#include "MathTypes.h"

#include <vector>
#include <map>

class InteractionBase;

class Performance
{
public:
    std::vector<std::shared_ptr<const Pointer>> mPointers;
    std::shared_ptr<InteractionBase> mInteraction;

    enum StateMask : uint16_t {
        ButtonLeft = 0x11, ButtonRight = 0x22, ButtonMiddle = 0x44, Hover = 0x1000,
        AnyButtonDown = 0x0f, AnyButton = 0xff, Anything = 0xffff,
    };

    bool SetInteraction(const std::shared_ptr<InteractionBase>& interaction, bool cancel = false);

    Vector2 GetPositionPrevious() const;
    Vector2 GetPositionCurrent() const;
    Vector2 GetPositionDelta() const;
    Vector2 GetPositionDown() const;
    float GetDistanceCurrent() const;
    float GetDistancePrevious() const;
    float GetAverageRoll() const;
    float GetAverageDrag() const;
    int GetDownCount() const;
    //float GetDownTime() const;
    bool GetIsDrag() const;

    Vector2 GetPositionPrevious(StateMask mask) const;
    Vector2 GetPositionCurrent(StateMask mask) const;
    Vector2 GetPositionDelta(StateMask mask) const;
    Vector2 GetPositionDown(StateMask mask) const;
    float GetDistanceCurrent(StateMask mask) const;

    bool FramePressed() const;
    bool FrameRelease() const;
    bool FramePressed(int button) const;
    bool FrameRelease(int button) const;

    float GetDistancePrevious(StateMask mask) const;
    float GetAverageRoll(StateMask mask) const;
    float GetAverageDrag(StateMask mask) const;
    bool IsDown() const;
    bool WasDown() const;
    bool IsDown(int button) const;
    bool WasDown(int button) const;
    bool HasButton(int button) const;

    int GetCount(StateMask mask) const;

private:
    int First(StateMask mask) const;
    void Next(int& i, StateMask mask) const;
    StateMask GetStateMask(const Pointer& pointer) const;
};

// Represents the "score" for an interaction which could be activated
struct ActivationScore {
    float Score;
    bool GetIsPotential() { return Score >= 1.f; }  // May activate in the future, but currently not ready
    bool GetIsSatisfied() { return Score >= 2.f; }  // Ready to activate (will activate if input ends)
    bool GetIsReady() { return Score >= 5.f; }      // Activate immediately if no contest (Satisfied or higher)
    bool GetIsActive() { return Score >= 10.f; }    // Force activate regardless of contest

    ActivationScore(float score = 0.0f) { Score = score; }
    bool operator <(ActivationScore o) { return Score < o.Score; }
    bool operator >(ActivationScore o) { return Score > o.Score; }
    bool operator ==(ActivationScore o) { return Score == o.Score; }
    bool operator !=(ActivationScore o) { return Score != o.Score; }
    // We are not ready
    static ActivationScore MakeNone() { return ActivationScore(0.f); }
    // We might be ready in the future
    static ActivationScore MakePotential() { return ActivationScore(1.f); }
    // We are able to be activated, but not requesting
    static ActivationScore MakeSatisfied() { return ActivationScore(2.f); }
    // Activate us if no one else is also ready
    static ActivationScore MakeSatisfiedAndReady() { return ActivationScore(5.f); }
    // We must be activated, even if a conflict exist
    static ActivationScore MakeActive() { return ActivationScore(100.f); }
};

class InteractionBase
{
public:
    // Called by the PlayDispatcher when determining which interaction is most appropriate
    virtual ActivationScore GetActivation(Performance performance) = 0;

    virtual bool OnBegin(Performance& performance) { return true; }
    virtual void OnUpdate(Performance& performance) { }
    virtual void OnCancel(Performance& performance) { }
    virtual void OnEnd(Performance& performance) { }
};

class InputDispatcher
{
    std::shared_ptr<Input> mInput;
    std::shared_ptr<Performance> mPerformance;

    std::vector<std::shared_ptr<InteractionBase>> mInteractions;

public:
    struct ActivationState {
        ActivationScore Score;
        std::shared_ptr<InteractionBase> Interaction;
        int Contest;
        int PotentialCount;
    };

    void Initialise(const std::shared_ptr<Input>& input);
    void RegisterInteraction(const std::shared_ptr<InteractionBase>& interaction, bool enable);
    void Update(bool allowInput);

    template<class T> const std::shared_ptr<T> FindInteraction() const
    {
        for (auto& interaction : mInteractions)
        {
            auto value = std::dynamic_pointer_cast<T>(interaction);
            if (value != nullptr) return value;
        }
        return nullptr;
    }

protected:
    ActivationState GetBestInteraction(const Performance& performance);
};
