
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace BlocklyBridge
{
    public class Dispatcher
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
            programmableMap.Add(programmable.GetGuid(), programmable);
        }

        public void SetTarget(IProgrammable programmable)
        {
            WebsocketServer.SendMessage(new JsonMessage("SetTarget", new
            {
                targetID = programmable.GetGuid(),
                targetName = programmable.GetName(),
                code = State.GetProgram(programmable.GetGuid()).Code,
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

        private void HandleMessage(JObject message)
        {
            string type = (string)message["type"];
            JObject data = (JObject)message["data"];
            switch (type)
            {
                case "call": RunMethod(data); break;
                case "save": SaveCode(data); break;
                default: Logger.Warn("Unknown type: " + type); break;
            }
        }

        private void SaveCode(JObject data)
        {
            string targetID = (string)data["targetID"];
            string codeXML = (string)data["code"];
            State.GetProgram(targetID).Code = codeXML;
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