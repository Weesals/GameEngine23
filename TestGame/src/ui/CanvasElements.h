#pragma once

#include "CanvasMeshBuilder.h"
#include "CanvasTransform.h"
#include <ui/font/FontRenderer.h>

#include <forward_list>

class CanvasElement {
protected:
	CanvasMeshBuilder* mBuilder;
	int mBufferId = -1;
	std::shared_ptr<Material> mMaterial;
	CanvasElement(CanvasMeshBuilder* builder) : mBufferId(-1), mBuilder(builder) { }
public:
	CanvasElement() : mBufferId(-1), mBuilder(nullptr) { }
	CanvasElement(const CanvasElement& other) = delete;
	CanvasElement(CanvasElement&& other) noexcept {
		mBuilder = other.mBuilder;
		mBufferId = other.mBufferId;
		other.mBufferId = -1;
	}
	~CanvasElement() {
		if (mBufferId != -1) mBuilder->Deallocate(mBufferId);
	}
	void SetMaterial(const std::shared_ptr<Material>& mat) { mMaterial = mat; }
	const std::shared_ptr<Material>& GetMaterial() const { return mMaterial; }
	bool IsValid() const { return mBufferId != -1; }
	int GetElementId() const { return mBufferId; }

};

class CanvasImage : public CanvasElement {
public:
	using CanvasElement::CanvasElement;
	CanvasImage(CanvasMeshBuilder* builder) : CanvasElement(builder) {
		mBufferId = mBuilder->Allocate(4, 6);
		auto rectVerts = mBuilder->MapVertices(mBufferId);
		Vector2 uv1(0.05f, 0.5f);
		auto uv = { uv1, uv1, uv1, uv1, };
		rectVerts.GetTexCoords().Set(uv);
		auto colors = { ColorB4::White, ColorB4::White, ColorB4::White, ColorB4::White, };
		rectVerts.GetColors().Set(colors);
		auto inds = { 0, 1, 2, 1, 3, 2, };
		rectVerts.GetIndices().Set(inds);
	}
	CanvasImage(CanvasImage&& other) noexcept : CanvasElement(std::move(other)) { }
	CanvasImage& operator =(CanvasImage&& other) noexcept {
		CanvasImage::~CanvasImage();
		return *new(this) CanvasImage(std::move(other));
	}
	void UpdateLayout(const CanvasLayout& layout) {
		auto rectVerts = mBuilder->MapVertices(mBufferId);
		std::array<Vector3, 4> p = { Vector3(0, 0, 0), Vector3(1, 0, 0), Vector3(0, 1, 0), Vector3(1, 1, 0), };
		for (auto& v : p) v = layout.TransformPositionN(v);
		rectVerts.GetPositions().Set(p);
		rectVerts.MarkChanged();
	}

};

class CanvasText : public CanvasElement {
protected:
	struct GlyphLayout {
		int mVertexOffset;
		int mGlyphId;
		Vector2 mLocalPosition;
	};
	std::string mText;
	std::shared_ptr<FontInstance> mFont;
	std::vector<GlyphLayout> mGlyphLayout;
	bool mIsInvalid = true;
	float mFontSize = 24;
public:
	using CanvasElement::CanvasElement;
	CanvasText(CanvasMeshBuilder* builder) : CanvasElement(builder) { }
	CanvasText(CanvasText&& other) noexcept : CanvasElement(std::move(other)) {
		mText = std::move(other.mText);
		mFont = std::move(other.mFont);
	}
	CanvasText& operator =(CanvasText&& other) noexcept {
		CanvasText::~CanvasText();
		return *new(this) CanvasText(std::move(other));
	}
	void SetText(std::string_view text);
	void SetFont(const std::shared_ptr<FontInstance>& font);
	void SetFontSize(float size);
	void UpdateLayout(const CanvasLayout& layout);
	void UpdateAnimation(float timer);
};


class CanvasCompositor {
	struct Node {
		int mContext;
		Node* mParent;
	};
	struct Item {
		Node* mNode;
		int mVertexRange;
	};
	struct Batch {
		std::shared_ptr<Material> mMaterial;
		RangeInt mIndexRange;
	};
	typedef std::forward_list<Node> NodeContainer;
	typedef std::forward_list<Item> ItemContainer;
	NodeContainer mNodes;
	ItemContainer mItems;
	std::vector<Batch> mBatches;
	CanvasMeshBuilder* mBuilder;
	BufferLayoutPersistent mIndices;
public:
	CanvasCompositor(CanvasMeshBuilder* builder);
	const BufferLayoutPersistent* GetIndices() const { return &mIndices; }
	void AppendElementData(int elementId, const std::shared_ptr<Material>& material);
	struct Builder {
		CanvasCompositor* mCompositor;
		NodeContainer::iterator mChildBefore;
		ItemContainer::iterator mItemBefore;
		int mIndex = 0;
		Builder(CanvasCompositor* compositor, ItemContainer::iterator itemBefore, NodeContainer::iterator childBefore);
		void AppendItem(Node* node, CanvasElement& element);
		NodeContainer::iterator InsertChild(Node* parent, int context);
		// Removes anything from this point onward with the specified node as a parent
		bool ClearChildrenRecursive(NodeContainer::iterator node);
	};
	struct Context {
		Builder* mBuilder;
		NodeContainer::iterator mNode;
		Context(Builder* builder, NodeContainer::iterator node)
			: mBuilder(builder), mNode(node)
		{ }
		CanvasCompositor* GetCompositor() const { return mBuilder->mCompositor; }
		NodeContainer& GetNodes() { return mBuilder->mCompositor->mNodes; }
		ItemContainer& GetItems() { return mBuilder->mCompositor->mItems; }
		void Append(CanvasElement& element) {
			mBuilder->AppendItem(&*mNode, element);
		}
		Context InsertChild(int element) {
			return Context(mBuilder, mBuilder->InsertChild(&*mNode, element));
		}
		void ClearRemainder() {
			if (mBuilder->mItemBefore != GetItems().end())
				mBuilder->ClearChildrenRecursive(mNode);
		}
	};
	Builder CreateBuilder() {
		if (mNodes.empty()) {
			mNodes.push_front(Node{ .mContext = -1, .mParent = nullptr, });
		}
		return Builder(this, mItems.before_begin(), mNodes.begin());
	}
	Context CreateRoot(Builder* builder) {
		return Context(builder, mNodes.begin());
	}

	void Render(CommandBuffer& cmdBuffer, const Material* material);
};
