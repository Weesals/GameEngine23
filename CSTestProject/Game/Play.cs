using Flecs.NET.Core;
using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Landscape;

namespace Weesals.Game {

    public struct Position {
        public Vector3 Value;
    }

    public class Play {
        Scene scene;

        LandscapeData landscape;
        LandscapeRenderer landscapeRenderer;
        World world;

        public Play() {
            scene = new(Core.ActiveInstance!.GetScene());

            landscape = new LandscapeData();
            landscape.SetSize(128);
            landscapeRenderer = new LandscapeRenderer();
            landscapeRenderer.Initialise(landscape, scene.RootMaterial);

            world = World.Create();

            var test = world.Entity()
                .Set(new Position());

            world.Query();
        }

        public void Render(CSGraphics graphics, BasePass pass) {
            landscapeRenderer.Render(graphics, pass);
        }
    }
}
