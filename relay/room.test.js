// node --test relay/room.test.js
import { test } from "node:test";
import assert from "node:assert/strict";
import { Room, Kind, GRACE_MS, newCode, validCode } from "./room.js";

function client(room, id, name, now = 0) {
  const c = { id, inbox: [], last(t) { return [...this.inbox].reverse().find((m) => m.t === t); } };
  room.join(id, name, (m) => c.inbox.push(JSON.parse(JSON.stringify(m))), now);
  return c;
}

function inputs(seat, tick, commands = []) { return { kind: Kind.Inputs, seat, tick, commands, hash: "", profileId: "" }; }

function twoPeople() {
  const room = new Room("ABCDE");
  const host = client(room, "host-0001", "Ann");
  const guest = client(room, "guest-001", "Bo");
  room.message(host.id, { t: "config", seats: [{ mode: "person" }, { mode: "person" }, { mode: "ai", profile: "hard" }, { mode: "off" }] });
  room.message(guest.id, { t: "take", seat: 2 });
  return { room, host, guest };
}

test("the first player hosts and takes seat 1", () => {
  const room = new Room("ABCDE");
  const a = client(room, "aaaa-0001", "Ann");
  const lobby = a.last("lobby");
  assert.equal(lobby.host, true);
  assert.equal(lobby.you, 1);
  assert.equal(lobby.seats[0].name, "Ann");
});

test("only the host configures and starts, and only when every person's seat is filled", () => {
  const room = new Room("ABCDE");
  const host = client(room, "host-0001", "Ann");
  const guest = client(room, "guest-001", "Bo");
  room.message(guest.id, { t: "config", seats: [{ mode: "off" }, { mode: "off" }] });
  assert.equal(room.seats[0].mode, "person", "a guest cannot change the setup");
  room.message(host.id, { t: "config", seats: [{ mode: "person" }, { mode: "person" }] });
  assert.equal(guest.last("lobby").you, 0, "the guest is not seated yet");
  room.message(host.id, { t: "start" });
  assert.match(host.last("error").message, /needs a player/);
  room.message(guest.id, { t: "take", seat: 2 });
  room.message(guest.id, { t: "start" });
  assert.equal(room.phase, "lobby", "a guest cannot start");
  room.message(host.id, { t: "start", seed: 77 });
  assert.equal(room.phase, "playing");
  const start = guest.last("start");
  assert.equal(start.seed, 77);
  assert.equal(start.you, 2);
  assert.deepEqual(start.seats.map((s) => s.mode), ["person", "person", "off", "off"]);
});

test("inputs pass to the others in order, only for the sender's own seat", () => {
  const { room, host, guest } = twoPeople();
  room.message(host.id, { t: "start", seed: 5 });
  room.message(host.id, { t: "ls", m: inputs(1, 4) });
  room.message(host.id, { t: "ls", m: inputs(2, 4) }); // not its seat
  room.message(host.id, { t: "ls", m: inputs(1, 4) }); // a tick already covered
  room.message(host.id, { t: "ls", m: inputs(1, 5, [{ kind: 3, actor: 2 }]) });
  const got = guest.inbox.filter((m) => m.t === "ls").map((m) => m.m);
  assert.deepEqual(got.map((m) => [m.seat, m.tick]), [[1, 4], [1, 5]]);
  assert.equal(got[1].commands[0].actor, 1, "orders are always for the sender's seat");
  assert.equal(host.inbox.filter((m) => m.t === "ls").length, 0, "nothing echoes back");
  assert.equal(room.log.length, 1, "only inputs that carry orders are logged");
});

test("a player gone past the grace period has their seat handed to an AI after their last inputs", () => {
  const { room, host, guest } = twoPeople();
  room.message(host.id, { t: "start", seed: 5 });
  room.message(guest.id, { t: "ls", m: inputs(2, 4) });
  room.message(guest.id, { t: "ls", m: inputs(2, 5) });
  room.leave(guest.id, 1000);
  assert.equal(host.last("dropped").seat, 2);
  assert.equal(room.nextCheck(), 1000 + GRACE_MS);
  room.check(1000 + GRACE_MS - 1);
  assert.equal(room.log.length, 0, "not before the grace period ends");
  room.check(1000 + GRACE_MS);
  const handover = host.last("ls").m;
  assert.deepEqual([handover.kind, handover.seat, handover.tick, handover.profileId], [Kind.Handover, 2, 6, "medium"]);
  assert.equal(room.log.length, 1, "the handover is logged");
});

test("a player back within the grace period gets the setup and the log, and keeps their seat", () => {
  const { room, host, guest } = twoPeople();
  room.message(host.id, { t: "start", seed: 5 });
  room.message(host.id, { t: "ls", m: inputs(1, 4, [{ kind: 3 }]) });
  room.message(guest.id, { t: "ls", m: inputs(2, 4) });
  room.leave(guest.id, 1000);
  const back = client(room, "guest-001", "Bo", 5000);
  const start = back.last("start");
  assert.equal(start.you, 2, "still seat 2");
  assert.equal(start.log.length, 1);
  assert.deepEqual(start.inputsThrough, [{ seat: 1, tick: 4 }, { seat: 2, tick: 4 }]);
  room.check(1000 + GRACE_MS * 2);
  assert.equal(room.log.length, 1, "no handover: they came back");
});

test("a player back after the handover watches", () => {
  const { room, host, guest } = twoPeople();
  room.message(host.id, { t: "start", seed: 5 });
  room.leave(guest.id, 0);
  room.check(GRACE_MS);
  const back = client(room, "guest-001", "Bo", GRACE_MS + 1);
  assert.equal(back.last("start").you, 0);
  room.message(back.id, { t: "ls", m: inputs(2, 9) });
  assert.equal(host.inbox.filter((m) => m.t === "ls").length, 1, "only the handover; a watcher sends nothing");
});

test("speed is shared", () => {
  const { room, host, guest } = twoPeople();
  room.message(host.id, { t: "start", seed: 5 });
  room.message(guest.id, { t: "speed", speed: 0 });
  assert.equal(host.last("speed").speed, 0);
  room.message(guest.id, { t: "speed", speed: 3 });
  assert.equal(room.speed, 0, "only 0, 1, 2 or 4");
});

test("room codes", () => {
  for (let i = 0; i < 50; i++) assert.ok(validCode(newCode()));
  assert.ok(!validCode("ABCD0"), "no zero");
  assert.ok(!validCode("abcde"));
});
