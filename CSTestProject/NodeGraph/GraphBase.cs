using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.NodeGraph {
    public class NodeProxy {

        public readonly string Name;

        public Int2 Position;

        public NodeProxy(string name) {
            Name = name;
        }

    }
    public class GraphBase {

        private List<NodeProxy> nodes = new();

        public CanvasRenderable Canvas;

        public NodeProxy CreateNodeProxy(string name) {
            var proxy = new NodeProxy(name);
            nodes.Add(proxy);
            return proxy;
        }

    }
}
