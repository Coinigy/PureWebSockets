namespace PureWebSockets
{
    public class RequestMessage
    {
        public MessageType Type { get; set; }
        public byte[] Data { get; set; }
    }

    public enum MessageType
    {
        BINARY,
        TEXT
    }
}