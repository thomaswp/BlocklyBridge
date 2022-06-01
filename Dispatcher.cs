
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace BlocklyBridge
{
    public class Dispatcher : IDisposable
    {
        public readonly string url;
        public readonly int port;

        private ProgramState _state = new ProgramState();
        public ProgramState State
        {
            get { return _state; }
            set
            {
                _state = value;
                SyncCode();
            }
        }

        public IProgrammable Target { get; private set; }

        private WebsocketServer websocket;
        private Action<Action> enqueueAction = action => action();
        
        // Start is called before the first frame update
        public Dispatcher(string url, int port, Action<Action> enqueueAction = null)
        {
            this.url = url;
            this.port = port;
            if (enqueueAction != null) this.enqueueAction = enqueueAction;
        }

        public event Action<ProgramState> OnSave;

        private Dictionary<string, IProgrammable> programmableMap = new Dictionary<string, IProgrammable>();

        public void Start(Type[] types, Action onConnected)
        {
            JsonMessage blocksJSON = BlocklyGenerator.GenerateBlocks(types);

            websocket = WebsocketServer.Start(url, port);
            WebsocketServer.OnMessage += WebsocketServer_OnMessage;
            WebsocketServer.OnConnected += () =>
            {
                WebsocketServer.SendMessage(blocksJSON);
                SyncCode();
                enqueueAction(onConnected);
            };
        }

        public void Register(IProgrammable programmable)
        {
            string guid = programmable.GetGuid();
            if (programmableMap.ContainsKey(guid))
            {
                Logger.Warn("Replacing duplicate GUID!: " + guid);
                Unregister(programmable);
            }
            programmableMap.Add(guid, programmable);
        }

        public void Unregister(IProgrammable programmable)
        {
            programmableMap.Remove(programmable.GetGuid());
        }

        public void SetTarget(IProgrammable programmable)
        {
            Target = programmable;
            var program = State.GetProgram(programmable.GetGuid());
            WebsocketServer.SendMessage(new JsonMessage("SetTarget", new
            {
                targetID = programmable.GetGuid(),
                targetName = programmable.GetName(),
                code = program.Code,
                varMap = program.VarMap,
            }));
        }

        private void SyncCode()
        {
            if (websocket == null) return;
            // TODO: Should probably only send relevant data
            WebsocketServer.SendMessage(new JsonMessage("SyncCode", State.Programs));
            //Logger.Log($"Sending {State.Programs.Count} programs");
        }

        private void WebsocketServer_OnMessage(string dataString)
        {
            Logger.Log("Receiving: " + dataString);
            JObject data = null;
            try
            {
                data = JObject.Parse(dataString);
            }
            catch { }
            if (data == null)
            {
                Logger.Log("Cannot parse message!");
                return;
            }
            enqueueAction(() => HandleMessage(data));
        }

        public void Dispose()
        {
            websocket.Stop();
        }

        private void HandleMessage(JObject message)
        {
            string type = (string)message["type"];
            JObject data = message["data"] as JObject;
            switch (type)
            {
                case "call": RunMethod(data); break;
                case "save": SaveCode(data); break;
                case "test": TestCode(data); break;
                default: Logger.Warn("Unknown type: " + type); break;
            }
        }

        private void TestCode(JObject data)
        {
            if (Target == null) return;
            if (!Target.TryTestCode())
            {
                // TODO: Handle failed?
            }
        }

        private void SaveCode(JObject data)
        {
            string targetID = (string)data["targetID"];
            string codeXML = (string)data["code"];
            string varMapJSON = (string)data["varMap"];
            var program = State.GetProgram(targetID);
            program.Code = codeXML;
            program.VarMap = varMapJSON;
            //Logger.Log($"Setting {targetID} to {codeXML}");
            if (OnSave != null) OnSave.Invoke(State);
        }

        private void RunMethod(JObject data)
        {
            string methodName = (string)data["methodName"];
            string threadID = (string)data["threadID"];
            string targetID = (string)data["targetID"];
            JArray argsArray = (JArray)data["args"];
            object[] args = argsArray.ToObject<object[]>();

            if (!programmableMap.TryGetValue(targetID, out IProgrammable target))
            {
                Logger.Warn("Missing interpreter for targetID: " + targetID);
                return;
            }

            var method = BlocklyGenerator.Call(target, methodName, args);
            if (method != null) target.EnqueueMethod(method);
            Action onFinished = () =>
            {
                WebsocketServer.SendMessage(new JsonMessage("BlockFinished", new
                {
                    targetID,
                    threadID,
                    returnValue = method?.GetReturnValue(),
                }));
            };
            if (method == null)
            {
                onFinished();
            }
            else
            {
                method.Do(onFinished);
            }
        }
    }

}