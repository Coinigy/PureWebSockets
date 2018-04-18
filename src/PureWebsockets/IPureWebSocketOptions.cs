﻿/*
 * Author: ByronP
 * Date: 4/18/2018
 * Mod: 4/18/2018
 * Coinigy Inc. Coinigy.com
 */
using System;
using System.Collections.Generic;
using System.Net;

namespace PureWebSockets
{
    public interface IPureWebSocketOptions
    {
        /// <summary>
        /// Headers including cookies to include in the connection.
        /// Use with caution as some headers can cause issues/failures in the framework.
        /// </summary>
        IEnumerable<Tuple<string, string>> Headers { get; set; }

        /// <summary>
        /// A proxy instance to use if required.
        /// </summary>
        IWebProxy Proxy { get; set; }

        /// <summary>
        /// The maximum number of items that can be waiting to send (default 10000).
        /// </summary>
        int SendQueueLimit { get; set; }

        /// <summary>
        /// The amount of time an object can wait to be sent before it is considered dead (default 30 minutes).
        /// A dead item will be ignored and removed from the send queue when it is hit.
        /// </summary>
        TimeSpan SendCacheItemTimeout { get; set; }

        /// <summary>
        /// Minimum time between sending items from the queue in ms (default 80ms).
        /// Setting this to lower then 10ms is not recomended.
        /// </summary>
        ushort SendDelay { get; set; }

        /// <summary>
        /// Strategy that is used when the connection is lost. This allows you to automatically try to restore a lost connection without lose of data.
        /// </summary>
        ReconnectStrategy MyReconnectStrategy { get; set; }

        /// <summary>
        /// If set to true verbose messages will be sent to std out.
        /// </summary>
        bool DebugMode { get; set; }

        /// <summary>
        /// Amount time in ms to wait for a clean disconnect to complete (default 20000ms).
        /// </summary>
        int DisconnectWait { get; set; }
    }
}
