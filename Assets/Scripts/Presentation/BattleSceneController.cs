using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// Advance Wars-style battle scenes. Each resolved attack cuts to a split stage: the attacker's formation on the
    /// left, the target's on the right, one ship per fifth of a craft's HP. The attacker fires across the divider, the
    /// target's HP counts down and ships drop out, then the survivor counters. Scenes replay
    /// <see cref="CombatResolver.Events"/> in order; play is paused while one runs, and a click skips it.
    /// </summary>
    public sealed class BattleSceneController
    {
        const float StageWidth = 960f, StageHeight = 540f, HalfWidth = StageWidth / 2f;
        const int MaxShips = 5;
        // Formation slots as fractions of the formation box (left, bottom), a loose V that reads as a squad.
        static readonly Vector2[] Slots = { new Vector2(0.38f, 0.34f), new Vector2(0.12f, 0.52f), new Vector2(0.62f, 0.54f), new Vector2(0.04f, 0.06f), new Vector2(0.70f, 0.08f) };
        const float FormationLeft = 29f, FormationWidth = 422f, FormationBottom = 92f, FormationHeight = 281f, ShipSize = 93f;

        static readonly Color Mint = new Color32(151, 247, 200, 255);
        static readonly Color Peach = new Color32(255, 177, 153, 255);
        static readonly Color Gold = new Color32(255, 222, 126, 255);
        static readonly Color Danger = new Color32(255, 125, 110, 255);

        sealed class Side
        {
            public VisualElement Root;
            public readonly List<Image> Ships = new List<Image>();
            public readonly List<bool> Gone = new List<bool>();
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
        public void Collect()
        {
            var events = loop.Services.Combat.Events;
            if (events.Count == 0) return;
            if (settings.battleScenes != GameSettings.BattleScenesOff)
                foreach (var e in events) queue.Enqueue(e);
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
                    if (!side.Gone[i]) side.Ships[i].style.translate = new Translate(0, Mathf.Sin(time * 3.4f + i * 1.3f) * 3f);
        }

        // ---------------------------------------------------------------- building a scene

        void Begin(CombatEvent e)
        {
            IsPlaying = true;
            overlay.style.display = DisplayStyle.Flex;
            stage.Clear();
            tweens.Clear();
            time = 0f;

            left = BuildSide(e.attackerType, e.attackerOwner, e.attackerHealthBefore, e.attackerMaxHealth, false, true);
            right = BuildSide(e.defenderType, e.defenderOwner, e.defenderHealthBefore, e.defenderMaxHealth, e.defenderAssigned, false);
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
            int shooters = Mathf.Max(1, ShipsFor(from.MaxHealth, shooterHp));
            var targets = AliveIndices(to);
            float flight = 0.4f * s, stagger = 0.08f * s;
            for (int i = 0; i < shooters && i < from.Ships.Count; i++)
            {
                int target = targets.Count > 0 ? targets[i % targets.Count] : 0;
                Bolt(t + i * stagger, flight, ShipCentre(from, i), ShipCentre(to, target), shooter == FactionId.Player ? Mint : Peach);
            }
            t += flight + (shooters - 1) * stagger;
            Add(t, 0f, k => { if (Sound != null) Sound("hit"); });
            foreach (int i in targets)
            {
                var ship = to.Ships[i];
                Add(t, 0.24f * s, k => ship.style.opacity = k >= 1f || (int)(k * 6f) % 2 == 1 ? 1f : 0.25f); // hit flicker
            }
            Pop(t, to, "-" + damage);
            float countdown = 0.55f * s;
            Add(t, countdown, k =>
            {
                int hp = Mathf.RoundToInt(Mathf.Lerp(hpBefore, hpAfter, k));
                SetHp(to, hp);
                int keep = ShipsFor(to.MaxHealth, hp);
                for (int i = keep; i < to.Ships.Count; i++) KnockOut(to, i);
            });
            return t + countdown + 0.15f * s;
        }

        Side BuildSide(string type, FactionId owner, int health, int maxHealth, bool assigned, bool isLeft)
        {
            var side = new Side { MaxHealth = Mathf.Max(1, maxHealth), Left = isLeft };
            var half = new VisualElement();
            half.AddToClassList("battle-half");
            half.AddToClassList(isLeft ? "battle-left" : "battle-right");
            half.AddToClassList(owner == FactionId.Player ? "battle-player" : "battle-enemy");
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

            var sprite = sprites.GetCraftSprite(type, owner);
            int shown = ShipsFor(side.MaxHealth, health);
            for (int i = 0; i < MaxShips; i++)
            {
                var img = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                img.AddToClassList("battle-ship");
                float x = FormationLeft + Slots[i].x * FormationWidth;
                img.style.left = isLeft ? x : HalfWidth - x - ShipSize;
                img.style.bottom = FormationBottom + Slots[i].y * FormationHeight;
                img.style.width = ShipSize;
                img.style.height = ShipSize;
                if (!isLeft) img.style.scale = new Scale(new Vector3(-1f, 1f, 1f)); // face the enemy
                bool alive = i < shown;
                img.style.display = alive ? DisplayStyle.Flex : DisplayStyle.None;
                half.Add(img);
                side.Ships.Add(img);
                side.Gone.Add(!alive);
            }

            var plate = new VisualElement();
            plate.AddToClassList("battle-plate");
            var nameRow = new VisualElement();
            nameRow.AddToClassList("battle-name-row");
            var name = new Label(Name(owner, type).ToUpperInvariant());
            name.AddToClassList("battle-name");
            name.style.color = owner == FactionId.Player ? Mint : Peach;
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
            side.BarFill.style.backgroundColor = owner == FactionId.Player ? Mint : Peach;
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
            Add(start, 0.3f, k =>
            {
                ship.style.opacity = 1f - k;
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

        static int ShipsFor(int maxHealth, int hp)
        {
            return hp <= 0 ? 0 : Mathf.Max(1, Mathf.CeilToInt(MaxShips * (float)hp / maxHealth));
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
            float x = FormationLeft + Slots[i].x * FormationWidth + ShipSize / 2f;
            if (!side.Left) x = StageWidth - x;
            float y = StageHeight - (FormationBottom + Slots[i].y * FormationHeight + ShipSize / 2f);
            return new Vector2(x, y);
        }

        string CraftName(string type)
        {
            var def = CatalogRepository.GetCraft(loop.Catalog, type);
            return def != null ? def.displayName : type;
        }

        string Name(FactionId owner, string type)
        {
            return (owner == FactionId.Player ? "Your " : "Enemy ") + CraftName(type);
        }
    }
}
