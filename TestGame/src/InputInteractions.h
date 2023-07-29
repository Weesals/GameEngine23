#pragma once

#include <InputDispatcher.h>

#include "Play.h"

class SelectInteraction : public InteractionBase
{
    Play* mPlay;

public:
    SelectInteraction(Play* play) : mPlay(play) { }
    ActivationScore GetActivation(Performance performance) override;
    bool OnBegin(Performance& performance) override;
    void OnUpdate(Performance& performance) override;

};

class OrderInteraction : public InteractionBase
{
    Play* mPlay;

public:
    OrderInteraction(Play* play) : mPlay(play) { }
    ActivationScore GetActivation(Performance performance) override;
    bool OnBegin(Performance& performance) override;
    void OnUpdate(Performance& performance) override;

};

class CameraInteraction : public InteractionBase
{
    Play* mPlay;

public:
    CameraInteraction(Play* play) : mPlay(play) { }
    ActivationScore GetActivation(Performance performance) override;
    void OnUpdate(Performance& performance) override;

};

class TerrainPaintInteraction : public InteractionBase
{
    Play* mPlay;

public:
    TerrainPaintInteraction(Play* play) : mPlay(play) { }
    ActivationScore GetActivation(Performance performance) override;
    void OnUpdate(Performance& performance) override;

};

class PlacementInteraction : public InteractionBase
{
    Play* mPlay;
    int mProtoId;
    Components::Transform mTransform;

    Play::OnRenderDelegate::Reference mOnRender;

public:
    PlacementInteraction(Play* play) : mPlay(play), mProtoId(-1) { }
    void SetPlacementProtoId(int protoId);
    int GetPlacementProtoId() const;
    ActivationScore GetActivation(Performance performance) override;
    bool OnBegin(Performance& performance) override;
    void OnUpdate(Performance& performance) override;
    void OnCancel(Performance& performance) override;
    void OnEnd(Performance& performance) override;

};
