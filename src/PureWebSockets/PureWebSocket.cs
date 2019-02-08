/*
 * Author: ByronP
 * Date: 1/14/2017
 * Mod: 01/30/2019
 * Coinigy Inc. Coinigy.com
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
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
        private readonly PureWebSocketOptions _options;
        private readonly BlockingCollection<KeyValuePair<DateTime, string>> _sendQueue = new BlockingCollection<KeyValuePair<DateTime, string>>();
        private bool _disconnectCalled;
        private bool _listenerRunning;
        private bool _senderRunning;
        private bool _monitorRunning;
        private bool _reconnecting;
        private bool _reconnectNeeded;
        private CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private Task _monitorTask;
        private Task _listenerTask;
        private Task _senderTask;

        /// <summary>
        /// The current state of the connection.
        /// </summary>
        public WebSocketState State => _ws.State;
        /// <summary>
        /// The current number of items waiting to be sent.
        /// </summary>
        public int SendQueueLength => _sendQueue.Count;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public TimeSpan DefaultKeepAliveInterval
        {
            get => _ws.Options.KeepAliveInterval;
            set => _ws.Options.KeepAliveInterval = value;
        }

        public event Data OnData;
        public event Message OnMessage;
        public event StateChanged OnStateChanged;
        public event Opened OnOpened;
        public event Closed OnClosed;
        public event Error OnError;
        public event SendFailed OnSendFailed;
        public event Fatality OnFatality;

        public PureWebSocket(string url, IPureWebSocketOptions options)
        {
            _options = (PureWebSocketOptions)options;
            Url = url;

            Log("Creating new instance.");

            InitializeClient();

            StartMonitor();
        }

        private void InitializeClient()
        {
            _ws = new ClientWebSocket();

            try
            {
                if (_options.IgnoreCertErrors)
                {
                    //NOTE: this will not work and a workaround will be available in netstandard 2.1
                    ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, errors) => true;
                }
            }
            catch (Exception ex)
            {
                Log($"Setting invalid certificate options threw an exception: {ex.Message}. Defaulting IgnoreCertErrors to false.");
                _options.IgnoreCertErrors = false;
            }

            if(_options.Cookies != null && _options.Cookies.Count > 0)
                _ws.Options.Cookies = _options.Cookies;

            if(_options.ClientCertificates != null && _options.ClientCertificates.Count > 0)
                _ws.Options.ClientCertificates = _options.ClientCertificates;

            if (_options.Proxy != null)
                _ws.Options.Proxy = _options.Proxy;

            if (_options.SubProtocols != null)
            {
                foreach (var protocol in _options.SubProtocols)
                {
                    try
                    {
                        _ws.Options.AddSubProtocol(protocol);
                    }
                    catch (Exception ex)
                    {
                        Log("Invalid or unsupported sub protocol, value: " + protocol + ", exception: " + ex.Message, nameof(_options.SubProtocols));
                    }
                }
            }

            // optionally add request header e.g. X-Key, testapikey123
            if (_options.Headers != null)
                foreach (var h in _options.Headers)
                {
                    try
                    {
                        _ws.Options.SetRequestHeader(h.Item1, h.Item2);
                    }
                    catch (Exception ex)
                    {
                        Log("Invalid or unsupported header, value: " + h + ", exception: " + ex.Message, nameof(_options.Headers));
                    }
                }
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

                Task.Run(async () =>
                {
                    while (_ws.State != WebSocketState.Open)
                    {
                        await Task.Delay(1);
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

        public async Task<bool> ConnectAsync()
        {
            Log("Connect called.");
            try
            {
                _disconnectCalled = false;
                await _ws.ConnectAsync(new Uri(Url), _tokenSource.Token);
                Log("Starting tasks.");
                StartListener();
                StartSender();

                await Task.Run(async () =>
                {
                    var st = DateTime.UtcNow;

                    while (_ws.State != WebSocketState.Open && (DateTime.UtcNow - st).TotalSeconds < 16)
                    {
                        await Task.Delay(1);
                    }
                });

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
                if ((State != WebSocketState.Open && !_reconnecting) || SendQueueLength >= _options.SendQueueLimit || _disconnectCalled)
                {
                    Log(SendQueueLength >= _options.SendQueueLimit ? $"Could not add item to send queue: queue limit reached, Data {data}" : $"Could not add item to send queue: State {State}, Queue Count {SendQueueLength}, Data {data}");
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

        public async Task<bool> SendAsync(string data)
        {
            try
            {
                if ((State != WebSocketState.Open && !_reconnecting) || SendQueueLength >= _options.SendQueueLimit || _disconnectCalled)
                {
                    Log(SendQueueLength >= _options.SendQueueLimit ? $"Could not add item to send queue: queue limit reached, Data {data}" : $"Could not add item to send queue: State {State}, Queue Count {SendQueueLength}, Data {data}");
                    return false;
                }

                await Task.Run(() =>
                {
                    Log($"Adding item to send queue: Data {data}");
                    _sendQueue.Add(new KeyValuePair<DateTime, string>(DateTime.UtcNow, data));
                });

                return true;
            }
            catch (Exception ex)
            {
                Log($"Send threw exception: {ex.Message}.");
                OnError?.Invoke(ex);
                throw;
            }
        }

        public bool Send(byte[] data, EncodingTypes encodingType = EncodingTypes.UTF8)
        {
            switch (encodingType)
            {
                case EncodingTypes.UTF7:
                    return Send(Encoding.UTF7.GetString(data));
                case EncodingTypes.UTF8:
                    return Send(Encoding.UTF8.GetString(data));
                case EncodingTypes.UTF32:
                    return Send(Encoding.UTF32.GetString(data));
                case EncodingTypes.ASCII:
                    return Send(Encoding.ASCII.GetString(data));
                case EncodingTypes.Unicode:
                    return Send(Encoding.Unicode.GetString(data));
                case EncodingTypes.BigEndianUnicode:
                    return Send(Encoding.BigEndianUnicode.GetString(data));
                case EncodingTypes.Default:
                    return Send(Encoding.Default.GetString(data));
                default:
                    return Send(Encoding.Default.GetString(data));
            }
        }

        public Task<bool> SendAsync(byte[] data, EncodingTypes encodingType = EncodingTypes.UTF8)
        {
            switch (encodingType)
            {
                case EncodingTypes.UTF7:
                    return SendAsync(Encoding.UTF7.GetString(data));
                case EncodingTypes.UTF8:
                    return SendAsync(Encoding.UTF8.GetString(data));
                case EncodingTypes.UTF32:
                    return SendAsync(Encoding.UTF32.GetString(data));
                case EncodingTypes.ASCII:
                    return SendAsync(Encoding.ASCII.GetString(data));
                case EncodingTypes.Unicode:
                    return SendAsync(Encoding.Unicode.GetString(data));
                case EncodingTypes.BigEndianUnicode:
                    return SendAsync(Encoding.BigEndianUnicode.GetString(data));
                case EncodingTypes.Default:
                    return SendAsync(Encoding.Default.GetString(data));
                default:
                    return SendAsync(Encoding.Default.GetString(data));
            }
        }

        private void StartMonitor()
        {
            Log("Starting monitor.");
            _monitorTask = Task.Run(async () =>
            {
                Log("Entering monitor loop.");
                _monitorRunning = true;
                _reconnectNeeded = false;
                try
                {
                    var lastState = State;
                    while (_ws != null && !_disposedValue)
                    {
                        if (lastState == State)
                        {
                            await Task.Delay(200);
                            continue;
                        }
                        if (_reconnecting)
                        {
                            // if we are reconnecting don't be so quick to fire off a state change
                            if (_options.MyReconnectStrategy != null)
                            {
                                await Task.Delay(_options.MyReconnectStrategy.GetReconnectInterval() + 1000);
                                if (_reconnecting)
                                {
                                    await Task.Delay(_options.MyReconnectStrategy.GetReconnectInterval());
                                    // this gives us a max of 10 seconds to do a reconnect
                                    if (!_reconnecting)
                                        return;
                                }
                                else
                                {
                                    return;
                                }
                            }
                        }
                        // don't fire if we just came off of an abort (reconnect)
                        if (lastState == WebSocketState.Aborted && (State == WebSocketState.Connecting || State == WebSocketState.Open))
                            break;
                        if (_reconnectNeeded && State == WebSocketState.Aborted)
                            break;

                        // check again since this can change before the first check
                        if (lastState == State)
                        {
                            await Task.Delay(200);
                            continue;
                        }

                        Log($"State changed from {lastState} to {State}.");
                        OnStateChanged?.Invoke(State, lastState);

                        if (State == WebSocketState.Open)
                            OnOpened?.Invoke();

                        if ((State == WebSocketState.Closed || State == WebSocketState.Aborted) && !_reconnecting)
                        {
                            if (lastState == WebSocketState.Open && !_disconnectCalled && _options.MyReconnectStrategy != null && !_options.MyReconnectStrategy.AreAttemptsComplete())
                            {
                                Log("Reconnect needed.");
                                // go through the reconnect strategy
                                // Exit the loop and start async reconnect
                                _reconnectNeeded = true;
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
                _monitorRunning = false;
                Log("Exiting monitor.");
                if (_reconnectNeeded && !_reconnecting && !_disconnectCalled)
                    DoReconnect();                
            });
        }

        private void DoReconnect()
        {
            Log("Entered reconnect.");
            _ = Task.Run(async () =>
             {
                 _tokenSource.Cancel();
                 _reconnecting = true;

                 if (!Task.WaitAll(new[] { _monitorTask, _listenerTask, _senderTask }, 15000))
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
                         InitializeClient();
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
                         await Task.Delay(_options.MyReconnectStrategy.GetReconnectInterval());
                         _options.MyReconnectStrategy.ProcessValues();
                         if (_options.MyReconnectStrategy.AreAttemptsComplete())
                         {
                             Log("Reconnect strategy has reached max connection attempts, going to fatality.");
                             // exit everything as dead...
                             OnFatality?.Invoke("Fatal network error. Max reconnect attempts reached.");
                             _reconnectNeeded = false;
                             _reconnecting = false;
                             _disconnectCalled = true;
                             _tokenSource.Cancel();
                             return;
                         }
                     }
                 if (connected)
                 {
                     Log("Reconnect success, restarting tasks.");
                     _reconnectNeeded = false;
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
        }

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
                        WebSocketReceiveResult res;

                        try
                        {
                            res = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _tokenSource.Token);
                        }
                        catch (Exception ex)
                        {
                            Log($"Receive threw an exception: {ex.Message}");
                            // Most likely socket error
                            _reconnectNeeded = true;
                            _ws.Abort();
                            break;
                        }

                        if (res == null)
                            goto READ;

                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            Log("Server requested close.");
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "SERVER REQUESTED CLOSE", _tokenSource.Token);
                            _disconnectCalled = true;
                            return Task.CompletedTask;
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
                                _ = Send("pong");
                            else
                            {
                                Log($"Message fully received: {message}");
                                Task.Run(() => OnMessage?.Invoke(message)).Wait(50);
                            }
                        }
                        else
                        {
                            var exactDataBuffer = new byte[res.Count];
                            Array.Copy(buffer, 0, exactDataBuffer, 0, res.Count);
                            // handle binary data
                            if (!res.EndOfMessage)
                            {
                                binary.AddRange(exactDataBuffer);
                                goto READ;
                            }

                            binary.AddRange(exactDataBuffer);
                            var binaryData = binary.ToArray();
                            LogData("Binary fully received", binaryData);
                            Task.Run(() => OnData?.Invoke(binaryData)).Wait(50);
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
                            if (msg.Key.Add(_options.SendCacheItemTimeout) < DateTime.UtcNow)
                            {
                                Log($"Message expired skipping: {msg.Key} {msg.Value}.");
                                continue;
                            }
                            var buffer = Encoding.UTF8.GetBytes(msg.Value);
                            try
                            {
                                Log($"Sending message: {msg.Key} {msg.Value}.");
                                await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _tokenSource.Token).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Log($"Sender threw sending exception: {ex.Message}.");
                                // Most likely socket error
                                OnSendFailed?.Invoke(msg.Value, ex);
                                _reconnectNeeded = true;
                                _ws.Abort();
                                break;
                            }
                        }
                        // limit to N ms per iteration
                        Thread.Sleep(_options.SendDelay);
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
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "NORMAL SHUTDOWN", _tokenSource.Token).Wait(_options.DisconnectWait);
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
                            if (i > 25)
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
            if (_options.DebugMode)
                Task.Run(() => Console.WriteLine($"{DateTime.Now:O} PureWebSocket.{memberName}: {message}"));
        }
        internal void LogData(string message, byte[] data, [CallerMemberName] string memberName = "")
        {
            if (_options.DebugMode)
                Task.Run(() => _options.DebugOutput.WriteLine($"{DateTime.Now:O} PureWebSocket.{memberName}: {message}, data: {BitConverter.ToString(data)}"));
        }
    }
}