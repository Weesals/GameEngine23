#include "../../MathTypes.h"
#include "../../Containers.h"
#include "../../Texture.h"

#include <vector>
#include <memory>

class Sprite {
	std::vector<Vector2> mPolygon;
};

void GenerateOutline(const std::shared_ptr<Texture>& texture) {
	auto pxdata = texture->GetData();
	auto size = texture->GetSize();
}
