using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace HostageDirector;

// Phase B: directs CT bots to hostages on hostage maps.
// Step 1 (spike): diagnostics to find a way to send a bot somewhere.
public class HostageDirector : BasePlugin
{
    public override string ModuleName => "HostageDirector";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "japel";

    private static string Fmt(Vector? v) => v == null ? "-" : $"{v.X:F0} {v.Y:F0} {v.Z:F0}";

    // netcon replays recent console history to every client, and `echo` output doesn't
    // reach it, so tools wrap queries in css_hd_mark lines and pass a tag to css_hd_dump.
    [ConsoleCommand("css_hd_mark", "Print a marker line (for scripted netcon queries)")]
    [CommandHelper(minArgs: 1, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnMark(CCSPlayerController? caller, CommandInfo command) =>
        command.ReplyToCommand($"[hd-mark] {command.ArgString}");

    [ConsoleCommand("css_hd_dump", "List bots (pos, goal, task, hostage state) and hostages. Optional tag.")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnDump(CCSPlayerController? caller, CommandInfo command)
    {
        string p = command.ArgCount > 1 ? $"[hd:{command.GetArg(1)}]" : "[hd]";
        foreach (var h in Utilities.FindAllEntitiesByDesignerName<CHostage>("hostage_entity"))
            command.ReplyToCommand($"{p} hostage #{h.Index} pos={Fmt(h.AbsOrigin)} leader={h.Leader.Value?.Index}");

        foreach (var player in Utilities.GetPlayers().Where(pl => pl.IsBot && pl.PawnIsAlive))
        {
            var pawn = player.PlayerPawn.Value;
            var bot = pawn?.Bot;
            if (pawn == null || bot == null) continue;
            command.ReplyToCommand(
                $"{p} {player.Team,-17} {player.PlayerName,-16} pos={Fmt(pawn.AbsOrigin)} goal={Fmt(bot.GoalPosition)} " +
                $"goalEnt={bot.GoalEntity.Value?.DesignerName ?? "-"} taskEnt={bot.TaskEntity.Value?.DesignerName ?? "-"} " +
                $"path={bot.PathIndex} escort={bot.HostageEscortCount} waitHostage={bot.IsWaitingForHostage}");
        }
    }
}
