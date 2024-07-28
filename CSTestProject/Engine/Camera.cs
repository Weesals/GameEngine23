using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public class Camera {
        private float fov = 3.14f / 2.0f;
        private float aspect = 1.0f;
        private float nearPlane = 0.1f;
        private float farPlane = 1000.0f;
        private Vector3 position = Vector3.Zero;
        private Quaternion orientation = Quaternion.Identity;

        private Matrix4x4 projectionMatrix;
        private Matrix4x4 viewMatrix;

        public float FOV {
            get => fov;
            set { if (fov == value) return; fov = value; InvalidateProjection(); }
        }
        public float Aspect {
            get => aspect;
            set { if (aspect == value) return; aspect = value; InvalidateProjection(); }
        }
        public float NearPlane {
            get => nearPlane;
            set { if (nearPlane == value) return; nearPlane = value; InvalidateProjection(); }
        }
        public float FarPlane {
            get => farPlane;
            set { if (farPlane == value) return; farPlane = value; InvalidateProjection(); }
        }

        public Vector3 Position {
            get => position;
            set { if (position == value) return; position = value; InvalidateView(); }
        }
        public Quaternion Orientation {
            get => orientation;
            set { if (orientation == value) return; orientation = value; InvalidateView(); }
        }

        public Vector3 Right => Vector3.Transform(Vector3.UnitX, orientation);
        public Vector3 Up => Vector3.Transform(Vector3.UnitY, orientation);
        public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, orientation);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvalidateProjection() {
            projectionMatrix.M11 = float.MaxValue;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvalidateView() {
            viewMatrix.M11 = float.MaxValue;
        }

        // Get (and calculate if needed) the camera matrices
        public Matrix4x4 GetProjectionMatrix() {
            if (projectionMatrix.M11 == float.MaxValue) {
                projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, nearPlane, farPlane);
                projectionMatrix = projectionMatrix.RHSToLHS();
            }
            return projectionMatrix;
        }
        public Matrix4x4 GetViewMatrix() {
            if (viewMatrix.M11 == float.MaxValue) {
                viewMatrix = Matrix4x4.CreateFromQuaternion(orientation) *
                    Matrix4x4.CreateTranslation(position);
                Matrix4x4.Invert(viewMatrix, out viewMatrix);
            }
            return viewMatrix;
        }
        public Matrix4x4 GetWorldMatrix() {
            return Matrix4x4.CreateFromQuaternion(orientation) *
                Matrix4x4.CreateTranslation(position);
        }

        // Viewport space is [0, 1]
        public Ray ViewportToRay(Vector2 vpos) {
            var viewProj = (GetViewMatrix() * GetProjectionMatrix());
            Matrix4x4.Invert(viewProj, out viewProj);
            var pos4 = new Vector4(vpos.X * 2.0f - 1.0f, 1.0f - vpos.Y * 2.0f, 0.0f, 1.0f);
            var origin = Vector4.Transform(pos4, viewProj);
            pos4.Z = 1.0f;
            var dest = Vector4.Transform(pos4, viewProj);
            origin /= origin.W;
            dest /= dest.W;
            return new Ray(origin.toxyz(), (dest - origin).toxyz());
        }
    }
}
