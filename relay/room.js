// One Orbital Foundry multiplayer room: the lobby, then the relay of a lockstep match.
//
// The relay never runs the game. In the lobby it holds the seat setup (which seats are people, AIs or off, their
// teams and AI profiles, and who has taken each person's seat). In play it passes each machine's lockstep messages to
// the others in one order, keeps the log a returning player replays, and when a player stays gone hands their seat
// to an AI from the tick after the last inputs it passed on from them, which every machine is sure to have.
//
// Plain JavaScript with no Cloudflare APIs, so it can be tested with Node (relay/room.test.js). worker.js wraps it in
// a Durable Object.

export const Kind = { Inputs: 0, Hash: 1, Handover: 2 };
export const SEATS = 4;
/// How long a player may be gone in play before an AI takes their seat.
export const GRACE_MS = 20000;
/// The AI profile a dropped player's seat is handed to.
export const HANDOVER_PROFILE = "medium";

export class Room {
  constructor(code, now = Date.now()) {
    this.code = code;
    this.created = now;
    this.phase = "lobby"; // lobby | playing
    this.clients = new Map(); // clientId -> { id, name, send, connected, seat, droppedAt }
    this.hostId = null;
    // Seat i+1: mode "person" (taken by a client), "ai" or "off".
    this.seats = [
      { mode: "person", profile: "medium", team: 0, clientId: null },
      { mode: "ai", profile: "medium", team: 0, clientId: null },
      { mode: "off", profile: "medium", team: 0, clientId: null },
      { mode: "off", profile: "medium", team: 0, clientId: null },
    ];
    this.difficulty = 1;
    this.seed = 0;
    // Play: the log a returning player replays (inputs that carry commands, and handovers), and the last tick each
    // person's inputs have been passed on for (empty inputs are not logged: a returning player fills them in).
    this.log = [];
    this.inputsThrough = {};
    this.handedOver = {};
    this.speed = 1;
  }

  // ------------------------------------------------------------------ connections

  /// A client connects (or reconnects with the same id). `send` takes a message object.
  join(clientId, name, send, now = Date.now()) {
    let c = this.clients.get(clientId);
    if (!c) {
      if (this.phase !== "lobby") {
        // A newcomer to a match under way watches it.
        c = { id: clientId, name: clean(name), send, connected: true, seat: 0, droppedAt: 0 };
        this.clients.set(clientId, c);
        this.sendStart(c);
        return;
      }
      c = { id: clientId, name: clean(name), send, connected: true, seat: 0, droppedAt: 0 };
      this.clients.set(clientId, c);
      if (!this.hostId) this.hostId = clientId;
      // The first open person's seat is theirs.
      const open = this.seats.findIndex((s) => s.mode === "person" && !s.clientId);
      if (open >= 0) { this.seats[open].clientId = clientId; c.seat = open + 1; }
      this.broadcastLobby();
      return;
    }
    c.send = send;
    c.connected = true;
    c.droppedAt = 0;
    if (name) c.name = clean(name);
    if (this.phase === "lobby") this.broadcastLobby();
    else this.sendStart(c);
  }

  /// A client's connection closed.
  leave(clientId, now = Date.now()) {
    const c = this.clients.get(clientId);
    if (!c) return;
    c.connected = false;
    c.droppedAt = now;
    if (this.phase === "lobby") {
      // In the lobby a seat is freed at once, and the host passes on.
      if (c.seat > 0) this.seats[c.seat - 1].clientId = null;
      c.seat = 0;
      this.clients.delete(clientId);
      if (this.hostId === clientId) {
        const next = [...this.clients.values()].find((x) => x.connected);
        this.hostId = next ? next.id : null;
      }
      this.broadcastLobby();
    } else {
      this.broadcast({ t: "dropped", seat: c.seat, name: c.name });
    }
  }

  /// When the next check for dropped players is due (ms), or 0 for none.
  nextCheck() {
    if (this.phase !== "playing") return 0;
    let due = 0;
    for (const c of this.clients.values())
      if (!c.connected && c.seat > 0 && !this.handedOver[c.seat]) due = due ? Math.min(due, c.droppedAt + GRACE_MS) : c.droppedAt + GRACE_MS;
    return due;
  }

  /// Hands the seats of players gone past the grace period to an AI.
  check(now = Date.now()) {
    if (this.phase !== "playing") return;
    for (const c of this.clients.values()) {
      if (c.connected || c.seat <= 0 || this.handedOver[c.seat] || now - c.droppedAt < GRACE_MS) continue;
      const tick = (this.inputsThrough[c.seat] || 0) + 1;
      const m = { kind: Kind.Handover, seat: c.seat, tick, commands: [], hash: "", profileId: HANDOVER_PROFILE };
      this.handedOver[c.seat] = tick;
      this.log.push(m);
      this.broadcast({ t: "ls", m });
      c.seat = 0; // if they come back, they watch
    }
  }

  // ------------------------------------------------------------------ messages

  message(clientId, msg, now = Date.now()) {
    const c = this.clients.get(clientId);
    if (!c || !msg || typeof msg.t !== "string") return;
    switch (msg.t) {
      case "take": return this.take(c, msg.seat | 0);
      case "config": return this.configure(c, msg);
      case "start": return this.start(c, msg);
      case "ls": return this.relay(c, msg.m);
      case "speed": return this.setSpeed(c, msg.speed);
      case "ping": return c.send({ t: "pong" });
    }
  }

  isHost(c) { return c.id === this.hostId; }

  /// A client takes an open person's seat (or 0 to stand up).
  take(c, seat) {
    if (this.phase !== "lobby") return;
    if (seat < 0 || seat > SEATS) return;
    if (seat > 0) {
      const s = this.seats[seat - 1];
      if (s.mode !== "person" || (s.clientId && s.clientId !== c.id)) return this.error(c, "That seat is not free.");
    }
    if (c.seat > 0) this.seats[c.seat - 1].clientId = null;
    c.seat = seat;
    if (seat > 0) this.seats[seat - 1].clientId = c.id;
    this.broadcastLobby();
  }

  /// The host sets each seat's mode, AI profile and team, and the difficulty.
  configure(c, msg) {
    if (this.phase !== "lobby" || !this.isHost(c)) return;
    if (Array.isArray(msg.seats))
      msg.seats.slice(0, SEATS).forEach((s, i) => {
        if (!s) return;
        const seat = this.seats[i];
        if (s.mode === "person" || s.mode === "ai" || s.mode === "off") {
          if (s.mode !== "person" && seat.clientId) {
            // A seat taken by a player stays theirs until they stand up.
            if (seat.clientId !== c.id) return;
            this.clients.get(seat.clientId).seat = 0;
            seat.clientId = null;
          }
          seat.mode = s.mode;
        }
        if (typeof s.profile === "string" && /^[a-z_]{1,32}$/.test(s.profile)) seat.profile = s.profile;
        if (Number.isInteger(s.team) && s.team >= 0 && s.team <= SEATS) seat.team = s.team;
      });
    if (Number.isInteger(msg.difficulty) && msg.difficulty >= 0 && msg.difficulty <= 2) this.difficulty = msg.difficulty;
    this.broadcastLobby();
  }

  /// Why the match cannot start yet, or "".
  notReady() {
    const inPlay = this.seats.filter((s) => s.mode !== "off");
    if (inPlay.length < 2) return "At least two seats must play.";
    if (!this.seats.some((s) => s.mode === "person")) return "At least one seat must be a person.";
    if (this.seats.some((s) => s.mode === "person" && !s.clientId)) return "Every person's seat needs a player.";
    return "";
  }

  start(c, msg) {
    if (this.phase !== "lobby" || !this.isHost(c)) return;
    const why = this.notReady();
    if (why) return this.error(c, why);
    this.phase = "playing";
    this.seed = Number.isInteger(msg && msg.seed) && msg.seed > 0 ? msg.seed : 1 + Math.floor(Math.random() * 2147483646);
    for (const x of this.clients.values()) this.sendStart(x);
  }

  /// The match setup, and for a returning player the log so far to replay.
  sendStart(c) {
    const seats = this.seats.map((s, i) => ({
      seat: i + 1, mode: s.mode, profile: s.profile, team: s.team,
      name: s.clientId && this.clients.get(s.clientId) ? this.clients.get(s.clientId).name : "",
    }));
    c.send({
      t: "start", code: this.code, seed: this.seed, difficulty: this.difficulty, seats,
      you: c.seat, speed: this.speed,
      log: this.log,
      // As a list: the game's JSON reader takes no maps.
      inputsThrough: Object.entries(this.inputsThrough).map(([seat, tick]) => ({ seat: Number(seat), tick })),
    });
  }

  /// A lockstep message from a player: checked, logged and passed to every other machine in arrival order.
  relay(c, m) {
    if (this.phase !== "playing" || !m || typeof m !== "object") return;
    if (m.kind === Kind.Inputs) {
      // A machine only speaks for its own seat, and only for ticks it has not covered yet.
      if (c.seat <= 0 || m.seat !== c.seat || this.handedOver[c.seat]) return;
      if (!Number.isInteger(m.tick) || m.tick <= (this.inputsThrough[c.seat] || 0)) return;
      if (!Array.isArray(m.commands)) m.commands = [];
      for (const cmd of m.commands) if (cmd && typeof cmd === "object") cmd.actor = c.seat;
      this.inputsThrough[c.seat] = m.tick;
      if (m.commands.length > 0) this.log.push(m);
      this.broadcast({ t: "ls", m }, c.id);
    } else if (m.kind === Kind.Hash) {
      if (m.seat !== c.seat && c.seat > 0) return;
      this.broadcast({ t: "ls", m }, c.id);
    }
    // Handovers come from the relay alone.
  }

  /// Shared game speed (0 pauses): every machine must run the same ticks, so it is for everyone.
  setSpeed(c, speed) {
    if (this.phase !== "playing" || c.seat <= 0) return;
    if (![0, 1, 2, 4].includes(speed)) return;
    this.speed = speed;
    this.broadcast({ t: "speed", speed, by: c.seat });
  }

  // ------------------------------------------------------------------ out

  lobbyState(c) {
    return {
      t: "lobby", code: this.code, you: c.seat, host: this.isHost(c), difficulty: this.difficulty,
      seats: this.seats.map((s, i) => ({
        seat: i + 1, mode: s.mode, profile: s.profile, team: s.team,
        name: s.clientId && this.clients.get(s.clientId) ? this.clients.get(s.clientId).name : "",
      })),
      watching: [...this.clients.values()].filter((x) => x.seat === 0).map((x) => x.name),
      notReady: this.notReady(),
    };
  }

  broadcastLobby() {
    for (const c of this.clients.values()) if (c.connected) c.send(this.lobbyState(c));
  }

  broadcast(msg, exceptId) {
    for (const c of this.clients.values()) if (c.connected && c.id !== exceptId) c.send(msg);
  }

  error(c, message) { c.send({ t: "error", message }); }

  /// True when nobody is connected (the room may be forgotten).
  empty() { return ![...this.clients.values()].some((c) => c.connected); }
}

function clean(name) {
  const s = String(name || "").replace(/[^\p{L}\p{N} _.-]/gu, "").trim().slice(0, 20);
  return s || "Player";
}

/// A room code: five letters and digits that cannot be mistaken for each other.
export function newCode(random = Math.random) {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  let code = "";
  for (let i = 0; i < 5; i++) code += alphabet[Math.floor(random() * alphabet.length)];
  return code;
}

export function validCode(code) { return /^[A-HJ-NP-Z2-9]{5}$/.test(code); }
