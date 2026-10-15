using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// The research web, opened from the player's Headquarters. Branches run left to right in coloured lanes, from
    /// the root on the left to the deepest research on the right, on a canvas beside a detail panel that never moves.
    /// Drag anywhere (on a node too) to move the canvas, click a node to see it, scroll or use the corner buttons to
    /// zoom; arrow keys or WASD pan, F fits the whole web, Enter researches the selected node. Only what the player
    /// has reached is drawn: owned and open nodes carry their names, a node one step further out is an undiscovered
    /// outline, and anything beyond that is not drawn at all. The game is paused while it is open.
    /// </summary>
    public sealed class ResearchController
    {
        const float ColumnStep = 230f, RowStep = 84f, NodeWidth = 172f, NodeHeight = 62f, LaneGap = 14f;
        const float MinScale = 0.4f, MaxScale = 1.6f;
        /// <summary>A press that moves this far (in pixels) becomes a drag rather than a click.</summary>
        const float DragThreshold = 6f;
        const float KeyPanSpeed = 700f;

        static readonly Dictionary<string, Color> BranchColours = new Dictionary<string, Color>
        {
            { "Core", new Color32(255, 222, 126, 255) },
            { "Metallurgy", new Color32(176, 190, 214, 255) },
            { "Electrics", new Color32(236, 150, 100, 255) },
            { "Chemistry", new Color32(120, 214, 240, 255) },
            { "Industry", new Color32(151, 247, 200, 255) },
            { "Power", new Color32(108, 211, 208, 255) },
            { "Commerce", new Color32(255, 177, 153, 255) },
            { "Logistics", new Color32(127, 245, 224, 255) },
        };

        GameLoop loop;
        VisualElement overlay, web, world, links, lanes, controls, pips;
        Label funds, branchLabel, nameLabel, stateLabel, textLabel, costLabel, ownedLabel, leadsLabel;
        Button buyButton;
        readonly Dictionary<string, Button> nodeButtons = new Dictionary<string, Button>();
        readonly List<(Vector2 from, Vector2 to, bool met, Color colour, bool lit)> linkLines = new List<(Vector2, Vector2, bool, Color, bool)>();
        /// <summary>Row of each authored web column (node.x), packed so empty columns take no space.</summary>
        readonly Dictionary<int, int> rowOf = new Dictionary<int, int>();
        ContentCatalog laidOut;
        Rect bounds;

        string selectedId = "", signature = "";
        Vector2 pan;
        float scale = 1f;
        bool pressed, dragged, needsFit;
        int pressId = -1;
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
            lanes = new VisualElement { pickingMode = PickingMode.Ignore };
            lanes.AddToClassList("research-lanes");
            world.Add(lanes);
            links = new VisualElement { pickingMode = PickingMode.Ignore };
            links.AddToClassList("research-links");
            links.generateVisualContent += DrawLinks;
            world.Add(links);
            web.Add(world);
            web.generateVisualContent += DrawGrid;

            // Zoom and fit buttons in the corner, and a reminder of the controls.
            controls = new VisualElement();
            controls.AddToClassList("research-controls");
            controls.Add(ControlButton("+", "Zoom in", () => ZoomBy(1.25f, WebCentre())));
            controls.Add(ControlButton("-", "Zoom out", () => ZoomBy(0.8f, WebCentre())));
            controls.Add(ControlButton("Fit", "Show the whole web [F]", FitAll));
            web.Add(controls);
            var hint = new Label("Drag to move  \u00b7  Scroll to zoom  \u00b7  Click a node for details  \u00b7  F fits all  \u00b7  Enter researches") { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("research-hint");
            web.Add(hint);

            // Every press starts as a possible click. It turns into a drag only once it moves a few pixels, so a
            // node can be dragged from as well as clicked. These listen on the way down, before a node button
            // sees the event, so a press always ends here however the button handles its release.
            web.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 && e.button != 1 && e.button != 2) return;
                if (controls.Contains(e.target as VisualElement)) return;
                pressed = true;
                dragged = false;
                pressId = e.pointerId;
                dragStart = e.position;
                panStart = pan;
            }, TrickleDown.TrickleDown);
            web.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!pressed || e.pointerId != pressId) return;
                if (e.pressedButtons == 0) { EndPress(); return; } // the release happened somewhere we did not see
                Vector2 delta = (Vector2)e.position - dragStart;
                if (!dragged && delta.sqrMagnitude < DragThreshold * DragThreshold) return;
                if (!dragged)
                {
                    // Taking the pointer from a node button cancels its click.
                    dragged = true;
                    web.CapturePointer(e.pointerId);
                    web.AddToClassList("research-web-dragging");
                }
                pan = panStart + delta;
                ApplyView();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            web.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != pressId) return;
                bool wasDrag = dragged;
                EndPress();
                if (wasDrag) e.StopPropagation();
            }, TrickleDown.TrickleDown);
            web.RegisterCallback<PointerCaptureOutEvent>(e => { if (dragged) EndPress(); });
            web.RegisterCallback<WheelEvent>(e =>
            {
                // Zoom about the pointer so the node under it stays put.
                float steps = Mathf.Clamp(e.delta.y, -6f, 6f) / 3f;
                ZoomBy(Mathf.Pow(0.88f, steps == 0 ? 0 : Mathf.Sign(steps) * Mathf.Max(1f, Mathf.Abs(steps))), (Vector2)e.localMousePosition);
                e.StopPropagation();
            });
            web.RegisterCallback<GeometryChangedEvent>(e => { if (IsOpen) ApplyView(); });

            leadsLabel = new Label { name = "research-node-leads" };
            leadsLabel.AddToClassList("body-text");
            leadsLabel.AddToClassList("small");
            leadsLabel.AddToClassList("research-leads");
            var detail = root.Q<VisualElement>("research-detail");
            if (detail != null) detail.Add(leadsLabel);

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
            needsFit = true;
            Refresh();
            if (Sound != null) Sound("click");
        }

        public void Close()
        {
            if (overlay == null) return;
            IsOpen = false;
            EndPress();
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
            Layout(loop.Catalog);
            HandleKeys();
            if (needsFit && signature != "" && web.contentRect.width > 1f)
            {
                needsFit = false;
                FitAll();
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
            if (needsFit && web.contentRect.width > 1f)
            {
                // Once laid out, show everything the player can see, or as much around the selection as fits.
                needsFit = false;
                FitAll();
            }
        }

        void RebuildNodes(ContentCatalog c, FactionState f)
        {
            foreach (var b in nodeButtons.Values) b.RemoveFromHierarchy();
            nodeButtons.Clear();
            linkLines.Clear();
            lanes.Clear();
            bool any = false;
            var shownBranches = new HashSet<string>();

            foreach (var node in c.research)
            {
                var vis = ResearchService.Visibility(c, f, node);
                if (vis == ResearchVisibility.Hidden) continue;
                foreach (var pre in node.prerequisites)
                {
                    var parent = CatalogRepository.GetResearch(c, pre.nodeId);
                    if (parent == null || ResearchService.Visibility(c, f, parent) == ResearchVisibility.Hidden) continue;
                    bool lit = node.id == selectedId || parent.id == selectedId;
                    linkLines.Add((Centre(parent), Centre(node), ResearchService.Level(f, parent.id) >= pre.minimumLevel, Branch(node.branch), lit));
                }

                string id = node.id;
                var button = new Button(() => { if (!dragged) Select(id); });
                button.AddToClassList("research-node");
                var centre = Centre(node);
                button.style.left = centre.x - NodeWidth / 2;
                button.style.top = centre.y - NodeHeight / 2;
                var rect = new Rect(centre.x - NodeWidth / 2, centre.y - NodeHeight / 2, NodeWidth, NodeHeight);
                bounds = any ? Union(bounds, rect) : rect;
                any = true;
                if (node.prerequisites.Count > 0) shownBranches.Add(node.branch);
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
            foreach (var lane in laneRects)
            {
                if (!shownBranches.Contains(lane.Key)) continue;
                var band = new VisualElement { pickingMode = PickingMode.Ignore };
                band.AddToClassList("research-lane");
                var colour = Branch(lane.Key);
                band.style.left = lane.Value.x;
                band.style.top = lane.Value.y;
                band.style.width = lane.Value.width;
                band.style.height = lane.Value.height;
                band.style.backgroundColor = new Color(colour.r, colour.g, colour.b, 0.07f);
                band.style.borderLeftColor = new Color(colour.r, colour.g, colour.b, 0.8f);
                var name = NodeLabel(lane.Key.ToUpperInvariant(), "research-lane-name");
                name.style.color = colour;
                band.Add(name);
                lanes.Add(band);
                bounds = any ? Union(bounds, lane.Value) : lane.Value;
                any = true;
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
                leadsLabel.text = "";
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
            leadsLabel.text = LeadsTo(c, f, node);
        }

        /// <summary>What researching this node opens up: named where the player can see it, counted where not.</summary>
        static string LeadsTo(ContentCatalog c, FactionState f, ResearchNodeDefinition node)
        {
            var names = new List<string>();
            int unknown = 0;
            foreach (var other in c.research)
            {
                if (!other.prerequisites.Exists(p => p.nodeId == node.id)) continue;
                var vis = ResearchService.Visibility(c, f, other);
                if (vis == ResearchVisibility.Open || vis == ResearchVisibility.Owned) names.Add(other.displayName);
                else unknown++;
            }
            if (unknown > 0) names.Add(unknown == 1 ? "1 undiscovered node" : unknown + " undiscovered nodes");
            return names.Count == 0 ? "" : "Leads to: " + string.Join(", ", names);
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

        /// <summary>
        /// Packs the authored web into lanes: depth (node.y) runs left to right and each branch gets its own band of
        /// rows (one per authored column, node.x), with a gap between branches. The root sits mid-height on the left.
        /// </summary>
        void Layout(ContentCatalog c)
        {
            if (laidOut == c) return;
            laidOut = c;
            rowOf.Clear();
            rowY.Clear();
            laneRects.Clear();
            var columns = new SortedDictionary<int, string>();
            int deepest = 1;
            foreach (var node in c.research)
            {
                deepest = Mathf.Max(deepest, node.y);
                if (node.prerequisites.Count > 0 && !columns.ContainsKey(node.x)) columns[node.x] = node.branch;
            }
            float y = 0f;
            string previous = null;
            foreach (var column in columns)
            {
                // Each branch starts with a header strip for its name, clear of the links fanning in from the left.
                bool first = column.Value != previous;
                if (first) y += (previous != null ? LaneGap : 0f) + LaneHeader;
                rowY[column.Key] = y;
                Rect lane;
                float top = y - RowStep / 2 + 3 - (first ? LaneHeader : 0f);
                var row = Rect.MinMaxRect(LaneLeft, top, DepthX(deepest) + NodeWidth / 2 + 16, y + RowStep / 2 - 3);
                laneRects[column.Value] = laneRects.TryGetValue(column.Value, out lane) ? Union(lane, row) : row;
                y += RowStep;
                previous = column.Value;
            }
            rootY = (y - RowStep + LaneHeader) / 2f;
        }

        readonly Dictionary<int, float> rowY = new Dictionary<int, float>();
        readonly Dictionary<string, Rect> laneRects = new Dictionary<string, Rect>();
        float rootY;
        const float LaneHeader = 20f;
        static readonly float LaneLeft = DepthX(1) - NodeWidth / 2 - 16f;

        static float DepthX(int depth) { return depth <= 0 ? 0f : 120f + depth * ColumnStep; }

        Vector2 Centre(ResearchNodeDefinition node)
        {
            float y;
            if (node.prerequisites.Count == 0 || !rowY.TryGetValue(node.x, out y)) y = rootY;
            return new Vector2(DepthX(node.y), y);
        }

        static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }

        Button ControlButton(string text, string tooltip, Action action)
        {
            var b = new Button(action) { text = text, tooltip = tooltip };
            b.AddToClassList("research-control");
            return b;
        }

        Vector2 WebCentre() { return web.contentRect.size * 0.5f; }

        /// <summary>Scales the view by a factor about a point in the web (its local coordinates).</summary>
        void ZoomBy(float factor, Vector2 about)
        {
            float next = Mathf.Clamp(scale * factor, MinScale, MaxScale);
            pan = about - (about - pan) * (next / scale);
            scale = next;
            ApplyView();
        }

        /// <summary>Frames everything drawn, as large as fits up to full size.</summary>
        void FitAll()
        {
            var size = web.contentRect.size;
            if (size.x < 1f || bounds.width < 1f) return;
            float fit = Mathf.Min((size.x - 60f) / bounds.width, (size.y - 90f) / bounds.height);
            scale = Mathf.Clamp(fit, MinScale, 1.1f);
            pan = new Vector2(size.x * 0.5f, (size.y - 30f) * 0.5f) - bounds.center * scale;
            ApplyView();
        }

        void EndPress()
        {
            if (pressId >= 0 && web != null && web.HasPointerCapture(pressId)) web.ReleasePointer(pressId);
            pressed = false;
            pressId = -1;
            if (web != null) web.RemoveFromClassList("research-web-dragging");
            // dragged stays set until the next press, so the node under a finished drag is not also clicked.
        }

        void HandleKeys()
        {
            var move = Vector2.zero;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) move.x += 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) move.x -= 1f;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) move.y += 1f;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) move.y -= 1f;
            if (move != Vector2.zero)
            {
                pan += move * KeyPanSpeed * Time.unscaledDeltaTime;
                ApplyView();
            }
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus)) ZoomBy(1.25f, WebCentre());
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) ZoomBy(0.8f, WebCentre());
            if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Home)) FitAll();
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && buyButton.enabledSelf && buyButton.resolvedStyle.display == DisplayStyle.Flex) Buy();
        }

        static Color Branch(string branch)
        {
            Color colour;
            return BranchColours.TryGetValue(branch ?? "", out colour) ? colour : new Color32(204, 177, 218, 255);
        }

        void ApplyView()
        {
            // Keep some of the web on screen, so it cannot be lost off an edge.
            var size = web.contentRect.size;
            if (size.x > 1f && bounds.width > 1f)
            {
                const float keep = 140f;
                pan.x = ClampAxis(pan.x, keep - bounds.xMax * scale, size.x - keep - bounds.xMin * scale);
                pan.y = ClampAxis(pan.y, keep - bounds.yMax * scale, size.y - keep - bounds.yMin * scale);
            }
            web.MarkDirtyRepaint();
            world.style.translate = new Translate(pan.x, pan.y);
            world.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }

        static float ClampAxis(float v, float min, float max)
        {
            return min > max ? (min + max) * 0.5f : Mathf.Clamp(v, min, max);
        }

        /// <summary>A faint grid behind the web that moves with it, so a drag always shows movement.</summary>
        void DrawGrid(MeshGenerationContext mgc)
        {
            var size = web.contentRect.size;
            if (size.x < 1f) return;
            var p = mgc.painter2D;
            p.strokeColor = new Color(1f, 1f, 1f, 0.045f);
            p.lineWidth = 1f;
            float step = 60f * scale;
            while (step < 24f) step *= 2f;
            p.BeginPath();
            for (float x = Mathf.Repeat(pan.x, step); x < size.x; x += step) { p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, size.y)); }
            for (float y = Mathf.Repeat(pan.y, step); y < size.y; y += step) { p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(size.x, y)); }
            p.Stroke();
        }

        void DrawLinks(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            p.lineCap = LineCap.Round;
            // Dim links first, then met ones, then those touching the selection on top.
            for (int pass = 0; pass < 3; pass++)
                foreach (var link in linkLines)
                {
                    int layer = link.lit ? 2 : link.met ? 1 : 0;
                    if (layer != pass) continue;
                    var colour = link.lit ? Color.Lerp(link.colour, Color.white, 0.45f) : link.colour;
                    colour.a = link.lit ? 1f : link.met ? 0.85f : 0.3f;
                    p.strokeColor = colour;
                    p.lineWidth = link.lit ? 5f : link.met ? 3.5f : 2.5f;
                    // From the parent's right edge to the child's left edge, as a smooth S-curve.
                    var from = link.from + new Vector2(NodeWidth / 2, 0);
                    var to = link.to - new Vector2(NodeWidth / 2, 0);
                    float bend = Mathf.Max(30f, (to.x - from.x) * 0.5f);
                    p.BeginPath();
                    p.MoveTo(from);
                    p.BezierCurveTo(from + new Vector2(bend, 0), to - new Vector2(bend, 0), to);
                    p.Stroke();
                    // A small dot where the link enters the child.
                    p.fillColor = colour;
                    p.BeginPath();
                    p.Arc(to, link.lit ? 4.5f : 3.5f, 0f, 360f);
                    p.Fill();
                }
        }
    }
}
