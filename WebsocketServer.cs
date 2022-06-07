using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BlocklyBridge
{
    internal class WebsocketServer
    {

        public static WebsocketServer Instance { get; private set; }

        public delegate void ConnectedEvent();
        public static event ConnectedEvent OnConnected;

        public delegate void OnMessageEvent(string data);
        public static event OnMessageEvent OnMessage;

        public readonly string url;
        public readonly int port;

        private Thread thread;
        private TcpListener server;
        private NetworkStream stream;

        private ConcurrentQueue<string> messages = new ConcurrentQueue<string>();

        private bool connected = false;
        private bool stopping = false;

        public static WebsocketServer Start(string url, int port)
        {
            if (Instance != null) throw new Exception("Socket already running");
            Instance = new WebsocketServer(url, port);
            return Instance;
        }

        public static void SendMessage(JsonMessage data, bool sendIfDisconnected = false)
        {
            SendMessage(data.ToJson(), sendIfDisconnected);
        }

        public static void SendMessage(string message, bool sendIfDisconnected = false)
        {
            if (!Instance.connected && !sendIfDisconnected) return;
            Instance.messages.Enqueue(message);
        }

        private WebsocketServer(string url, int port)
        {
            this.url = url;
            this.port = port;

            thread = new Thread(() =>
            {
                server = new TcpListener(IPAddress.Parse(url), port);

                server.Start();
                Logger.Log("Server has started.\nWaiting for a connection...");

                while (!stopping)
                {
                    connected = false;
                    TcpClient client = server.AcceptTcpClient();
                    connected = true;

                    Logger.Log("A client connected.");

                    stream = client.GetStream();

                    while (!stopping && client.Connected)
                    {
                        Thread.Sleep(0);

                        while (!stopping && !messages.IsEmpty)
                        {
                            string message;
                            if (messages.TryDequeue(out message))
                            {
                                SendSocketMessage(message);
                            }
                        }

                        int available = client.Available;
                        if (!client.Connected || !stream.DataAvailable || available < 3)
                        {
                            continue;
                        }

                        byte[] bytes = new byte[available];
                        stream.Read(bytes, 0, available);
                        //ByteArrayToFile("Test.txt", bytes);
                        // string s = Encoding.UTF8.GetString(bytes);
                        string s = Encoding.ASCII.GetString(bytes);
                        //Debug.Log("Receiving message: '" + s + "'");
                        //Debug.Log(string.Join(",", bytes));

                        if (Regex.IsMatch(s, "^GET", RegexOptions.IgnoreCase))
                        {
                            Logger.Log(string.Format("=====Handshaking from client=====\n{0}", s));

                            // 1. Obtain the value of the "Sec-WebSocket-Key" request header without any leading or trailing whitespace
                            // 2. Concatenate it with "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" (a special GUID specified by RFC 6455)
                            // 3. Compute SHA-1 and Base64 hash of the new value
                            // 4. Write the hash back as the value of "Sec-WebSocket-Accept" response header in an HTTP response
                            string swk = Regex.Match(s, "Sec-WebSocket-Key: (.*)").Groups[1].Value.Trim();
                            string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                            byte[] swkaSha1 = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
                            string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);

                            // HTTP/1.1 defines the sequence CR LF as the end-of-line marker
                            byte[] response = Encoding.UTF8.GetBytes(
                                    "HTTP/1.1 101 Switching Protocols\r\n" +
                                    "Connection: Upgrade\r\n" +
                                    "Upgrade: websocket\r\n" +
                                    "Sec-WebSocket-Accept: " + swkaSha1Base64 + "\r\n\r\n");

                            stream.Write(response, 0, response.Length);

                            OnConnected();
                        }
                        else
                        {
                            bool fin = (bytes[0] & 0b10000000) != 0,
                                mask = (bytes[1] & 0b10000000) != 0; // must be true, "All messages from the client to the server have this bit set"

                            int opcode = bytes[0] & 0b00001111, // expecting 1 - text message
                                    msglen = bytes[1] - 128, // & 0111 1111
                                    offset = 2;

                            if (msglen == 126)
                            {
                                // TODO: Hits a bug with firefox that leads to a message length longer than bytes
                                // was ToUInt16(bytes, offset) but the result is incorrect
                                msglen = BitConverter.ToUInt16(new byte[] { bytes[3], bytes[2] }, 0);
                                offset = 4;
                            }
                            else if (msglen == 127)
                            {
                                Logger.Log("TODO: msglen == 127, needs qword to store msglen");
                                // i don't really know the byte order, please edit this
                                // msglen = BitConverter.ToUInt64(new byte[] { bytes[5], bytes[4], bytes[3], bytes[2], bytes[9], bytes[8], bytes[7], bytes[6] }, 0);
                                // offset = 10;
                            }

                            if (msglen == 0)
                                Logger.Log("msglen == 0");
                            else if (mask)
                            {
                                byte[] decoded = new byte[msglen];
                                byte[] masks = new byte[4] { bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3] };
                                offset += 4;

                                for (int i = 0; i < msglen; ++i)
                                    decoded[i] = (byte)(bytes[offset + i] ^ masks[i % 4]);

                                string text = Encoding.UTF8.GetString(decoded);
                                //Debug.Log(text);
                                OnMessage(text);

                                if (text == "Disconnect") break;
                            }
                            else
                                Logger.Log("mask bit not set");
                        }
                    }
                    Logger.Log("Client closed");
                }
            });
            thread.Start();
        }

        private void SendSocketMessage(string message)
        {
            Logger.Log("Sending: " + message);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            int offset = 0;
            bool continuation = false;
            while (offset < messageBytes.Length)
            {
                int remaining = message.Length - offset;
                byte length = (byte)Math.Min(125, remaining);
                byte opcode = 0b10000001;
                // This is a continuation if we haven't finished the message
                if (continuation) opcode &= 0b11111110;
                // This is not finalized if we have more than 125 remaining
                if (remaining > 125) opcode &= 0b01111111;
                byte[] header = new byte[] { opcode, length };
                //Debug.Log("Sending header: " + string.Join(",", header));
                try
                {
                    stream.Write(header, 0, header.Length);
                    stream.Write(messageBytes, offset, length);
                    //Debug.Log(offset + "/" + messageBytes.Length);
                    offset += length;
                    continuation = true;
                }
                catch
                {
                    Logger.Warn("Error writing to stream; dropping message.");
                    break;
                }
            }

            try
            {
                stream.Flush();
            }
            catch { }
        }

        public static void StopInstance()
        {
            Instance.Stop();
        }

        public void Stop()
        {
            try
            {
                stopping = true;
                thread.Abort();
                server.Stop();
                Logger.Log("Server stopped");
            }
            catch (Exception e)
            {
                Logger.Log(e);
            }
        }
    }

    [Serializable]
    public class JsonMessage
    {
        public string type;
        public object data;

        public JsonMessage(string type, object data)
        {
            this.type = type;
            this.data = data;
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}