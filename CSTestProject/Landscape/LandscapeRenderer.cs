using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Landscape {
    public class LandscapeRenderer {

        public const int TileResolution = 8;

        // The mesh that is instanced across the surface
        CSMesh mTileMesh;
        CSTexture mHeightMap;
		CSTexture mControlMap;
		Material mLandMaterial;

		MeshDrawInstanced mLandscapeDraw;
		int mLandscapeDrawHash;

		LandscapeData mLandscapeData;

		int mRevision;

		struct Metadata {
            public float MinHeight;
			public float MaxHeight;
		};
		Metadata mMetadata;

		LandscapeData.LandscapeChangeEvent mDirtyRegion;

		public LandscapeRenderer() {
		}

        unsafe public void Initialise(LandscapeData landscapeData, Material rootMaterial) {
			mLandscapeData = landscapeData;
			if (mLandMaterial == null) {
                mLandMaterial = new Material("./assets/landscape.hlsl");
				if (rootMaterial != null)
					mLandMaterial.InheritProperties(rootMaterial);
				var tex = CSResources.LoadTexture("./assets/T_Grass_BaseColor.png");
				mLandMaterial.SetTexture("GrassTexture", tex);
			}
			mLandscapeData.mChangeListeners += LandscapeChanged;
			mLandscapeDraw = new MeshDrawInstanced(RequireTileMesh(), mLandMaterial);
			mLandscapeDraw.AddInstanceElement("INSTANCE", BufferFormat.FORMAT_R16G16_UINT, sizeof(Short2));
		}

        private void LandscapeChanged(LandscapeData data, LandscapeData.LandscapeChangeEvent changed) {
            mDirtyRegion.CombineWith(changed);
        }

        public CSMesh RequireTileMesh() {
            if (!mTileMesh.IsValid()) {
                mTileMesh = CSMesh.Create("LandscapeTile");
                mTileMesh.RequireVertexNormals(BufferFormat.FORMAT_R8G8B8A8_UNORM);
                mTileMesh.SetVertexCount((TileResolution + 1) * (TileResolution + 1));
                mTileMesh.SetIndexCount(TileResolution * TileResolution * 6);
                var data = mTileMesh.GetMeshData();
                var vpositions = new TypedBufferView<Vector3>(data.mPositions, new RangeInt(0, data.mVertexCount));
                var vnormals = new TypedBufferView<Vector3>(data.mNormals, new RangeInt(0, data.mVertexCount));
                for (int y = 0; y < TileResolution + 1; ++y) {
                    for (int x = 0; x < TileResolution + 1; ++x) {
                        int v = x + y * (TileResolution + 1);
                        vpositions[v] = new Vector3((float)x, 0, (float)y);
                        vnormals[v] = new Vector3(0.0f, 1.0f, 0.0f);
                    }
                }
                var indices = new TypedBufferView<int>(data.mIndices, new RangeInt(0, data.mIndexCount));
                for (int y = 0; y < TileResolution; ++y) {
                    for (int x = 0; x < TileResolution; ++x) {
                        int i = (x + y * TileResolution) * 6;
                        int v0 = x + (y + 0) * (TileResolution + 1);
                        int v1 = x + (y + 1) * (TileResolution + 1);
                        indices[i + 0] = v0;
                        indices[i + 1] = v1 + 1;
                        indices[i + 2] = v0 + 1;
                        indices[i + 3] = v0;
                        indices[i + 4] = v1;
                        indices[i + 5] = v1 + 1;
                    }
                }
            }
            return mTileMesh;
        }

        unsafe public void Render(CSGraphics graphics, RenderPass pass) {
            var scale = mLandscapeData.GetScale();
            var xform = Matrix4x4.CreateScale(scale, 1.0f, scale) *
                Matrix4x4.CreateTranslation(mLandscapeData.GetSizing().Location);
            var frustum = pass.Frustum;
            var viewProj = frustum.CalculateViewProj();
            var localFrustum = frustum.TransformToLocal(xform);

            // Pack heightmap data into a texture
            if (!mHeightMap.IsValid()) {
                mHeightMap = CSTexture.Create();
                mHeightMap.SetSize(mLandscapeData.GetSize());
                mRevision = -1;
                mDirtyRegion = LandscapeData.LandscapeChangeEvent.MakeAll(mLandscapeData.GetSize());
            }
            // The terrain has changed, need to update the texture
            if (mDirtyRegion.GetHasChanges()) {
                // Get the heightmap data
                var heightmap = mLandscapeData.GetRawHeightMap();
                var sizing = mLandscapeData.GetSizing();
                // Get the min/max range of heights (to normalize into the texture)
                int heightMin = int.MaxValue, heightMax = int.MinValue;
                foreach (var height in heightmap) {
                    heightMin = Math.Min(heightMin, height.Height);
                    heightMax = Math.Max(heightMax, height.Height);
                }
                // Get the inner texture data
                var pxHeightData = mHeightMap.GetTextureData();
                var range = mDirtyRegion.Range;
                for (int y = range.Min.Y; y < range.Max.Y; ++y) {
                    for (int x = range.Min.X; x < range.Max.X; ++x) {
                        var px = sizing.ToIndex(new Int2(x, y));
                        var h11 = heightmap[px];
                        uint c = 0;
                        // Pack height into first byte
                        ((byte*)&c)[0] = (byte)(255 * (heightmap[px].Height - heightMin) / Math.Max(1, heightMax - heightMin));
                        {
                            var h01 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x - 1, y), 0, sizing.Size - 1))];
                            var h21 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x + 1, y), 0, sizing.Size - 1))];
                            var h10 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x, y - 1), 0, sizing.Size - 1))];
                            var h12 = heightmap[sizing.ToIndex(Int2.Clamp(new Int2(x, y + 1), 0, sizing.Size - 1))];
                            var nrm = new Vector3((float)(h01.Height - h21.Height), (float)sizing.Scale1024, (float)(h10.Height - h12.Height));
                            nrm = Vector3.Normalize(nrm);
                            // Pack normal into 2nd and 3rd
                            ((byte*)&c)[1] = (byte)(127 + (nrm.X * 127));
                            ((byte*)&c)[2] = (byte)(127 + (nrm.Z * 127));
                        }

                        // Write the pixel
                        ((uint*)pxHeightData.mData)[px] = c;
                    }
                }
                // Mark the texture as having been changed
                mHeightMap.MarkChanged();
                // Allow the shader to reconstruct the height range
                mMetadata.MinHeight = (float)heightMin / LandscapeData.HeightScale;
                mMetadata.MaxHeight = (float)heightMax / LandscapeData.HeightScale;
                // Mark the data as current
                mDirtyRegion = LandscapeData.LandscapeChangeEvent.MakeNone();
                mRevision = mLandscapeData.GetRevision();

                // Calculate material parameters
                mLandMaterial.SetValue("Model", xform);
                mLandMaterial.SetTexture("HeightMap", mHeightMap);
                mLandMaterial.SetValue("HeightRange", new Vector4(mMetadata.MinHeight, mMetadata.MaxHeight, 0.0f, 0.0f));
            }

            // How many tile instances we need to render
            Int2 tileCount = (mLandscapeData.GetSize() + TileResolution - 1) / TileResolution;

            // Calculate the min/max AABB of the projected frustum 
            Vector2 visMin, visMax;
            {
                Span<Vector3> points = stackalloc Vector3[4];
                localFrustum.IntersectPlane(Vector3.UnitY, 0.0f, points);
                visMin = points[0].toxz();
                visMax = points[0].toxz();
                foreach (var point in points) {
                    visMin = Vector2.Min(visMin, point.toxz());
                    visMax = Vector2.Max(visMax, point.toxz());
                }
            }
            Int2 visMinI = Int2.Max(Int2.FloorToInt(visMin / TileResolution), (Int2)0);
            Int2 visMaxI = Int2.Min(Int2.CeilToInt(visMax / TileResolution), tileCount - 1);

            var instanceOffsets = graphics.RequireFrameData<UShort2>(Int2.CMul(visMaxI - visMinI + 1));
            int i = 0;
            // Render the generated instances
            for (int y = visMinI.Y; y <= visMaxI.Y; ++y) {
                for (int x = visMinI.X; x <= visMaxI.X; ++x) {
                    var value = new UShort2((ushort)(x * TileResolution), (ushort)(y * TileResolution));
                    var ctr = new Vector3((x + 0.5f) * TileResolution, 1.0f, (y + 0.5f) * TileResolution);
                    var ext = new Vector3(TileResolution / 2.0f, 2.0f, TileResolution / 2.0f);
                    if (!localFrustum.GetIsVisible(ctr, ext)) continue;
                    instanceOffsets[i] = value;
                    ++i;
                }
            }
            // TODO: Return the unused per-frame data?

            var drawHash = 0;
            foreach (var item in instanceOffsets) drawHash = HashCode.Combine(drawHash, item);
            fixed (UShort2* instanceData = instanceOffsets) {
                mLandscapeDraw.SetInstanceData(instanceData, i, 0, drawHash != mLandscapeDrawHash);
            }
            mLandscapeDraw.Draw(graphics, pass, CSDrawConfig.MakeDefault());
            mLandscapeDrawHash = drawHash;
        }


    }
}
