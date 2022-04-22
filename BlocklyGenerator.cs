using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BlocklyBridge
{
    public class BlocklyGenerator
    {
        static Dictionary<string, MethodInfo> methodMap, eventMap;

        public static JsonMessage GenerateBlocks()
        {
            methodMap = new Dictionary<string, MethodInfo>();
            eventMap = new Dictionary<string, MethodInfo>();

            var q = from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.IsClass && t.Namespace == "Farmbot"
                    select t;
            var scriptables = q.Where(t => Attribute.IsDefined(t, typeof(ScriptableBehavior)));

            BlocklyDefinitions definitions = new BlocklyDefinitions();

            foreach (Type scriptable in scriptables)
            {
                ScriptableBehavior behavior = (ScriptableBehavior)Attribute.GetCustomAttribute(scriptable, typeof(ScriptableBehavior));

                string category = behavior.name;
                var methods = scriptable.GetMethods()
                    .Where(method => Attribute.IsDefined(method, typeof(ScriptableMethod)));
                foreach (MethodInfo method in methods)
                {
                    BlocklyMethod blocklyMethod = new BlocklyMethod(method.Name, category);
                    if (!typeof(AsyncMethod).IsAssignableFrom(method.ReturnType))
                    {
                        blocklyMethod.returnType = new BlocklyType(method.ReturnType);
                    } 
                    else if (method.ReturnType.IsGenericType && 
                        typeof(AsyncFunction<>).IsAssignableFrom(method.ReturnType.GetGenericTypeDefinition()))
                    {
                        blocklyMethod.returnType = new BlocklyType(method.ReturnType.GetGenericArguments()[0]);
                    }
                    if (blocklyMethod.returnType != null && blocklyMethod.returnType.type == null)
                    {
                        Logger.Warn("Unknown return for method: " + method.Name);
                    }

                    foreach (var param in method.GetParameters())
                    {
                        blocklyMethod.parameters.Add(new BlocklyParameter(param.Name, new BlocklyType(param.ParameterType)));
                    }
                    //Debug.Log(scriptable.Name + "." + method.Name);
                    definitions.methods.Add(blocklyMethod);
                    methodMap.Add(blocklyMethod.name, method);
                }

                var events = scriptable.GetMethods()
                    .Where(method => Attribute.IsDefined(method, typeof(ScriptableEvent)));
                foreach (MethodInfo method in events)
                {
                    ScriptableEvent scriptableEvent = (ScriptableEvent)Attribute.GetCustomAttribute(method, typeof(ScriptableEvent));

                    BlocklyEvent blocklyEvent = new BlocklyEvent(method.Name, category, scriptableEvent.stackable);
                    definitions.events.Add(blocklyEvent);

                    eventMap.Add(blocklyEvent.name, method);
                }

                definitions.categories.Add(new BlocklyCategory(category, behavior.color));
            }

            return new JsonMessage("DefineBlocks", definitions);
        }

        internal static AsyncMethod Call(IProgrammable target, string name, object[] args)
        {
            if (!methodMap.ContainsKey(name))
            {
                if (!eventMap.ContainsKey(name))
                {
                    Logger.Warn("Unknown method: " + name);
                }

                return null;
            }

            var method = methodMap[name];
            var @params = method.GetParameters();
            if (@params.Length != args.Length)
            {
                Logger.Warn($"Incorrect number of args for {name}: {args.Length} vs {@params.Length}");
                return null;
            }
            for (int i = 0; i < args.Length; i++)
            {
                var paramType = @params[i].ParameterType;
                object arg = args[i];
                if (paramType.IsEnum && arg is string)
                {
                    args[i] = arg = Enum.Parse(paramType, (string)arg);
                }
                var argType = arg == null ? typeof(object) : arg.GetType();
                if (!paramType.IsAssignableFrom(argType))
                {
                    Logger.Log($"For arg {i} of {name}: cannot assign {argType} {arg} to {paramType}");
                    return null;
                }
            }
            object component = target.GetObjectForType(method.DeclaringType);
            AsyncMethod async = (AsyncMethod) method.Invoke(component, args);
            return async;
        }

        internal static void SendEvent(IProgrammable target, string eventName)
        {
            string id = target.GetGuid();
            WebsocketServer.SendMessage(new JsonMessage("TriggerEvent", new
            {
                eventName = eventName,
                targetID = id,
            }));
        }
    }

    [Serializable]
    class BlocklyDefinitions
    {
        public List<BlocklyMethod> methods = new List<BlocklyMethod>();
        public List<BlocklyEvent> events = new List<BlocklyEvent>();
        public List<BlocklyCategory> categories = new List<BlocklyCategory>();

    }

    [Serializable]
    class BlocklyCategory
    {
        public string name;
        public int color;

        public BlocklyCategory(string name, int color)
        {
            this.name = name;
            this.color = color;
        }
    }

    abstract class BlocklyCallable
    {
        public string name;
        public string category;

        public BlocklyCallable(string name, string category)
        {
            this.name = name;
            this.category = category;
        }
    }

    class BlocklyEvent : BlocklyCallable
    {
        public bool isStackable;

        public BlocklyEvent(string name, string category, bool isStackable) : base(name, category)
        {
            this.isStackable = isStackable;
        }
    }

    [Serializable]
    class BlocklyMethod : BlocklyCallable
    {
        public BlocklyType returnType;
        public List<BlocklyParameter> parameters = new List<BlocklyParameter>();

        public BlocklyMethod(string name, string category) : base(name, category) { }
    }

    [Serializable]
    class BlocklyParameter
    {
        public BlocklyType type;
        public string name;

        public BlocklyParameter(string name, BlocklyType type)
        {
            this.name = name;
            this.type = type;
        }
    }

    [Serializable]
    class BlocklyType
    {
        public string type;
        public bool isEnum;
        public string[] options;

        public BlocklyType(Type type)
        {
            this.type = type.Name;

            if (type.IsPrimitive)
            {
                isEnum = false;
                options = null;
            }
            else if (type.IsEnum)
            {
                isEnum = true;
                options = type.GetEnumNames();
            }
            else
            {
                Logger.Warn("Unknown type: " + type.Name);
                this.type = null;
                isEnum = false;
                options = null;
            }
        }
    }

    public class AsyncMethod
    {
        protected List<Func<bool>> todo = new List<Func<bool>>();

        public string BlockingCategory { get; private set; }

        public virtual object GetReturnValue()
        {
            return null;
        }

        public AsyncMethod UpdateUntil(Func<bool> until)
        {
            todo.Add(until);
            return this;
        }

        public AsyncMethod Do(Action action)
        {
            todo.Add(() =>
            {
                action();
                return true;
            });
            return this;
        }

        public AsyncMethod SetBlockingCategory(string category)
        {
            BlockingCategory = category;
            return this;
        }

        public bool Update()
        {
            while (todo.Count > 0 && todo[0]()) todo.RemoveAt(0);
            return todo.Count == 0;
        }
    }

    public class AsyncFunction<T> : AsyncMethod
    {
        private T value;

        public override object GetReturnValue()
        {
            return value;
        }

        protected void SetReturnValue(T value)
        {
            this.value = value;
        }

        public AsyncFunction<T> Return(Func<T> returner)
        {
            todo.Add(() =>
            {
                value = returner();
                return true;
            });
            return this;
        }
    }

}