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
            // this is just a timer to send data for us (simulate activity)
            _timer = new Timer(OnTickAsync, null, 4000, 1);

        RESTART:
            var socketOptions = new PureWebSocketOptions
            {
                DebugMode = false, // set this to true to see a ton O' logging
                SendDelay = 100, // the delay in ms between sending messages
                IgnoreCertErrors = true,
                MyReconnectStrategy = new ReconnectStrategy(2000, 4000, 20) // automatic reconnect if connection is lost
            };

            _ws = new PureWebSocket("wss://demos.kaazing.com/echo", socketOptions, "MyOptionalInstanceName1");

            _ws.OnStateChanged += Ws_OnStateChanged;
            _ws.OnMessage += Ws_OnMessage;
            _ws.OnClosed += Ws_OnClosed;
            _ws.OnSendFailed += Ws_OnSendFailed;
            await _ws.ConnectAsync();

            Console.ReadLine();
            _ws.Dispose(true);
            goto RESTART;
        }

        private static void Ws_OnSendFailed(object sender, string data, Exception ex)
        {
            OutputConsole.WriteLine($"{DateTime.Now} {((PureWebSocket)sender).InstanceName} Send Failed: {ex.Message}\r\n", ConsoleColor.Red);
        }

        private static void Ws_OnClosed(object sender, WebSocketCloseStatus reason)
        {
            OutputConsole.WriteLine($"{DateTime.Now} {((PureWebSocket)sender).InstanceName} Connection Closed: {reason}\r\n", ConsoleColor.Red);
        }

        private static void Ws_OnMessage(object sender, string message)
        {
            OutputConsole.WriteLine($"{DateTime.Now} {((PureWebSocket)sender).InstanceName} New message: {message}\r\n", ConsoleColor.Green);
        }

        private static void Ws_OnStateChanged(object sender, WebSocketState newState, WebSocketState prevState)
        {
            OutputConsole.WriteLine($"{DateTime.Now} Status changed from {prevState} to {newState} on instance {((PureWebSocket)sender).InstanceName}\r\n", ConsoleColor.Yellow);
        }

        /// <summary>
        /// Called by the timer to make activity.
        /// </summary>
        /// <param name="state">The state.</param>
        private static async void OnTickAsync(object state)
        {
            if (_ws.State != WebSocketState.Open) return;

            if (_sendCount == 1000)
            {
                _timer = new Timer(OnTickAsync, null, 30000, 1);
                await OutputConsole.WriteLineAsync($"{DateTime.Now} Max Send Count Reached: {_sendCount}\r\n", ConsoleColor.Red);
                _sendCount = 0;
            }
            if (await _ws.SendAsync($"{_sendCount} | {DateTime.Now.Ticks}"))
            {
                _sendCount++;
            }
            else
            {
                Ws_OnSendFailed(_ws, "", new Exception("Send Returned False"));
            }
        }
    }
}