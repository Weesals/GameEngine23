#include "InputInteractions.h"

#include <algorithm>
#include <numbers>
#include "Geometry.h"

ActivationScore SelectInteraction::GetActivation(Performance performance)
{
    // Selection requires a left-mouse button to activate
    if (!performance.HasButton(0)) return ActivationScore::MakeNone();
    return ActivationScore::MakeSatisfied();
}
bool SelectInteraction::OnBegin(Performance& performance)
{
    auto& graphics = mPlay->GetGraphics();
    auto& input = mPlay->GetInput();
    auto& selection = mPlay->GetSelection();
    auto& camera = mPlay->GetCamera();
    auto& world = mPlay->GetWorld();

    // Find entity under the mouse
    Ray ray = camera.ViewportToRay(performance.GetPositionCurrent() / graphics->GetClientSize());
    auto nearest = world->RaycastEntity(ray);

    // If not holding shift, clear selection
    if (!input->IsKeyDown(0x10))
        selection->Clear();
    // Append to selection
    if (nearest.is_alive())
        selection->Append(nearest);

    return true;
}
void SelectInteraction::OnUpdate(Performance& performance)
{
    // Unit selection ends when the mouse button is released
    if (!performance.IsDown()) performance.SetInteraction(nullptr);
}



ActivationScore OrderInteraction::GetActivation(Performance performance)
{
    // Selection requires a left-mouse button to activate
    if (!performance.HasButton(1)) return ActivationScore::MakeNone();
    return ActivationScore::MakeSatisfied();
}
bool OrderInteraction::OnBegin(Performance& performance)
{
    auto& graphics = mPlay->GetGraphics();
    auto& input = mPlay->GetInput();
    auto& selection = mPlay->GetSelection();
    auto& camera = mPlay->GetCamera();
    auto& world = mPlay->GetWorld();

    // Find target location
    Ray ray = camera.ViewportToRay(performance.GetPositionCurrent() / graphics->GetClientSize());
    Landscape::LandscapeHit hit;
    auto pos = world->GetLandscape()->Raycast(ray, hit)
        ? hit.mHitPosition
        : ray.ProjectTo(Plane(Vector3::Up, 0.0f));

    auto target = world->RaycastEntity(ray);
    // Order selected entities
    mPlay->SendActionRequest({
            .mActionTypeId = -1,
            .mActionTypes = Actions::ActionTypes::All,
            .mTarget = target,
            .mLocation = pos,
        });

    // Flash effect to make ordering more obvious
    // TODO: Only flash when action resolves to attack/gather/interact
    if (target.is_alive())
        world->FlashEntity(target, WorldEffects::HighlightConfig::MakeDefault());
    return true;
}
void OrderInteraction::OnUpdate(Performance& performance)
{
    if (!performance.IsDown()) performance.SetInteraction(nullptr);
}


ActivationScore CameraInteraction::GetActivation(Performance performance)
{
    // Camera drag activates when the RMB (1) is dragged
    if (!performance.HasButton(2)) return ActivationScore::MakeNone();
    if (!performance.GetIsDrag()) return ActivationScore::MakeSatisfied();
    return ActivationScore::MakeSatisfiedAndReady();
}
void CameraInteraction::OnUpdate(Performance& performance)
{
    auto& graphics = mPlay->GetGraphics();
    auto& input = mPlay->GetInput();
    auto& selection = mPlay->GetSelection();
    auto& camera = mPlay->GetCamera();
    auto& world = mPlay->GetWorld();

    auto pos = camera.GetPosition();
    if (performance.IsDown(1))
    {
        // Hold right-mouse to rotate
        auto pivotPoint = camera.ViewportToRay(Vector2(0.5f, 0.5f)).Normalize().GetPoint(80.0f);
        pos -= pivotPoint;
        auto rot = camera.GetOrientation();
        auto newRot =
            Quaternion::CreateFromAxisAngle(Vector3::Right, performance.GetPositionDelta().y * 0.005f)
            * rot
            * Quaternion::CreateFromAxisAngle(Vector3::Up, performance.GetPositionDelta().x * 0.005f);
        pos = Vector3::Transform(pos, rot.Inverse() * newRot);
        pos += pivotPoint;
        camera.SetOrientation(newRot);
    }
    else
    {
        // Default is pan
        auto ray0 = camera.ViewportToRay(performance.GetPositionPrevious() / graphics->GetClientSize());
        auto ray1 = camera.ViewportToRay(performance.GetPositionCurrent() / graphics->GetClientSize());
        pos += ray0.ProjectTo(Plane(Vector3::Up, 0.0f)) - ray1.ProjectTo(Plane(Vector3::Up, 0.0f));
    }
    camera.SetPosition(pos);

    // Cancel interaction on mouse up
    if (!performance.IsDown()) performance.SetInteraction(nullptr);
}



ActivationScore TerrainPaintInteraction::GetActivation(Performance performance)
{
    // Terrain paint is a LMB drag
    if (!performance.HasButton(0)) return ActivationScore::MakeNone();
    if (!performance.GetIsDrag()) return ActivationScore::MakeSatisfied();
    return ActivationScore::MakeSatisfiedAndReady();
}
void TerrainPaintInteraction::OnUpdate(Performance& performance)
{
    const float Range = 4.0f;
    auto& graphics = mPlay->GetGraphics();
    auto& input = mPlay->GetInput();
    auto& selection = mPlay->GetSelection();
    auto& camera = mPlay->GetCamera();
    auto& world = mPlay->GetWorld();

    // Get brush position
    Ray ray = camera.ViewportToRay(performance.GetPositionCurrent() / graphics->GetClientSize());
    auto hit = ray.ProjectTo(Plane(Vector3::Up, 0.0f));
    auto& sizing = world->GetLandscape()->GetSizing();
    auto& heightMap = world->GetLandscape()->GetRawHeightMap();
    // Get brush range in cell space
    Int2 min = Int2::Max({ 0, 0 }, sizing.WorldToLandscape(hit - Range));
    Int2 max = Int2::Min(sizing.Size, sizing.WorldToLandscape(hit + Range) + 1);
    for (int y = min.y; y < max.y; ++y)
    {
        for (int x = min.x; x < max.x; ++x)
        {
            // Brush to cell distance
            float dst = (sizing.LandscapeToWorld(Int2(x, y)) - hit).xz().Length() / Range;
            if (dst >= 1.0f) continue;
            // Smoothing function
            dst = dst * dst * (2.0f - dst * dst);
            // Perform mutation
            auto& hcell = heightMap[sizing.ToIndex(Int2(x, y))];
            hcell.Height = std::max(hcell.Height, (short)((1.0f - dst) * 1024.0f));
        }
    }
    // Allow the renderer (and other systems) to process updated terrain data
    world->GetLandscape()->NotifyLandscapeChanged(
        Landscape::LandscapeChangeEvent(RectInt::FromMinMax(min, max), true)
    );

    // Cancel interaction on mouse up
    if (!performance.IsDown(1)) performance.SetInteraction(nullptr);
}


void PlacementInteraction::SetPlacementProtoId(int protoId)
{
    mProtoId = protoId;
}
int PlacementInteraction::GetPlacementProtoId() const
{
    return mProtoId;
}
ActivationScore PlacementInteraction::GetActivation(Performance performance)
{
    if (mProtoId != -1 && !performance.IsDown()) return ActivationScore::MakeActive();
    return ActivationScore::MakeNone();
}
bool PlacementInteraction::OnBegin(Performance& performance)
{
    mOnRender = mPlay->RegisterOnRender([this](auto& cmdBuffer)
        {
            if (mProtoId == -1) return;
            auto& world = mPlay->GetWorld();
            auto prefab = world->GetPrototypes()->GetPrototypePrefab(mProtoId);
            auto model = world->GetPrototypes()->GetModel(prefab.get<Components::Renderable>()->mModelId);
            const auto& mat = world->GetLitMaterial();
            mat->SetUniform("Model", mTransform.GetMatrix());
            mat->SetUniform("Highlight", Color(0.5f, 0.5f, 0.5f, 0.5f));
            model->Render(cmdBuffer, mat);
        });
    return true;
}
void PlacementInteraction::OnUpdate(Performance& performance)
{
    // Update placement position
    auto mray = mPlay->GetCamera().ViewportToRay(performance.GetPositionCurrent() / mPlay->GetGraphics()->GetClientSize());
    mTransform = Components::Transform(mray.ProjectTo(Plane(Vector3::Up, 0.0f)), (float)std::numbers::pi);
    mTransform.mPosition.x = std::round(mTransform.mPosition.x);
    mTransform.mPosition.z = std::round(mTransform.mPosition.z);
    // Perform placement with left-click
    if (performance.FrameRelease(0))
    {
        auto constructionProtoId = mPlay->GetWorld()->GetPrototypes()->GetPrototypeId("Construction");
        auto construction = mPlay->GetWorld()->SpawnEntity(constructionProtoId, mPlay->GetWorld()->GetPlayer(1), mTransform);
        construction.set(Components::Construction{.mProtoId = mProtoId, });
        mPlay->SendActionRequest(Actions::ActionRequest{
            .mActionTypeId = Systems::BuildSystem::ActionId,
            .mTarget = construction,
        });
        performance.SetInteraction(nullptr);
    }
    // Cancel with right-click
    if (performance.FrameRelease(1))
    {
        performance.SetInteraction(nullptr);
    }
}
void PlacementInteraction::OnCancel(Performance& performance) { OnEnd(performance); }
void PlacementInteraction::OnEnd(Performance& performance)
{
    SetPlacementProtoId(-1);
}
