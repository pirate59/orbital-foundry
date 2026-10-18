using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>A seat as the relay describes it: who plays it (a person, an AI or nobody), and the player's name.</summary>
    [Serializable]
    public sealed class RelaySeat
    {
        public int seat;
        public string mode = "off"; // person | ai | off
        public string profile = "medium";
        public int team;
        public string name = "";
    }

    /// <summary>How far the relay has passed on a seat's inputs.</summary>
    [Serializable]
    public sealed class RelayThrough
    {
        public int seat;
        public int tick;
    }

    /// <summary>
    /// Every message to or from the relay (relay/room.js), in one shape JsonUtility can read and write: <see cref="t"/>
    /// says which it is, and only its own fields mean anything.
    /// </summary>
    [Serializable]
    public sealed class RelayMessage
    {
        public string t = "";
        // lobby and start
        public string code = "";
        public int you;
        public bool host;
        public int difficulty;
        public List<RelaySeat> seats = new List<RelaySeat>();
        public List<string> watching = new List<string>();
        public string notReady = "";
        public int seed;
        public int speed = 1;
        public List<LockstepMessage> log = new List<LockstepMessage>();
        public List<RelayThrough> inputsThrough = new List<RelayThrough>();
        // play
        public LockstepMessage m;
        public int seat;
        public int by;
        public string name = "";
        public string message = "";
    }

    /// <summary>A WebSocket that is polled, never called back: the same in the browser and in the editor.</summary>
    public interface IRelaySocket
    {
        /// <summary>0 connecting, 1 open, 2 closed.</summary>
        int State { get; }
        bool Send(string text);
        /// <summary>The next message that has arrived, or null.</summary>
        string Receive();
        void Close();
    }

    /// <summary>
    /// The connection to one multiplayer room on the relay. Poll it every frame: it reads what has arrived and raises
    /// the matching event. In a match it reconnects by itself after a drop (the relay keeps the seat for a while and
    /// sends the match so far to replay).
    /// </summary>
    public sealed class RelayClient
    {
        public readonly string BaseUrl, Code, ClientId, PlayerName;
        IRelaySocket socket;
        float reconnectAt;
        bool closedByUs;
        /// <summary>The socket factory: the browser's in a web build, .NET's in the editor and tests.</summary>
        public static Func<string, IRelaySocket> Open = DefaultOpen;

        public event Action<RelayMessage> Lobby, Started, Error;
        public event Action<RelayMessage> Dropped;
        public event Action<int, int> SpeedChanged; // speed, by seat
        /// <summary>A lockstep message for the session (see <see cref="Transport"/>).</summary>
        readonly Queue<LockstepMessage> lockstep = new Queue<LockstepMessage>();

        /// <summary>Whether a match has started (from here on a dropped connection is retried).</summary>
        public bool Playing { get; private set; }
        public bool Connected { get { return socket != null && socket.State == 1; } }
        public bool Reconnecting { get { return Playing && !closedByUs && (socket == null || socket.State == 2); } }

        public RelayClient(string baseUrl, string code, string clientId, string playerName)
        {
            BaseUrl = baseUrl.TrimEnd('/');
            Code = code.ToUpperInvariant();
            ClientId = clientId;
            PlayerName = playerName;
            Connect();
        }

        public string Url
        {
            get
            {
                return BaseUrl + "/relay/" + Code + "?client=" + Uri.EscapeDataString(ClientId) + "&name=" + Uri.EscapeDataString(PlayerName ?? "");
            }
        }

        void Connect()
        {
            if (socket != null) socket.Close();
            socket = Open(Url);
        }

        /// <summary>Reads what has arrived; reconnects a dropped match connection every couple of seconds.</summary>
        public void Poll(float now)
        {
            if (socket == null) return;
            if (socket.State == 2 && Playing && !closedByUs)
            {
                if (now >= reconnectAt) { reconnectAt = now + 2f; Connect(); }
                return;
            }
            for (int i = 0; i < 4000; i++)
            {
                string text = socket.Receive();
                if (text == null) break;
                RelayMessage msg;
                try { msg = JsonUtility.FromJson<RelayMessage>(text); }
                catch (Exception) { continue; }
                if (msg == null) continue;
                Dispatch(msg);
            }
        }

        void Dispatch(RelayMessage msg)
        {
            switch (msg.t)
            {
                case "lobby": if (Lobby != null) Lobby(msg); break;
                case "start":
                    Playing = true;
                    if (Started != null) Started(msg);
                    break;
                case "ls": if (msg.m != null) lockstep.Enqueue(msg.m); break;
                case "speed": if (SpeedChanged != null) SpeedChanged(msg.speed, msg.by); break;
                case "dropped": if (Dropped != null) Dropped(msg); break;
                case "error": if (Error != null) Error(msg); break;
            }
        }

        public void Send(RelayMessage msg) { if (socket != null) socket.Send(JsonUtility.ToJson(msg)); }

        public void TakeSeat(int seat) { Send(new RelayMessage { t = "take", seat = seat }); }
        public void Configure(List<RelaySeat> seats, int difficulty) { Send(new RelayMessage { t = "config", seats = seats, difficulty = difficulty }); }
        /// <summary>Starts the match (host only); a seed of 0 lets the relay choose one.</summary>
        public void StartMatch(int seed = 0) { Send(new RelayMessage { t = "start", seed = seed }); }
        public void SetSpeed(int speed) { Send(new RelayMessage { t = "speed", speed = speed }); }

        public void Close()
        {
            closedByUs = true;
            if (socket != null) socket.Close();
            socket = null;
        }

        /// <summary>The lockstep transport over this connection.</summary>
        public ILockstepTransport Transport() { return new RelayTransport(this); }

        sealed class RelayTransport : ILockstepTransport
        {
            readonly RelayClient client;
            public RelayTransport(RelayClient client) { this.client = client; }
            public void Send(LockstepMessage message) { client.Send(new RelayMessage { t = "ls", m = message }); }
            public void Receive(List<LockstepMessage> into) { while (client.lockstep.Count > 0) into.Add(client.lockstep.Dequeue()); }
        }

        // ------------------------------------------------------------------ sockets

        static IRelaySocket DefaultOpen(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new BrowserSocket(url);
#else
            return new DotNetSocket(url);
#endif
        }

        /// <summary>The relay's address: in the browser, the site that served the game; elsewhere a local relay.</summary>
        public static string DefaultBaseUrl()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return BrowserSocket.PageBase();
#else
            var custom = Environment.GetEnvironmentVariable("ORBITAL_FOUNDRY_RELAY");
            return string.IsNullOrEmpty(custom) ? "ws://localhost:8787" : custom;
#endif
        }

        /// <summary>The http(s) address that matches a socket base, for asking the relay for a new room code.</summary>
        public static string HttpBase(string socketBase)
        {
            return socketBase.StartsWith("wss://") ? "https://" + socketBase.Substring(6)
                : socketBase.StartsWith("ws://") ? "http://" + socketBase.Substring(5) : socketBase;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        sealed class BrowserSocket : IRelaySocket
        {
            [System.Runtime.InteropServices.DllImport("__Internal")] static extern int OrbitalFoundry_WsOpen(string url);
            [System.Runtime.InteropServices.DllImport("__Internal")] static extern int OrbitalFoundry_WsState(int id);
            [System.Runtime.InteropServices.DllImport("__Internal")] static extern int OrbitalFoundry_WsSend(int id, string text);
            [System.Runtime.InteropServices.DllImport("__Internal")] static extern string OrbitalFoundry_WsReceive(int id);
            [System.Runtime.InteropServices.DllImport("__Internal")] static extern void OrbitalFoundry_WsClose(int id);
            [System.Runtime.InteropServices.DllImport("__Internal")] static extern string OrbitalFoundry_PageSocketBase();

            readonly int id;
            public BrowserSocket(string url) { id = OrbitalFoundry_WsOpen(url); }
            public int State { get { return OrbitalFoundry_WsState(id); } }
            public bool Send(string text) { return OrbitalFoundry_WsSend(id, text) == 1; }
            public string Receive() { return OrbitalFoundry_WsReceive(id); }
            public void Close() { OrbitalFoundry_WsClose(id); }
            public static string PageBase() { return OrbitalFoundry_PageSocketBase(); }
        }
#else
        /// <summary>.NET's WebSocket client, for the editor and tests: a background reader fills a queue.</summary>
        sealed class DotNetSocket : IRelaySocket
        {
            readonly System.Net.WebSockets.ClientWebSocket ws = new System.Net.WebSockets.ClientWebSocket();
            readonly System.Collections.Concurrent.ConcurrentQueue<string> inbox = new System.Collections.Concurrent.ConcurrentQueue<string>();
            readonly System.Threading.CancellationTokenSource stop = new System.Threading.CancellationTokenSource();
            readonly object sending = new object();
            volatile int state;

            public DotNetSocket(string url)
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await ws.ConnectAsync(new Uri(url), stop.Token);
                        state = 1;
                        var buffer = new byte[1 << 16];
                        var text = new System.Text.StringBuilder();
                        while (!stop.IsCancellationRequested)
                        {
                            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token);
                            if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                            text.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, r.Count));
                            if (!r.EndOfMessage) continue;
                            inbox.Enqueue(text.ToString());
                            text.Clear();
                        }
                    }
                    catch (Exception) { }
                    state = 2;
                });
            }

            public int State { get { return state; } }

            public bool Send(string text)
            {
                if (state != 1) return false;
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                try
                {
                    // One send at a time, in order.
                    lock (sending) ws.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, stop.Token).Wait();
                    return true;
                }
                catch (Exception) { state = 2; return false; }
            }

            public string Receive()
            {
                string text;
                return inbox.TryDequeue(out text) ? text : null;
            }

            public void Close()
            {
                stop.Cancel();
                state = 2;
                try { ws.Abort(); } catch (Exception) { }
            }
        }
#endif
    }
}
