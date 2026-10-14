using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// The research web, opened from the player's Headquarters. Nodes sit at their authored web positions on a
    /// panned and zoomed canvas beside a detail panel that never moves. Only what the player has reached is drawn:
    /// owned and open nodes carry their names, a node one step further out is an undiscovered outline, and anything
    /// beyond that is not drawn at all. The game is paused while it is open.
    /// </summary>
    public sealed class ResearchController
    {
        const float SpacingX = 160f, SpacingY = 124f, NodeWidth = 138f, NodeHeight = 64f;
        const float MinScale = 0.45f, MaxScale = 1.6f;

        static readonly Dictionary<string, Color> BranchColours = new Dictionary<string, Color>
        {
            { "Core", new Color32(255, 222, 126, 255) },
            { "Metallurgy", new Color32(176, 190, 214, 255) },
            { "Electrics", new Color32(236, 150, 100, 255) },
            { "Chemistry", new Color32(120, 214, 240, 255) },
            { "Industry", new Color32(151, 247, 200, 255) },
            { "Power", new Color32(108, 211, 208, 255) },
            { "Commerce", new Color32(255, 177, 153, 255) },
        };

        GameLoop loop;
        VisualElement overlay, web, world, links, pips;
        Label funds, branchLabel, nameLabel, stateLabel, textLabel, costLabel, ownedLabel;
        Button buyButton;
        readonly Dictionary<string, Button> nodeButtons = new Dictionary<string, Button>();
        readonly List<(Vector2 from, Vector2 to, bool met, Color colour)> linkLines = new List<(Vector2, Vector2, bool, Color)>();

        string selectedId = "", signature = "";
        Vector2 pan;
        float scale = 1f;
        bool dragging, dragged, needsCentre;
        Vector2 dragStart, panStart;

        public Action<CommandResult> Feedback;
        public Action<string> Sound;
        public bool IsOpen { get; private set; }

        public void Initialize(VisualElement root, GameLoop gameLoop)
        {
            loop = gameLoop;
            overlay = root.Q<VisualElement>("research-overlay");
            web = root.Q<VisualElement>("research-web");
            funds = root.Q<Label>("research-funds");
            branchLabel = root.Q<Label>("research-node-branch");
            nameLabel = root.Q<Label>("research-node-name");
            stateLabel = root.Q<Label>("research-node-state");
            textLabel = root.Q<Label>("research-node-text");
            costLabel = root.Q<Label>("research-node-cost");
            ownedLabel = root.Q<Label>("research-node-owned");
            pips = root.Q<VisualElement>("research-node-pips");
            buyButton = root.Q<Button>("research-buy");
            var close = root.Q<Button>("research-close");
            if (overlay == null || web == null || buyButton == null || close == null || nameLabel == null)
            {
                Debug.LogError("Research UI elements are missing from Main.uxml.");
                return;
            }

            world = new VisualElement { name = "research-world", pickingMode = PickingMode.Ignore };
            world.AddToClassList("research-world");
            world.style.transformOrigin = new TransformOrigin(0, 0);
            links = new VisualElement { pickingMode = PickingMode.Ignore };
            links.AddToClassList("research-links");
            links.generateVisualContent += DrawLinks;
            world.Add(links);
            web.Add(world);

            web.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 && e.button != 1 && e.button != 2) return;
                dragging = true;
                dragged = false;
                dragStart = e.position;
                panStart = pan;
            }, TrickleDown.TrickleDown);
            web.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                Vector2 delta = (Vector2)e.position - dragStart;
                if (!dragged && delta.sqrMagnitude < 25f) return;
                if (!dragged) web.CapturePointer(e.pointerId);
                dragged = true;
                pan = panStart + delta;
                ApplyView();
            });
            web.RegisterCallback<PointerUpEvent>(e =>
            {
                dragging = false;
                if (web.HasPointerCapture(e.pointerId)) web.ReleasePointer(e.pointerId);
            });
            web.RegisterCallback<WheelEvent>(e =>
            {
                float next = Mathf.Clamp(scale * (e.delta.y > 0 ? 0.9f : 1.1f), MinScale, MaxScale);
                // Zoom about the pointer so the node under it stays put.
                Vector2 local = (Vector2)e.localMousePosition;
                pan = local - (local - pan) * (next / scale);
                scale = next;
                ApplyView();
                e.StopPropagation();
            });

            buyButton.clicked += Buy;
            close.clicked += Close;
            overlay.style.display = DisplayStyle.None;
        }

        public void Open()
        {
            if (overlay == null || loop == null || loop.World == null) return;
            IsOpen = true;
            overlay.style.display = DisplayStyle.Flex;
            signature = "";
            if (string.IsNullOrEmpty(selectedId)) selectedId = FirstChoice();
            needsCentre = true;
            Refresh();
            if (Sound != null) Sound("click");
        }

        public void Close()
        {
            if (overlay == null) return;
            IsOpen = false;
            overlay.style.display = DisplayStyle.None;
        }

        FactionState Player { get { return WorldLookup.GetFaction(loop.World, FactionId.Player); } }

        string FirstChoice()
        {
            var c = loop.Catalog;
            var f = Player;
            foreach (var node in c.research)
                if (ResearchService.Visibility(c, f, node) == ResearchVisibility.Open) return node.id;
            return c.research.Count > 0 ? c.research[0].id : "";
        }

        /// <summary>Rebuilds the web when research, funds or the selection changed; cheap to call every frame.</summary>
        public void Refresh()
        {
            if (!IsOpen || world == null) return;
            if (needsCentre && web.contentRect.width > 1f)
            {
                // Once laid out, put the root near the top centre of the web.
                needsCentre = false;
                var size = web.contentRect.size;
                pan = new Vector2(size.x * 0.5f, Mathf.Min(110f, size.y * 0.2f));
                scale = 1f;
                ApplyView();
            }
            var c = loop.Catalog;
            var f = Player;
            if (f == null) return;
            var sb = new StringBuilder(selectedId).Append('|').Append(f.currency).Append('|').Append(f.ion).Append('|');
            foreach (var r in f.research) sb.Append(r.nodeId).Append(r.level).Append(',');
            string next = sb.ToString();
            if (next == signature) return;
            signature = next;

            funds.text = "Credits: " + f.currency.ToString("N0") + "  ·  Ion: " + f.ion.ToString("N0");
            RebuildNodes(c, f);
            RefreshDetail(c, f);
        }

        void RebuildNodes(ContentCatalog c, FactionState f)
        {
            foreach (var b in nodeButtons.Values) b.RemoveFromHierarchy();
            nodeButtons.Clear();
            linkLines.Clear();

            foreach (var node in c.research)
            {
                var vis = ResearchService.Visibility(c, f, node);
                if (vis == ResearchVisibility.Hidden) continue;
                foreach (var pre in node.prerequisites)
                {
                    var parent = CatalogRepository.GetResearch(c, pre.nodeId);
                    if (parent == null || ResearchService.Visibility(c, f, parent) == ResearchVisibility.Hidden) continue;
                    linkLines.Add((Centre(parent), Centre(node), ResearchService.Level(f, parent.id) >= pre.minimumLevel, Branch(node.branch)));
                }

                string id = node.id;
                var button = new Button(() => { if (!dragged) Select(id); });
                button.AddToClassList("research-node");
                button.style.left = node.x * SpacingX - NodeWidth / 2;
                button.style.top = node.y * SpacingY - NodeHeight / 2;
                button.style.width = NodeWidth;
                button.style.height = NodeHeight;
                var colour = Branch(node.branch);
                button.style.borderLeftColor = colour;
                button.style.borderTopColor = colour;

                int level = ResearchService.Level(f, node.id);
                string state = StateClass(c, f, node, vis);
                button.AddToClassList(state);
                button.EnableInClassList("research-selected", node.id == selectedId);
                if (vis == ResearchVisibility.Hinted)
                {
                    button.Add(NodeLabel("?", "research-node-mark"));
                    button.Add(NodeLabel("Undiscovered", "research-node-sub"));
                }
                else
                {
                    button.Add(NodeLabel(node.displayName, "research-node-name"));
                    button.Add(NodeLabel(level >= node.levels.Count ? "Complete" : node.levels.Count > 1 ? "Level " + level + " / " + node.levels.Count : level > 0 ? "Owned" : CostText(node.levels[0]), "research-node-sub"));
                }
                world.Add(button);
                nodeButtons[node.id] = button;
            }
            links.MarkDirtyRepaint();
        }

        static Label NodeLabel(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            return l;
        }

        static string StateClass(ContentCatalog c, FactionState f, ResearchNodeDefinition node, ResearchVisibility vis)
        {
            if (vis == ResearchVisibility.Hinted) return "research-hinted";
            if (ResearchService.NextLevel(f, node) == null) return "research-maxed";
            return ResearchService.CheckPurchase(c, f, node) == null ? "research-affordable"
                : ResearchService.Level(f, node.id) > 0 ? "research-owned" : "research-unaffordable";
        }

        void RefreshDetail(ContentCatalog c, FactionState f)
        {
            var node = CatalogRepository.GetResearch(c, selectedId);
            if (node == null || ResearchService.Visibility(c, f, node) == ResearchVisibility.Hidden)
            {
                selectedId = FirstChoice();
                node = CatalogRepository.GetResearch(c, selectedId);
                if (node == null) return;
            }
            var vis = ResearchService.Visibility(c, f, node);
            int level = ResearchService.Level(f, node.id);
            var next = ResearchService.NextLevel(f, node);
            branchLabel.text = node.branch.ToUpperInvariant();
            branchLabel.style.color = Branch(node.branch);
            pips.Clear();

            if (vis == ResearchVisibility.Hinted)
            {
                nameLabel.text = "Undiscovered";
                stateLabel.text = "Not yet in reach";
                var needs = new List<string>();
                foreach (var pre in node.prerequisites)
                {
                    var parent = CatalogRepository.GetResearch(c, pre.nodeId);
                    if (parent != null && ResearchService.Level(f, parent.id) < pre.minimumLevel)
                        needs.Add(parent.displayName + (pre.minimumLevel > 1 ? " level " + pre.minimumLevel : ""));
                }
                textLabel.text = "Research " + string.Join(" and ", needs) + " to reveal what this is.";
                costLabel.text = "";
                ownedLabel.text = "";
                buyButton.style.display = DisplayStyle.None;
                return;
            }

            nameLabel.text = node.displayName;
            for (int i = 0; i < node.levels.Count; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("research-pip");
                if (i < level) pip.style.backgroundColor = Branch(node.branch);
                pips.Add(pip);
            }
            var refusal = ResearchService.CheckPurchase(c, f, node);
            stateLabel.text = next == null ? "Fully researched"
                : refusal == null ? "Available"
                : refusal.code == "LOCKED" ? "Locked"
                : refusal.code == "NO_MONEY" || refusal.code == "NO_ION" ? "Not enough funds yet"
                : refusal.message;
            textLabel.text = next != null ? (node.levels.Count > 1 ? "Level " + (level + 1) + ": " : "") + next.description : "All levels owned.";
            costLabel.text = next != null ? "Cost: " + CostText(next) : "";
            buyButton.style.display = next != null ? DisplayStyle.Flex : DisplayStyle.None;
            buyButton.SetEnabled(refusal == null && loop.World.mode == MatchMode.Factory);
            buyButton.text = level > 0 ? "Research level " + (level + 1) : "Research";

            var owned = new StringBuilder();
            for (int i = 0; i < level; i++) owned.Append("- ").Append(node.levels[i].description).Append('\n');
            ownedLabel.text = owned.ToString().TrimEnd();
        }

        string CostText(ResearchLevelDefinition level)
        {
            return level.creditCost + " credits · " + ResearchService.IonPrice(loop.Catalog, Player, level) + " Ion";
        }

        void Select(string id)
        {
            selectedId = id;
            signature = "";
            Refresh();
            if (Sound != null) Sound("click");
        }

        void Buy()
        {
            var r = loop.Submit(new GameCommand { kind = CommandKind.Research, actor = FactionId.Player, contentId = selectedId });
            signature = "";
            Refresh();
            if (Feedback != null) Feedback(r);
        }

        static Vector2 Centre(ResearchNodeDefinition node)
        {
            return new Vector2(node.x * SpacingX, node.y * SpacingY);
        }

        static Color Branch(string branch)
        {
            Color colour;
            return BranchColours.TryGetValue(branch ?? "", out colour) ? colour : new Color32(204, 177, 218, 255);
        }

        void ApplyView()
        {
            world.style.translate = new Translate(pan.x, pan.y);
            world.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }

        void DrawLinks(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            p.lineCap = LineCap.Round;
            foreach (var link in linkLines)
            {
                var colour = link.colour;
                colour.a = link.met ? 0.95f : 0.3f;
                p.strokeColor = colour;
                p.lineWidth = link.met ? 4f : 2.5f;
                p.BeginPath();
                p.MoveTo(link.from);
                // A short elbow keeps links readable when a node has parents in two columns.
                float midY = (link.from.y + link.to.y) * 0.5f;
                p.LineTo(new Vector2(link.from.x, midY));
                p.LineTo(new Vector2(link.to.x, midY));
                p.LineTo(link.to);
                p.Stroke();
            }
        }
    }
}
