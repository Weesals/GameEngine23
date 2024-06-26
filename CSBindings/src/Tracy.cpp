#define TRACY_ENABLE
#define _CRT_SECURE_NO_WARNINGS

#include "../externals/tracy/public/tracy/Tracy.hpp"
#include "../externals/tracy/public/TracyClient.cpp"
//#include <../externals/tracy-profiler/client/TracyProfiler.hpp>
//#include <../externals/tracy-profiler/client/TracyScoped.hpp>

#if defined(_MSC_VER)
#define PER_EXPORT extern "C" __declspec(dllexport)
#define PER_CALL __stdcall
#else
#define PER_EXPORT extern "C" __attribute__((visibility("default")))
#define PER_CALL
#endif

PER_EXPORT tracy::SourceLocationData* PER_CALL TracyCreateLocation(const char* name, const char* method, const char* file,
    uint32_t line, uint32_t color) {
    const auto location = new tracy::SourceLocationData{ name, method, file, line, color };
    return location;
}

PER_EXPORT tracy::ScopedZone* PER_CALL TracyCreateZone(tracy::SourceLocationData* location) {
    const auto zone = new tracy::ScopedZone(location, true);
    return zone;
}

PER_EXPORT void PER_CALL TracyDeleteZone(const tracy::ScopedZone* ptr) {
    delete ptr;
}

PER_EXPORT void PER_CALL TracyZoneText(tracy::ScopedZone& zone, const char* text) {
    zone.Text(text, strlen(text));
}

PER_EXPORT void PER_CALL TracyZoneName(tracy::ScopedZone& zone, const char* name) {
    zone.Name(name, strlen(name));
}

PER_EXPORT void PER_CALL TracyZoneColor(tracy::ScopedZone& zone, uint32_t color) {
    zone.Color(color);
}

PER_EXPORT void PER_CALL TracyZoneValue(tracy::ScopedZone& zone, uint64_t value) {
    zone.Value(value);
}

PER_EXPORT void PER_CALL TracyFrameMark() {
    FrameMark;
}

PER_EXPORT void PER_CALL TracyFrameMarkNamed(const char* name) {
    FrameMarkNamed(name);
}

PER_EXPORT void PER_CALL TracyFrameMarkStart(const char* name) {
    FrameMarkStart(name);
}

PER_EXPORT void PER_CALL TracyFrameMarkEnd(const char* name) {
    FrameMarkEnd(name);
}

PER_EXPORT void PER_CALL TracyPlotData(const char* name, int64_t value) {
    TracyPlot(name, value);
}

PER_EXPORT void PER_CALL TracyConfigurePlot(const char* name, tracy::PlotFormatType type, bool step, bool fill, uint32_t color) {
    TracyPlotConfig(name, type, step, fill, color);
}

PER_EXPORT void PER_CALL TracyMessageAppInfo(const char* text) {
    TracyAppInfo(text, strlen(text));
}

PER_EXPORT void PER_CALL TracyTracyMessage(const char* text) {
    TracyMessage(text, strlen(text));
}

PER_EXPORT void PER_CALL TracyTracyMessageColor(const char* text, uint32_t color) {
    TracyMessageC(text, strlen(text), color);
}

PER_EXPORT void PER_CALL TracyMemAlloc(void* ptr, size_t size) {
    TracyAlloc(ptr, size);
}

PER_EXPORT void PER_CALL TracyMemFree(void* ptr) {
    TracyFree(ptr);
}

void* SimpleProfilerMarker(const char* name) {
    auto location = TracyCreateLocation(name, name, name, 0, 0xffffff);
    return TracyCreateZone(location);
}

void SimpleProfilerMarkerEnd(void* zone) {
    TracyDeleteZone((tracy::ScopedZone*)zone);
}
