using NetSquare.Core;

#region Source
namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Represents the network message extentions component.
    /// </summary>
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
            message.MsgType = (byte)NetSquareMessageType.Reply;
            message.ReplyID = messageFrom.ReplyID;
            messageFrom.Client?.AddTCPMessage(message);
        }
    }
}
#endregion
