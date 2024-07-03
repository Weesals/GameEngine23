using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;

public static class ECSTestProgram {
    public struct TestComponent {
        public int TestValue;
    }
    public struct TestComponent2 {
        public int TestValue;
    }
    public static void Main() {
        var world = new World();
        var entity = world.Stage.CreateEntity();
        world.Stage.AddComponent<TestComponent>(entity);
        world.Stage.GetComponentRef<TestComponent>(entity) = new TestComponent() { TestValue = 5, };
        world.Stage.AddComponent<TestComponent2>(entity);
        world.Stage.GetComponentRef<TestComponent2>(entity) = new TestComponent2() { TestValue = 1, };

        Debug.Assert(world.Stage.GetComponentRef<TestComponent>(entity).TestValue == 5);
        Debug.Assert(world.Stage.GetComponentRef<TestComponent2>(entity).TestValue == 1);
        world.Stage.RemoveComponent<TestComponent>(entity);
        Debug.Assert(!world.Stage.HasComponent<TestComponent>(entity));
        Debug.Assert(world.Stage.HasComponent<TestComponent2>(entity));

        var entity2 = world.Stage.CreateEntity();
        world.Stage.AddComponent<TestComponent>(entity2) = new TestComponent() { TestValue = 7, };
        world.Stage.AddComponent<TestComponent2>(entity2) = new TestComponent2() { TestValue = 4, };

        for (int i = 0; i < 2; i++) {
            var entity3 = world.Stage.CreateEntity();
            world.Stage.AddComponent<TestComponent>(entity3) = new TestComponent() { TestValue = 7, };
            world.Stage.AddComponent<TestComponent2>(entity3) = new TestComponent2() { TestValue = 6, };
        }

        foreach (var tentity in world.Stage.GetEntities()) {
            Console.WriteLine("== Entity " + tentity.Index);
            foreach (var component in world.Stage.GetEntityComponents(tentity)) {
                var type = component.GetRawType();
                Console.WriteLine("  Found component " + type);
                if (component.GetIs<TestComponent>()) {
                    var cmp = component.GetRO<TestComponent>();
                    Console.WriteLine("   - Value " + cmp.TestValue);
                }
                if (component.GetIs<TestComponent2>()) {
                    var cmp = component.GetRO<TestComponent2>();
                    Console.WriteLine("   - Value " + cmp.TestValue);
                }
            }
        }

        world.AddSystem((ref TestComponent2 c2) => {
            c2.TestValue += 1;
        });
        world.AddSystem((ref TestComponent c1, ref TestComponent2 c2) => {
            c1.TestValue += 1;
        });
        world.AddSystem((ref Entity entity, ref TestComponent2 c2) => {
            c2.TestValue += 1;
        });
        for (int s = 0; ; ++s) {
            world.Step();
            if ((s % 100) == 0) Debug.WriteLine(s);
        }
    }
}
