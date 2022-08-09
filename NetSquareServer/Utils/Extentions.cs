using NetSquare.Core;

namespace NetSquareServer.Utils
{
    public static class NetworkMessageExtentions
    {
        /// <summary>
        /// Reply a message to this message
        /// </summary>
        /// <param name="messageFrom"></param>
        /// <param name="message">Reply</param>
        public static void Reply(this NetworkMessage messageFrom, NetworkMessage message)
        {
            message.HeadID = messageFrom.HeadID;
            message.SetType(messageFrom.TypeID);
            messageFrom.Client?.AddTCPMessage(message);
        }
    }
}