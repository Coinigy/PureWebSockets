/*
 * Author: ByronP
 * Date: 1/14/2017
 * Coinigy Inc. Coinigy.com
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PureWebSockets
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
        private CancellationTokenSource _tokenSource = new CancellationTokenSource();

        private Task _monitorTask;
        private Task _listenerTask;
        private Task _senderTask;


        public WebSocketState State => _ws.State;

        public event Data OnData;
        public event Message OnMessage;
        public event StateChanged OnStateChanged;
        public event Opened OnOpened;
        public event Closed OnClosed;
        public event Error OnError;
        public event SendFailed OnSendFailed;
        public event Fatality OnFatality;

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
                _ws.ConnectAsync(new Uri(Url), _tokenSource.Token).Wait(15000);

                StartListener();
                StartSender();

                Task.Run(() =>
                {
                    while (_ws.State != WebSocketState.Open)
                    {
                        
                    }
                }).Wait(15000);
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
            _monitorTask = Task.Run(() =>
            {
                _monitorRunning = true;
                var needsReconnect = false;
                try
                {
                    var lastState = State;
                    while (_ws != null && !_disposedValue)
                    {
                        if (lastState == State) continue;

                        OnStateChanged?.Invoke(State, lastState);

                        if (State == WebSocketState.Open)
                            OnOpened?.Invoke();

                        if ((State == WebSocketState.Closed || State == WebSocketState.Aborted) && !_reconnecting)
                        {
                            if (lastState == WebSocketState.Open && !_disconnectCalled && _reconnectStrategy != null &&
                                !_reconnectStrategy.AreAttemptsComplete())
                            {
                                // go through the reconnect strategy
                                // Exit the loop and start async reconnect
                                needsReconnect = true;
                                break;
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
                if (needsReconnect && !_reconnecting && !_disconnectCalled)
#pragma warning disable 4014
                    DoReconnect();
#pragma warning restore 4014
                _monitorRunning = false;
            });
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task DoReconnect()
        {
#pragma warning disable 4014
            Task.Run(async () =>
            {
                _tokenSource.Cancel();
                _reconnecting = true;
                if (!Task.WaitAll(new[] {_monitorTask, _listenerTask, _senderTask}, 15000))
                {
                    // exit everything as dead...
                    OnFatality?.Invoke("Fatal network error. Network services fail to shut down.");
                    _reconnecting = false;
                    _disconnectCalled = true;
                    _tokenSource.Cancel();
                    return;
                }
                _ws.Dispose();

                OnStateChanged?.Invoke(WebSocketState.Connecting, WebSocketState.Aborted);

                _tokenSource = new CancellationTokenSource();

                var connected = false;
                while (!_disconnectCalled && !_disposedValue && !connected && !_tokenSource.IsCancellationRequested)
                    try
                    {
                        _ws = new ClientWebSocket();
                        if (!_monitorRunning)
                            StartMonitor();
                        connected = _ws.ConnectAsync(new Uri(Url), _tokenSource.Token).Wait(15000);
                    }
                    catch
                    {
                        _ws.Dispose();
                        Thread.Sleep(_reconnectStrategy.GetReconnectInterval());
                        _reconnectStrategy.ProcessValues();
                        if (_reconnectStrategy.AreAttemptsComplete())
                        {
                            // exit everything as dead...
                            OnFatality?.Invoke("Fatal network error. Max reconnect attemps reached.");
                            _reconnecting = false;
                            _disconnectCalled = true;
                            _tokenSource.Cancel();
                            return;
                        }
                    }
                if (connected)
                {
                    _reconnecting = false;
                    if (!_monitorRunning)
                        StartMonitor();
                    if (!_listenerRunning)
                        StartListener();
                    if (!_senderRunning)
                        StartSender();
                }
            });
#pragma warning restore 4014
        }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        private void StartListener()
        {
            _listenerTask = Task.Run(async () =>
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

                        try
                        {
                            res = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _tokenSource.Token);
                        }
                        catch
                        {
                            // Most likely socket error
                            _ws.Abort();
                            break;
                        }

                        if (res == null)
                            goto READ;

                        if (res.MessageType == WebSocketMessageType.Close)
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "SERVER REQUESTED CLOSE",
                                _tokenSource.Token);

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
            _senderTask = Task.Run(async () =>
            {
                _senderRunning = true;
                try
                {
                    while (!_disposedValue && !_reconnecting)
                    {
                        if (_ws.State == WebSocketState.Open && !_reconnecting)
                        {
                            var msg = _sendQueue.Take(_tokenSource.Token);
                            var buffer = Encoding.UTF8.GetBytes(msg);
                            try
                            {
                                await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text,
                                    true, _tokenSource.Token);
                            }
                            catch (Exception ex)
                            {
                                // Most likely socket error
                                OnSendFailed?.Invoke(msg, ex);
                                _ws.Abort();
                                break;
                            }
                        }
                        // limit to 80 ms per iteration
                        Thread.Sleep(80);
                    }
                }
                catch (Exception ex)
                {
                    OnSendFailed?.Invoke("", ex);
                    OnError?.Invoke(ex);
                }
                _senderRunning = false;
                return Task.CompletedTask;
            });
        }

        public void Disconnect()
        {
            try
            {
                _disconnectCalled = true;
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "NORMAL SHUTDOWN", _tokenSource.Token).Wait(20000);
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