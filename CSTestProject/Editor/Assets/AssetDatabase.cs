using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Editor.Assets {
    public class AssetMetadata {
        public string Identifier;
        public AssetMetadata(string identifier) {
            Identifier = identifier;
        }
    }
    public class AssetDatabase {

        private Dictionary<string, AssetMetadata> metadata = new();

        public AssetMetadata AllocateMetadata() {
            string identifier;
            while (true) {
                identifier = Random.Shared.Next().ToString("X");
                if (!metadata.ContainsKey(identifier)) break;
            }
            var meta = new AssetMetadata(identifier);
            metadata.Add(identifier, meta);
            return meta;
        }

        public AssetMetadata? GetMetadata(string identifier) {
            return metadata.TryGetValue(identifier, out var meta) ? meta : default;
        }

    }
}
