#include "CanvasElements.h"

#include <GraphicsDeviceBase.h>
#include <MaterialEvaluator.h>
#include <Containers.h>

void CanvasText::SetText(std::string_view text) {
	mText = text;
	mIsInvalid = true;
}
void CanvasText::SetFont(const std::shared_ptr<FontInstance>& font) {
	mFont = font;
	if (mMaterial == nullptr) mMaterial = std::make_shared<Material>(L"assets/text.hlsl");
	mMaterial->SetUniformTexture("Texture", mFont->GetTexture());
	mIsInvalid = true;
}
void CanvasText::SetFontSize(float size) {
	mFontSize = size;
}

void CanvasText::UpdateLayout(const CanvasLayout& layout) {
	if (mIsInvalid) {
		mIsInvalid = false;
		int vcount = 0;
		for (auto chr : mText) {
			auto glyph = mFont->GetGlyph(mFont->GetGlyphId(chr));
			if (glyph.mGlyph != chr) continue;
			vcount += 4;
		}
		if (mBufferId != -1 && mBuilder->MapVertices(mBufferId).GetVertexCount() != vcount) {
			mBuilder->Deallocate(mBufferId);
			mBufferId = -1;
		}
		if (mBufferId == -1) {
			mBufferId = mBuilder->Allocate(vcount, vcount * 6 / 4);
			auto rectVerts = mBuilder->MapVertices(mBufferId);
			auto inds = rectVerts.GetIndices();
			for (int v = 0, i = 0; i < inds.size(); i += 6, v += 4) {
				inds[i + 0] = v + 0;
				inds[i + 1] = v + 1;
				inds[i + 2] = v + 2;
				inds[i + 3] = v + 1;
				inds[i + 4] = v + 3;
				inds[i + 5] = v + 2;
			}
			for (auto& color : rectVerts.GetColors()) color = ColorB4::White;
		}
	}
	auto textVerts = mBuilder->MapVertices(mBufferId);
	auto positions = textVerts.GetPositions();
	auto uvs = textVerts.GetTexCoords();
	int index = 0;
	Vector2 pos = layout.mPosition.xy();
	float atlasTexelSize = 1.0f / mFont->GetTexture()->GetSize().x;
	float lineHeight = (float)mFont->GetLineHeight();
	mGlyphLayout.resize(mText.size());
	float scale = mFontSize / mFont->GetLineHeight();
	for (int c = 0; c < mText.size(); ++c) {
		auto chr = mText[c];
		auto glyphId = mFont->GetGlyphId(chr);
		auto glyph = mFont->GetGlyph(glyphId);
		if (glyph.mGlyph != chr) continue;
		auto uv_1 = (Vector2)glyph.mAtlasOffset * atlasTexelSize;
		auto uv_2 = (Vector2)(glyph.mAtlasOffset + glyph.mSize) * atlasTexelSize;
		auto glypSize2 = Vector2((float)glyph.mAdvance, lineHeight) * scale;
		auto size2 = (Vector2)glyph.mSize * scale;
		auto offset2 = (Vector2)glyph.mOffset * scale;

		mGlyphLayout[c] = GlyphLayout{
			.mVertexOffset = index,
			.mGlyphId = glyphId,
			.mLocalPosition = pos + glypSize2 / 2.0f,
		};

		auto glyphPos0 = pos + offset2;
		auto glyphPos1 = glyphPos0 + size2;
		uvs[index] = Vector2(uv_1.x, uv_1.y);
		positions[index++] = Vector2(glyphPos0.x, glyphPos0.y);;
		uvs[index] = Vector2(uv_2.x, uv_1.y);
		positions[index++] = Vector2(glyphPos1.x, glyphPos0.y);
		uvs[index] = Vector2(uv_1.x, uv_2.y);
		positions[index++] = Vector2(glyphPos0.x, glyphPos1.y);
		uvs[index] = Vector2(uv_2.x, uv_2.y);
		positions[index++] = Vector2(glyphPos1.x, glyphPos1.y);

		pos.x += glypSize2.x;
		if (c > 0)  pos.x += mFont->GetKerning(mText[c - 1], chr) * scale;
	}
	textVerts.MarkChanged();
}
void CanvasText::UpdateAnimation(float timer) {
	mFontSize = 120;
	timer -= floorf(timer / 4.0f) * 4.0f;
	auto lineHeight = mFont->GetLineHeight();
	auto textVerts = mBuilder->MapVertices(mBufferId);
	auto positions = textVerts.GetPositions();
	auto easein = Easing::ElasticOut(0.5f, 2.5f);
	auto easeout = Easing::Power2Out(0.333f);
	float scale = mFontSize / mFont->GetLineHeight();
	for (int c = 0; c < mGlyphLayout.size(); ++c) {
		auto& layout = mGlyphLayout[c];
		auto& glyph = mFont->GetGlyph(layout.mGlyphId);
		int index = layout.mVertexOffset;
		auto glyphPos0 = layout.mLocalPosition
			+ ((Vector2)glyph.mOffset
			- Vector2(glyph.mAdvance / 2.0f, lineHeight / 2.0f)) * scale;
		auto glyphPos1 = glyphPos0 + (Vector2)glyph.mSize * scale;
		auto l = easein(timer - c * 0.1f) * easeout(4.0f - timer);
		glyphPos0 = Vector2::Lerp(layout.mLocalPosition, glyphPos0, l);
		glyphPos1 = Vector2::Lerp(layout.mLocalPosition, glyphPos1, l);
		positions[index++] = Vector2(glyphPos0.x, glyphPos0.y);
		positions[index++] = Vector2(glyphPos1.x, glyphPos0.y);
		positions[index++] = Vector2(glyphPos0.x, glyphPos1.y);
		positions[index++] = Vector2(glyphPos1.x, glyphPos1.y);
	}
}



CanvasCompositor::Builder::Builder(CanvasCompositor* compositor, ItemContainer::iterator itemBefore, NodeContainer::iterator childBefore)
	: mCompositor(compositor), mChildBefore(childBefore), mItemBefore(itemBefore)
{ }
void CanvasCompositor::Builder::AppendItem(Node* node, CanvasElement& element) {
	auto next = mItemBefore; ++next;
	if (next != mCompositor->mItems.end() && next->mNode == node) {
		next->mVertexRange = element.GetElementId();
		mItemBefore = next;
	}
	else {
		mItemBefore = mCompositor->mItems.insert_after(mItemBefore, Item{
			.mNode = node,
			.mVertexRange = element.GetElementId(),
		});
		// Clear all future indices
		mCompositor->mIndices.mCount = mIndex;
	}
	if (mIndex >= mCompositor->mIndices.mCount)
		mCompositor->AppendElementData(element.GetElementId(), element.GetMaterial());
	auto verts = mCompositor->mBuilder->MapVertices(element.GetElementId());
	mIndex += verts.mIndexRange.length;
}
CanvasCompositor::NodeContainer::iterator CanvasCompositor::Builder::InsertChild(Node* parent, int context) {
	auto next = mChildBefore; ++next;
	if (next != mCompositor->mNodes.end() && next->mContext == context) {
		next->mParent = parent;
		mChildBefore = next;
	}
	else {
		mChildBefore = mCompositor->mNodes.insert_after(mChildBefore, Node{
			.mContext = context,
			.mParent = parent,
		});
	}
	return mChildBefore;
}
// Removes anything from this point onward with the specified node as a parent
bool CanvasCompositor::Builder::ClearChildrenRecursive(NodeContainer::iterator node) {
	for (; ; ) {
		auto item = mItemBefore; ++item;
		if (item == mCompositor->mItems.end()) break;
		auto p = item->mNode;
		// Must be a child (or at the end)
		if (p != &*node) {
			auto child = mChildBefore; ++child;
			if (child == mCompositor->mNodes.end() || child->mParent != &*node) break;
			if (ClearChildrenRecursive(child)) {
				auto next = mChildBefore; ++next;
				assert(next == child);
				mCompositor->mNodes.erase_after(mChildBefore);
			}
			continue;
		}
		mCompositor->mItems.erase_after(mItemBefore);
		//itemBefore = item;
	}
	return true;
}

CanvasCompositor::CanvasCompositor(CanvasMeshBuilder* builder)
	: mBuilder(builder)
{
	mIndices = BufferLayoutPersistent((size_t)this + 1, 0, BufferLayout::Usage::Index, 0);
	mIndices.AppendElement(BufferLayout::Element("INDEX", BufferFormat::FORMAT_R32_UINT));
}
void CanvasCompositor::AppendElementData(int elementId, const std::shared_ptr<Material>& material) {
	auto verts = mBuilder->MapVertices(elementId);
	auto inds = verts.GetIndices();
	if (mIndices.mAllocCount < mIndices.mCount + inds.size()) {
		mIndices.AllocResize(
			std::max(
				mIndices.mAllocCount + 2048,
				mIndices.mCount + (int)inds.size()
			)
		);
	}
	int istart = mIndices.mCount;
	int icount = (int)inds.size();
	if (mBatches.empty() || mBatches.back().mMaterial != material) {
		mBatches.push_back(Batch{
			.mMaterial = material,
			.mIndexRange = RangeInt(istart, 0)
		});
	}
	TypedBufferView<uint32_t> outInds(&mIndices.mElements[0], RangeInt(istart, icount));
	for (int i = 0; i < inds.size(); ++i) {
		outInds[i] = (int)inds[i] + verts.GetPositions().mRange.start;
	}
	mIndices.mCount += icount;
	mBatches.back().mIndexRange.length += icount;
}
void CanvasCompositor::Render(CommandBuffer& cmdBuffer, const Material* material) {
	if (mIndices.mCount == 0) return;
	std::vector<const BufferLayout*> bindings;
	bindings.push_back(GetIndices());
	bindings.push_back(mBuilder->GetVertices());
	for (const auto& batch : mBatches) {
		InplaceVector<const Material*> materials;
		if (batch.mMaterial != nullptr) materials.push_back(batch.mMaterial.get());
		materials.push_back(material);
		auto pso = cmdBuffer.GetGraphics()->RequirePipeline(bindings, materials);
		auto resources = MaterialEvaluator::ResolveResources(cmdBuffer, pso, materials);
		auto drawConfig = DrawConfig::MakeDefault();
		drawConfig.mIndexBase = batch.mIndexRange.start;
		drawConfig.mIndexCount = batch.mIndexRange.length;
		cmdBuffer.DrawMesh(bindings, pso, resources, drawConfig);
	}
}
