using NetSquare.Core;

namespace NetSquare.Server.Utils
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
            message.MsgType = (byte)MessageType.Reply;
            message.ReplyID = messageFrom.ReplyID;
            messageFrom.Client?.AddTCPMessage(message);
        }
    }
}