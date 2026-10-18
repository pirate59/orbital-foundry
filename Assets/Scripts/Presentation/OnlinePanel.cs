using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// Play online, on the title screen: host a room (and share its code) or join one by code, then the lobby. Everyone
    /// takes a person's seat or watches; the host sets each seat (person, AI or off), the AIs' difficulty and the
    /// teams, and starts the match when every person's seat is filled. The relay (relay/room.js) holds the lobby.
    /// </summary>
    public sealed class OnlinePanel
    {
        static readonly string[] Modes = { "person", "ai", "off" };
        static readonly string[] Profiles = { "easy", "medium", "hard" };
        const string NameKey = "OrbitalFoundry.OnlineName", ClientKey = "OrbitalFoundry.OnlineClient";

        VisualElement panel, connectView, lobbyView, seatRows;
        TextField nameField, codeField;
        Label status, codeLabel, lobbyNote;
        Button startButton;
        RelayMessage lobby;
        readonly List<VisualElement> hidden = new List<VisualElement>();

        public RelayClient Client { get; private set; }
        public bool IsOpen { get { return panel != null && panel.style.display == DisplayStyle.Flex; } }

        /// <summary>The match has started: the client, and the relay's start message.</summary>
        public Action<RelayClient, RelayMessage> Started;
        public Action Click;

        public void Initialize(VisualElement root)
        {
            var content = root.Q<VisualElement>(className: "title-content");
            var buttons = root.Q<VisualElement>(className: "title-buttons");
            if (content == null || buttons == null) return;
            // The other title rows step aside while this is open.
            foreach (var child in content.Children())
                if (child.name != "title-art" && !child.ClassListContains("title-logo") && !child.ClassListContains("title-tagline")) hidden.Add(child);

            var open = new Button(Open) { text = "Play online", name = "title-online" };
            open.AddToClassList("tool");
            open.AddToClassList("big");
            open.AddToClassList("title-button");
            buttons.Insert(Math.Min(2, buttons.childCount), open);

            panel = new VisualElement { name = "online-panel" };
            panel.style.alignItems = Align.Center;
            panel.style.marginTop = 8;
            panel.style.display = DisplayStyle.None;

            connectView = new VisualElement();
            connectView.style.alignItems = Align.Center;
            connectView.Add(Heading("Play online"));
            nameField = Field("Your name", PlayerPrefs.GetString(NameKey, ""), 20);
            connectView.Add(nameField);
            var row = Row();
            row.Add(Big("Host a game", Host));
            connectView.Add(row);
            row = Row();
            codeField = Field("Room code", "", 5);
            codeField.style.width = 200;
            row.Add(codeField);
            row.Add(Big("Join", Join));
            connectView.Add(row);
            panel.Add(connectView);

            lobbyView = new VisualElement();
            lobbyView.style.alignItems = Align.Center;
            lobbyView.style.display = DisplayStyle.None;
            codeLabel = Heading("");
            codeLabel.style.fontSize = 22;
            lobbyView.Add(codeLabel);
            seatRows = new VisualElement();
            seatRows.style.marginTop = 6;
            lobbyView.Add(seatRows);
            lobbyNote = Note("");
            lobbyView.Add(lobbyNote);
            row = Row();
            startButton = Big("Start", () => { Play(); if (Client != null) Client.StartMatch(); });
            row.Add(startButton);
            lobbyView.Add(row);
            panel.Add(lobbyView);

            status = Note("");
            panel.Add(status);
            var back = Row();
            back.Add(Big("Back", Close));
            panel.Add(back);
            content.Insert(content.IndexOf(buttons), panel);
        }

        void Open()
        {
            Play();
            foreach (var e in hidden) e.style.display = DisplayStyle.None;
            panel.style.display = DisplayStyle.Flex;
            ShowConnect("");
        }

        /// <summary>Opens straight onto the lobby: the host took everyone back from a match.</summary>
        public void ReopenLobby()
        {
            foreach (var e in hidden) e.style.display = DisplayStyle.None;
            panel.style.display = DisplayStyle.Flex;
            connectView.style.display = DisplayStyle.None;
            lobbyView.style.display = DisplayStyle.Flex;
            status.text = "Back in the lobby: the host can start a rematch.";
        }

        /// <summary>Back to the title screen, leaving any room.</summary>
        public void Close()
        {
            Play();
            if (Client != null && !Client.Playing) { Client.Close(); Client = null; }
            panel.style.display = DisplayStyle.None;
            foreach (var e in hidden) e.style.display = StyleKeyword.Null;
        }

        void ShowConnect(string message)
        {
            connectView.style.display = DisplayStyle.Flex;
            lobbyView.style.display = DisplayStyle.None;
            status.text = message;
        }

        void Host() { Connect(NewCode()); }

        void Join()
        {
            string code = (codeField.value ?? "").Trim().ToUpperInvariant();
            if (code.Length != 5) { status.text = "A room code is five letters and digits."; return; }
            Connect(code);
        }

        void Connect(string code)
        {
            Play();
            string name = (nameField.value ?? "").Trim();
            if (name.Length == 0) name = "Player";
            PlayerPrefs.SetString(NameKey, name);
            if (Client != null) Client.Close();
            Client = new RelayClient(RelayClient.DefaultBaseUrl(), code, ClientId(), name);
            Client.Lobby += ShowLobby;
            Client.Error += m => status.text = m.message;
            Client.Started += m => { if (Started != null) Started(Client, m); };
            status.text = "Connecting to room " + code + "...";
        }

        /// <summary>Reads the connection; call every frame while on the title screen.</summary>
        public void Poll()
        {
            if (Client == null) return;
            Client.Poll(Time.unscaledTime);
            if (lobby == null && Client.Reconnecting) status.text = "Could not reach the server. Check the code and your connection.";
        }

        void ShowLobby(RelayMessage m)
        {
            lobby = m;
            connectView.style.display = DisplayStyle.None;
            lobbyView.style.display = DisplayStyle.Flex;
            status.text = "";
            codeLabel.text = "Room " + m.code + "   (share this code)";
            seatRows.Clear();
            foreach (var s in m.seats)
            {
                var seat = s;
                var row = Row();
                var swatch = new VisualElement();
                swatch.style.width = swatch.style.height = 14;
                swatch.style.marginRight = 6;
                swatch.style.backgroundColor = SpaceSpriteLibrary.FactionColor((FactionId)seat.seat);
                row.Add(swatch);
                var label = new Label("Seat " + seat.seat);
                label.style.width = 56;
                label.AddToClassList("menu-label");
                row.Add(label);
                var mode = Chip(seat.mode == "person" ? "Person" : seat.mode == "ai" ? "AI" : "Off", 70,
                    () => Change(seat.seat - 1, x => x.mode = Modes[(Array.IndexOf(Modes, x.mode) + 1) % Modes.Length]));
                mode.SetEnabled(m.host);
                mode.EnableInClassList("choice-active", seat.mode != "off");
                row.Add(mode);
                if (seat.mode == "ai")
                {
                    var profile = Chip(Title(seat.profile), 78, () => Change(seat.seat - 1, x => x.profile = Profiles[(Array.IndexOf(Profiles, x.profile) + 1) % Profiles.Length]));
                    profile.SetEnabled(m.host);
                    row.Add(profile);
                }
                if (seat.mode != "off")
                {
                    var team = Chip(GameSettings.TeamNames[Mathf.Clamp(seat.team, 0, GameSettings.TeamNames.Length - 1)], 80,
                        () => Change(seat.seat - 1, x => x.team = (x.team + 1) % GameSettings.TeamNames.Length));
                    team.SetEnabled(m.host);
                    row.Add(team);
                }
                if (seat.mode == "person")
                {
                    if (seat.seat == m.you) row.Add(Chip("You (" + seat.name + ")  ·  stand up", 170, () => { Play(); Client.TakeSeat(0); }));
                    else if (seat.name.Length > 0) row.Add(Named(seat.name));
                    else row.Add(Chip("Take this seat", 170, () => { Play(); Client.TakeSeat(seat.seat); }));
                }
                seatRows.Add(row);
            }
            string watching = m.watching.Count > 0 ? "Watching: " + string.Join(", ", m.watching) + ".  " : "";
            lobbyNote.text = watching + (m.notReady.Length > 0 ? m.notReady
                : m.host ? "Ready: start when everyone is here." : "Waiting for the host to start.");
            startButton.style.display = m.host ? DisplayStyle.Flex : DisplayStyle.None;
            startButton.SetEnabled(m.notReady.Length == 0);
        }

        /// <summary>The host changes one seat: the whole setup goes to the relay, which tells everyone.</summary>
        void Change(int index, Action<RelaySeat> edit)
        {
            Play();
            if (lobby == null || Client == null) return;
            var seats = new List<RelaySeat>();
            foreach (var s in lobby.seats) seats.Add(new RelaySeat { seat = s.seat, mode = s.mode, profile = s.profile, team = s.team });
            edit(seats[index]);
            Client.Configure(seats, lobby.difficulty);
        }

        /// <summary>This browser's id with the relay, kept so a player who reloads the page gets their seat back.</summary>
        static string ClientId()
        {
            string id = PlayerPrefs.GetString(ClientKey, "");
            if (id.Length < 8)
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(ClientKey, id);
                PlayerPrefs.Save();
            }
            return id;
        }

        /// <summary>A room code: five letters and digits that cannot be mistaken for each other (as relay/room.js).</summary>
        static string NewCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var random = new System.Random(Guid.NewGuid().GetHashCode());
            var chars = new char[5];
            for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[random.Next(alphabet.Length)];
            return new string(chars);
        }

        static string Title(string id) { return id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id.Substring(1); }

        void Play() { if (Click != null) Click(); }

        static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;
            return row;
        }

        static Label Heading(string text)
        {
            var l = new Label(text);
            l.AddToClassList("menu-label");
            l.style.fontSize = 16;
            l.style.marginBottom = 4;
            return l;
        }

        static Label Note(string text)
        {
            var l = new Label(text);
            l.AddToClassList("menu-label");
            l.style.marginTop = 6;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.maxWidth = 520;
            return l;
        }

        static Label Named(string name)
        {
            var l = new Label(name);
            l.AddToClassList("menu-label");
            l.style.width = 170;
            l.style.marginLeft = 4;
            return l;
        }

        static TextField Field(string label, string value, int max)
        {
            var f = new TextField(label) { value = value, maxLength = max };
            f.style.width = 300;
            // A short label, so the box keeps its room.
            f.labelElement.style.minWidth = 90;
            f.labelElement.style.width = 90;
            f.style.marginTop = 4;
            return f;
        }

        static Button Big(string text, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("tool");
            b.AddToClassList("big");
            b.AddToClassList("title-button");
            return b;
        }

        static Button Chip(string text, int width, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("tool");
            b.AddToClassList("speed-chip");
            b.style.width = width;
            return b;
        }
    }
}
