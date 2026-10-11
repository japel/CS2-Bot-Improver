using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace HostageDirector;

// Escort: while a CT bot carries a hostage, the nearest free CT bots are sent to it with
// native CCSBot::MoveTo (re-issued every tick of this timer, because the bot's own idle
// logic overwrites the goal). Bots that see or recently saw an enemy are left alone so
// they fight. (Radio follow-me was tried first: bots ignore it.)
public partial class HostageDirector
{
    private int _escortCount = 2;              // escorts per carrier; 0 = off
    private const float EscortMaxDist = 2500f; // only teammates reasonably close
    private const float EscortKeepDist = 200f; // close enough: stop ordering
    private int _escortOrders;

    private void StartEscort()
    {
        AddTimer(0.5f, UpdateEscort, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        RegisterListener<Listeners.OnMapStart>(_ =>
            AddTimer(0.5f, UpdateEscort, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE));
    }

    private void UpdateEscort()
    {
        if (_escortCount <= 0) return;
        var carriers = Utilities.FindAllEntitiesByDesignerName<CHostage>("hostage_entity")
            .Select(h => h.Leader.Value)
            .OfType<CBaseEntity>()
            .Where(e => e.IsValid && e.AbsOrigin != null)
            .ToList();
        if (carriers.Count == 0) return;

        float now = Server.CurrentTime;
        var free = Players()
            .Where(p => p.IsBot && p.PawnIsAlive && p.Team == CsTeam.CounterTerrorist)
            .Where(p => p.PlayerPawn.Value is { Bot: { } b, AbsOrigin: not null } pawn
                        && carriers.All(c => c.Index != pawn.Index)
                        && !b.IsEnemyVisible && now - b.LastSawEnemyTimestamp > 2f)
            .ToList();

        foreach (var carrier in carriers)
        {
            var at = carrier.AbsOrigin!;
            var picks = free
                .Select(p => (p, d: Dist(p.PlayerPawn.Value!.AbsOrigin!, at)))
                .Where(x => x.d < EscortMaxDist)
                .OrderBy(x => x.d)
                .Take(_escortCount)
                .ToList();
            foreach (var (p, d) in picks)
            {
                free.Remove(p);
                if (d > EscortKeepDist && TryMoveTo(p, new Vector(at.X, at.Y, at.Z), RouteType.Fastest))
                    _escortOrders++;
            }
        }
    }

    [ConsoleCommand("css_hd_escort", "Escorts per hostage carrier (0 = off; no arg: show)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnEscort(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount > 1 && int.TryParse(command.GetArg(1), out int n)) _escortCount = Math.Max(0, n);
        command.ReplyToCommand($"[hd] escort={_escortCount} orders={_escortOrders}");
    }

    [ConsoleCommand("css_hd_botcmd", "Run a client command as a bot: <bot name> <command...>")]
    [CommandHelper(minArgs: 2, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBotCmd(CCSPlayerController? caller, CommandInfo command)
    {
        var bot = Players().FirstOrDefault(p => p.IsBot && p.PlayerName == command.GetArg(1));
        if (bot == null) { command.ReplyToCommand("[hd] no such bot"); return; }
        string cmd = command.ArgString[command.GetArg(1).Length..].Trim();
        bot.ExecuteClientCommandFromServer(cmd);
        command.ReplyToCommand($"[hd] {bot.PlayerName} <- {cmd}");
    }
}
