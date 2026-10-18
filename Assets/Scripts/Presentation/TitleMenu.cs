using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// The title screen's pages. The first shows only where to go: Continue (when there is a save), Start a Game,
    /// Multiplayer, Settings, and How to Play (smaller, for newcomers). Start a Game sets up the seats and the trade
    /// goal, then starts (or starts a sandbox mid-game); Multiplayer is the online panel (host, join, lobby). The
    /// buttons and panels are the title's own (Main.uxml, SeatSetupPanel, OnlinePanel), moved into pages, so their
    /// wiring stays where it was. Behind it all, when there is one, the giant ship (TitleShowcase), with a shade on the
    /// menu's side so the text stays readable.
    /// </summary>
    public sealed class TitleMenu
    {
        VisualElement overlay, mainPage, setupPage, stars, shade;
        Label footer, notice;
        OnlinePanel online;
        public Action Click;

        public void Initialize(VisualElement root, OnlinePanel onlinePanel)
        {
            online = onlinePanel;
            overlay = root.Q<VisualElement>("title-overlay");
            var content = root.Q<VisualElement>(className: "title-content");
            var buttons = root.Q<VisualElement>(className: "title-buttons");
            if (overlay == null || content == null || buttons == null) return;
            stars = root.Q<VisualElement>("title-stars");
            footer = root.Q<Label>("title-footer");
            notice = root.Q<Label>("title-notice");
            var art = root.Q<VisualElement>("title-art");
            if (art != null) art.style.display = DisplayStyle.None; // the giant ship takes the portraits' place

            var resume = root.Q<Button>("title-resume");
            var start = root.Q<Button>("title-new");
            var sandbox = root.Q<Button>("title-test");
            var settings = root.Q<Button>("title-settings");
            var help = root.Q<Button>("help-getting_started");
            var difficulty = root.Q<VisualElement>("title-difficulty");
            var seats = root.Q<VisualElement>("seat-setup");

            // The first page: where to go.
            mainPage = Page("title-main");
            mainPage.AddToClassList("title-menu");
            if (resume != null) { resume.text = "Continue"; mainPage.Add(resume); }
            mainPage.Add(Big("Start a Game", ShowSetup));
            mainPage.Add(Big("Multiplayer", ShowOnline));
            if (settings != null) mainPage.Add(settings);
            if (help != null) { help.AddToClassList("title-secondary"); mainPage.Add(help); }

            // Start a Game: the seats and the goal, then go.
            setupPage = Page("title-setup");
            setupPage.Add(Heading("Start a game"));
            if (seats != null) setupPage.Add(seats);
            if (difficulty != null)
            {
                var label = difficulty.Q<Label>(className: "title-difficulty-label");
                if (label != null) label.text = "Trade goal";
                difficulty.tooltip = "How much a minute a side must sell, and hold for 30 seconds, to win by trade. AIs without a difficulty of their own play at this one.";
                setupPage.Add(difficulty);
            }
            var row = new VisualElement();
            row.AddToClassList("title-row-buttons");
            if (start != null) { start.AddToClassList("primary"); row.Add(start); }
            if (sandbox != null) { sandbox.text = "Sandbox: mid-game start"; sandbox.tooltip = "A new map with 20,000 credits and Ion and one of each ship: for trying combat, research and the late game."; row.Add(sandbox); }
            row.Add(Big("Back", ShowMain));
            setupPage.Add(row);

            int at = content.IndexOf(buttons);
            content.Insert(at, mainPage);
            content.Insert(at + 1, setupPage);
            content.Remove(buttons);
            online.Closed = ShowMain;

            // The shade behind the menu, over the ship.
            shade = new VisualElement { name = "title-shade", pickingMode = PickingMode.Ignore };
            shade.AddToClassList("title-shade");
            shade.style.backgroundImage = new StyleBackground(Gradient());
            shade.style.display = DisplayStyle.None;
            overlay.Insert(stars != null ? overlay.IndexOf(stars) + 1 : 0, shade);
            ShowMain();
        }

        /// <summary>The giant ship behind the title: its camera's picture, filling the screen (null: the plain starfield).</summary>
        public void SetBackdrop(RenderTexture picture)
        {
            if (overlay == null) return;
            bool show = picture != null;
            overlay.style.backgroundImage = show ? new StyleBackground(Background.FromRenderTexture(picture)) : new StyleBackground(StyleKeyword.Null);
            overlay.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            overlay.EnableInClassList("title-cinematic", show);
            if (stars != null) stars.style.display = show ? DisplayStyle.None : DisplayStyle.Flex; // the sky has its own stars
            if (shade != null) shade.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ShowMain() { Pages(true, false); }

        /// <summary>Esc on the title: back a page (from Start a Game, or out of Multiplayer). False on the first page.</summary>
        public bool Back()
        {
            if (setupPage != null && setupPage.style.display == DisplayStyle.Flex) { Play(); ShowMain(); return true; }
            if (online != null && online.IsOpen) { online.Close(); return true; }
            return false;
        }

        public void ShowSetup() { Play(); Pages(false, true); }

        public void ShowOnline()
        {
            Play();
            Pages(false, false);
            online.Open();
        }

        /// <summary>Straight onto the online lobby (the host took everyone back from a match).</summary>
        public void ShowLobby()
        {
            Pages(false, false);
            online.ReopenLobby();
        }

        void Pages(bool main, bool setup)
        {
            if (mainPage == null) return;
            mainPage.style.display = main ? DisplayStyle.Flex : DisplayStyle.None;
            setupPage.style.display = setup ? DisplayStyle.Flex : DisplayStyle.None;
            if (footer != null) footer.style.display = main ? DisplayStyle.Flex : DisplayStyle.None;
            if (!main && !setup) return;
            online.Hide();
        }

        void Play() { if (Click != null) Click(); }

        static VisualElement Page(string name)
        {
            var page = new VisualElement { name = name };
            page.AddToClassList("title-page");
            return page;
        }

        static Label Heading(string text)
        {
            var l = new Label(text);
            l.AddToClassList("title-page-heading");
            return l;
        }

        static Button Big(string text, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("tool");
            b.AddToClassList("big");
            b.AddToClassList("title-button");
            return b;
        }

        /// <summary>The title's night colour, solid on the menu's side and fading out across the ship.</summary>
        static Texture2D Gradient()
        {
            const int width = 256;
            var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Title Shade" };
            var c = new Color(16f / 255f, 10f / 255f, 30f / 255f);
            for (int x = 0; x < width; x++)
            {
                float t = x / (width - 1f);
                float a = 0.88f * (1f - Mathf.SmoothStep(0.25f, 1f, t));
                tex.SetPixel(x, 0, new Color(c.r, c.g, c.b, a));
            }
            tex.Apply();
            return tex;
        }
    }
}
