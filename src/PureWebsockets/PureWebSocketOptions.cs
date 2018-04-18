/*
 * Author: ByronP
 * Date: 4/17/2018
 * Mod: 4/17/2018
 * Coinigy Inc. Coinigy.com
 */
using System;
using System.Collections.Generic;
using System.Net;

namespace PureWebSockets
{
    public class PureWebSocketOptions
    {
        /// <summary>
        /// Headers including cookies to include in the connection.
        /// Use with caution as some headers can cause issues/failures in the framework.
        /// </summary>
        public IEnumerable<Tuple<string, string>> Headers { get; set; }
        
        /// <summary>
        /// A proxy instance to use if required.
        /// </summary>
        public IWebProxy Proxy { get; set; }
        
        /// <summary>
        /// The maximum number of items that can be waiting to send (default 10000).
        /// </summary>
        public int SendQueueLimit { get; set; }

        /// <summary>
        /// The amount of time an object can wait to be sent before it is considered dead (default 30 minutes).
        /// A dead item will be ignored and removed from the send queue when it is hit.
        /// </summary>
        public TimeSpan SendCacheItemTimeout { get; set; }

        /// <summary>
        /// Minimum time between sending items from the queue in ms (default 80ms).
        /// Setting this to lower then 10ms is not recomended.
        /// </summary>
        public ushort SendDelay { get; set; }

        /// <summary>
        /// Strategy that is used when the connection is lost. This allows you to automatically try to restore a lost connection without lose of data.
        /// </summary>
        public ReconnectStrategy MyReconnectStrategy { get; set; }

        /// <summary>
        /// If set to true verbose messages will be sent to std out.
        /// </summary>
        public bool DebugMode { get; set; }

        /// <summary>
        /// Amount time in ms to wait for a clean disconnect to complete (default 20000ms).
        /// </summary>
        public int DisconnectWait { get; set; }

        public PureWebSocketOptions()
        {
            SendQueueLimit = 10000;
            SendCacheItemTimeout = TimeSpan.FromMinutes(30);
            SendDelay = 80;
            DisconnectWait = 20000;
        }
    }
}
