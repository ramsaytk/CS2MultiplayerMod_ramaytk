namespace CS2MultiplayerMod.Core.Diagnostics
{
    /// <summary>
    /// Logging abstraction for the multiplayer core.
    ///
    /// The core deliberately does not reference Colossal.Logging (or any game
    /// assembly) so it stays portable and unit-testable. The game layer supplies
    /// a concrete adapter; tests can pass <see cref="NullModLogger"/>.
    /// </summary>
    public interface IModLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Error(string message, System.Exception exception);
    }
}
