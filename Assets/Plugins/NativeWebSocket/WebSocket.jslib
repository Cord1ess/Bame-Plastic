// NativeWebSocket WebGL plugin (vendored, MIT) — browser WebSocket bridge for the C# WebSocketFactory.
var LibraryWebSocket = {
  $webSocketState: {
    instances: {},
    lastId: 0,
    onOpen: null,
    onMessage: null,
    onError: null,
    onClose: null,
    debug: false
  },

  WebSocketSetOnOpen: function(callback) { webSocketState.onOpen = callback; },
  WebSocketSetOnMessage: function(callback) { webSocketState.onMessage = callback; },
  WebSocketSetOnError: function(callback) { webSocketState.onError = callback; },
  WebSocketSetOnClose: function(callback) { webSocketState.onClose = callback; },

  WebSocketAllocate: function(urlPtr) {
    var url = UTF8ToString(urlPtr);
    var id = webSocketState.lastId++;
    webSocketState.instances[id] = { subprotocols: [], url: url, ws: null };
    return id;
  },

  WebSocketFree: function(instanceId) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return 0;
    if (instance.ws !== null && instance.ws.readyState < 2) instance.ws.close();
    delete webSocketState.instances[instanceId];
    return 0;
  },

  WebSocketConnect: function(instanceId) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (instance.ws !== null) return -2;

    instance.ws = new WebSocket(instance.url);
    instance.ws.binaryType = 'arraybuffer';

    instance.ws.onopen = function() {
      if (webSocketState.onOpen)
        Module.dynCall_vi(webSocketState.onOpen, instanceId);
    };

    instance.ws.onmessage = function(ev) {
      if (webSocketState.onMessage === null) return;
      if (ev.data instanceof ArrayBuffer) {
        var array = new Uint8Array(ev.data);
        var buffer = _malloc(array.length);
        HEAPU8.set(array, buffer);
        try { Module.dynCall_viii(webSocketState.onMessage, instanceId, buffer, array.length); }
        finally { _free(buffer); }
      } else {
        // text → bytes (UTF-8)
        var length = lengthBytesUTF8(ev.data) + 1;
        var buffer = _malloc(length);
        stringToUTF8(ev.data, buffer, length);
        try { Module.dynCall_viii(webSocketState.onMessage, instanceId, buffer, length - 1); }
        finally { _free(buffer); }
      }
    };

    instance.ws.onerror = function(ev) {
      if (webSocketState.onError === null) return;
      var msg = "WebSocket error.";
      var length = lengthBytesUTF8(msg) + 1;
      var buffer = _malloc(length);
      stringToUTF8(msg, buffer, length);
      try { Module.dynCall_vii(webSocketState.onError, instanceId, buffer); }
      finally { _free(buffer); }
    };

    instance.ws.onclose = function(ev) {
      if (webSocketState.onClose)
        Module.dynCall_vii(webSocketState.onClose, instanceId, ev.code);
      instance.ws = null;
    };

    return 0;
  },

  WebSocketClose: function(instanceId, code, reasonPtr) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (instance.ws === null) return -3;
    if (instance.ws.readyState === 2) return -4;
    if (instance.ws.readyState === 3) return -5;
    var reason = (reasonPtr ? UTF8ToString(reasonPtr) : undefined);
    try { instance.ws.close(code, reason); } catch (err) { return -7; }
    return 0;
  },

  WebSocketSend: function(instanceId, bufferPtr, length) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (instance.ws === null) return -3;
    if (instance.ws.readyState !== 1) return -6;
    instance.ws.send(HEAPU8.buffer.slice(bufferPtr, bufferPtr + length));
    return 0;
  },

  WebSocketSendText: function(instanceId, messagePtr) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (instance.ws === null) return -3;
    if (instance.ws.readyState !== 1) return -6;
    instance.ws.send(UTF8ToString(messagePtr));
    return 0;
  },

  WebSocketGetState: function(instanceId) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (instance.ws === null) return 3;
    return instance.ws.readyState;
  }
};

autoAddDeps(LibraryWebSocket, '$webSocketState');
mergeInto(LibraryManager.library, LibraryWebSocket);
