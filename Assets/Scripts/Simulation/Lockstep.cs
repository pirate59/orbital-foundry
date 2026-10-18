using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>What a lockstep message carries.</summary>
    public enum LockstepKind
    {
        /// <summary>A person's commands for one tick (often none). Every tick needs one from every person.</summary>
        Inputs,
        /// <summary>A machine's world fingerprint after a tick, to catch drift.</summary>
        Hash,
        /// <summary>A person's seat is played by an AI from <see cref="LockstepMessage.tick"/> on.</summary>
        Handover,
    }

    /// <summary>One message between machines. Plain data, so any transport can carry it as JSON.</summary>
    [Serializable]
    public sealed class LockstepMessage
    {
        public LockstepKind kind;
        public FactionId seat;
        public int tick;
        public List<GameCommand> commands = new List<GameCommand>();
        public string hash = "";
        public string profileId = "";

        public LockstepMessage Copy() { return JsonUtility.FromJson<LockstepMessage>(JsonUtility.ToJson(this)); }
    }

    /// <summary>
    /// Carries messages between the machines in a match. Every message sent reaches every other machine, in the order
    /// it was sent (phase 7's relay guarantees this; the in-memory hub does it for tests).
    /// </summary>
    public interface ILockstepTransport
    {
        void Send(LockstepMessage message);
        /// <summary>Messages that have arrived since the last call, in arrival order.</summary>
        void Receive(List<LockstepMessage> into);
    }

    /// <summary>
    /// Machines in one process joined in memory, for tests and local trials: what one sends, the others receive after
    /// <see cref="DelayPolls"/> calls to Receive (a stand-in for network latency), in order.
    /// </summary>
    public sealed class LoopbackHub
    {
        readonly List<Endpoint> endpoints = new List<Endpoint>();
        public int DelayPolls;

        public ILockstepTransport Join()
        {
            var e = new Endpoint(this);
            endpoints.Add(e);
            return e;
        }

        sealed class Endpoint : ILockstepTransport
        {
            readonly LoopbackHub hub;
            readonly List<(int due, LockstepMessage message)> inbox = new List<(int, LockstepMessage)>();
            int polls;
            /// <summary>A machine cut off (a dropped player): it neither sends nor receives.</summary>
            public bool Cut;

            public Endpoint(LoopbackHub hub) { this.hub = hub; }

            public void Send(LockstepMessage message)
            {
                if (Cut) return;
                foreach (var other in hub.endpoints)
                    if (other != this && !other.Cut) other.inbox.Add((other.polls + hub.DelayPolls, message.Copy()));
            }

            public void Receive(List<LockstepMessage> into)
            {
                polls++;
                int n = 0;
                while (n < inbox.Count && inbox[n].due <= polls) into.Add(inbox[n++].message);
                inbox.RemoveRange(0, n);
            }
        }

        /// <summary>Cuts a machine off, as a player whose connection dropped.</summary>
        public void Cut(ILockstepTransport transport) { ((Endpoint)transport).Cut = true; }
    }

    /// <summary>
    /// Deterministic lockstep for one machine. A person's command is not carried out when given: it is stamped for a
    /// tick <see cref="InputDelay"/> ahead and sent to every machine, and each carries it out in that seat's turn of
    /// that tick. A tick runs only when every person's inputs for it have arrived, so every machine plays the same
    /// commands at the same moment of the same tick and stays identical. Every <see cref="HashEvery"/> ticks the
    /// machines compare world fingerprints. AIs run on every machine and send nothing.
    /// </summary>
    public sealed class LockstepSession
    {
        public readonly FactionId LocalSeat;
        public readonly int InputDelay;
        public readonly int HashEvery;
        readonly GameLoop loop;
        readonly ILockstepTransport transport;
        /// <summary>Seats played by people (here or elsewhere): every tick needs their inputs.</summary>
        readonly List<FactionId> people = new List<FactionId>();
        /// <summary>Inputs by tick and seat.</summary>
        readonly Dictionary<int, Dictionary<FactionId, List<GameCommand>>> inputs = new Dictionary<int, Dictionary<FactionId, List<GameCommand>>>();
        /// <summary>Seats handed to an AI, and the tick their AI starts.</summary>
        readonly Dictionary<FactionId, (int tick, string profile)> handovers = new Dictionary<FactionId, (int, string)>();
        readonly Dictionary<int, string> ownHashes = new Dictionary<int, string>();
        readonly List<(FactionId seat, int tick, string hash)> theirHashes = new List<(FactionId, int, string)>();
        readonly List<GameCommand> pending = new List<GameCommand>();
        readonly List<LockstepMessage> received = new List<LockstepMessage>();
        /// <summary>The last tick this machine has sent its inputs for.</summary>
        int sentThrough;

        /// <summary>The first tick a machine's world differed from another's (-1: none), and with which seat's machine.</summary>
        public int DesyncTick = -1;
        public FactionId DesyncWith = FactionId.Neutral;
        public event Action<int, FactionId> Desynced;

        /// <summary>Raised when a local command is carried out, with its real result (Submit only says it was sent).</summary>
        public event Action<GameCommand, CommandResult> LocalCommandRan;

        public LockstepSession(GameLoop loop, FactionId localSeat, ILockstepTransport transport, int inputDelay = 3, int hashEvery = 50)
        {
            this.loop = loop;
            this.transport = transport;
            LocalSeat = localSeat;
            InputDelay = Math.Max(1, inputDelay);
            HashEvery = Math.Max(1, hashEvery);
            var w = loop.World;
            foreach (var f in w.factions)
                if (!f.aiControlled)
                {
                    people.Add(f.id);
                    loop.SetController(f.id, new LockstepSide(this, f.id));
                }
            // The first ticks have no inputs from anyone: nobody could have sent them in time.
            sentThrough = w.tick + InputDelay;
        }

        public IList<FactionId> People { get { return people; } }

        /// <summary>
        /// A command from the person here, carried out on every machine <see cref="InputDelay"/> ticks from now. The
        /// result only says it was sent; <see cref="LocalCommandRan"/> brings the real one.
        /// </summary>
        public CommandResult Submit(GameCommand cmd)
        {
            var copy = RecordingSide.Copy(cmd);
            copy.actor = LocalSeat; // a machine only ever gives orders for its own seat
            pending.Add(copy);
            return new CommandResult { success = true, stateChanged = false, code = "SENT", message = "" };
        }

        /// <summary>
        /// Hands a person's seat to an AI from <paramref name="fromTick"/> on, on every machine. Its inputs are no longer
        /// waited for from that tick. The tick must still be ahead of every machine (phase 7's relay picks it: the tick
        /// after the last inputs it passed on from that seat).
        /// </summary>
        public void HandOver(FactionId seat, int fromTick, string profileId = "")
        {
            var m = new LockstepMessage { kind = LockstepKind.Handover, seat = seat, tick = fromTick, profileId = profileId ?? "" };
            transport.Send(m);
            Note(m);
        }

        /// <summary>Takes in what has arrived. Called before each tick is attempted.</summary>
        public void Pump()
        {
            received.Clear();
            transport.Receive(received);
            foreach (var m in received) Note(m);
        }

        void Note(LockstepMessage m)
        {
            switch (m.kind)
            {
                case LockstepKind.Inputs:
                    Inputs(m.tick)[m.seat] = m.commands;
                    break;
                case LockstepKind.Hash:
                    theirHashes.Add((m.seat, m.tick, m.hash));
                    CompareHashes();
                    break;
                case LockstepKind.Handover:
                    if (m.tick <= loop.World.tick && DesyncTick < 0)
                    {
                        // Too late to apply here on the same tick as elsewhere: this machine has parted ways.
                        Desync(loop.World.tick, m.seat);
                        break;
                    }
                    if (!handovers.ContainsKey(m.seat)) handovers[m.seat] = (m.tick, m.profileId);
                    break;
            }
        }

        Dictionary<FactionId, List<GameCommand>> Inputs(int tick)
        {
            Dictionary<FactionId, List<GameCommand>> bySeat;
            if (!inputs.TryGetValue(tick, out bySeat)) inputs[tick] = bySeat = new Dictionary<FactionId, List<GameCommand>>();
            return bySeat;
        }

        /// <summary>
        /// Sends this machine's inputs up to <paramref name="tick"/> + the delay, then says whether every person's inputs
        /// for <paramref name="tick"/> are in, so it may run.
        /// </summary>
        public bool ReadyFor(int tick)
        {
            while (sentThrough < tick + InputDelay)
            {
                sentThrough++;
                var m = new LockstepMessage { kind = LockstepKind.Inputs, seat = LocalSeat, tick = sentThrough };
                m.commands.AddRange(pending);
                pending.Clear();
                transport.Send(m);
                Note(m);
            }
            return Missing(tick) == FactionId.Neutral;
        }

        /// <summary>The first person whose inputs for <paramref name="tick"/> have not arrived, or Neutral.</summary>
        public FactionId Missing(int tick)
        {
            if (tick <= InputDelay) return FactionId.Neutral; // before anyone could send
            Dictionary<FactionId, List<GameCommand>> bySeat;
            inputs.TryGetValue(tick, out bySeat);
            foreach (var seat in people)
            {
                (int tick, string profile) h;
                if (handovers.TryGetValue(seat, out h) && tick >= h.tick) continue; // an AI plays it now
                if (bySeat == null || !bySeat.ContainsKey(seat)) return seat;
            }
            return FactionId.Neutral;
        }

        /// <summary>After a whole tick: fingerprint the world on the beat, and forget inputs already played.</summary>
        public void AfterTick(int tick)
        {
            inputs.Remove(tick);
            if (tick % HashEvery != 0) return;
            string hash = WorldHash.Of(loop.World);
            ownHashes[tick] = hash;
            transport.Send(new LockstepMessage { kind = LockstepKind.Hash, seat = LocalSeat, tick = tick, hash = hash });
            CompareHashes();
        }

        void CompareHashes()
        {
            for (int i = theirHashes.Count - 1; i >= 0; i--)
            {
                var (seat, tick, hash) = theirHashes[i];
                string mine;
                if (!ownHashes.TryGetValue(tick, out mine)) continue;
                theirHashes.RemoveAt(i);
                if (mine != hash && (DesyncTick < 0 || tick < DesyncTick)) Desync(tick, seat);
            }
            // Fingerprints every machine has answered need not be kept.
            if (ownHashes.Count > 8)
            {
                int oldest = int.MaxValue;
                foreach (var k in ownHashes.Keys) oldest = Math.Min(oldest, k);
                ownHashes.Remove(oldest);
            }
        }

        void Desync(int tick, FactionId with)
        {
            DesyncTick = tick;
            DesyncWith = with;
            if (Desynced != null) Desynced(tick, with);
        }

        /// <summary>A person's seat's turn in a tick: its commands for the tick, or its AI taking over.</summary>
        internal void Play(FactionId seat, WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router)
        {
            (int tick, string profile) h;
            if (handovers.TryGetValue(seat, out h) && w.tick >= h.tick)
            {
                // From this tick an AI plays the seat, on every machine alike.
                var f = WorldLookup.GetFaction(w, seat);
                f.aiControlled = true;
                f.aiProfileId = h.profile;
                people.Remove(seat);
                handovers.Remove(seat);
                var ai = new AIController(loop.Services, seat, new AIFactoryPlanner(), new AITactics(loop.Services));
                loop.SetController(seat, ai);
                ai.Tick(w, c, map, router);
                return;
            }
            Dictionary<FactionId, List<GameCommand>> bySeat;
            List<GameCommand> list;
            if (!inputs.TryGetValue(w.tick, out bySeat) || !bySeat.TryGetValue(seat, out list)) return;
            foreach (var cmd in list)
            {
                cmd.actor = seat;
                var r = router.Execute(w, c, map, cmd);
                if (seat == LocalSeat && LocalCommandRan != null) LocalCommandRan(cmd, r);
            }
        }
    }

    /// <summary>A person's seat in lockstep: it plays the commands every machine received for it, tick by tick.</summary>
    public sealed class LockstepSide : ISideController
    {
        readonly LockstepSession session;
        public readonly FactionId Seat;

        public LockstepSide(LockstepSession session, FactionId seat) { this.session = session; Seat = seat; }

        public void Tick(WorldState w, ContentCatalog c, MapDefinition map, CommandRouter router) { session.Play(Seat, w, c, map, router); }
    }
}
