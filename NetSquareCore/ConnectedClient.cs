using System.Net;
using System.Net.Sockets;

namespace NetSquare.Core
{
    public class ConnectedClient
    {
        public readonly object sendSyncRoot = new object();
        public readonly object receiveSyncRoot = new object();
        public uint ID { get; set; }
        public Socket Socket { get; set; }
    }
}