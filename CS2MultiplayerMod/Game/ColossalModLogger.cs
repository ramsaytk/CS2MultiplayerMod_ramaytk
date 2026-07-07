using System;
using Colossal.Logging;
using CS2MultiplayerMod.Core.Diagnostics;

namespace CS2MultiplayerMod.Game
{
    /// <summary>
    /// Adapts the core's game-agnostic <see cref="IModLogger"/> onto Colossal's
    /// <see cref="ILog"/>. This is the single seam where the portable core meets the
    /// game's logging; nothing under <c>Core/</c> references Colossal types.
    /// </summary>
    public sealed class ColossalModLogger : IModLogger
    {
        private readonly ILog _log;

        public ColossalModLogger(ILog log)
        {
            _log = log;
        }

        public void Debug(string message) => _log.Debug(message);
        public void Info(string message) => _log.Info(message);
        public void Warn(string message) => _log.Warn(message);
        public void Error(string message) => _log.Error(message);
        public void Error(string message, Exception exception) => _log.Error(message + " :: " + exception);
    }
}
