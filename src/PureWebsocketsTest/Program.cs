using System;
using System.Net.WebSockets;
using System.Threading;
using PureWebSockets;

namespace CoreWebsocketsTest
{
    public class Program
    {
        private static PureWebSocket _ws;

        public static void Main(string[] args)
        {
            _ws = new PureWebSocket("wss://echo.websocket.org", new ReconnectStrategy(10000, 60000));
            _ws.OnStateChanged += Ws_OnStateChanged;
            _ws.OnMessage += Ws_OnMessage;
            _ws.OnClosed += Ws_OnClosed;
            _ws.OnSendFailed += _ws_OnSendFailed;
            _ws.Connect();

            var timer = new Timer(OnTick, null, 1000, 500);

            Console.ReadLine();
        }

        private static void _ws_OnSendFailed(string data, Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{DateTime.Now} Send Failed: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("");
        }

        private static void OnTick(object state)
        {
            _ws.Send(DateTime.Now.Ticks.ToString());
        }

        private static void Ws_OnClosed(WebSocketCloseStatus reason)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{DateTime.Now} Connection Closed: {reason}");
            Console.ResetColor();
            Console.WriteLine("");
            Console.ReadLine();
        }

        private static void Ws_OnMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{DateTime.Now} New message: {message}");
            Console.ResetColor();
            Console.WriteLine("");
        }

        private static void Ws_OnStateChanged(WebSocketState newState, WebSocketState prevState)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{DateTime.Now} Status changed from {prevState} to {newState}");
            Console.ResetColor();
            Console.WriteLine("");
        }
    }
}