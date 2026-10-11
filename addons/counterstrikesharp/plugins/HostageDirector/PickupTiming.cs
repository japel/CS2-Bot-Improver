using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace HostageDirector;

// Pickup timing: how long a CT holds use next to a free hostage before hostage_follows,
// split by whether it carries a defuse/rescue kit. Answers "is the kit worth $400 here?".
public partial class HostageDirector
{
    private readonly Dictionary<int, float> _useSince = [];          // slot -> first tick holding use near a hostage
    private readonly List<(bool kit, float secs)> _pickups = [];

    private void StartPickupTiming()
    {
        AddTimer(0.05f, UpdatePickupTiming, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _useSince.Clear();
            AddTimer(0.05f, UpdatePickupTiming, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventHostageFollows>((ev, _) =>
        {
            var p = ev.Userid;
            if (p == null || !_useSince.Remove(p.Slot, out float since)) return HookResult.Continue;
            bool kit = p.PlayerPawn.Value?.ItemServices is { Handle: not 0 } s && new CCSPlayer_ItemServices(s.Handle).HasDefuser;
            float secs = Server.CurrentTime - since;
            _pickups.Add((kit, secs));
            Console.WriteLine($"[hd] pickup {p.PlayerName} kit={kit} {secs:F2}s");
            return HookResult.Continue;
        });
    }

    private void UpdatePickupTiming()
    {
        var free = Utilities.FindAllEntitiesByDesignerName<CHostage>("hostage_entity")
            .Where(h => h.IsValid && h.Leader.Value == null && h.AbsOrigin != null)
            .Select(h => h.AbsOrigin!)
            .ToList();
        float now = Server.CurrentTime;
        foreach (var p in Players())
        {
            bool using_ = free.Count > 0 && p.PawnIsAlive && p.Team == CsTeam.CounterTerrorist
                && p.PlayerPawn.Value?.AbsOrigin is { } pos && free.Any(h => Dist(h, pos) < NearHostage)
                && (p.Buttons & PlayerButtons.Use) != 0;
            if (!using_) _useSince.Remove(p.Slot);
            else _useSince.TryAdd(p.Slot, now);
        }
    }

    [ConsoleCommand("css_hd_pickups", "Hostage pickup durations with / without kit (reset: css_hd_pickups reset)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnPickups(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.GetArg(1) == "reset") _pickups.Clear();
        foreach (bool kit in new[] { true, false })
        {
            var s = _pickups.Where(x => x.kit == kit).Select(x => x.secs).OrderBy(x => x).ToList();
            command.ReplyToCommand($"[hd] pickups kit={kit} n={s.Count}" +
                (s.Count > 0 ? $" median={s[s.Count / 2]:F2}s min={s[0]:F2}s max={s[^1]:F2}s" : ""));
        }
    }
}
