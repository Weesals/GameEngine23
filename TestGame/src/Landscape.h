#pragma once

#include <MathTypes.h>
#include <Delegate.h>

#include <algorithm>
#include <numeric>
#include <string>
#include <vector>

// A terrain
class Landscape
{
public:
    // How many units of Height is 1 unit in world space
    static const int HeightScale = 1024;

    // Hold data related to the position/size of the landscape in the world
    struct SizingData
    {
        // Location of the terrain 0,0 coord (minimum corner)
        Vector3 Location;
        // How many cells the terrain contains
        Int2 Size;
        // The size of each cell in world-units/1024 (roughly mm)
        int Scale1024;
        SizingData(Int2 size, int scale1024 = 1024) : Size(size), Scale1024(scale1024) { }

        // Get the index of a specified cell coordinate
        int ToIndex(Int2 pnt) const { return pnt.x + pnt.y * Size.x; }
        // Get the cell for the specified index
        Int2 FromIndex(int index) const { return Int2(index % Size.x, index / Size.x); }

        // Check if a point exists inside of the terrain
        bool IsInBounds(Int2 pnt) const { return (uint32_t)pnt.x < (uint32_t)Size.x && (uint32_t)pnt.y < (uint32_t)Size.y; }

        // Convert from world to local (cell) space
        Int2 WorldToLandscape(Vector3 worldPos) const { return (Int2)((worldPos - Location).xz() * (1024.f / Scale1024) + 0.5f); }
        Int2 WorldToLandscape(Vector2 worldPos) const { return (Int2)((worldPos - Location.xz()) * (1024.f / Scale1024) + 0.5f); }
        Vector3 LandscapeToWorld(Int2 landscapePos) const { return Vector3((((Vector2)landscapePos) * (Scale1024 / 1024.f))).xzy() + Location; }

        Int2 WorldToLandscape(Vector2 worldPos, Vector2& outLerp) const
        {
            worldPos -= Location.xz();
            worldPos *= 1024.f / Scale1024;
            auto pnt = (Int2)(worldPos);
            outLerp = worldPos - (Vector2)pnt;
            return pnt;
        }
    };

    // Data used by the vertex shader to position a vertex (ie. its height)
    struct HeightCell
    {
        short Height;
        float GetHeightF() const { return (float)Height / (float)HeightScale; }
        static HeightCell Default;
    };
    // Data used by the fragment shader to determine shading (ie. texture)
    struct ControlCell
    {
        uint8_t TypeId;
        static ControlCell Default;
    };
    // Data used to render water over the terrain
    struct WaterCell
    {
        uint8_t Data;
        short GetHeight() {
            return (short)((Data - 127) << 3);
        }
        void SetHeight(short value) {
            Data = std::clamp((value >> 3) + 127, 0, 255);
        }
        bool GetIsInvalid() { return Data == 0; }
        static WaterCell Default;
    };

    // Contains data to determine what was changed in the terrain
    struct LandscapeChangeEvent
    {
        RectInt Range;
        bool HeightMapChanged;
        bool ControlMapChanged;
        bool WaterMapChanged;
        LandscapeChangeEvent();
        LandscapeChangeEvent(RectInt range, bool heightMap = false, bool controlMap = false, bool waterMap = false);
        bool GetHasChanges();

        // Expand this to include the passed in range/flags
        void CombineWith(const LandscapeChangeEvent& other);
        // Create a changed event that covers everything for the entire terrain
        static LandscapeChangeEvent All(Int2 size);
        // Create a changed event that includes nothing
        static LandscapeChangeEvent None();
    };

    struct LandscapeHit
    {
        Vector3 mHitPosition;
    };

    // Simplified data access API
    template<class CellType>
    class DataReader
    {
    protected:
        SizingData mSizing;
        const std::vector<CellType> mCells;
    public:
        DataReader(const SizingData& sizing, const std::vector<CellType> cells)
            : mSizing(sizing), mCells(cells) { }
        const CellType& GetAt(Int2 pnt) const { return mCells[mSizing.ToIndex(pnt)]; }
    };
    class HeightMapReadOnly : public DataReader<HeightCell> {
    public:
        using DataReader::DataReader;
        float GetHeightAtF(Vector2 pos) const;
    };
    class ControlMapReadOnly : public DataReader<ControlCell> { using DataReader::DataReader; };
    class WaterMapReadOnly : public DataReader<WaterCell> { using DataReader::DataReader; };

    typedef Delegate<const Landscape&, const LandscapeChangeEvent&> ChangeDelegate;

private:
    SizingData mSizing;
    std::vector<HeightCell> mHeightMap;
    std::vector<ControlCell> mControlMap;
    std::vector<WaterCell> mWaterMap;

    // Used to track if the landscape has changed since last
    int mRevision;

    // Listen for changes to the landscape data
    //std::vector<ChangeCallback> mChangeListeners;
    ChangeDelegate mChangeListeners;

public:
    Landscape();

    const SizingData& GetSizing() const { return mSizing; }
    Int2 GetSize() const { return mSizing.Size; }
    float GetScale() const { return (float)mSizing.Scale1024 / 1024.0f; }

    int GetRevision() const { return mRevision; }
    bool GetIsWaterEnabled() const { return !mWaterMap.empty(); }

    void SetLocation(Vector3 location);
    void SetSize(Int2 size);
    void SetScale(int scale1024);
    void SetWaterEnabled(bool enable);

    void NotifyLandscapeChanged()
    {
        NotifyLandscapeChanged(LandscapeChangeEvent::All(GetSize()));
    }
    void NotifyLandscapeChanged(const LandscapeChangeEvent& changeEvent)
    {
        ++mRevision;
        // TODO: Support listeners which add/remove other listeners or themselves
        //for (auto listener : mChangeListeners) listener(*this, changeEvent);
        mChangeListeners.Invoke(*this, changeEvent);
    }
    ChangeDelegate::Reference RegisterOnLandscapeChanged(const ChangeDelegate::Function& callback)
    {
        return mChangeListeners.Add(callback);
    }

    // Helper accessors
    HeightMapReadOnly GetHeightMap()
    {
        return HeightMapReadOnly(mSizing, mHeightMap);
    }
    ControlMapReadOnly GetControlMap()
    {
        return ControlMapReadOnly(mSizing, mControlMap);
    }
    WaterMapReadOnly GetWaterMap()
    {
        return WaterMapReadOnly(mSizing, mWaterMap);
    }

    // Get the raw data of the landscape; MUST manually call
    // NotifyLandscapeChanged with changed range afterwards
    std::vector<HeightCell> &GetRawHeightMap() { return mHeightMap; }
    std::vector<ControlCell> &GetRawControlMap() { return mControlMap; }
    std::vector<WaterCell> &GetRawWaterMap() { return mWaterMap; }

    bool Raycast(const Ray& ray, LandscapeHit& hit, float maxDst = std::numeric_limits<float>::max()) const;

};

