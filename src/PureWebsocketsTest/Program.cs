using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using PureWebSockets;

namespace PureWebsocketsTest
{
    public class Program
    {
        private static PureWebSocket _ws;
        private static int _sendCount;
        private static Timer _timer;
        public static async Task Main(string[] args)
        {
            _timer = new Timer(OnTickAsync, null, 2000, 1);

            RESTART:
            var socketOptions = new PureWebSocketOptions()
            {
                DebugMode = true,
                SendDelay = 100,
                IgnoreCertErrors = true,
                MyReconnectStrategy = new ReconnectStrategy(2000, 4000, 20)
            };

            _ws = new PureWebSocket("wss://demos.kaazing.com/echo", socketOptions);

            _ws.OnStateChanged += Ws_OnStateChanged;
            _ws.OnMessage += Ws_OnMessage;
            _ws.OnClosed += Ws_OnClosed;
            _ws.OnSendFailed += Ws_OnSendFailed;
            await _ws.ConnectAsync();
            
            Console.ReadLine();
            _ws.Dispose(true);
            goto RESTART;
        }

        private static void Ws_OnSendFailed(string data, Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{DateTime.Now} Send Failed: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("");
        }

        private static async void OnTickAsync(object state)
        {
            if (_ws.State != WebSocketState.Open) return;

            if (_sendCount == 1000)
            {
                _timer = new Timer(OnTickAsync, null, 30000, 1);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{DateTime.Now} Max Send Count Reached: {_sendCount}");
                Console.ResetColor();
                Console.WriteLine("");
                _sendCount = 0;
            }
            if (await _ws.SendAsync(_sendCount + " | " + DateTime.Now.Ticks.ToString()))
            {
                _sendCount++;
            }
            else
            {
                Ws_OnSendFailed("", new Exception("Send Returned False"));
            }
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