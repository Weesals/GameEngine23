using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.Utility {
    public class Morton {

        public uint EncodeMorton(Int2 v) {
            return (ExpandBits((uint)v.Y) << 1) | ExpandBits((uint)v.X);
        }
        public uint EncodeMorton3(Int3 v) {
            return (ExpandBits3((uint)v.Z) << 2) | (ExpandBits3((uint)v.Y) << 1) | ExpandBits3((uint)v.X);
        }

        public Int2 DecodeMorton(uint v) {
            return new((int)CompactBits(v), (int)CompactBits(v >> 1));
        }
        public Int3 DecodeMorton3(uint v) {
            return new((int)CompactBits3(v), (int)CompactBits3(v >> 1), (int)CompactBits3(v >> 2));
        }

        public uint ExpandBits(uint x) {
            x &= 0x0000ffff;
            x = (x | (x << 08)) & 0x00ff00ff;
            x = (x | (x << 04)) & 0x0f0f0f0f;
            x = (x | (x << 02)) & 0x33333333;
            x = (x | (x << 01)) & 0x55555555;
            return x;
        }
        public uint ExpandBits3(uint x) {
            x &= 0x000003ff;
            x = (x | (x << 16)) & 0xff0000ff;
            x = (x | (x << 08)) & 0x0300f00f;
            x = (x | (x << 04)) & 0x030c30c3;
            x = (x | (x << 02)) & 0x09249249;
            return x;
        }
        public uint CompactBits(uint x) {
            x &= 0x55555555;
            x = (x | (x >> 01)) & 0x33333333;
            x = (x | (x >> 02)) & 0x0f0f0f0f;
            x = (x | (x >> 04)) & 0x00ff00ff;
            x = (x | (x >> 08)) & 0x0000ffff;
            return x;
        }
        public uint CompactBits3(uint x) {
            x &= 0x09249249;
            x = (x | (x >> 02)) & 0x030c30c3;
            x = (x | (x >> 04)) & 0x0300f00f;
            x = (x | (x >> 08)) & 0xff0000ff;
            x = (x | (x >> 16)) & 0x000003ff;
            return x;
        }

    }
}
