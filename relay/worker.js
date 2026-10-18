// The Orbital Foundry Worker: the static game (deploy/site) plus the multiplayer relay.
//   GET /relay/new          -> { code }: a fresh room code
//   GET /relay/<CODE>       -> WebSocket into that room (?client=<id>&name=<name>)
// Everything else is served from the static assets. Each room is a Durable Object holding a Room (room.js).

import { Room, newCode, validCode } from "./room.js";

/// How often a room's progress is written while a match runs (orders themselves are written as they come).
const SAVE_EVERY_MS = 5000;
/// How long a room nobody is in is kept.
const EMPTY_ROOM_MS = 60 * 60 * 1000;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith("/relay/")) return env.ASSETS.fetch(request);
    const part = url.pathname.slice("/relay/".length);
    if (part === "new") return Response.json({ code: newCode() }, { headers: { "Cache-Control": "no-store" } });
    const code = part.toUpperCase();
    if (!validCode(code)) return new Response("Unknown room code.", { status: 404 });
    if (request.headers.get("Upgrade") !== "websocket") return new Response("Expected a WebSocket.", { status: 426 });
    const stub = env.ROOMS.get(env.ROOMS.idFromName(code));
    return stub.fetch(request);
  },
};

/// One room. It lives in memory while anyone is connected, and is kept in the object's storage so that a restart of
/// the relay loses nothing: the room comes back with every seat away, and the players reconnect and rebuild.
export class RoomObject {
  constructor(state) {
    this.state = state;
    this.storage = state.storage;
    this.room = null;
    this.saveTimer = null;
    state.blockConcurrencyWhile(async () => {
      const snap = await this.storage.get("room");
      if (!snap) return;
      const entries = await this.storage.list({ prefix: "log:" });
      this.room = this.attach(Room.restore(snap, [...entries.values()]));
    });
  }

  /// Wires a room to this object's storage.
  attach(room) {
    room.persist = {
      log: (entry, index) => { this.storage.put("log:" + String(index).padStart(8, "0"), entry); },
      save: (urgent) => {
        if (urgent) return this.saveNow();
        if (!this.saveTimer) this.saveTimer = setTimeout(() => this.saveNow(), SAVE_EVERY_MS);
      },
      clear: () => {
        // A rematch: the old match's orders go.
        this.storage.list({ prefix: "log:" }).then((entries) => { if (entries.size) this.storage.delete([...entries.keys()]); });
      },
    };
    return room;
  }

  saveNow() {
    if (this.saveTimer) { clearTimeout(this.saveTimer); this.saveTimer = null; }
    if (this.room) this.storage.put("room", this.room.snapshot());
  }

  async fetch(request) {
    const url = new URL(request.url);
    const code = url.pathname.slice("/relay/".length).toUpperCase();
    if (!this.room) this.room = this.attach(new Room(code));
    const clientId = (url.searchParams.get("client") || "").slice(0, 64);
    if (!/^[A-Za-z0-9-]{8,64}$/.test(clientId)) return new Response("A client id is needed.", { status: 400 });
    const name = url.searchParams.get("name") || "";

    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);
    server.accept();
    let open = true;
    const send = (msg) => {
      if (!open) return;
      try { server.send(JSON.stringify(msg)); } catch { open = false; }
    };
    this.room.join(clientId, name, send);
    this.storage.deleteAlarm();
    server.addEventListener("message", (event) => {
      let msg;
      try { msg = JSON.parse(typeof event.data === "string" ? event.data : new TextDecoder().decode(event.data)); } catch { return; }
      this.room.message(clientId, msg);
    });
    const closed = () => {
      if (!open) return;
      open = false;
      // A newer connection from the same player may already have replaced this one.
      const c = this.room.clients.get(clientId);
      if (c && c.send === send) this.room.leave(clientId);
      if (this.room.empty()) this.storage.setAlarm(Date.now() + EMPTY_ROOM_MS);
    };
    server.addEventListener("close", closed);
    server.addEventListener("error", closed);
    return new Response(null, { status: 101, webSocket: client });
  }

  /// An hour after the last player left: forget the room.
  async alarm() {
    if (this.room && !this.room.empty()) return;
    this.room = null;
    await this.storage.deleteAll();
  }
}
