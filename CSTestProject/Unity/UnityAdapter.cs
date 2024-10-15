using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnityEngine {
    public static class Time {
        public static float time;
        public static float deltaTime;
        public static int frameCount;

        public static void Update(float dt) {
            time += dt;
            deltaTime = dt;
            ++frameCount;
        }
    }
}
