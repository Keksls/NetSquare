using System.Net;
using System.Net.Sockets;

namespace NetSquareServer
{
    public class ConnectedClient
    {
        public uint ID { get; internal set; }
        public IPAddress ServerIP { get; internal set; }
        public TcpClient TCPClient { get; internal set; }
    }
}