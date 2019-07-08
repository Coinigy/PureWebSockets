namespace PureWebSockets
{
    public class ReconnectStrategy
    {
        /*
         * The initial maximum number of attempts to make.
        */
        private readonly int? _initMaxAttempts;

        /*
         * The maximum number of milliseconds to delay a reconnection attempt.
         */

        private readonly int _maxReconnectInterval;

        /*
         * The number of milliseconds to add to each reconnect attempt.
        */
        private readonly int _reconnectStepInterval;

        private int _attemptsMade;

        /*
        * The maximum number of reconnection attempts that will be made before giving up. If null, reconnection attempts will be continue to be made forever.
        */

        private int? _maxAttempts;

        /*
        * The number of milliseconds to delay before attempting to reconnect.
        */

        private int _minReconnectInterval;

        public ReconnectStrategy()
        {
            _reconnectStepInterval = 3000;
            _minReconnectInterval = 3000;
            _maxReconnectInterval = 30000;
            _maxAttempts = null; //forever
            _initMaxAttempts = null;
            _attemptsMade = 0;
        }

        public ReconnectStrategy(int minReconnectInterval, int maxReconnectInterval, int? maxAttempts)
        {
            _reconnectStepInterval = minReconnectInterval;
            _minReconnectInterval = minReconnectInterval > maxReconnectInterval
                ? maxReconnectInterval
                : minReconnectInterval;
            _maxReconnectInterval = maxReconnectInterval;
            _maxAttempts = maxAttempts;
            _initMaxAttempts = maxAttempts;
            _attemptsMade = 0;
        }

        public ReconnectStrategy(int minReconnectInterval, int maxReconnectInterval)
        {
            _reconnectStepInterval = minReconnectInterval;
            _minReconnectInterval = minReconnectInterval > maxReconnectInterval
                ? maxReconnectInterval
                : minReconnectInterval;
            _maxReconnectInterval = maxReconnectInterval;
            _maxAttempts = null;
            _initMaxAttempts = null;
            _attemptsMade = 0;
        }

        public ReconnectStrategy(int reconnectInterval)
        {
            _reconnectStepInterval = reconnectInterval;
            _minReconnectInterval = reconnectInterval;
            _maxReconnectInterval = reconnectInterval;
            _maxAttempts = null;
            _initMaxAttempts = null;
            _attemptsMade = 0;
        }

        public ReconnectStrategy(int reconnectInterval, int? maxAttempts)
        {
            _reconnectStepInterval = reconnectInterval;
            _minReconnectInterval = reconnectInterval;
            _maxReconnectInterval = reconnectInterval;
            _maxAttempts = maxAttempts;
            _initMaxAttempts = maxAttempts;
            _attemptsMade = 0;
        }

        public void SetMaxAttempts(int attempts)
        {
            _maxAttempts = attempts;
        }

        public void Reset()
        {
            _attemptsMade = 0;
            _minReconnectInterval = _reconnectStepInterval;
            _maxAttempts = _initMaxAttempts;
        }

        public void SetAttemptsMade(int count)
        {
            _attemptsMade = count;
        }

        internal void ProcessValues()
        {
            _attemptsMade++;
            if (_minReconnectInterval < _maxReconnectInterval) _minReconnectInterval += _reconnectStepInterval;

            if (_minReconnectInterval > _maxReconnectInterval) _minReconnectInterval = _maxReconnectInterval;
        }

        public int GetReconnectInterval()
        {
            return _minReconnectInterval;
        }

        public bool AreAttemptsComplete()
        {
            return _attemptsMade == _maxAttempts;
        }
    }
}