using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Game5.Game.Networking {
    public class CommandIdAttribute : Attribute {
        public int Id;
        public CommandIdAttribute(int id) { Id = id; }
    }
    /// <summary>
    /// Subclasses of this class should be synchronised to all other connected clients
    /// and handle interacting with the game world. All interactions should be done
    /// through a Command, so that they are replicated on all clients
    /// </summary>
    [Serializable]
    public abstract class Command {
        public virtual void Serialize(Serializer serializer) {
        }
    }

    public interface ICommandInterceptor {
        bool IsMatch(Command command);
        void Invoke(Command command);
    }
    public class CommandInterceptor<T> : ICommandInterceptor where T : Command {
        public Action<T> Callback;
        public CommandInterceptor(Action<T> callback) {
            Callback = callback;
        }
        public bool IsMatch(Command command) { return command is T; }
        public void Invoke(Command command) { Callback((T)command); }
    }

    /// <summary>
    /// This is used to allow Networking to serialize command types
    /// and recreate them on the other end. Each command provides
    /// an ID via its class attributes (or gets one auto generated)
    /// </summary>
    public static class CommandActivator {
        private static Dictionary<int, Type> commandTypes;
        private static Dictionary<Type, int> commandIds;

        static CommandActivator() {
            commandTypes = new Dictionary<int, Type>();
            commandIds = new Dictionary<Type, int>();
            foreach (Type type in AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(myType => myType.IsClass && !myType.IsAbstract && myType.IsSubclassOf(typeof(Command)))) {
                var commandId = type.GetCustomAttribute<CommandIdAttribute>();
                int id = commandId != null ? commandId.Id : HashInt.Compute(type.Name);
                if (commandTypes.TryGetValue(id, out Type existing)) {
                    Debug.WriteLine($"Command with id {id} already exists!\n" +
                        "Inserting {type} found {existing}");
                }
                commandTypes.Add(id, type);
                commandIds.Add(type, id);
            }
        }
        public static Command CreateInstance(int cmdId) {
            return (Command)Activator.CreateInstance(commandTypes[cmdId]);
        }
        public static int GetCommandTypeId(Command cmd) {
            return cmd != null ? GetCommandTypeId(cmd.GetType()) : -1;
        }
        public static int GetCommandTypeId(Type type) {
            return commandIds[type];
        }

        public static void SerializeCommand(ref Command command, Serializer serializer) {
            var cmdId = command != null ? GetCommandTypeId(command) : -1;
            if (serializer.Serialize(ref cmdId))
                command = CommandActivator.CreateInstance(cmdId);
            command.Serialize(serializer);
        }

    }

}
