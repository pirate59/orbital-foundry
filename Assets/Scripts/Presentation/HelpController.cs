using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    [Serializable]
    public sealed class HelpSection
    {
        public string heading = "";
        public string text = "";
    }

    [Serializable]
    public sealed class HelpTopic
    {
        public string id = "";
        public string title = "";
        public string summary = "";
        public List<HelpSection> sections = new List<HelpSection>();
        public List<string> related = new List<string>();
    }

    [Serializable]
    public sealed class HelpContent
    {
        public List<HelpTopic> topics = new List<HelpTopic>();
    }

    /// <summary>
    /// Small help popups ("info-bites") loaded from Resources/help.json and opened from the "?" buttons around the HUD.
    /// Buttons carry their topic ID in userData, so a button can change topic as the selection changes. The popup does
    /// not pause the game.
    /// </summary>
    public sealed class HelpController
    {
        public const string IndexTopic = "index";
        const string SeenIntroKey = "help_seen_getting_started";

        readonly Dictionary<string, HelpTopic> topics = new Dictionary<string, HelpTopic>();
        readonly List<HelpTopic> ordered = new List<HelpTopic>();
        VisualElement popup, related;
        Label title, summary;
        ScrollView body;

        public Action<string> Sound;
        public bool IsOpen { get; private set; }

        public void Initialize(VisualElement root)
        {
            var asset = Resources.Load<TextAsset>("help");
            if (asset != null)
            {
                var content = JsonUtility.FromJson<HelpContent>(asset.text);
                foreach (var t in content.topics)
                {
                    topics[t.id] = t;
                    ordered.Add(t);
                }
            }
            else Debug.LogError("Resources/help.json is missing.");

            popup = root.Q<VisualElement>("help-popup");
            title = root.Q<Label>("help-title");
            summary = root.Q<Label>("help-summary");
            body = root.Q<ScrollView>("help-body");
            related = root.Q<VisualElement>("help-related");
            var close = root.Q<Button>("help-close");
            if (popup == null || title == null || summary == null || body == null || related == null || close == null)
            {
                Debug.LogError("Help UI elements are missing from Main.uxml.");
                return;
            }
            close.clicked += Close;
            popup.style.display = DisplayStyle.None;

            // Help buttons in the layout carry the help-wire class and are named help-<topic>.
            root.Query<Button>(className: "help-wire").ForEach(b =>
            {
                if (b.name != null && b.name.StartsWith("help-")) Wire(b, b.name.Substring(5));
            });
        }

        public bool HasTopic(string id) { return id == IndexTopic || topics.ContainsKey(id); }

        /// <summary>A small round "?" button that opens a topic.</summary>
        public Button CreateButton(string topicId)
        {
            var b = new Button { text = "?" };
            b.AddToClassList("help-q");
            b.AddToClassList("help-wire");
            Wire(b, topicId);
            return b;
        }

        void Wire(Button b, string topicId)
        {
            b.userData = topicId;
            b.tooltip = "What is this?";
            b.clicked += () => Show(b.userData as string);
        }

        /// <summary>Points an existing "?" button at another topic, hiding it when there is none.</summary>
        public static void SetTopic(Button b, string topicId)
        {
            if (b == null) return;
            b.userData = topicId;
            b.style.display = string.IsNullOrEmpty(topicId) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void Show(string topicId)
        {
            if (popup == null || string.IsNullOrEmpty(topicId)) return;
            body.Clear();
            related.Clear();
            if (topicId == IndexTopic)
            {
                title.text = "How to play";
                summary.text = "Pick a topic. You can also click any small ? button in the game.";
                foreach (var t in ordered) related.Add(TopicLink(t));
            }
            else
            {
                HelpTopic topic;
                if (!topics.TryGetValue(topicId, out topic)) return;
                title.text = topic.title;
                summary.text = topic.summary;
                foreach (var s in topic.sections)
                {
                    var heading = new Label(s.heading);
                    heading.AddToClassList("help-heading");
                    body.Add(heading);
                    var text = new Label(s.text);
                    text.AddToClassList("help-text");
                    body.Add(text);
                }
                foreach (var id in topic.related)
                {
                    HelpTopic other;
                    if (topics.TryGetValue(id, out other)) related.Add(TopicLink(other));
                }
                related.Add(TopicLink(null));
            }
            body.scrollOffset = Vector2.zero;
            popup.style.display = DisplayStyle.Flex;
            IsOpen = true;
            if (Sound != null) Sound("click");
        }

        Button TopicLink(HelpTopic topic)
        {
            string id = topic != null ? topic.id : IndexTopic;
            var b = new Button(() => Show(id)) { text = topic != null ? topic.title : "All topics" };
            b.AddToClassList("help-link");
            return b;
        }

        public void Close()
        {
            if (popup == null) return;
            popup.style.display = DisplayStyle.None;
            IsOpen = false;
        }

        /// <summary>Shows "Getting started" the first time this browser starts a new game.</summary>
        public void ShowIntroOnce()
        {
            if (PlayerPrefs.GetInt(SeenIntroKey, 0) == 1) return;
            PlayerPrefs.SetInt(SeenIntroKey, 1);
            PlayerPrefs.Save();
            Show("getting_started");
        }
    }
}
