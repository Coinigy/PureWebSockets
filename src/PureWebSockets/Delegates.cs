using System;
using System.Net.WebSockets;

namespace PureWebSockets
{
    public delegate void Message(object sender, string message);

    public delegate void Data(object sender, byte[] data);

    public delegate void StateChanged(object sender, WebSocketState newState, WebSocketState prevState);

    public delegate void Opened(object sender);

    public delegate void Closed(object sender, WebSocketCloseStatus reason);

    public delegate void Error(object sender, Exception ex);

    public delegate void SendFailed(object sender, string data, Exception ex);

    public delegate void Fatality(object sender, string reason);
}