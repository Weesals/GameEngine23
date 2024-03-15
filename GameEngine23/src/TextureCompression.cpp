//#include "../externals/nvtt/nvtt.h"
#include "MathTypes.h"
#include "TextureCompression.h"
#include <stdint.h>
#include <vector>
/*
void CompressTextureData(InputData* img, nvtt::Format format, void* outData) {
	auto& size = img->size;
	auto& data = img->data;
	nvtt::InputOptions inputOptions;
	inputOptions.setTextureLayout(nvtt::TextureType_2D, size.x, size.y, size.z);
	inputOptions.setMipmapGeneration(false, -1);
	inputOptions.setMipmapData(data, size.x, size.y, size.z);
	inputOptions.setGamma(1.0f, 1.0f);
	inputOptions.setWrapMode(nvtt::WrapMode_Repeat);
	inputOptions.setFormat(nvtt::InputFormat_BGRA_8UB);

	nvtt::CompressionOptions compressionOptions;
	compressionOptions.setQuality(nvtt::Quality_Normal);
	compressionOptions.setFormat(format);
	compressionOptions.setColorWeights(1, 1, 1);

	nvtt::Compressor compressor;
	compressor.enableCudaAcceleration(true);

	struct : public nvtt::OutputHandler {
		std::vector<uint8_t> buffer;
		virtual void beginImage(int size, int width, int height, int depth, int face, int miplevel) { }
		virtual void endImage() { }
		virtual bool writeData(const void* data, int size) {
			int oldSize = (int)buffer.size();
			buffer.resize(buffer.size() + size);
			std::memcpy(buffer.data() + oldSize, data, size);
			return true;
		}
	} outputHandler;
	outputHandler.buffer.reserve(compressor.estimateSize(inputOptions, compressionOptions));

	nvtt::OutputOptions outputOptions;
	outputOptions.setOutputHeader(false);
	outputOptions.setOutputHandler(&outputHandler);

	compressor.process(inputOptions, compressionOptions, outputOptions);
	std::memcpy(outData, outputHandler.buffer.data(), outputHandler.buffer.size());
}

namespace NVTTCompress {

	void CompressTextureBC1(InputData* img, void* outData) {
		return CompressTextureData(img, nvtt::Format_BC1, outData);
	}
	void CompressTextureBC2(InputData* img, void* outData) {
		return CompressTextureData(img, nvtt::Format_BC2, outData);
	}
	void CompressTextureBC3(InputData* img, void* outData) {
		return CompressTextureData(img, nvtt::Format_BC3, outData);
	}
	void CompressTextureBC4(InputData* img, void* outData) {
		return CompressTextureData(img, nvtt::Format_BC4, outData);
	}
	void CompressTextureBC5(InputData* img, void* outData) {
		return CompressTextureData(img, nvtt::Format_BC5, outData);
	}
}
*/