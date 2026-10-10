using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Runtime.CompilerServices;

namespace HostageDirector;

// "Covered pickup": a CT bot next to a free hostage may only press use when it hasn't seen
// an enemy for a moment. Benchmarks showed carriers dying ~1 s after pickup to T bots
// camping the hostages; this makes the bot fight first and pick up once the area is clear.
public partial class HostageDirector
{
    private const float NearHostage = 150f;        // units; use stays free elsewhere (doors)
    private const float EnemyMemorySecs = 2f;
    private const ulong InUse = (ulong)PlayerButtons.Use;

    private bool _coverEnabled = true;
    private object? _botController;
    private readonly Dictionary<int, long> _suppressed = [];   // slot -> suppression id
    private int _suppressStarts;

    // Keeps BotControllerApi types out of the plugin type so the plugin loads without it.
    private static class Bridge
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object? TryGet() =>
            new PluginCapability<BotControllerApi.IBotControllerApi>("botcontroller:api").Get();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static long Suppress(object api, int slot, ulong mask) =>
            ((BotControllerApi.IBotControllerApi)api).StartUsercmdSuppression(slot, mask);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool Cancel(object api, int slot, long id) =>
            ((BotControllerApi.IBotControllerApi)api).CancelUsercmdSuppression(slot, id);
    }

    private void StartCoveredPickup()
    {
        AddTimer(0.1f, UpdateCoveredPickup, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _suppressed.Clear();
            AddTimer(0.1f, UpdateCoveredPickup, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
    }

    private void UpdateCoveredPickup()
    {
        try { _botController ??= Bridge.TryGet(); }
        catch { _botController = null; }
        if (_botController == null) return;

        var freeHostages = Utilities.FindAllEntitiesByDesignerName<CHostage>("hostage_entity")
            .Where(h => h.IsValid && h.Leader.Value == null && h.AbsOrigin != null)
            .Select(h => h.AbsOrigin!)
            .ToList();
        float now = Server.CurrentTime;

        foreach (var player in Players())
        {
            if (!player.IsBot) continue;
            int slot = player.Slot;
            bool want = false;

            if (_coverEnabled && freeHostages.Count > 0 && player.PawnIsAlive
                && player.Team == CsTeam.CounterTerrorist
                && player.PlayerPawn.Value is { Bot: { } bot, AbsOrigin: { } pos })
            {
                bool nearHostage = freeHostages.Any(h => Dist(h, pos) < NearHostage);
                bool threat = bot.IsEnemyVisible || now - bot.LastSawEnemyTimestamp < EnemyMemorySecs;
                want = nearHostage && threat;
            }

            bool has = _suppressed.TryGetValue(slot, out long id);
            if (want && !has)
            {
                long newId = Bridge.Suppress(_botController, slot, InUse);
                if (newId > 0) { _suppressed[slot] = newId; _suppressStarts++; }
            }
            else if (!want && has)
            {
                Bridge.Cancel(_botController, slot, id);
                _suppressed.Remove(slot);
            }
        }
    }

    private static float Dist(Vector a, Vector b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    [ConsoleCommand("css_hd_cover", "Covered hostage pickup for CT bots: on|off (no arg: show)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnCover(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount > 1) _coverEnabled = command.GetArg(1) == "on";
        command.ReplyToCommand(
            $"[hd] cover={(_coverEnabled ? "on" : "off")} botcontroller={(_botController != null ? "ok" : "missing")} " +
            $"active={_suppressed.Count} starts={_suppressStarts}");
    }
}
