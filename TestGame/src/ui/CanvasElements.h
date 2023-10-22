#pragma once

#include "CanvasMeshBuilder.h"
#include "CanvasTransform.h"

#include <list>

class CanvasElement {
protected:
	CanvasMeshBuilder* mBuilder;
	int mBufferId;
	CanvasElement(CanvasMeshBuilder* builder) : mBufferId(-1), mBuilder(builder) { }
public:
	CanvasElement() : mBufferId(-1), mBuilder(nullptr) { }
	bool IsValid() const { return mBufferId != -1; }
	int GetElementId() const { return mBufferId; }

};

class CanvasImage : public CanvasElement {
public:
	using CanvasElement::CanvasElement;
	CanvasImage(CanvasMeshBuilder* builder) : CanvasElement(builder) {
		mBufferId = mBuilder->Allocate(4, 6);
	}
	~CanvasImage() {
		mBuilder->Deallocate(mBufferId);
	}

	void UpdateLayout(const CanvasLayout& layout) {
		auto rectVerts = mBuilder->MapVertices(mBufferId);
		std::array<Vector3, 4> p = { Vector3(0, 0, 0), Vector3(1, 0, 0), Vector3(0, 1, 0), Vector3(1, 1, 0), };
		for (auto& v : p) v = layout.TransformPositionN(v);
		rectVerts.GetPositions().Set(p);
		auto uv = { Vector2(0, 0), Vector2(1, 0), Vector2(0, 1), Vector2(1, 1), };
		rectVerts.GetTexCoords().Set(uv);
		auto colors = { ColorB4::White, ColorB4::White, ColorB4::White, ColorB4::White, };
		rectVerts.GetColors().Set(colors);
		auto inds = { 0, 1, 2, 1, 3, 2, };
		rectVerts.GetIndices().Set(inds);
		rectVerts.MarkChanged();
	}

};


class CanvasCompositor {
	struct Node {
		int mContext;
		int mChildCount;
		
	};
	struct Item {
		int mVertexRange;
		int mContext;
	};
	std::list<Item> mItems;
public:
	struct Context {
		CanvasCompositor* mCompositor;
		int mContext;
		std::list<Item>::iterator mInsertion;
		void Append(CanvasElement& element) {
			//mItems
		}
		void Clear() {

		}
	};
};
