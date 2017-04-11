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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PureWebSockets
{
    public class PureWebSocket : IDisposable
    {
        private string Url { get; }
        private ClientWebSocket _ws;
        private readonly BlockingCollection<KeyValuePair<DateTime, string>> _sendQueue = new BlockingCollection<KeyValuePair<DateTime, string>>();
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
        public TimeSpan SendCacheItemTimeout { get; set; }
        public ushort SendDelay { get; set; }
        public int SendQueueLength => _sendQueue.Count;
        public int SendQueueLimit { get; set; }
        public bool DebugMode { get; set; }

        public event Data OnData;
        public event Message OnMessage;
        public event StateChanged OnStateChanged;
        public event Opened OnOpened;
        public event Closed OnClosed;
        public event Error OnError;
        public event SendFailed OnSendFailed;
        public event Fatality OnFatality;

        public PureWebSocket(string url, int queueLimit = 1000)
        {
            Log("Creating new instance.");
            SendQueueLimit = queueLimit;
            Url = url;
            _ws = new ClientWebSocket();
            SendCacheItemTimeout = TimeSpan.FromMinutes(30);
            SendDelay = 80;
            StartMonitor();
        }

        public PureWebSocket(string url, TimeSpan sendCacheItemTimeout, int queueLimit = 1000)
        {
            Log("Creating new instance.");
            SendQueueLimit = queueLimit;
            Url = url;
            _ws = new ClientWebSocket();
            SendCacheItemTimeout = sendCacheItemTimeout;
            SendDelay = 80;
            StartMonitor();
        }

        public PureWebSocket(string url, ReconnectStrategy reconnectStrategy, int queueLimit = 1000)
        {
            Log("Creating new instance.");
            SendQueueLimit = queueLimit;
            Url = url;
            _reconnectStrategy = reconnectStrategy;
            SendCacheItemTimeout = TimeSpan.FromMinutes(30);
            SendDelay = 80;
            _ws = new ClientWebSocket();
            StartMonitor();
        }

        public PureWebSocket(string url, TimeSpan sendCacheItemTimeout, ReconnectStrategy reconnectStrategy, int queueLimit = 1000)
        {
            Log("Creating new instance.");
            SendQueueLimit = queueLimit;
            Url = url;
            _reconnectStrategy = reconnectStrategy;
            _ws = new ClientWebSocket();
            SendDelay = 80;
            SendCacheItemTimeout = sendCacheItemTimeout;
            StartMonitor();
        }

        public bool Connect()
        {
            Log("Connect called.");
            try
            {
                _disconnectCalled = false;
                _ws.ConnectAsync(new Uri(Url), _tokenSource.Token).Wait(15000);
                Log("Starting tasks.");
                StartListener();
                StartSender();

                Task.Run(() =>
                {
                    while (_ws.State != WebSocketState.Open)
                    {

                    }
                }).Wait(15000);
                Log($"Connect result: {_ws.State == WebSocketState.Open}, State {_ws.State}");
                return _ws.State == WebSocketState.Open;
            }
            catch (Exception ex)
            {
                Log($"Connect threw exception: {ex.Message}.");
                OnError?.Invoke(ex);
                throw;
            }
        }

        public bool Send(string data)
        {
            try
            {
                if (State != WebSocketState.Open || SendQueueLength >= SendQueueLimit)
                {
                    Log(SendQueueLength >= SendQueueLimit ? $"Could not add item to send queue: queue limit reached, Data {data}" : $"Could not add item to send queue: State {State}, Queue Count {SendQueueLength}, Data {data}");
                    return false;
                }
                Task.Run(() =>
                {
                    Log($"Adding item to send queue: Data {data}");
                    _sendQueue.Add(new KeyValuePair<DateTime, string>(DateTime.UtcNow, data));
                }).Wait(100, _tokenSource.Token);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Send threw exception: {ex.Message}.");
                OnError?.Invoke(ex);
                throw;
            }
        }

        private void StartMonitor()
        {
            Log("Starting monitor.");
            _monitorTask = Task.Run(() =>
            {
                Log("Entering monitor loop.");
                _monitorRunning = true;
                var needsReconnect = false;
                try
                {
                    var lastState = State;
                    while (_ws != null && !_disposedValue)
                    {
                        if (lastState == State)
                        {
                            Thread.Sleep(200);
                            continue;
                        }
                        Log($"State changed from {lastState} to {State}.");
                        OnStateChanged?.Invoke(State, lastState);

                        if (State == WebSocketState.Open)
                            OnOpened?.Invoke();

                        if ((State == WebSocketState.Closed || State == WebSocketState.Aborted) && !_reconnecting)
                        {
                            if (lastState == WebSocketState.Open && !_disconnectCalled && _reconnectStrategy != null &&
                                !_reconnectStrategy.AreAttemptsComplete())
                            {
                                Log("Reconnect needed.");
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
                    }
                }
                catch (Exception ex)
                {
                    Log($"Monitor threw exception: {ex.Message}.");
                    OnError?.Invoke(ex);
                }
                if (needsReconnect && !_reconnecting && !_disconnectCalled)
#pragma warning disable 4014
                    DoReconnect();
#pragma warning restore 4014
                _monitorRunning = false;
                Log("Exiting monitor.");
            });
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task DoReconnect()
        {
#pragma warning disable 4014
            Log("Entered reconnect.");
            Task.Run(async () =>
            {
                _tokenSource.Cancel();
                _reconnecting = true;
                if (!Task.WaitAll(new[] {_monitorTask, _listenerTask, _senderTask}, 15000))
                {
                    Log("Reconnect fatality, tasks failed to stop before the timeout.");
                    // exit everything as dead...
                    OnFatality?.Invoke("Fatal network error. Network services fail to shut down.");
                    _reconnecting = false;
                    _disconnectCalled = true;
                    _tokenSource.Cancel();
                    return;
                }
                Log("Disposing of current websocket.");
                _ws.Dispose();

                OnStateChanged?.Invoke(WebSocketState.Connecting, WebSocketState.Aborted);

                _tokenSource = new CancellationTokenSource();

                var connected = false;
                while (!_disconnectCalled && !_disposedValue && !connected && !_tokenSource.IsCancellationRequested)
                    try
                    {
                        Log("Creating new websocket.");
                        _ws = new ClientWebSocket();
                        if (!_monitorRunning)
                        {
                            Log("Starting monitor.");
                            StartMonitor();
                        }
                        Log("Attempting connect.");
                        connected = _ws.ConnectAsync(new Uri(Url), _tokenSource.Token).Wait(15000);
                        Log($"Connect result: {connected}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Reconnect threw an error: {ex.Message}.");
                        Log("Disposing of current websocket.");
                        _ws.Dispose();
                        Log("Processing reconnect strategy.");
                        Thread.Sleep(_reconnectStrategy.GetReconnectInterval());
                        _reconnectStrategy.ProcessValues();
                        if (_reconnectStrategy.AreAttemptsComplete())
                        {
                            Log("Reconnect strategy has reached max connection attempts, going to fatality.");
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
                    Log("Reconnect success, restarting tasks.");
                    _reconnecting = false;
                    if (!_monitorRunning)
                        StartMonitor();
                    if (!_listenerRunning)
                        StartListener();
                    if (!_senderRunning)
                        StartSender();
                }
                else
                {
                    Log("Reconnect failed.");
                }
            });
#pragma warning restore 4014
        }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        private void StartListener()
        {
            Log("Starting listener.");
            _listenerTask = Task.Run(async () =>
            {
                Log("Entering listener loop.");
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
                        catch ( Exception ex )
                        {
                            Log($"Receive threw an exception: {ex.Message}");
                            // Most likely socket error
                            _ws.Abort();
                            break;
                        }

                        if (res == null)
                            goto READ;

                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            Log("Server requested close.");
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "SERVER REQUESTED CLOSE", _tokenSource.Token);
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
                            {
                                Log($"Message fully received: {message}");
                                Task.Run(() => OnMessage?.Invoke(message)).Wait(50);
                            }
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
                            Log($"Binary fully received: {Encoding.UTF8.GetString(binary.ToArray())}");
                            Task.Run(() => OnData?.Invoke(binary.ToArray())).Wait(50);
                        }

                        // ReSharper disable once RedundantAssignment
                        buffer = null;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Listener threw exception: {ex.Message}.");
                    OnError?.Invoke(ex);
                }
                _listenerRunning = false;
                Log("Listener exiting");
                return Task.CompletedTask;
            });
        }

        private void StartSender()
        {
            Log("Starting sender.");
            _senderTask = Task.Run(async () =>
            {
                Log("Entering sender loop.");
                _senderRunning = true;
                try
                {
                    while (!_disposedValue && !_reconnecting)
                    {
                        if (_ws.State == WebSocketState.Open && !_reconnecting)
                        {
                            var msg = _sendQueue.Take(_tokenSource.Token);
                            if (msg.Key.Add(SendCacheItemTimeout) < DateTime.UtcNow)
                            {
                                Log($"Message expired skipping: {msg.Key} {msg.Value}.");
                                continue;
                            }
                            var buffer = Encoding.UTF8.GetBytes(msg.Value);
                            try
                            {
                                Log($"Sending message: {msg.Key} {msg.Value}.");
                                await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text,
                                    true, _tokenSource.Token);
                            }
                            catch (Exception ex)
                            {
                                Log($"Sender threw sending exception: {ex.Message}.");
                                // Most likely socket error
                                OnSendFailed?.Invoke(msg.Value, ex);
                                _ws.Abort();
                                break;
                            }
                        }
                        // limit to N ms per iteration
                        Thread.Sleep(SendDelay);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Sender threw exception: {ex.Message}.");
                    OnSendFailed?.Invoke("", ex);
                    OnError?.Invoke(ex);
                }
                _senderRunning = false;
                Log("Exiting sender.");
                return Task.CompletedTask;
            });
        }

        public void Disconnect()
        {
            try
            {
                Log("Disconnect called, closing websocket.");
                _disconnectCalled = true;
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "NORMAL SHUTDOWN", _tokenSource.Token).Wait(20000);
            }
            catch (Exception ex)
            {
                Log($"Disconnect threw exception: {ex.Message}.");
                // ignored
            }
        }

        #region IDisposable Support

        private bool _disposedValue; // To detect redundant calls

        protected virtual void Dispose(bool disposing, bool waitForSendsToComplete)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects).
                    if (_sendQueue.Count > 0 && _senderRunning)
                    {
                        var i = 0;
                        while (_sendQueue.Count > 0 && _senderRunning)
                        {
                            i++;
                            Task.Delay(1000).Wait();
                            if(i > 25)
                                break;
                        }
                    }
                    Disconnect();
                    _tokenSource.Cancel();
                    Thread.Sleep(500);
                    _tokenSource.Dispose();
                    _ws.Dispose();
                    GC.Collect();
                }

                _disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Log("Dispose called.");
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        public void Dispose(bool waitForSendsToComplete)
        {
            Log($"Dispose called, with waitForSendsToComplete = {waitForSendsToComplete}.");
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true, waitForSendsToComplete);
        }

        #endregion

        internal void Log(string message, [CallerMemberName] string memberName = "")
        {
            if (DebugMode)
                Console.WriteLine($"{DateTime.Now:O} PureWebSocket.{memberName}: {message}");
        }
    }
}