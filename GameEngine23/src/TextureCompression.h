#pragma once

#include "MathTypes.h"
#include <vector>

struct InputData {
	Int3 size;
	unsigned char* data;
};
/*
namespace NVTTCompress {
	extern "C" void __declspec(dllexport) CompressTextureBC1(InputData * img, void* outData);
	extern "C" void __declspec(dllexport) CompressTextureBC2(InputData * img, void* outData);
	extern "C" void __declspec(dllexport) CompressTextureBC3(InputData * img, void* outData);
	extern "C" void __declspec(dllexport) CompressTextureBC4(InputData * img, void* outData);
	extern "C" void __declspec(dllexport) CompressTextureBC5(InputData * img, void* outData);
}
*/