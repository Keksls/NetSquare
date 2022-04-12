using System.Net.Sockets;

namespace NetSquareServer.Utils
{
    public static class SocketExtentions
    {
        public static bool IsConnected(this Socket s)
        {
            bool part1 = s.Poll(1000, SelectMode.SelectRead);
            bool part2 = (s.Available == 0);
            if ((part1 && part2) || !s.Connected)
                return false;
            else
                return true;
        }
    }
}