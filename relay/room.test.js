// node --test relay/room.test.js
import { test } from "node:test";
import assert from "node:assert/strict";
import { Room, Kind, newCode, validCode } from "./room.js";

function client(room, id, name) {
  const c = {
    id, inbox: [],
    last(t) { return [...this.inbox].reverse().find((m) => m.t === t); },
    lockstep(kind) { return this.inbox.filter((m) => m.t === "ls" && m.m.kind === kind).map((m) => m.m); },
  };
  room.join(id, name, (m) => c.inbox.push(JSON.parse(JSON.stringify(m))));
  return c;
}

function inputs(seat, tick, commands = []) { return { kind: Kind.Inputs, seat, tick, commands, hash: "", profileId: "" }; }
function control(kind, seat) { return { kind, seat, tick: 0, commands: [], hash: "", profileId: "" }; }

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
  const got = guest.lockstep(Kind.Inputs);
  assert.deepEqual(got.map((m) => [m.seat, m.tick]), [[1, 4], [1, 5]]);
  assert.equal(got[1].commands[0].actor, 1, "orders are always for the sender's seat");
  assert.equal(host.inbox.filter((m) => m.t === "ls").length, 0, "nothing echoes back");
  assert.equal(room.log.length, 1, "only inputs that carry orders are logged");
});

test("an away player's seat is filled in, keeping pace with the players who are there", () => {
  const { room, host, guest } = twoPeople();
  room.message(host.id, { t: "start", seed: 5 });
  room.message(guest.id, { t: "ls", m: inputs(2, 4) });
  room.message(guest.id, { t: "ls", m: control(Kind.Away, 2) });
  assert.deepEqual(host.last("away"), { t: "away", seat: 2, isAway: true });
  room.message(host.id, { t: "ls", m: inputs(1, 4) });
  room.message(host.id, { t: "ls", m: inputs(1, 9) });
  const fills = host.lockstep(Kind.Through);
  assert.deepEqual(fills.map((m) => [m.seat, m.tick]), [[2, 9]], "filled through the host's latest tick");
  assert.deepEqual(guest.lockstep(Kind.Through).map((m) => m.tick), [9], "the away machine hears it too");
  room.message(guest.id, { t: "ls", m: inputs(2, 5) });
  assert.equal(host.lockstep(Kind.Inputs).length, 1, "an away seat's own inputs are refused until it is back");
});

test("a player who is back resumes after the last tick filled in", () => {
  const { room, host, guest } = twoPeople();
  room.message(host.id, { t: "start", seed: 5 });
  room.message(guest.id, { t: "ls", m: control(Kind.Away, 2) });
  room.message(host.id, { t: "ls", m: inputs(1, 12) });
  room.message(guest.id, { t: "ls", m: control(Kind.Back, 2) });
  const resume = guest.lockstep(Kind.Resume);
  assert.deepEqual(resume.map((m) => [m.seat, m.tick]), [[2, 13]]);
  assert.deepEqual(host.last("away"), { t: "away", seat: 2, isAway: false });
  room.message(guest.id, { t: "ls", m: inputs(2, 13) });
  assert.deepEqual(host.lockstep(Kind.Inputs).map((m) => [m.seat, m.tick]), [[2, 13]], "and its inputs flow again");
  room.message(host.id, { t: "ls", m: inputs(1, 20) });
  assert.equal(host.lockstep(Kind.Through).length, 1, "no more filling once it is back");
});

test("a closed window leaves the seat away; reconnecting rebuilds the match and claims it back", () => {
  const { room, host, guest } = twoPeople();
  room.message(host.id, { t: "start", seed: 5 });
  room.message(host.id, { t: "ls", m: inputs(1, 4, [{ kind: 3 }]) });
  room.message(guest.id, { t: "ls", m: inputs(2, 4) });
  room.leave(guest.id);
  assert.equal(host.last("dropped").seat, 2);
  assert.deepEqual(host.last("away"), { t: "away", seat: 2, isAway: true });
  room.message(host.id, { t: "ls", m: inputs(1, 30) });
  const back = client(room, "guest-001", "Bo");
  const start = back.last("start");
  assert.equal(start.you, 2, "still their seat");
  assert.deepEqual(start.awaySeats, [2], "and away until they say they are back");
  assert.equal(start.log.length, 1);
  assert.deepEqual(start.inputsThrough, [{ seat: 1, tick: 30 }, { seat: 2, tick: 30 }]);
  room.message(back.id, { t: "ls", m: control(Kind.Back, 2) });
  assert.equal(back.lockstep(Kind.Resume)[0].tick, 31);
});

test("a newcomer to a match under way watches", () => {
  const { room, host } = twoPeople();
  room.message(host.id, { t: "start", seed: 5 });
  const late = client(room, "late-0001", "Cy");
  assert.equal(late.last("start").you, 0);
  room.message(late.id, { t: "ls", m: inputs(2, 9) });
  assert.equal(host.inbox.filter((m) => m.t === "ls").length, 0, "a watcher sends nothing");
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
