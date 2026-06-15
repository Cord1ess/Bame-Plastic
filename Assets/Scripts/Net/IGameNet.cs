using System;

namespace BamePlastic.Net
{
    /// The game hot-path seam — separate from INetworkService (lobby). Sends/receives raw BINARY frames on the
    /// same WebSocket; the Spring Boot server relays them verbatim to the other room members. GameNet owns the
    /// encode/decode + authority routing on top of this.
    public interface IGameNet
    {
        void SendBinary(byte[] frame);     // send a raw game frame to the rest of the room
        event Action<byte[]> OnBinary;     // a game frame arrived from another player
    }
}
