using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Utility;

namespace Weesals.Game {
    public struct TeamResKey : IEquatable<TeamResKey> {
        public int TeamId;
        public int ResourceId;
        public TeamResKey(int teamId, int resourceId) { TeamId = teamId; ResourceId = resourceId; }
        public bool Equals(TeamResKey o) { return TeamId == o.TeamId && ResourceId == o.ResourceId; }
        public override int GetHashCode() { return TeamId * 5 + (int)ResourceId; }
        public override string ToString() { return TeamId + ": " + ResourceId; }
    }
    public struct Hostility {
        public enum Flags : byte {
            CanAttack = 0x01, IsHostile = 0x02,
            None = 0x00, Neutral = CanAttack, Hostile = CanAttack | IsHostile
        }
        public Flags Flag;
        public Hostility(Flags flag) { Flag = flag; }
        public bool CanAttack => (Flag & Flags.CanAttack) != 0;
        public bool IsHostile => (Flag & Flags.IsHostile) != 0;
        public static readonly Hostility None = new Hostility(Flags.None);
        public static readonly Hostility Neutral = new Hostility(Flags.Neutral);
        public static readonly Hostility Hostile = new Hostility(Flags.Hostile);
    }
    public partial class PlayerSystem : SystemBase {

        public interface IResourceListener {
            void NotifyResourceChanged(HashSet<TeamResKey> changed);
        }

        public struct PlayerData {
            public uint Flags;
        }
        public struct ResourceData {
            public int Amount;
        }
        public struct AllegianceData {
            public Hostility Hostility { get; private set; }
            public bool CanControl { get; private set; }
            public bool CanAttack => Hostility.CanAttack;
            public bool IsHostile => Hostility.IsHostile;
            public static readonly AllegianceData Self = new AllegianceData() { Hostility = Hostility.None, CanControl = true, };
            public static readonly AllegianceData Ally = new AllegianceData() { Hostility = Hostility.None, CanControl = false, };
            public static readonly AllegianceData Indifferent = new AllegianceData() { Hostility = Hostility.Neutral, CanControl = false, };
            public static readonly AllegianceData Enemy = new AllegianceData() { Hostility = Hostility.Hostile, CanControl = false, };
        }

        private PlayerData[] players;
        private Dictionary<TeamResKey, ResourceData> resources;
        private HashSet<TeamResKey> changed;
        private HashSet<IResourceListener> listeners = new();

        protected override void OnCreate() {
            base.OnCreate();
            players = new PlayerData[32];
            resources = new(128);
            changed = new(32);
        }
        protected override void OnDestroy() {
            //players.Dispose();
            //resources.Dispose();
            //changed.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate() {
            if (changed.Count > 0) {
                using var newListeners = new PooledList<IResourceListener>();
                foreach (var item in listeners) newListeners.Add(item);
                foreach (var listener in newListeners) listener.NotifyResourceChanged(changed);
                changed.Clear();
            }
        }

        public void RegisterListener(IResourceListener listener, bool enable) {
            if (enable) listeners.Add(listener);
            else listeners.Remove(listener);
        }

        public PlayerData GetPlayerById(int playerId) {
            return players[playerId];
        }

        public PlayerResources GetPlayerResources() {
            return new PlayerResources(resources, changed);
        }

        public struct PlayerResources {
            private Dictionary<TeamResKey, ResourceData> resources;
            private HashSet<TeamResKey> changed;
            public PlayerResources(Dictionary<TeamResKey, ResourceData> resources, HashSet<TeamResKey> changed) {
                this.resources = resources;
                this.changed = changed;
            }
            public int GetAmount(int playerId, int resourceId) {
                return GetAmount(new TeamResKey(playerId, resourceId));
            }
            public int GetAmount(TeamResKey resource) {
                return resources.TryGetValue(resource, out var data) ? data.Amount : 0;
            }
            public bool CanConsume(int playerId, ResourceAmount amount) {
                var key = new TeamResKey(playerId, amount.ResourceIndex);
                resources.TryGetValue(key, out var data);
                return amount.Amount <= data.Amount;
            }
            public bool Consume(int playerId, ResourceAmount amount) {
                var key = new TeamResKey(playerId, amount.ResourceIndex);
                resources.TryGetValue(key, out var data);
                if (amount.Amount > data.Amount) return false;
                data.Amount -= amount.Amount;
                resources[key] = data;
                changed.Add(key);
                return true;
            }
            public void Deliver(int playerId, ResourceAmount amount) {
                var key = new TeamResKey(playerId, amount.ResourceIndex);
                resources.TryGetValue(key, out var data);
                data.Amount += amount.Amount;
                resources[key] = data;
                changed.Add(key);
            }
        }

        public AllegianceData GetAllegiance(Entity self, Entity other) {
            var selfTeamId = Stage.GetComponent<ECTeam>(self);
            var otherTeamId = Stage.GetComponent<ECTeam>(other);
            if (selfTeamId.SlotId == otherTeamId.SlotId) return AllegianceData.Self;
            return AllegianceData.Enemy;
        }

        public void CopyStateFrom(PlayerSystem other) {
            players = (PlayerData[])other.players.Clone();
            //resources.Clone(other.resources);
            throw new NotImplementedException();
        }

        public void ForceComplete() {
            //Dependency.Complete();
            throw new NotImplementedException();
        }
    }
}
