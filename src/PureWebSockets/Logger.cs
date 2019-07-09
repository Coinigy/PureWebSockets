/*
 * Author: ByronP
 * Date: 07/08/2019
 * Mod: 07/08/2019
 * Coinigy Inc. Coinigy.com
 */

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace PureWebSockets
{
    internal class Logger
    {
        private readonly PureWebSocketOptions _options;

        public Logger(PureWebSocketOptions options)
        {
            _options = options;
        }

        internal void Log(string message, [CallerMemberName] string memberName = "")
        {
            if (_options.DebugMode)
            {
                Task.Run(() => _options.DebugOutput.WriteLine($"{DateTime.Now:O} PureWebSocket.{memberName}: {message}"));
            }
        }

        internal Task LogAsync(string message, [CallerMemberName] string memberName = "")
        {
            if (_options.DebugMode)
            {
                return _options.DebugOutput.WriteLineAsync($"{DateTime.Now:O} PureWebSocket.{memberName}: {message}");
            }

            return Task.CompletedTask;
        }

        internal void LogData(string message, byte[] data, [CallerMemberName] string memberName = "")
        {
            if (_options.DebugMode)
            {
                Task.Run(() =>
                    _options.DebugOutput.WriteLine(
                        $"{DateTime.Now:O} PureWebSocket.{memberName}: {message}, data: {BitConverter.ToString(data)}"));
            }
        }

        internal Task LogDataAsync(string message, byte[] data, [CallerMemberName] string memberName = "")
        {
            if (_options.DebugMode)
            {
                return _options.DebugOutput.WriteLineAsync(
                    $"{DateTime.Now:O} PureWebSocket.{memberName}: {message}, data: {BitConverter.ToString(data)}");
            }

            return Task.CompletedTask;
        }
    }
}
