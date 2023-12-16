using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public class Model {

        private List<Mesh> meshes = new();

        public IReadOnlyList<Mesh> Meshes => meshes;

        public void AppendMesh(Mesh mesh) {
            meshes.Add(mesh);
        }

        public static Model CreateFrom(CSModel other) {
            var model = new Model();
            foreach (var mesh in other.Meshes) {
                model.AppendMesh(Mesh.CreateFrom(mesh));
            }
            return model;
        }

    }
}
