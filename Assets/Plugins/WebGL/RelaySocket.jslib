// Browser WebSockets for the multiplayer relay. C# opens a socket by URL and gets an id back, then polls it each
// frame: messages wait in a queue here until read, so nothing runs C# from a JavaScript callback.
mergeInto(LibraryManager.library, {
  OrbitalFoundry_WsOpen: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    if (!Module.ofSockets) Module.ofSockets = { next: 1, all: {} };
    var id = Module.ofSockets.next++;
    var entry = { queue: [], state: 0 }; // 0 connecting, 1 open, 2 closed
    Module.ofSockets.all[id] = entry;
    try {
      var ws = new WebSocket(url);
      entry.ws = ws;
      ws.onopen = function () { entry.state = 1; };
      ws.onmessage = function (e) { if (typeof e.data === "string") entry.queue.push(e.data); };
      ws.onclose = function () { entry.state = 2; };
      ws.onerror = function () { entry.state = 2; };
    } catch (e) {
      entry.state = 2;
    }
    return id;
  },

  OrbitalFoundry_WsState: function (id) {
    var entry = Module.ofSockets && Module.ofSockets.all[id];
    return entry ? entry.state : 2;
  },

  OrbitalFoundry_WsSend: function (id, textPtr) {
    var entry = Module.ofSockets && Module.ofSockets.all[id];
    if (!entry || entry.state !== 1) return 0;
    try { entry.ws.send(UTF8ToString(textPtr)); return 1; } catch (e) { return 0; }
  },

  // The next message, or null when none is waiting. The string is handed to C#, which frees it.
  OrbitalFoundry_WsReceive: function (id) {
    var entry = Module.ofSockets && Module.ofSockets.all[id];
    if (!entry || entry.queue.length === 0) return null;
    var text = entry.queue.shift();
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  },

  OrbitalFoundry_WsClose: function (id) {
    var entry = Module.ofSockets && Module.ofSockets.all[id];
    if (!entry) return;
    try { if (entry.ws) entry.ws.close(); } catch (e) {}
    delete Module.ofSockets.all[id];
  },

  // Whether the page is out of view (a background tab): the browser then runs it about once a second.
  OrbitalFoundry_PageHidden: function () {
    return document.hidden ? 1 : 0;
  },

  // The page's own origin as a WebSocket base ("wss://host"), for reaching the relay that served it.
  OrbitalFoundry_PageSocketBase: function () {
    var base = (location.protocol === "https:" ? "wss://" : "ws://") + location.host;
    var size = lengthBytesUTF8(base) + 1;
    var buffer = _malloc(size);
    stringToUTF8(base, buffer, size);
    return buffer;
  },
});
