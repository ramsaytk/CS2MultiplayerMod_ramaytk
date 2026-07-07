using System;

namespace CS2MultiplayerMod.Core.Diagnostics
{
    /// <summary>A logger that discards everything. Useful as a default and in tests.</summary>
    public sealed class NullModLogger : IModLogger
    {
        public static readonly NullModLogger Instance = new NullModLogger();

        private NullModLogger() { }

        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception exception) { }
    }
}
