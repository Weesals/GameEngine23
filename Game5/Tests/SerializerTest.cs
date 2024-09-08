using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Serialization;

namespace Game5.Tests {
    public class SerializerTest {

        private static void Serialize(TSONNode test) {
            using (var d = test.CreateChild("Derp1")) {
                int v = 1;
                d.Serialize(ref v);
                int v2 = 2;
                d.Serialize(ref v2);
            }
            using (var d = test.CreateChild("Derp2")) {
                int v = 10;
                d.Serialize(ref v);
                using (var n = d.CreateBinary()) {
                    int vn = 10;
                    n.Serialize(ref vn);
                    n.Serialize(ref vn);
                }
                d.Serialize(ref v);
            }
            using (var d = test.CreateChild("Derp3")) {
                int v = 6;
                d.Serialize(ref v);
            }
        }
        
        public void Test() {
            var buffer = new DataBuffer();
            using (var docroot = TSONNode.CreateWrite(buffer)) {
                using (var test = docroot.CreateChild("Test")) {
                    Serialize(test);
                }
            }
            using (var docroot = TSONNode.CreateRead(buffer)) {
                using (var test = docroot.CreateChild("Test")) {
                    Serialize(test);
                }
            }
        }
    }
}
