using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// The title screen's new-game seats: four rows, one per corner of the Four Corners map, each Off, You or AI, with an
    /// AI's difficulty and aggressiveness and an optional team. At least two seats must play, and only one can be You;
    /// with no You the game is an exhibition the person watches. Choices are kept in <see cref="GameSettings"/>.
    /// </summary>
    public sealed class SeatSetupPanel
    {
        readonly GameSettings settings;
        readonly Button[] mode = new Button[4], difficulty = new Button[4], aggression = new Button[4], team = new Button[4];
        Label summary;
        Button startButton;

        /// <summary>Played on any change (a click sound).</summary>
        public Action Changed;

        public SeatSetupPanel(GameSettings settings) { this.settings = settings; }

        /// <summary>Builds the rows into the title screen, above its buttons.</summary>
        public void Initialize(VisualElement root)
        {
            var content = root.Q<VisualElement>(className: "title-content");
            var buttons = root.Q<VisualElement>(className: "title-buttons");
            startButton = root.Q<Button>("title-new");
            if (content == null) return;
            var panel = new VisualElement { name = "seat-setup" };
            panel.style.marginTop = 6;
            panel.style.marginBottom = 6;
            panel.AddToClassList("title-section");
            var heading = new Label("Players");
            heading.AddToClassList("menu-label");
            panel.Add(heading);
            for (int i = 0; i < 4; i++)
            {
                int seat = i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 2;
                var swatch = new VisualElement();
                swatch.style.width = 14;
                swatch.style.height = 14;
                swatch.style.marginRight = 6;
                swatch.style.backgroundColor = SpaceSpriteLibrary.FactionColor((FactionId)(seat + 1));
                swatch.style.borderTopLeftRadius = swatch.style.borderTopRightRadius = swatch.style.borderBottomLeftRadius = swatch.style.borderBottomRightRadius = 7;
                row.Add(swatch);
                var name = new Label("Seat " + (seat + 1));
                name.style.width = 56;
                name.AddToClassList("menu-label");
                row.Add(name);
                mode[seat] = Chip(row, () => CycleMode(seat), 64);
                difficulty[seat] = Chip(row, () => { settings.seatDifficulty[seat] = (settings.seatDifficulty[seat] + 1) % 3; Apply(); }, 78);
                aggression[seat] = Chip(row, () => { settings.seatAggression[seat] = (settings.seatAggression[seat] + 1) % 3; Apply(); }, 86);
                team[seat] = Chip(row, () => { settings.seatTeam[seat] = (settings.seatTeam[seat] + 1) % 3; Apply(); }, 80);
                panel.Add(row);
            }
            summary = new Label();
            summary.AddToClassList("menu-label");
            summary.style.marginTop = 4;
            panel.Add(summary);
            int at = buttons != null ? content.IndexOf(buttons) : -1;
            if (at >= 0) content.Insert(at, panel); else content.Add(panel);
            Refresh();
        }

        Button Chip(VisualElement row, Action click, int width)
        {
            var b = new Button(click);
            b.AddToClassList("tool");
            b.AddToClassList("speed-chip");
            b.style.width = width;
            row.Add(b);
            return b;
        }

        void CycleMode(int seat)
        {
            // Off -> You -> AI -> Off. Only one seat can be You: choosing it hands any other You seat to an AI.
            int next = settings.seatMode[seat] == GameSettings.SeatOff ? GameSettings.SeatYou
                : settings.seatMode[seat] == GameSettings.SeatYou ? GameSettings.SeatAi : GameSettings.SeatOff;
            if (next == GameSettings.SeatYou)
                for (int i = 0; i < 4; i++) if (i != seat && settings.seatMode[i] == GameSettings.SeatYou) settings.seatMode[i] = GameSettings.SeatAi;
            settings.seatMode[seat] = next;
            Apply();
        }

        void Apply()
        {
            settings.Save();
            Refresh();
            if (Changed != null) Changed();
        }

        /// <summary>Whether the seats make a game, and a line saying what it will be.</summary>
        public bool Valid(out string message)
        {
            int playing = 0, you = 0;
            var teams = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 4; i++)
            {
                if (settings.seatMode[i] == GameSettings.SeatOff) continue;
                playing++;
                if (settings.seatMode[i] == GameSettings.SeatYou) you++;
                teams.Add(settings.seatTeam[i] == 0 ? 100 + i : settings.seatTeam[i]);
            }
            if (playing < 2) { message = "Choose at least two players."; return false; }
            if (teams.Count < 2) { message = "Everyone is on one team: put someone on the other side."; return false; }
            message = you == 0 ? "Exhibition: " + playing + " AIs play and you watch."
                : "You and " + (playing - 1) + " AI" + (playing > 2 ? "s" : "") + " on Four Corners.";
            return true;
        }

        void Refresh()
        {
            for (int i = 0; i < 4; i++)
            {
                if (mode[i] == null) continue;
                int m = settings.seatMode[i];
                mode[i].text = GameSettings.SeatModeNames[m];
                mode[i].EnableInClassList("choice-active", m != GameSettings.SeatOff);
                bool ai = m == GameSettings.SeatAi;
                difficulty[i].text = new[] { "Easy", "Medium", "Hard" }[settings.seatDifficulty[i]];
                aggression[i].text = GameSettings.AggressionNames[settings.seatAggression[i]];
                team[i].text = GameSettings.TeamNames[settings.seatTeam[i]];
                difficulty[i].style.visibility = ai ? Visibility.Visible : Visibility.Hidden;
                aggression[i].style.visibility = ai ? Visibility.Visible : Visibility.Hidden;
                team[i].style.visibility = m != GameSettings.SeatOff ? Visibility.Visible : Visibility.Hidden;
            }
            string message;
            bool ok = Valid(out message);
            if (summary != null) summary.text = message;
            if (startButton != null) startButton.SetEnabled(ok);
        }
    }
}
