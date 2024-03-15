using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using PlayerData = Game5.Game.PlayerSystem.PlayerData;

namespace Game5.Game.Networking {
    public class PlayerBridge {

        public int SlotId;
        public Entity PlayerEntity;
        public PlayerData Data { get; private set; }
        public uint Flags => Data.Flags;
        public int CheatsUsed { get; private set; }
        public bool IsGodMode { get; private set; }

        public List<ItemReference> Selection = new();

        public void PullState(PlayerData data) {
            Data = data;
        }

        public void NotifyCheatUsed() {
            CheatsUsed++;
        }
        public void SetGodMode(bool godMode) {
            IsGodMode = godMode;
        }

        public void CopyStateFrom(PlayerBridge other) {
            CheatsUsed = other.CheatsUsed;
            Selection.Clear();
            Selection.AddRange(other.Selection);
        }

    }
}
