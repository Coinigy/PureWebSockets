using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PureWebSockets;

namespace PureWebsockets
{
    public class PureWebSocket : IDisposable
    {
        private string Url { get; }
        private ClientWebSocket _ws;
        private readonly BlockingCollection<string> _sendQueue = new BlockingCollection<string>();
        private readonly ReconnectStrategy _reconnectStrategy;
        private bool _disconnectCalled;
        private bool _listenerRunning;
        private bool _senderRunning;
        private bool _monitorRunning;
        private bool _reconnecting;

        public WebSocketState State => _ws.State;

        public event Data OnData;
        public event Message OnMessage;
        public event StateChanged OnStateChanged;
        public event Opened OnOpened;
        public event Closed OnClosed;
        public event Error OnError;
        public event SendFailed OnSendFailed;

        public PureWebSocket(string url)
        {
            Url = url;
            _ws = new ClientWebSocket();
            StartMonitor();
        }

        public PureWebSocket(string url, ReconnectStrategy reconnectStrategy)
        {
            Url = url;
            _reconnectStrategy = reconnectStrategy;
            _ws = new ClientWebSocket();
            StartMonitor();
        }

        public void Connect()
        {
            try
            {
                _disconnectCalled = false;
                _ws.ConnectAsync(new Uri(Url), CancellationToken.None).Wait(15000);

                StartListener();
                StartSender();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
                throw;
            }
        }

        public bool Send(string data)
        {
            try
            {
                if (State != WebSocketState.Open) return false;

                _sendQueue.Add(data);
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
                throw;
            }

        }

        private void StartMonitor()
        {
            Task.Run(() =>
            {
                _monitorRunning = true;
                try
                {
                    var lastState = State;
                    while (_ws != null && !_disposedValue)
                    {
                        if (lastState == State) continue;

                        OnStateChanged?.Invoke(State, lastState);

                        if (State == WebSocketState.Open)
                            OnOpened?.Invoke();

                        if (State == WebSocketState.Closed || State == WebSocketState.Aborted)
                        {
                            if (lastState == WebSocketState.Open && !_disconnectCalled && _reconnectStrategy != null && !_reconnectStrategy.AreAttemptsComplete())
                            {
                                // go through the reconnect strategy
                                // Exit the loop and start async reconnect
                                DoReconnect();
                            }
                            OnClosed?.Invoke(_ws.CloseStatus ?? WebSocketCloseStatus.Empty);
                            if (_ws.CloseStatus != null && _ws.CloseStatus != WebSocketCloseStatus.NormalClosure)
                                OnError?.Invoke(new Exception(_ws.CloseStatus + " " + _ws.CloseStatusDescription));
                        }

                        lastState = State;

                        Task.Delay(20).Wait();
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                }
                _monitorRunning = false;
            });
        }

        private void DoReconnect()
        {
            _reconnecting = true;
            Thread.Sleep(_reconnectStrategy.GetReconnectInterval());

            _ws.Dispose();

            _ws = new ClientWebSocket();
            // ERROR throws an error saying the websocket has already been started
            _ws.ConnectAsync(new Uri(Url), CancellationToken.None).Wait(15000);

            _reconnecting = false;
            if(!_monitorRunning)
                StartMonitor();
            if(!_listenerRunning)
                StartListener();
            if(!_senderRunning)
                StartSender();
            _reconnectStrategy.ProcessValues();
        }

        private void StartListener()
        {
            Task.Run(async () =>
            {
                _listenerRunning = true;
                try
                {
                    while (_ws.State == WebSocketState.Open && !_disposedValue && !_reconnecting)
                    {
                        var message = "";
                        var binary = new List<byte>();

                        READ:

                        var buffer = new byte[1024];
                        WebSocketReceiveResult res = null;

                        RETRY:

                        try
                        {
                            res = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        }
                        catch
                        {
                            if (!_disconnectCalled && !_disposedValue && !_reconnecting)
                            {
                                Thread.Sleep(2000);
                                if (_ws.State == WebSocketState.Open)
                                    goto RETRY;
                                if (_reconnectStrategy != null && _reconnectStrategy.AreAttemptsComplete() != true)
                                    goto RETRY;
                            }
                            return Task.CompletedTask;
                        }
                        if (res == null)
                            goto RETRY;

                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "SERVER REQUESTED CLOSE",
                                CancellationToken.None);
                        }

                        // handle text data
                        if (res.MessageType == WebSocketMessageType.Text)
                        {
                            if (!res.EndOfMessage)
                            {
                                message += Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                                goto READ;
                            }
                            message += Encoding.UTF8.GetString(buffer).TrimEnd('\0');

                            // support ping/pong if initiated by the server (see RFC 6455)
                            if (message.Trim() == "ping")
#pragma warning disable 4014
                                Send("pong");
#pragma warning restore 4014
                            else
                                Task.Run(() => OnMessage?.Invoke(message)).Wait(50);
                        }
                        else
                        {
                            // handle binary data
                            if (!res.EndOfMessage)
                            {
                                binary.AddRange(buffer.Where(b => b != '\0'));
                                goto READ;
                            }

                            binary.AddRange(buffer.Where(b => b != '\0'));

                            Task.Run(() => OnData?.Invoke(binary.ToArray())).Wait(50);
                        }

                        // ReSharper disable once RedundantAssignment
                        buffer = null;
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                }
                _listenerRunning = false;
                return Task.CompletedTask;
            });
        }

        private void StartSender()
        {
            try
            {
                var thread = new Thread(async () =>
                    {
                        _senderRunning = true;
                        try
                        {
                            while (!_disposedValue && !_reconnecting)
                            {
                                if (_ws.State == WebSocketState.Open && !_reconnecting)
                                {
                                    var msg = _sendQueue.Take();
                                    var buffer = Encoding.UTF8.GetBytes(msg);
                                    try
                                    {
                                        await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text,
                                            true, CancellationToken.None);
                                    }
                                    catch (Exception ex)
                                    {
                                        OnSendFailed?.Invoke(msg, ex);
                                    }

                                }
                                // limit to 100 ms per iteration
                                Thread.Sleep(100);
                            }
                        }
                        catch (Exception ex)
                        {
                            OnSendFailed?.Invoke("", ex);
                            OnError?.Invoke(ex);
                        }
                        _senderRunning = false;
                    })
                { IsBackground = true };
                thread.Start();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        public void Disconnect()
        {
            try
            {
                _disconnectCalled = true;
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "NORMAL SHUTDOWN", CancellationToken.None).Wait(20000);
            }
            catch
            {
                // ignored
            }
        }

        #region IDisposable Support
        private bool _disposedValue; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects).
                    Disconnect();
                    _ws.Dispose();
                }

                _disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}
