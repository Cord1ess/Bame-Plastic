/*
 * NativeWebSocket — vendored (MIT) from https://github.com/endel/NativeWebSocket
 * One-file build: System.Net.WebSockets on standalone/editor; browser WebSocket via WebSocket.jslib on WebGL.
 * Vendored locally because the project couldn't fetch the UPM git package (offline). Trimmed to what the game
 * uses (Connect / SendText / SendBytes / Close / OnOpen/OnMessage/OnError/OnClose / DispatchMessageQueue).
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NativeWebSocket
{
    public delegate void WebSocketOpenEventHandler();
    public delegate void WebSocketMessageEventHandler(byte[] data);
    public delegate void WebSocketErrorEventHandler(string errorMsg);
    public delegate void WebSocketCloseEventHandler(WebSocketCloseCode closeCode);

    public enum WebSocketCloseCode
    {
        NotSet = 0,
        Normal = 1000,
        Away = 1001,
        ProtocolError = 1002,
        UnsupportedData = 1003,
        Undefined = 1004,
        NoStatus = 1005,
        Abnormal = 1006,
        InvalidData = 1007,
        PolicyViolation = 1008,
        TooBig = 1009,
        MandatoryExtension = 1010,
        ServerError = 1011,
        TlsHandshakeFailure = 1015
    }

    public enum WebSocketState { Connecting, Open, Closing, Closed }

    public interface IWebSocket
    {
        event WebSocketOpenEventHandler OnOpen;
        event WebSocketMessageEventHandler OnMessage;
        event WebSocketErrorEventHandler OnError;
        event WebSocketCloseEventHandler OnClose;
        WebSocketState State { get; }
    }

    public static class WebSocketHelpers
    {
        public static WebSocketCloseCode ParseCloseCodeEnum(int closeCode)
        {
            if (Enum.IsDefined(typeof(WebSocketCloseCode), closeCode)) return (WebSocketCloseCode)closeCode;
            return WebSocketCloseCode.Undefined;
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // ---------------- WebGL: browser WebSocket via the .jslib ----------------
    public class WebSocket : IWebSocket
    {
        public event WebSocketOpenEventHandler OnOpen;
        public event WebSocketMessageEventHandler OnMessage;
        public event WebSocketErrorEventHandler OnError;
        public event WebSocketCloseEventHandler OnClose;

        int _instanceId;

        public WebSocket(string url, Dictionary<string, string> headers = null)
        {
            if (!WebSocketFactory.IsInitialized()) WebSocketFactory.Initialize();
            _instanceId = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.Add(this);
        }

        public WebSocket(string url, string subprotocol) : this(url) { }
        public WebSocket(string url, List<string> subprotocols) : this(url) { }

        public int InstanceId => _instanceId;

        public Task Connect()
        {
            int ret = WebSocketFactory.WebSocketConnect(_instanceId);
            if (ret < 0) OnError?.Invoke(GetErrorMessageFromCode(ret));
            return Task.CompletedTask;
        }

        public void CancelConnection()
        {
            if (State == WebSocketState.Open) Close(WebSocketCloseCode.Abnormal);
        }

        public Task Close(WebSocketCloseCode code = WebSocketCloseCode.Normal, string reason = null)
        {
            int ret = WebSocketFactory.WebSocketClose(_instanceId, (int)code, reason);
            if (ret < 0) OnError?.Invoke(GetErrorMessageFromCode(ret));
            return Task.CompletedTask;
        }

        public Task Send(byte[] data)
        {
            int ret = WebSocketFactory.WebSocketSend(_instanceId, data, data.Length);
            if (ret < 0) OnError?.Invoke(GetErrorMessageFromCode(ret));
            return Task.CompletedTask;
        }

        public Task SendText(string message)
        {
            int ret = WebSocketFactory.WebSocketSendText(_instanceId, message);
            if (ret < 0) OnError?.Invoke(GetErrorMessageFromCode(ret));
            return Task.CompletedTask;
        }

        public WebSocketState State
        {
            get
            {
                int state = WebSocketFactory.WebSocketGetState(_instanceId);
                if (state < 0) OnError?.Invoke(GetErrorMessageFromCode(state));
                switch (state)
                {
                    case 0: return WebSocketState.Connecting;
                    case 1: return WebSocketState.Open;
                    case 2: return WebSocketState.Closing;
                    case 3: return WebSocketState.Closed;
                    default: return WebSocketState.Closed;
                }
            }
        }

        public void DelegateOnOpenEvent() => OnOpen?.Invoke();
        public void DelegateOnMessageEvent(byte[] data) => OnMessage?.Invoke(data);
        public void DelegateOnErrorEvent(string errorMsg) => OnError?.Invoke(errorMsg);
        public void DelegateOnCloseEvent(int closeCode) => OnClose?.Invoke(WebSocketHelpers.ParseCloseCodeEnum(closeCode));

        public void DispatchMessageQueue() { /* WebGL dispatches via jslib callbacks immediately */ }

        static string GetErrorMessageFromCode(int errorCode)
        {
            switch (errorCode)
            {
                case -1: return "WebSocket instance not found.";
                case -2: return "WebSocket is already connected or in connecting state.";
                case -3: return "WebSocket is not connected.";
                case -4: return "WebSocket is already closing.";
                case -5: return "WebSocket is already closed.";
                case -6: return "WebSocket is not in open state.";
                case -7: return "Cannot close WebSocket. An invalid code was specified or reason is too long.";
                default: return "Unknown error.";
            }
        }
    }
#else
    // ---------------- Standalone / Editor: System.Net.WebSockets ----------------
    public class WebSocket : IWebSocket
    {
        public event WebSocketOpenEventHandler OnOpen;
        public event WebSocketMessageEventHandler OnMessage;
        public event WebSocketErrorEventHandler OnError;
        public event WebSocketCloseEventHandler OnClose;

        readonly Uri _uri;
        readonly Dictionary<string, string> _headers;
        readonly List<string> _subprotocols;
        System.Net.WebSockets.ClientWebSocket _socket;
        CancellationTokenSource _cts;
        CancellationToken _ct;

        // message queue dispatched on the Unity main thread by DispatchMessageQueue()
        readonly List<byte[]> _messageList = new List<byte[]>();
        readonly object _lock = new object();
        bool _opened, _closeQueued;
        WebSocketCloseCode _queuedCloseCode = WebSocketCloseCode.NotSet;
        string _queuedError;

        public WebSocket(string url, Dictionary<string, string> headers = null)
        {
            _uri = new Uri(url);
            _headers = headers ?? new Dictionary<string, string>();
            _subprotocols = new List<string>();
        }

        public WebSocket(string url, string subprotocol) : this(url) { _subprotocols.Add(subprotocol); }
        public WebSocket(string url, List<string> subprotocols) : this(url) { _subprotocols.AddRange(subprotocols); }

        public WebSocketState State
        {
            get
            {
                if (_socket == null) return WebSocketState.Closed;
                switch (_socket.State)
                {
                    case System.Net.WebSockets.WebSocketState.Connecting: return WebSocketState.Connecting;
                    case System.Net.WebSockets.WebSocketState.Open: return WebSocketState.Open;
                    case System.Net.WebSockets.WebSocketState.CloseSent:
                    case System.Net.WebSockets.WebSocketState.CloseReceived: return WebSocketState.Closing;
                    default: return WebSocketState.Closed;
                }
            }
        }

        public async Task Connect()
        {
            try
            {
                _cts = new CancellationTokenSource();
                _ct = _cts.Token;
                _socket = new System.Net.WebSockets.ClientWebSocket();
                foreach (var kv in _headers) _socket.Options.SetRequestHeader(kv.Key, kv.Value);
                foreach (var sp in _subprotocols) _socket.Options.AddSubProtocol(sp);

                await _socket.ConnectAsync(_uri, _ct);
                lock (_lock) { _opened = true; }

                await Receive();
            }
            catch (Exception ex)
            {
                lock (_lock) { _queuedError = ex.Message; _closeQueued = true; _queuedCloseCode = WebSocketCloseCode.Abnormal; }
            }
            finally
            {
                if (_socket != null) { _socket.Dispose(); _socket = null; }
            }
        }

        public Task Send(byte[] bytes) => SendMessage(System.Net.WebSockets.WebSocketMessageType.Binary, bytes);
        public Task SendText(string message) => SendMessage(System.Net.WebSockets.WebSocketMessageType.Text, Encoding.UTF8.GetBytes(message));

        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        async Task SendMessage(System.Net.WebSockets.WebSocketMessageType type, byte[] buffer)
        {
            if (_socket == null || _socket.State != System.Net.WebSockets.WebSocketState.Open) return;
            await _sendLock.WaitAsync(_ct);
            try { await _socket.SendAsync(new ArraySegment<byte>(buffer), type, true, _ct); }
            catch (Exception ex) { lock (_lock) { _queuedError = ex.Message; } }
            finally { _sendLock.Release(); }
        }

        async Task Receive()
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);
            using (var ms = new System.IO.MemoryStream())
            {
                while (_socket != null && _socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    System.Net.WebSockets.WebSocketReceiveResult result;
                    ms.SetLength(0);
                    do
                    {
                        result = await _socket.ReceiveAsync(buffer, _ct);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        {
                            await _socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            lock (_lock) { _closeQueued = true; _queuedCloseCode = WebSocketHelpers.ParseCloseCodeEnum((int)(result.CloseStatus ?? System.Net.WebSockets.WebSocketCloseStatus.NormalClosure)); }
                            return;
                        }
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                    } while (!result.EndOfMessage);

                    byte[] data = ms.ToArray();
                    lock (_lock) { _messageList.Add(data); }
                }
            }
        }

        public async Task Close(WebSocketCloseCode code = WebSocketCloseCode.Normal, string reason = null)
        {
            try
            {
                if (_socket != null && (_socket.State == System.Net.WebSockets.WebSocketState.Open))
                    await _socket.CloseAsync((System.Net.WebSockets.WebSocketCloseStatus)(int)code, reason ?? "", CancellationToken.None);
            }
            catch { /* ignore */ }
            finally { _cts?.Cancel(); }
        }

        public void CancelConnection() => _cts?.Cancel();

        // pump queued events onto the calling (Unity main) thread
        public void DispatchMessageQueue()
        {
            bool opened = false; string err = null; List<byte[]> msgs = null; bool close = false; WebSocketCloseCode cc = WebSocketCloseCode.NotSet;
            lock (_lock)
            {
                if (_opened) { opened = true; _opened = false; }
                if (_queuedError != null) { err = _queuedError; _queuedError = null; }
                if (_messageList.Count > 0) { msgs = new List<byte[]>(_messageList); _messageList.Clear(); }
                if (_closeQueued) { close = true; cc = _queuedCloseCode; _closeQueued = false; }
            }
            if (opened) OnOpen?.Invoke();
            if (err != null) OnError?.Invoke(err);
            if (msgs != null) foreach (var m in msgs) OnMessage?.Invoke(m);
            if (close) OnClose?.Invoke(cc);
        }
    }
#endif
}
