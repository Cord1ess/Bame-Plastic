using System;
using UnityEngine;

namespace BamePlastic.Net
{
    /// Tiny little-endian binary writer/reader for the game hot-path frames (see GameNet). No allocations on
    /// the read side; the writer uses a reusable buffer. Quantization helpers match NETWORKING.md (positions
    /// in cm, yaw 0..360 → ushort, speed cm/s).
    public class NetWriter
    {
        byte[] _buf;
        int _pos;
        public NetWriter(int capacity = 64) { _buf = new byte[capacity]; _pos = 0; }

        void Need(int n) { if (_pos + n > _buf.Length) Array.Resize(ref _buf, Mathf.Max(_buf.Length * 2, _pos + n)); }

        public NetWriter U8(byte v)   { Need(1); _buf[_pos++] = v; return this; }
        public NetWriter I16(short v) { Need(2); _buf[_pos++] = (byte)(v & 0xFF); _buf[_pos++] = (byte)((v >> 8) & 0xFF); return this; }
        public NetWriter U16(ushort v){ Need(2); _buf[_pos++] = (byte)(v & 0xFF); _buf[_pos++] = (byte)((v >> 8) & 0xFF); return this; }
        public NetWriter I32(int v)   { Need(4); _buf[_pos++] = (byte)(v & 0xFF); _buf[_pos++] = (byte)((v >> 8) & 0xFF); _buf[_pos++] = (byte)((v >> 16) & 0xFF); _buf[_pos++] = (byte)((v >> 24) & 0xFF); return this; }

        // quantized helpers
        public NetWriter PosCm(float metres) => I32(Mathf.RoundToInt(metres * 100f));
        public NetWriter PosCm16(float metres) => I16((short)Mathf.Clamp(Mathf.RoundToInt(metres * 100f), short.MinValue, short.MaxValue));
        public NetWriter Yaw(float deg) { deg = Mathf.Repeat(deg, 360f); return U16((ushort)Mathf.RoundToInt(deg / 360f * 65535f)); }
        public NetWriter SpeedCm(float mps) => I16((short)Mathf.Clamp(Mathf.RoundToInt(mps * 100f), short.MinValue, short.MaxValue));
        public NetWriter Bool(bool b) => U8(b ? (byte)1 : (byte)0);

        /// Copy out exactly the bytes written (a fresh array — safe to hand to the socket).
        public byte[] ToArray() { var a = new byte[_pos]; Array.Copy(_buf, a, _pos); return a; }
        public void Reset() { _pos = 0; }
    }

    public struct NetReader
    {
        readonly byte[] _b;
        int _pos;
        public NetReader(byte[] bytes, int start = 0) { _b = bytes; _pos = start; }

        public bool HasMore => _pos < _b.Length;
        public byte U8()   => _b[_pos++];
        public short I16() { short v = (short)(_b[_pos] | (_b[_pos + 1] << 8)); _pos += 2; return v; }
        public ushort U16(){ ushort v = (ushort)(_b[_pos] | (_b[_pos + 1] << 8)); _pos += 2; return v; }
        public int I32()   { int v = _b[_pos] | (_b[_pos + 1] << 8) | (_b[_pos + 2] << 16) | (_b[_pos + 3] << 24); _pos += 4; return v; }

        public float PosCm()   => I32() / 100f;
        public float PosCm16() => I16() / 100f;
        public float Yaw()     => U16() / 65535f * 360f;
        public float SpeedCm() => I16() / 100f;
        public bool Bool()     => U8() != 0;
    }

    /// Message ids for the game hot-path (1 byte, first byte of every binary frame).
    public static class MsgId
    {
        public const byte BusState        = 0x01;
        public const byte AvatarPose      = 0x02;
        public const byte IntentCollect   = 0x10;
        public const byte IntentGrab      = 0x11;
        public const byte IntentThrow     = 0x12;
        public const byte IntentShove     = 0x13;
        public const byte PassengerBoard  = 0x20;
        public const byte PassengerAlight = 0x21;
        public const byte FareCollected   = 0x22;
        public const byte EarningsSync    = 0x23;
        public const byte PauseState      = 0x30;   // driver→room: paused (byte) + whoSlot (byte)
        public const byte RoleReassign    = 0x31;   // driver→room: failover — newDriverSlot (byte)
    }
}
