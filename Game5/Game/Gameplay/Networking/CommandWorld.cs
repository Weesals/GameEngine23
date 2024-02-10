using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Game5.Game.Networking {
    /// <summary>
    /// A command that is invoked on the world
    /// ie. Commanding units
    /// NOT including things like player chat
    /// </summary>
    public abstract class CommandWorld : Command {

        public long ExecutionTimeMS;

        public virtual void Invoke(WorldController controller) { }

        public override void Serialize(Serializer serializer) {
            serializer.Serialize(ref ExecutionTimeMS);
            base.Serialize(serializer);
        }

    }
}
