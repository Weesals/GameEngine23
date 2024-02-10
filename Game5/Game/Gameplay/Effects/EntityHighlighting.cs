using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals;
using Weesals.Engine;
using Weesals.Utility;

namespace Game5.Game {
    public interface IHighlightListener {
        void NotifyHighlightChanged(GenericTarget target, Color color);
    }
    public class EntityHighlighting {

        public struct FlashConfig {
            public uint BeginTimeMS;
            public Color Color;
            public int Count;
            public uint DurationMS;
            public readonly uint EndTimeMS { get { return BeginTimeMS + DurationMS; } }
        }

        public struct FlashingData : IDisposable {
            public struct EntityFlash {
                public GenericTarget Target;
                public FlashConfig Config;
            }
            private uint currentTime;
            private List<EntityFlash> flashes = new();
            public FlashingData() { }
            public void Dispose() { }
            private int GetIndex(GenericTarget target) {
                for (int i = 0; i < flashes.Count; i++)
                    if (flashes[i].Target == target) return i;
                return -1;
            }
            public void BeginFlashing(GenericTarget target, FlashConfig config) {
                var flash = new EntityFlash() { Target = target, Config = config, };
                flash.Config.BeginTimeMS = currentTime + 1;
                var index = GetIndex(target);
                if (index == -1) flashes.Add(flash);
                else flashes[index] = flash;
            }
            public void Update(uint timeMS, IHighlightListener highlighting) {
                var prevTime = currentTime;
                currentTime = timeMS;
                for (int i = 0; i < flashes.Count; i++) {
                    var flash = flashes[i];
                    var state0 = GetFlashState(flash.Config, prevTime);
                    var state1 = GetFlashState(flash.Config, currentTime);
                    // Flash requires highlight update
                    if (state0 != state1) {
                        var color = (state1 & 1) == 0 ? flash.Config.Color : new Color(0, 0, 0, 0);
                        highlighting.NotifyHighlightChanged(flash.Target, color);
                    }
                    // Flash has ended
                    if (state1 == -3) flashes.RemoveAtSwapBack(i--);
                }
            }
            private int GetFlashState(FlashConfig config, uint time) {
                var timeN = (float)(time - config.BeginTimeMS) / config.DurationMS;
                if (timeN < 0f) return -1;
                var flashC = (int)(2 * config.Count * timeN);
                if (timeN >= 1) flashC = -3;
                return flashC;
            }
        }

        public IHighlightListener Listener { get; private set; }

        public FlashingData Flashing = new();

        public EntityHighlighting(IHighlightListener listener) {
            Listener = listener;
        }

        protected void OnDisable() {
            Flashing.Dispose();
        }

        public void Update(uint timeMS) {
            Flashing.Update(timeMS, Listener);
        }


        // TODO: Should be described in ScriptableObjects
        public static readonly FlashConfig OrderEffect = new FlashConfig() {
            Color = new Color(64, 64, 64, 64),
            Count = 1,
            DurationMS = 500,
        };
        public static readonly FlashConfig DamageEffect = new FlashConfig() {
            Color = new Color(64, 16, 0, 0),
            Count = 1,
            DurationMS = 500,
        };
        public static readonly FlashConfig DefaultEffect = new FlashConfig() {
            Color = Color.White * 0.5f,
            Count = 3,
            DurationMS = 500,
        };
    }
}
