using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// Advance Wars-style battle scenes. Each resolved attack cuts to a split stage: the attacker's formation on the
    /// left, the target's on the right, each craft shown as its fleet (see <see cref="Fleet"/>): ten small Mobile
    /// Guards, four big Heavy Bombers, one huge Flying Fortress, losing ships as they lose HP. The attacker fires across the divider, the
    /// target's HP counts down and ships drop out, then the survivor counters. Scenes replay
    /// <see cref="CombatResolver.Events"/> in order; play is paused while one runs, and a click skips it.
    /// </summary>
    public sealed class BattleSceneController
    {
        const float StageWidth = 960f, StageHeight = 540f, HalfWidth = StageWidth / 2f;
        // The box a fleet flies in, and a Mobile Guard ship's size; bigger ships scale up from it.
        // Close up: the fleets fill most of each half.
        const float FormationLeft = 16f, FormationWidth = 448f, FormationBottom = 64f, FormationHeight = 340f, GuardShipSize = 96f;

        static readonly Color Mint = new Color32(151, 247, 200, 255);
        static readonly Color Peach = new Color32(255, 177, 153, 255);
        static readonly Color Gold = new Color32(255, 222, 126, 255);
        static readonly Color Danger = new Color32(255, 125, 110, 255);

        sealed class Side
        {
            public VisualElement Root;
            public readonly List<Image> Ships = new List<Image>();
            public readonly List<bool> Gone = new List<bool>();
            /// <summary>Each ship's lower-left corner within its half, as if on the left (the right half mirrors it).</summary>
            public readonly List<Vector2> At = new List<Vector2>();
            public float Size;
            public CraftDefinition Def;
            /// <summary>Each ship's 3D model on the battle stage (null where there is none: its picture shows instead).</summary>
            public readonly List<Model> Models = new List<Model>();
            /// <summary>Each model's flicker and knock-out state, mirroring what its picture would do.</summary>
            public readonly List<float> Opacity = new List<float>();
            public readonly List<float> Shrink = new List<float>();
            /// <summary>When each ship was last jolted by a hit (or recoiled from firing), and which way.</summary>
            public readonly List<float> JoltAt = new List<float>();
            public readonly List<float> JoltBy = new List<float>();
            /// <summary>How far each knocked-out ship has tumbled away (0 to 1), and which ships trail smoke.</summary>
            public readonly List<float> Tumble = new List<float>();
            public readonly List<bool> Smoking = new List<bool>();
            /// <summary>How far the side has slid in from its edge (pixels; 0 once in place).</summary>
            public float Slide;
            public Label Hp;
            public VisualElement BarFill;
            public int MaxHealth;
            public bool Left;
        }

        sealed class Tween
        {
            public float Start, Duration;
            public Action<float> Apply;
            public bool Started, Done;
        }

        readonly SpaceSpriteLibrary sprites;
        /// <summary>The 3D set the fleets are filmed on, shown behind the scene's overlay.</summary>
        readonly BattleStage stage3D = new BattleStage();
        Image stageView;
        float shakeAt = -10f, shakePower;
        readonly GameSettings settings;
        GameLoop loop;
        VisualElement overlay, stage, fx;
        Label callout, log;
        Side left, right;
        readonly Queue<CombatEvent> queue = new Queue<CombatEvent>();
        readonly List<Tween> tweens = new List<Tween>();
        float time, endTime;

        public Action<string> Sound;
        public bool IsPlaying { get; private set; }
        /// <summary>Set by the bootstrap: shows a line in the status bar (the summary of an instant AI turn).</summary>
        public Action<string> Report;

        public BattleSceneController(SpaceSpriteLibrary sprites, GameSettings settings)
        {
            this.sprites = sprites;
            this.settings = settings;
        }

        public void Initialize(VisualElement root, GameLoop gameLoop)
        {
            loop = gameLoop;
            overlay = new VisualElement { name = "battle-scene" };
            overlay.AddToClassList("battle-overlay");
            overlay.RegisterCallback<PointerDownEvent>(e => { Skip(); e.StopPropagation(); });
            stage = new VisualElement();
            stage.AddToClassList("battle-stage");
            stage.style.width = StageWidth;
            stage.style.height = StageHeight;
            overlay.Add(stage);
            var hint = new Label("Click to skip");
            hint.AddToClassList("battle-hint");
            overlay.Add(hint);
            root.Add(overlay);
            overlay.style.display = DisplayStyle.None;
            // The stage is laid out at 960x540 and scaled down to fit smaller windows.
            overlay.RegisterCallback<GeometryChangedEvent>(e =>
            {
                var size = overlay.contentRect.size;
                if (size.x < 1f || size.y < 1f) return;
                float k = Mathf.Min(1f, (size.x - 24f) / StageWidth, (size.y - 60f) / StageHeight);
                stage.style.scale = new Scale(new Vector3(k, k, 1f));
            });
        }

        /// <summary>Takes newly resolved attacks from the combat resolver; plays them unless scenes are off.</summary>
        /// <summary>The battle the player chose to watch (-1: none); its scenes play too.</summary>
        public System.Func<int> WatchedBattle;

        /// <summary>
        /// Whether an attack's scene is for this player: in a battle they fight in, or one they chose to watch (a
        /// spectator sees them all). Other seats' battles elsewhere play on without interrupting.
        /// </summary>
        bool Shown(CombatEvent e)
        {
            var me = Perspective.Viewer;
            return Perspective.Spectating || e.battleSides.Contains(me) || e.attackerOwner == me || e.defenderOwner == me
                || (WatchedBattle != null && e.battleId >= 0 && e.battleId == WatchedBattle());
        }

        public void Collect()
        {
            var events = loop.Services.Combat.Events;
            if (events.Count == 0) return;
            // Instant AI turns skip the AI's own attack scenes and sum them up instead.
            bool skipAi = settings.aiTurns == GameSettings.AiTurnsInstant;
            int aiAttacks = 0, dealt = 0, taken = 0;
            foreach (var e in events)
            {
                if (!Shown(e)) continue;
                // In a battle the player fights, an Instant AI turn is summed up instead (a spectator, or a watched battle, shows every scene).
                if (skipAi && !Perspective.Spectating && e.attackerOwner != Perspective.Viewer && e.battleSides.Contains(Perspective.Viewer))
                {
                    aiAttacks++;
                    if (e.defenderOwner == Perspective.Viewer) dealt += Mathf.Max(0, e.defenderHealthBefore - e.defenderHealthAfter);
                    taken += Mathf.Max(0, e.attackerHealthBefore - e.attackerHealthAfter);
                    continue;
                }
                if (settings.battleScenes != GameSettings.BattleScenesOff) queue.Enqueue(e);
            }
            if (aiAttacks > 0 && Report != null)
                Report("Enemy turns: " + aiAttacks + (aiAttacks == 1 ? " attack" : " attacks") + ", " + dealt + " damage to you"
                    + (taken > 0 ? ", " + taken + " back from your return fire." : "."));
            events.Clear();
            if (!IsPlaying && queue.Count > 0) Begin(queue.Dequeue());
        }

        public void Tick(float dt)
        {
            if (!IsPlaying) return;
            time += dt;
            Advance();
            if (time >= endTime) Finish();
        }

        public void Skip()
        {
            if (!IsPlaying) return;
            time = endTime;
            Advance();
            Finish();
        }

        void Finish()
        {
            if (queue.Count > 0) { Begin(queue.Dequeue()); return; }
            IsPlaying = false;
            overlay.style.display = DisplayStyle.None;
            stage3D.Close();
        }

        void Advance()
        {
            // By index: effects started during playback (ships knocked out) append to the list.
            for (int i = 0; i < tweens.Count; i++)
            {
                var t = tweens[i];
                if (t.Done || time < t.Start) continue;
                t.Started = true;
                float k = t.Duration <= 0f ? 1f : Mathf.Clamp01((time - t.Start) / t.Duration);
                t.Apply(k);
                if (k >= 1f) t.Done = true;
            }
            // Live ships bob gently so the stage never looks frozen.
            foreach (var side in new[] { left, right })
                for (int i = 0; i < side.Ships.Count; i++)
                {
                    float bob = side.Gone[i] ? 0f : Mathf.Sin(time * 3.4f + i * 1.3f) * 3f;
                    if (!side.Gone[i]) side.Ships[i].style.translate = new Translate(0, bob);
                    var model = side.Models[i];
                    if (model == null) continue;
                    // The model stands where its picture would, sliding in with its side, kicked back by hits and
                    // recoil, and falling away once knocked out.
                    var at = ShipPoint(side, i) + new Vector2(0f, bob);
                    float since = time - side.JoltAt[i];
                    if (since < 0.35f) at.x += side.JoltBy[i] * (1f - since / 0.35f) * Mathf.Cos(since * 40f);
                    float fall = side.Tumble[i];
                    at += new Vector2((side.Left ? -40f : 40f) * fall, 90f * fall * fall);
                    // Only ships still in the fight, or a wreck mid-fall; ships lost before the battle never show.
                    bool shown = !side.Gone[i] || (side.Tumble[i] > 0f && side.Tumble[i] < 1f);
                    stage3D.Pose(model, at, side.Left, 1f - 0.35f * fall, shown && side.Opacity[i] > 0.5f, fall);
                    model.Animate(Time.deltaTime);
                }
            // A short, gentle rumble on heavy impacts: eased in and out, slow enough to follow, and only a few pixels.
            float shake = time - shakeAt;
            if (shake < ShakeSeconds)
            {
                float k = shake / ShakeSeconds;
                float amp = shakePower * Mathf.Sin(k * Mathf.PI) * (1f - k);
                stage.style.translate = new Translate(Mathf.Sin(time * 34f) * amp, Mathf.Cos(time * 27f) * amp * 0.5f);
            }
            else stage.style.translate = new Translate(0, 0);
        }

        /// <summary>A ship's centre on the stage, with its side's slide-in.</summary>
        static Vector2 ShipPoint(Side side, int i)
        {
            return ShipCentre(side, i) + new Vector2(side.Slide, 0f);
        }

        const float ShakeSeconds = 0.4f, ShakeMax = 4f;

        void Shake(float power)
        {
            power = Mathf.Min(ShakeMax, power);
            float since = time - shakeAt;
            float remaining = since < ShakeSeconds ? shakePower * (1f - since / ShakeSeconds) : 0f; // a fresh hit never weakens a shake
            shakeAt = time;
            shakePower = Mathf.Max(power, remaining);
        }

        /// <summary>Kicks a ship sideways: away from the enemy when hit, back from its own shot when firing.</summary>
        static void Jolt(Side side, int i, float now, float pixels)
        {
            side.JoltAt[i] = now;
            side.JoltBy[i] = (side.Left ? -1f : 1f) * pixels;
        }

        // ---------------------------------------------------------------- building a scene

        void Begin(CombatEvent e)
        {
            IsPlaying = true;
            overlay.style.display = DisplayStyle.Flex;
            stage3D.Open();
            stage.Clear();
            tweens.Clear();
            time = 0f;

            left = BuildSide(e.attackerType, e.attackerOwner, e.attackerHealthBefore, e.attackerMaxHealth, false, true);
            right = BuildSide(e.defenderType, e.defenderOwner, e.defenderHealthBefore, e.defenderMaxHealth, e.defenderAssigned, false);
            // The 3D stage sits over both halves' backdrops and under the divider, bolts, numbers and callouts.
            if (stage3D.Texture != null)
            {
                if (stageView == null)
                {
                    stageView = new Image { image = stage3D.Texture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                    stageView.style.position = Position.Absolute;
                    stageView.style.left = 0;
                    stageView.style.top = 0;
                    stageView.style.width = StageWidth;
                    stageView.style.height = StageHeight;
                }
                stage.Add(stageView);
            }
            var divider = new VisualElement();
            divider.AddToClassList("battle-divider");
            stage.Add(divider);
            fx = new VisualElement { pickingMode = PickingMode.Ignore };
            fx.AddToClassList("battle-fx");
            stage.Add(fx);
            callout = new Label();
            callout.AddToClassList("battle-callout");
            callout.style.opacity = 0f;
            stage.Add(callout);
            log = new Label(Name(e.attackerOwner, e.attackerType) + " attacks " + Name(e.defenderOwner, e.defenderType).ToLowerInvariant()
                + (e.defenderAssigned ? " (cell defender)." : "."));
            log.AddToClassList("battle-log");
            stage.Add(log);

            bool full = settings.battleScenes == GameSettings.BattleScenesFull;
            float s = full ? 1f : 0.55f;
            float t = 0f;
            Add(t, 0.35f * s, k =>
            {
                float ease = 1f - (1f - k) * (1f - k);
                left.Root.style.translate = new Translate(-HalfWidth * (1f - ease), 0);
                left.Slide = -HalfWidth * (1f - ease);
                right.Slide = HalfWidth * (1f - ease);
                right.Root.style.translate = new Translate(HalfWidth * (1f - ease), 0);
            });
            t += 0.4f * s;
            if (full) { Callout(t, 0.5f, "ATTACK", Gold); t += 0.5f; }
            t = Exchange(t, s, left, right, e.attackerOwner, e.attackerHealthBefore, e.defenderHealthBefore, e.defenderHealthAfter, e.damage);

            string message = Name(e.attackerOwner, e.attackerType) + " hits " + Name(e.defenderOwner, e.defenderType).ToLowerInvariant() + " for " + e.damage + ".";
            if (e.defenderHealthAfter <= 0)
            {
                Callout(t, 0.7f * Mathf.Max(s, 0.8f), "DESTROYED", Danger);
                t += 0.7f * Mathf.Max(s, 0.8f);
                message += " " + CraftName(e.defenderType) + " destroyed. No counter.";
            }
            else if (e.counter > 0)
            {
                t += 0.2f * s;
                if (full) { Callout(t, 0.45f, "COUNTER", Gold); t += 0.45f; }
                t = Exchange(t, s, right, left, e.defenderOwner, e.defenderHealthAfter, e.attackerHealthBefore, e.attackerHealthAfter, e.counter);
                message += " Counter " + e.counter + ".";
                if (e.attackerHealthAfter <= 0)
                {
                    Callout(t, 0.7f * Mathf.Max(s, 0.8f), "DESTROYED", Danger);
                    t += 0.7f * Mathf.Max(s, 0.8f);
                    message += " " + CraftName(e.attackerType) + " destroyed.";
                }
            }
            string final = message;
            Add(t, 0f, k => log.text = final);
            endTime = t + 0.5f * s;
            Advance();
        }

        /// <summary>One side fires: bolts cross the divider, the target flashes, its HP counts down and ships drop out.</summary>
        float Exchange(float t, float s, Side from, Side to, FactionId shooter, int shooterHp, int hpBefore, int hpAfter, int damage)
        {
            int shooters = Mathf.Max(1, Fleet.ShownShips(from.Def, shooterHp));
            var targets = AliveIndices(to);
            float flight = 0.4f * s, stagger = 0.08f * s;
            var colour = shooter == Perspective.Viewer ? Mint : Peach;
            for (int i = 0; i < shooters && i < from.Ships.Count; i++)
            {
                int target = targets.Count > 0 ? targets[i % targets.Count] : 0;
                Bolt(t + i * stagger, flight, ShipCentre(from, i), ShipCentre(to, target), colour);
                int gun = i;
                // Each shot flashes at the muzzle and the ship rocks back.
                Add(t + i * stagger, 0f, k =>
                {
                    stage3D.MuzzleFlash(ShipPoint(from, gun) + new Vector2((from.Left ? 1f : -1f) * from.Size * 0.45f, 0f), from.Size, colour);
                    Jolt(from, gun, time, from.Size * 0.06f);
                });
            }
            t += flight + (shooters - 1) * stagger;
            Add(t, 0f, k => { if (Sound != null) Sound("hit"); });
            // Impact: every ship struck bursts into sparks, fire and smoke and is knocked back; the scene shakes.
            Add(t, 0f, k =>
            {
                foreach (int i in targets)
                {
                    stage3D.Burst(ShipPoint(to, i), to.Size * 0.75f, false);
                    Jolt(to, i, time, to.Size * 0.14f);
                }
                if (damage >= 4) Shake(1.5f + damage * 0.25f); // only heavier hits rumble
            });
            foreach (int i in targets)
            {
                var ship = to.Ships[i];
                int index = i;
                Add(t, 0.24f * s, k =>
                {
                    float o = k >= 1f || (int)(k * 6f) % 2 == 1 ? 1f : 0.25f; // hit flicker
                    ship.style.opacity = o;
                    to.Opacity[index] = o;
                });
            }
            Pop(t, to, "-" + damage);
            float countdown = 0.55f * s;
            Add(t, countdown, k =>
            {
                int hp = Mathf.RoundToInt(Mathf.Lerp(hpBefore, hpAfter, k));
                SetHp(to, hp);
                int keep = Fleet.ShownShips(to.Def, hp);
                for (int i = keep; i < to.Ships.Count; i++) KnockOut(to, i);
                // Below half HP the survivors trail smoke.
                if (k >= 1f && hp > 0 && hp * 2 < to.MaxHealth)
                    for (int i = 0; i < to.Ships.Count; i++)
                        if (!to.Gone[i] && !to.Smoking[i] && to.Models[i] != null) { to.Smoking[i] = true; stage3D.Smoke(to.Models[i]); }
            });
            return t + countdown + 0.15f * s;
        }

        Side BuildSide(string type, FactionId owner, int health, int maxHealth, bool assigned, bool isLeft)
        {
            var def = CatalogRepository.GetCraft(loop.Catalog, type);
            var side = new Side { MaxHealth = Mathf.Max(1, maxHealth), Left = isLeft, Def = def };
            var half = new VisualElement();
            half.AddToClassList("battle-half");
            half.AddToClassList(isLeft ? "battle-left" : "battle-right");
            half.AddToClassList(owner == Perspective.Viewer ? "battle-player" : "battle-enemy");
            side.Root = half;

            var rng = new System.Random(isLeft ? 3 : 7);
            for (int i = 0; i < 24; i++)
            {
                var star = new VisualElement { pickingMode = PickingMode.Ignore };
                star.AddToClassList("battle-star");
                star.style.left = (float)rng.NextDouble() * HalfWidth;
                star.style.top = (float)rng.NextDouble() * StageHeight * 0.6f;
                half.Add(star);
            }
            var deck = new VisualElement();
            deck.AddToClassList("battle-deck");
            half.Add(deck);

            // Detailed comic portraits when the toon shader is available, the pixel-art ship otherwise.
            // A Fort is drawn as its station art; craft as their comic portrait (pixel art if the toon shader is unavailable).
            var sprite = def == null && CatalogRepository.GetBuilding(loop.Catalog, type) != null
                ? sprites.GetStationSprite(type, owner)
                : ComicCraft.Portrait(type, owner) ?? sprites.GetCraftSprite(type, owner);
            int shown = Fleet.ShownShips(def, health);
            LayOut(side, Fleet.ShownFullFleet(def));
            for (int i = 0; i < side.At.Count; i++)
            {
                var img = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                img.AddToClassList("battle-ship");
                img.style.left = isLeft ? side.At[i].x : HalfWidth - side.At[i].x - side.Size;
                img.style.bottom = side.At[i].y;
                img.style.width = side.Size;
                img.style.height = side.Size;
                if (!isLeft) img.style.scale = new Scale(new Vector3(-1f, 1f, 1f)); // face the enemy
                bool alive = i < shown;
                img.style.display = alive ? DisplayStyle.Flex : DisplayStyle.None;
                half.Add(img);
                side.Ships.Add(img);
                side.Gone.Add(!alive);
                // The ship's own model, filmed on the 3D stage; its picture stays only where there is no model.
                var model = stage3D.Add(loop.Catalog, type, owner, side.Size);
                side.Models.Add(model);
                side.Opacity.Add(1f);
                side.Shrink.Add(1f);
                side.JoltAt.Add(-10f);
                side.JoltBy.Add(0f);
                side.Tumble.Add(0f);
                side.Smoking.Add(false);
                if (model != null) img.style.visibility = Visibility.Hidden;
            }

            var plate = new VisualElement();
            plate.AddToClassList("battle-plate");
            var nameRow = new VisualElement();
            nameRow.AddToClassList("battle-name-row");
            var name = new Label(Name(owner, type).ToUpperInvariant());
            name.AddToClassList("battle-name");
            name.style.color = owner == Perspective.Viewer ? Mint : Peach;
            nameRow.Add(name);
            if (assigned)
            {
                var badge = new Label("DEFENDER");
                badge.AddToClassList("battle-badge");
                nameRow.Add(badge);
            }
            plate.Add(nameRow);
            var hpRow = new VisualElement();
            hpRow.AddToClassList("battle-hp-row");
            side.Hp = new Label();
            side.Hp.AddToClassList("battle-hp");
            hpRow.Add(side.Hp);
            var bar = new VisualElement();
            bar.AddToClassList("battle-bar");
            side.BarFill = new VisualElement();
            side.BarFill.AddToClassList("battle-bar-fill");
            side.BarFill.style.backgroundColor = owner == Perspective.Viewer ? Mint : Peach;
            bar.Add(side.BarFill);
            hpRow.Add(bar);
            plate.Add(hpRow);
            half.Add(plate);
            SetHp(side, health);
            stage.Add(half);
            return side;
        }

        // ---------------------------------------------------------------- effects

        void Add(float start, float duration, Action<float> apply)
        {
            tweens.Add(new Tween { Start = start, Duration = duration, Apply = apply });
        }

        void Callout(float start, float duration, string text, Color colour)
        {
            Add(start, duration, k =>
            {
                callout.text = text;
                callout.style.color = colour;
                callout.style.borderTopColor = callout.style.borderBottomColor = callout.style.borderLeftColor = callout.style.borderRightColor = colour;
                callout.style.opacity = k < 1f ? 1f : 0f;
            });
            if (text == "DESTROYED") Add(start, 0f, k => { if (Sound != null) Sound("boom"); });
        }

        void Bolt(float start, float duration, Vector2 from, Vector2 to, Color colour)
        {
            var bolt = new VisualElement { pickingMode = PickingMode.Ignore };
            bolt.AddToClassList("battle-bolt");
            bolt.style.backgroundColor = colour;
            bolt.style.display = DisplayStyle.None;
            float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            bolt.style.rotate = new Rotate(angle);
            fx.Add(bolt);
            Add(start, duration, k =>
            {
                var p = Vector2.Lerp(from, to, k * k);
                bolt.style.left = p.x - 14f;
                bolt.style.top = p.y - 2f;
                bolt.style.display = k < 1f ? DisplayStyle.Flex : DisplayStyle.None;
            });
            Add(start, 0f, k => { if (Sound != null) Sound("zap"); });
        }

        void Pop(float start, Side side, string text)
        {
            var pop = new Label(text) { pickingMode = PickingMode.Ignore };
            pop.AddToClassList("battle-pop");
            pop.style.display = DisplayStyle.None;
            pop.style.left = (side.Left ? 0f : HalfWidth) + HalfWidth * 0.32f;
            fx.Add(pop);
            Add(start, 0.9f, k =>
            {
                pop.style.display = k < 1f ? DisplayStyle.Flex : DisplayStyle.None;
                pop.style.top = StageHeight * 0.32f - 50f * k;
                pop.style.opacity = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            });
        }

        void KnockOut(Side side, int i)
        {
            if (side.Gone[i]) return;
            side.Gone[i] = true;
            var ship = side.Ships[i];
            float start = time;
            // A big blast, then the wreck tumbles away and drops out of the fight.
            stage3D.Burst(ShipPoint(side, i), side.Size * 1.1f, true);
            Shake(ShakeMax);
            Add(start, 0.8f, k =>
            {
                side.Tumble[i] = k;
                side.Opacity[i] = k < 1f ? 1f : 0f;
            });
            Add(start, 0.3f, k =>
            {
                ship.style.opacity = 1f - k;
                side.Shrink[i] = (1f + 0.3f * (1f - k)) * (1f - 0.7f * k);
                float sx = (side.Left ? 1f : -1f) * (1f + 0.3f * (1f - k)) * (1f - 0.7f * k);
                ship.style.scale = new Scale(new Vector3(sx, Mathf.Abs(sx), 1f));
                ship.tintColor = Color.Lerp(Gold, Color.white, k);
            });
        }

        static void SetHp(Side side, int hp)
        {
            hp = Mathf.Max(0, hp);
            side.Hp.text = hp + " / " + side.MaxHealth;
            float frac = (float)hp / side.MaxHealth;
            side.BarFill.style.width = Length.Percent(100f * frac);
            if (frac <= 0.3f) side.BarFill.style.backgroundColor = Danger;
        }

        /// <summary>
        /// Places a fleet's ships in the formation box: a wedge with the leader nearest the enemy, rows stepping back
        /// and spreading up the stage, the whole shrunk if it would not fit and centred in the box.
        /// </summary>
        static void LayOut(Side side, int count)
        {
            side.Size = side.Def == null ? GuardShipSize * 3f : GuardShipSize * Fleet.Scale(side.Def); // a Fort stands large
            var wedge = Fleet.Wedge(count);
            var raw = new Vector2[wedge.Length];
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < wedge.Length; i++)
            {
                // Back rows sit further from the divider and a touch higher, so the squad reads in depth.
                raw[i] = new Vector2(-wedge[i].y * side.Size * 1.05f, wedge[i].x * side.Size * 0.72f + wedge[i].y * side.Size * 0.1f);
                min = Vector2.Min(min, raw[i]);
                max = Vector2.Max(max, raw[i]);
            }
            var span = max - min;
            // Ships and spacing shrink together until the fleet (its spread plus one ship) fits the box.
            float k = Mathf.Min(1f, FormationWidth / (span.x + side.Size), FormationHeight / (span.y + side.Size));
            side.Size *= k;
            var origin = new Vector2(FormationLeft + (FormationWidth - (span.x + side.Size / k) * k) * 0.5f,
                FormationBottom + (FormationHeight - (span.y + side.Size / k) * k) * 0.5f);
            for (int i = 0; i < raw.Length; i++) side.At.Add(origin + (raw[i] - min) * k);
        }

        static List<int> AliveIndices(Side side)
        {
            var list = new List<int>();
            for (int i = 0; i < side.Ships.Count; i++) if (!side.Gone[i]) list.Add(i);
            return list;
        }

        /// <summary>A ship's centre in stage coordinates.</summary>
        static Vector2 ShipCentre(Side side, int i)
        {
            float x = side.At[i].x + side.Size / 2f;
            if (!side.Left) x = StageWidth - x;
            float y = StageHeight - (side.At[i].y + side.Size / 2f);
            return new Vector2(x, y);
        }

        string CraftName(string type)
        {
            var def = CatalogRepository.GetCraft(loop.Catalog, type);
            if (def != null) return def.displayName;
            var building = CatalogRepository.GetBuilding(loop.Catalog, type);
            return building != null ? building.displayName : type;
        }

        string Name(FactionId owner, string type)
        {
            return (owner == Perspective.Viewer ? "Your " : "Enemy ") + CraftName(type);
        }
    }
}
