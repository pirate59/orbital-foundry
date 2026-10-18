// The Orbital Foundry Worker: the static game (deploy/site) plus the multiplayer relay.
//   GET /relay/new          -> { code }: a fresh room code
//   GET /relay/<CODE>       -> WebSocket into that room (?client=<id>&name=<name>)
// Everything else is served from the static assets. Each room is a Durable Object holding a Room (room.js).

import { Room, newCode, validCode } from "./room.js";

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

/// One room. Its state lives in memory for the life of the match: while anyone is connected the object stays up.
export class RoomObject {
  constructor(state) {
    this.state = state;
    this.room = null;
  }

  async fetch(request) {
    const url = new URL(request.url);
    const code = url.pathname.slice("/relay/".length).toUpperCase();
    if (!this.room) this.room = new Room(code);
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
      this.schedule();
    };
    server.addEventListener("close", closed);
    server.addEventListener("error", closed);
    return new Response(null, { status: 101, webSocket: client });
  }

  /// Wakes for the next dropped-player check.
  schedule() {
    const due = this.room ? this.room.nextCheck() : 0;
    if (due) this.state.storage.setAlarm(due);
  }

  async alarm() {
    if (!this.room) return;
    this.room.check(Date.now());
    this.schedule();
  }
}
