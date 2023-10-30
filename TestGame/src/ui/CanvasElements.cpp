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
	mDefaultStyle.mFontSize = size;
}
void CanvasText::SetColor(ColorB4 color) {
	mDefaultStyle.mColor = color;
	mIsInvalid = true;
}

void CanvasText::UpdateGlyphLayout(const CanvasLayout& layout) {
	float lineHeight = (float)mFont->GetLineHeight();
	mGlyphLayout.clear();
	mStyles.clear();
	mStyles.push_back(mDefaultStyle);
	Vector2 pos = Vector2::Zero;
	Vector2 size = Vector2::Zero;
	std::vector<ColorB4> colorStack;
	std::vector<float> sizeStack;
	int activeStyle = 0;
	auto CompareConsume = [](const std::string& str, int& c, std::string_view key) {
		if (str.compare(c, key.length(), key) != 0) return false;
		c += (int)key.length();
		return true;
	};
	for (int c = 0; c < mText.size(); ++c) {
		auto chr = mText[c];
		if (chr == '<') {
			static std::string_view colorkey = "<color=";
			if (CompareConsume(mText, c, colorkey)) {
				for (; c < mText.length() && std::isspace(mText[c]); ++c);
				CompareConsume(mText, c, "0x");
				CompareConsume(mText, c, "#");
				uint32_t color = 0;
				int count = 0;
				for (; c < mText.length() && std::isxdigit(mText[c]); ++c, ++count) {
					color = (color << 4) | (
						std::isdigit(mText[c]) ? mText[c] - '0' :
						(std::toupper(mText[c]) - 'A') + 10
					);
				}
				// Upscale to 32 bit
				if (count <= 4) color = ((color & 0xf000) * 0x11000) | ((color & 0x0f00) * 0x1100) | ((color & 0x00f0) * 0x110) | ((color & 0x000f) * 0x11);
				// If no alpha specified, force full alpha
				if (count == 3 || count == 6) color |= 0xff000000;
				colorStack.push_back(ColorB4::FromARGB(color));
				activeStyle = -1;
				continue;
			}
			static std::string_view colorendkey = "</color";
			if (CompareConsume(mText, c, colorendkey)) {
				colorStack.pop_back();
				activeStyle = -1;
				continue;
			}
			static std::string_view sizekey = "<size=";
			if (CompareConsume(mText, c, sizekey)) {
				char* end = &mText[c];
				sizeStack.push_back(std::strtof(end, &end));
				c = (int)(end - mText.data());
				activeStyle = -1;
				continue;
			}
			static std::string_view sizeendkey = "</size";
			if (CompareConsume(mText, c, sizeendkey)) {
				sizeStack.pop_back();
				activeStyle = -1;
				continue;
			}
		}
		auto glyphId = mFont->GetGlyphId(chr);
		auto glyph = mFont->GetGlyph(glyphId);
		if (glyph.mGlyph != chr) continue;

		if (activeStyle == -1) {
			auto style = mDefaultStyle;
			if (!colorStack.empty()) style.mColor = colorStack.back();
			if (!sizeStack.empty()) style.mFontSize = sizeStack.back();
			activeStyle = (int)(std::find(mStyles.begin(), mStyles.end(), style) - mStyles.begin());
			if (activeStyle >= mStyles.size()) mStyles.push_back(style);
		}

		auto& style = mStyles[activeStyle];
		float scale = style.mFontSize / lineHeight;
		auto glyphSize2 = Vector2((float)glyph.mAdvance, lineHeight) * scale;

		if (pos.x + glyphSize2.x >= layout.mAxisX.w) {
			pos.x = 0;
			pos.y += lineHeight * scale;
			if (pos.y + glyphSize2.y > layout.mAxisY.w) break;
			if (pos.x + glyphSize2.x >= layout.mAxisX.w) break;
		}
		mGlyphLayout.push_back(GlyphLayout{
			.mVertexOffset = -1,
			.mGlyphId = (uint16_t)glyphId,
			.mStyleId = (uint16_t)activeStyle,
			.mLocalPosition = pos + glyphSize2 / 2.0f,
		});
		pos.x += glyphSize2.x;
		size = Vector2::Max(size, Vector2(pos.x, pos.y + (float)(glyph.mOffset.y + glyph.mSize.y) * scale));
		if (c > 0)  pos.x += mFont->GetKerning(mText[c - 1], chr) * scale;
	}
	auto offset = (layout.GetSize() - size) / 2.0f;
	for (auto& layout : mGlyphLayout) {
		layout.mLocalPosition += offset;
	}
}
void CanvasText::UpdateLayout(const CanvasLayout& layout) {
	mLayout = layout;
	UpdateGlyphLayout(layout);

	int vcount = (int)mGlyphLayout.size() * 4;
	if (mIsInvalid || mBufferId == -1 || mBuilder->MapVertices(mBufferId).GetVertexCount() < vcount) {
		mIsInvalid = false;
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
		}
		{
			auto rectVerts = mBuilder->MapVertices(mBufferId);
			for (auto& color : rectVerts.GetColors()) color = mDefaultStyle.mColor;
		}
	}
	auto textVerts = mBuilder->MapVertices(mBufferId);
	auto positions = textVerts.GetPositions();
	auto uvs = textVerts.GetTexCoords();
	auto colors = textVerts.GetColors();
	auto atlasTexelSize = 1.0f / mFont->GetTexture()->GetSize().x;
	auto lineHeight = (float)mFont->GetLineHeight();
	int vindex = 0;
	for (int c = 0; c < mGlyphLayout.size(); ++c) {
		auto& layout = mGlyphLayout[c];
		auto& glyph = mFont->GetGlyph(layout.mGlyphId);
		if (glyph.mGlyph == -1) continue;
		auto& style = mStyles[layout.mStyleId];
		auto scale = style.mFontSize / lineHeight;
		layout.mVertexOffset = vindex;
		auto uv_1 = (Vector2)(glyph.mAtlasOffset) * atlasTexelSize;
		auto uv_2 = (Vector2)(glyph.mAtlasOffset + glyph.mSize) * atlasTexelSize;
		auto size2 = (Vector2)glyph.mSize * scale;
		auto glyphOffMin = (Vector2)glyph.mOffset - Vector2((float)glyph.mAdvance, lineHeight) / 2.0f;
		auto glyphPos0 = mLayout.TransformPosition2D(layout.mLocalPosition + glyphOffMin * scale);
		auto glyphDeltaX = mLayout.mAxisX.xy() * size2.x;
		auto glyphDeltaY = mLayout.mAxisY.xy() * size2.y;
		colors[vindex] = style.mColor;
		uvs[vindex] = Vector2(uv_1.x, uv_1.y);
		positions[vindex++] = glyphPos0;
		colors[vindex] = style.mColor;
		uvs[vindex] = Vector2(uv_2.x, uv_1.y);
		positions[vindex++] = glyphPos0 + glyphDeltaX;
		colors[vindex] = style.mColor;
		uvs[vindex] = Vector2(uv_1.x, uv_2.y);
		positions[vindex++] = glyphPos0 + glyphDeltaY;
		colors[vindex] = style.mColor;
		uvs[vindex] = Vector2(uv_2.x, uv_2.y);
		positions[vindex++] = glyphPos0 + glyphDeltaY + glyphDeltaX;
	}
	for (; vindex < positions.size(); ++vindex) positions[vindex] = { };
	textVerts.MarkChanged();
}
void CanvasText::UpdateAnimation(float timer) {
	timer -= floorf(timer / 4.0f) * 4.0f;
	auto lineHeight = mFont->GetLineHeight();
	auto textVerts = mBuilder->MapVertices(mBufferId);
	auto positions = textVerts.GetPositions();
	auto easein = Easing::ElasticOut(0.5f, 2.5f);
	auto easeout = Easing::Power2Out(0.333f);
	for (int c = 0; c < mGlyphLayout.size(); ++c) {
		auto& layout = mGlyphLayout[c];
		auto& glyph = mFont->GetGlyph(layout.mGlyphId);
		if (glyph.mGlyph == -1) continue;
		auto& style = mStyles[layout.mStyleId];
		int index = layout.mVertexOffset;
		float scale = style.mFontSize / mFont->GetLineHeight();
		auto glyphOffMin = (Vector2)glyph.mOffset - Vector2(glyph.mAdvance / 2.0f, lineHeight / 2.0f);
		auto glyphOffMax = glyphOffMin + (Vector2)glyph.mSize;
		glyphOffMin *= scale;
		glyphOffMax *= scale;
		auto l = easein(timer - c * 0.1f) * easeout(4.0f - timer);
		glyphOffMin = Vector2::Lerp(Vector2::Zero, glyphOffMin, l);
		glyphOffMax = Vector2::Lerp(Vector2::Zero, glyphOffMax, l);
		auto glyphPos0 = mLayout.TransformPosition2D(layout.mLocalPosition + glyphOffMin);
		auto glyphDeltaX = mLayout.mAxisX.xy() * (glyphOffMax.x - glyphOffMin.x);
		auto glyphDeltaY = mLayout.mAxisY.xy() * (glyphOffMax.y - glyphOffMin.y);
		positions[index++] = glyphPos0;
		positions[index++] = glyphPos0 + glyphDeltaX;
		positions[index++] = glyphPos0 + glyphDeltaY;
		positions[index++] = glyphPos0 + glyphDeltaY + glyphDeltaX;
	}
	textVerts.MarkChanged();
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
