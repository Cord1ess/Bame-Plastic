/*
 * NativeWebSocket WebGL factory (vendored, MIT). Bridges the browser WebSocket implemented in WebSocket.jslib
 * to the C# WebSocket class. Only compiled for WebGL builds.
 */
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;

namespace NativeWebSocket
{
    public static class WebSocketFactory
    {
        /* delegates matching the jslib callback signatures */
        public delegate void OnOpenCallback(int instanceId);
        public delegate void OnMessageCallback(int instanceId, IntPtr msgPtr, int msgSize);
        public delegate void OnErrorCallback(int instanceId, IntPtr errorPtr);
        public delegate void OnCloseCallback(int instanceId, int closeCode);

        [DllImport("__Internal")] public static extern int WebSocketAllocate(string url);
        [DllImport("__Internal")] public static extern void WebSocketFree(int instanceId);
        [DllImport("__Internal")] public static extern int WebSocketConnect(int instanceId);
        [DllImport("__Internal")] public static extern int WebSocketClose(int instanceId, int code, string reason);
        [DllImport("__Internal")] public static extern int WebSocketSend(int instanceId, byte[] data, int length);
        [DllImport("__Internal")] public static extern int WebSocketSendText(int instanceId, string message);
        [DllImport("__Internal")] public static extern int WebSocketGetState(int instanceId);

        [DllImport("__Internal")] public static extern void WebSocketSetOnOpen(OnOpenCallback cb);
        [DllImport("__Internal")] public static extern void WebSocketSetOnMessage(OnMessageCallback cb);
        [DllImport("__Internal")] public static extern void WebSocketSetOnError(OnErrorCallback cb);
        [DllImport("__Internal")] public static extern void WebSocketSetOnClose(OnCloseCallback cb);

        static readonly Dictionary<int, WebSocket> instances = new Dictionary<int, WebSocket>();
        static bool initialized;

        public static bool IsInitialized() => initialized;

        public static void Initialize()
        {
            WebSocketSetOnOpen(OnOpen);
            WebSocketSetOnMessage(OnMessage);
            WebSocketSetOnError(OnError);
            WebSocketSetOnClose(OnClose);
            initialized = true;
        }

        public static void Add(WebSocket ws) { instances[ws.InstanceId] = ws; }

        [MonoPInvokeCallback(typeof(OnOpenCallback))]
        public static void OnOpen(int instanceId)
        {
            if (instances.TryGetValue(instanceId, out var ws)) ws.DelegateOnOpenEvent();
        }

        [MonoPInvokeCallback(typeof(OnMessageCallback))]
        public static void OnMessage(int instanceId, IntPtr msgPtr, int msgSize)
        {
            if (instances.TryGetValue(instanceId, out var ws))
            {
                byte[] bytes = new byte[msgSize];
                Marshal.Copy(msgPtr, bytes, 0, msgSize);
                ws.DelegateOnMessageEvent(bytes);
            }
        }

        [MonoPInvokeCallback(typeof(OnErrorCallback))]
        public static void OnError(int instanceId, IntPtr errorPtr)
        {
            if (instances.TryGetValue(instanceId, out var ws))
                ws.DelegateOnErrorEvent(Marshal.PtrToStringAuto(errorPtr));
        }

        [MonoPInvokeCallback(typeof(OnCloseCallback))]
        public static void OnClose(int instanceId, int closeCode)
        {
            if (instances.TryGetValue(instanceId, out var ws)) ws.DelegateOnCloseEvent(closeCode);
        }
    }
}
#endif
