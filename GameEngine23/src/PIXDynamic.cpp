#define PIX _DEBUG

#if PIX
#define PIXGetCaptureState __PIXGetCaptureState
#define PIXReportCounter __PIXReportCounter
#define PIXEventsReplaceBlock __PIXEventsReplaceBlock
#define PIXGetThreadInfo __PIXGetThreadInfo
#define PIXNotifyWakeFromFenceSignal __PIXNotifyWakeFromFenceSignal
#define PIXRecordMemoryAllocationEvent __PIXRecordMemoryAllocationEvent
#define PIXRecordMemoryFreeEvent __PIXRecordMemoryFreeEvent
#include <pix3.h>
struct PIXEventsThreadInfo;
DWORD(WINAPI* Fn_PIXGetCaptureState)() = nullptr;
void(WINAPI* Fn_PIXReportCounter)(_In_ PCWSTR name, float value) = nullptr;
UINT64(WINAPI* Fn_PIXEventsReplaceBlock)(PIXEventsThreadInfo* threadInfo, bool getEarliestTime) = nullptr;
PIXEventsThreadInfo* (WINAPI* Fn_PIXGetThreadInfo)() noexcept = nullptr;
void(WINAPI* Fn_PIXNotifyWakeFromFenceSignal)(_In_ HANDLE event) noexcept = nullptr;
void(WINAPI* Fn_PIXRecordMemoryAllocationEvent)(USHORT allocatorId, void* baseAddress, size_t size, UINT64 metadata) noexcept = nullptr;
void(WINAPI* Fn_PIXRecordMemoryFreeEvent)(USHORT allocatorId, void* baseAddress, size_t size, UINT64 metadata) noexcept = nullptr;
extern "C" DWORD WINAPI PIXGetCaptureState() { return Fn_PIXGetCaptureState(); }
extern "C" void WINAPI PIXReportCounter(_In_ PCWSTR name, float value) { Fn_PIXReportCounter(name, value); }
extern "C" UINT64 WINAPI PIXEventsReplaceBlock(PIXEventsThreadInfo* threadInfo, bool getEarliestTime) noexcept {
    return Fn_PIXEventsReplaceBlock(threadInfo, getEarliestTime);
}
extern "C" PIXEventsThreadInfo* WINAPI PIXGetThreadInfo() noexcept {
    return Fn_PIXGetThreadInfo();
}
extern "C" void WINAPI PIXNotifyWakeFromFenceSignal(_In_ HANDLE event) {
    Fn_PIXNotifyWakeFromFenceSignal(event);
}
extern "C" void WINAPI PIXRecordMemoryAllocationEvent(USHORT allocatorId, void* baseAddress, size_t size, UINT64 metadata) {
    Fn_PIXRecordMemoryAllocationEvent(allocatorId, baseAddress, size, metadata);
}
extern "C" void WINAPI PIXRecordMemoryFreeEvent(USHORT allocatorId, void* baseAddress, size_t size, UINT64 metadata) {
    Fn_PIXRecordMemoryFreeEvent(allocatorId, baseAddress, size, metadata);
}

extern "C" HMODULE gPixModule;
#endif

void PIXMarkerBegin(ID3D12GraphicsCommandList6* cmdList, const std::wstring_view& name) {
#if PIX
    if (Fn_PIXGetCaptureState == nullptr && gPixModule) {
        HMODULE gPixModule = GetModuleHandle(L"WinPixEventRuntime.dll");
        if (!gPixModule) gPixModule = LoadLibrary(L"WinPixEventRuntime.dll");
        Fn_PIXGetCaptureState = (decltype(Fn_PIXGetCaptureState))GetProcAddress(gPixModule, "PIXGetCaptureState");
        Fn_PIXReportCounter = (decltype(Fn_PIXReportCounter))GetProcAddress(gPixModule, "PIXReportCounter");
        Fn_PIXEventsReplaceBlock = (decltype(Fn_PIXEventsReplaceBlock))GetProcAddress(gPixModule, "PIXEventsReplaceBlock");
        Fn_PIXGetThreadInfo = (decltype(Fn_PIXGetThreadInfo))GetProcAddress(gPixModule, "PIXGetThreadInfo");
        Fn_PIXNotifyWakeFromFenceSignal = (decltype(Fn_PIXNotifyWakeFromFenceSignal))GetProcAddress(gPixModule, "PIXNotifyWakeFromFenceSignal");
        Fn_PIXRecordMemoryAllocationEvent = (decltype(Fn_PIXRecordMemoryAllocationEvent))GetProcAddress(gPixModule, "PIXRecordMemoryAllocationEvent");
        Fn_PIXRecordMemoryFreeEvent = (decltype(Fn_PIXRecordMemoryFreeEvent))GetProcAddress(gPixModule, "PIXRecordMemoryFreeEvent");
    }
    if (Fn_PIXGetCaptureState) PIXBeginEvent(cmdList, 0, name.data());
#endif
}
void PIXMarkerEnd(ID3D12GraphicsCommandList6* cmdList) {
#if PIX
    if (Fn_PIXGetCaptureState) PIXEndEvent(cmdList);
#endif
}
