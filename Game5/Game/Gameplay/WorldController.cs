using Game5.Game.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Landscape;

namespace Game5.Game {
    /// <summary>
    /// Provides the capability to mutate worlds in a controlled and
    /// deterministic way. Also handles stepping the world time in a
    /// deterministic way.
    /// </summary>
    public class WorldController : IDisposable {

        public long WorldStep = 16;

        // The simulation being controlled
        public Simulation Simulation;

        // List of commands to be invoked
        private List<CommandWorld> commands = new List<CommandWorld>();
        //private List<PlayerBridge> playerBridges = new List<PlayerBridge>();

        //public ScriptedWorld ScriptedWorld { get; private set; }

        public long SimulationTimeMS { get { return Simulation.TimeSystem.TimeCurrentMS; } }

        public void Initialise() {
            Simulation = new Simulation();
            Initialise(Simulation);
        }
        public void Initialise(Simulation world) {
            Simulation = world;
            //playerBridges.Add(new PlayerBridge() { SlotId = 0, });
            //playerBridges.Add(new PlayerBridge() { SlotId = 1, });
            //playerBridges.Add(new PlayerBridge() { SlotId = 2, });

            //ScriptedWorld = new ScriptedWorld();
            //ScriptedWorld.Initialise(world);
        }
        public void Dispose() {
            //ScriptedWorld.Dispose();
            //Simulation.EntitiesWorld.Dispose();
        }

        public void StepTo(long time, int maxSteps = 20) {
            Step(time - SimulationTimeMS, maxSteps);
            //Simulation.SetDisplayTimeOffset(((time - WorldStep) - SimulationTimeMS) / 1000f);
        }
        public void StepToVariable(long time) {
            var delta = time - SimulationTimeMS;
            ExecuteCommandsTo(SimulationTimeMS);
            Simulation.Step(delta);
            //Simulation.SetDisplayTimeOffset(0);
        }
        public void Step(long timeSpan, int maxSteps = 20) {
            var stepInterval = WorldStep;
            for (int i = 0; i < maxSteps; i++) {
                if (timeSpan < stepInterval) break;
                //SimulationTime += stepInterval;
                timeSpan -= stepInterval;
                ExecuteCommandsTo(SimulationTimeMS);
                Simulation.Step(stepInterval);
            }
        }

        // Ensure all commands are executed up to the specified simulation time
        // SHOULD ALWAYS BE WITHIN 1 STEP of any executed commands
        private void ExecuteCommandsTo(long simulationTime) {
            for (int i = 0; i < commands.Count; i++) {
                if (commands[i].ExecutionTimeMS <= simulationTime) {
                    commands[i].Invoke(this);
                    commands.RemoveAt(i--);
                }
            }
        }

        // Add a command to be execeuted in the future
        public void PushCommand(CommandWorld command, bool isServer) {
            if (isServer && SimulationTimeMS >= command.ExecutionTimeMS)
                Debug.WriteLine("Attempting to add " + command.GetType().Name + " which wouldve already been run");
            commands.Add(command);
        }

        // Get a player accessor
        /*public PlayerBridge GetPlayerBySlotId(int slotId) {
            return playerBridges[slotId];
        }*/

        // Various APIs for interacting with the world (called from Commands)

        public void SpawnEntity(int protoId, Int2 location, sbyte playerId) {
            Simulation.SpawnEntity(protoId, location, playerId);
        }

        public void BeginTraining(Entity entity, string trainName) {
            //var entityAccessor = Simulation.ProtoSystem.GetEntityAccessor(entity);
            //var protoI = entityAccessor.GetPrototypeId();
            //if (protoI < 0) return;
            /*var abilities = World.ProtoSystem.GetActionData(protoI);
            if (!abilities.IsValid) return;
            var abilityI = abilities.FindByName(trainName, ActionDispatchSystem.ActionTypes.Train);
            //World.AbilitySystem.BeginAbility(entityId, abilityI);
            World.ActionQueueSystem.EnqueueAction(entityId, new ActionRequest(ActionTypes.None) {
                ActionId = 2,
            });*/
            var spawnProtoId = Simulation.ProtoSystem.GetPrototypeIdByName(trainName);
            if (spawnProtoId < 0) { Debug.WriteLine($"Could not find proto for {trainName}"); return; }
            var action = Simulation.ActionQueueSystem.CreateActionInstance(ActionRequest.CreateTrain(spawnProtoId));
            EnqueueAction(entity, action, true);
        }

        public void BeginUpgrade(Entity entity, string techName) {
            //var entityAccessor = Simulation.GetEntityAccessor(entity);
            //var protoI = entityAccessor.GetPrototypeId();
            //if (protoI < 0) return;
            /*var abilities = World.ProtoSystem.GetActionData(protoI);
            if (!abilities.IsValid) return;
            var abilityI = abilities.FindByName(techName, ActionDispatchSystem.ActionTypes.Upgrade);
            //World.AbilitySystem.BeginAbility(entityId, abilityI);
            World.ActionQueueSystem.EnqueueAction(entityId, new ActionRequest(ActionTypes.None) {
                ActionId = 1,
            });*/
        }

        public bool CanControl(PlayerBridge player, ItemReference entity) {
            if (!Simulation.World.IsValid(entity.GetEntity())) return false;
            if (player.IsGodMode) return true;
            var team = Simulation.World.GetComponent<ECTeam>(entity.GetEntity());
            //var teamId = Simulation.GetEntityAccessor(entity).GetOwnerId();
            return team.SlotId == player.SlotId;
        }
        public void NotifyHasUsedCheat(PlayerBridge player) {
            player.NotifyCheatUsed();
        }
        public void ClearActionItem(Entity entity, int requestId) {
            //World.AbilitySystem.ClearTrainingQueue(World.GetEntity(entityId), rindex);
            Simulation.ActionDispatchSystem.CancelAction(entity, new RequestId(requestId));
            Simulation.ActionQueueSystem.CancelAction(entity, new RequestId(requestId));
        }
        public void ClearActionQueue(Entity entity) {
            //World.ClearActionQueue(entityId);
            Simulation.ActionDispatchSystem.CancelAction(entity, RequestId.All);
            Simulation.ActionQueueSystem.CancelAction(entity, RequestId.All);
        }
        public void EnqueueAction(Entity entity, OrderInstance action, bool append) {
            if (!append) ClearActionQueue(entity);
            Simulation.ActionQueueSystem.EnqueueAction(entity, action);
        }

        public ItemReference AttemptPlace(int protoId, int playerId, Int2 location) {
            var constructionProtoId = Simulation.ProtoSystem.GetConstructionProtoId(protoId);
            var entity = Simulation.SpawnEntity(constructionProtoId, location, playerId);
            Simulation.World.AddComponent(entity, new ECConstruction() {
                ProtoId = protoId,
                ConsumptionId = RequestId.Invalid,
            });

            var protoData = Simulation.ProtoSystem.GetPrototypeData(entity);
            var pos = Simulation.Landscape.Sizing.WorldToLandscape(SimulationWorld.SimulationToWorld(location));
            var size = Simulation.Landscape.Sizing.WorldToLandscape(SimulationWorld.SimulationToWorld(protoData.Footprint.Size));
            var dirtId = Simulation.Landscape.Layers.FindLayerId("TL_Dirt");
            Simulation.Landscape.PaintRectangle(dirtId, new RectI(pos - size / 2, size));

            return Simulation.EntityProxy.MakeHandle(entity);
        }

        public void DestroyEntity(Entity entity) {
            Simulation.LifeSystem.KillEntity(entity);
        }

        // Allow state duplication and restoration
        public void CopyStateFrom(WorldController other) {
            commands.Clear();
            commands.AddRange(other.commands);
            Simulation.CopyStateFrom(other.Simulation);
        }

    }
}
