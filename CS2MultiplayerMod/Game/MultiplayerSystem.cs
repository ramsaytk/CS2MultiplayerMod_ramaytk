using Game;
using CS2MultiplayerMod.Core.Session;

namespace CS2MultiplayerMod.Game
{
    /// <summary>
    /// The ECS heartbeat for multiplayer. Registered into
    /// <see cref="global::Game.SystemUpdatePhase.UIUpdate"/> so it runs every frame in
    /// every state — main menu, paused, and during play. (GameSimulation would stop
    /// ticking in the menu and while paused, freezing connection handling exactly when
    /// the player is in the options screen pressing Host/Join.) <see cref="OnUpdate"/>
    /// pumps the <see cref="MultiplayerService"/> — draining received messages and
    /// flushing keep-alives — on the main thread, which is what makes it safe for
    /// session observers to touch ECS data.
    ///
    /// Also enforces the settings screen's master switch: turning "Enable Mod" off
    /// tears down any active session, so the toggle genuinely disables multiplayer
    /// rather than just hiding UI.
    ///
    /// Declared <c>partial</c> because Unity's Entities source generators extend
    /// system types.
    /// </summary>
    public partial class MultiplayerSystem : GameSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.log.Info(nameof(MultiplayerSystem) + " created.");
        }

        protected override void OnUpdate()
        {
            MultiplayerService service = Mod.Service;
            if (service == null) return;

            if (!MultiplayerService.ModEnabled)
            {
                if (service.Session.Role != SessionRole.None)
                {
                    Mod.log.Info("[MP] Mod disabled in settings — closing the active session.");
                    service.Disconnect();
                }
                return;
            }

            service.Update();
        }
    }
}
