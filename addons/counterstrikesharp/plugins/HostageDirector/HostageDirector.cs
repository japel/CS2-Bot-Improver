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

    [ConsoleCommand("css_hd_dump", "List bots (pos, goal, task, hostage state) and hostages")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnDump(CCSPlayerController? caller, CommandInfo command)
    {
        foreach (var h in Utilities.FindAllEntitiesByDesignerName<CHostage>("hostage_entity"))
            command.ReplyToCommand($"[hd] hostage #{h.Index} pos={Fmt(h.AbsOrigin)} leader={h.Leader.Value?.Index}");

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsBot && p.PawnIsAlive))
        {
            var pawn = p.PlayerPawn.Value;
            var bot = pawn?.Bot;
            if (pawn == null || bot == null) continue;
            command.ReplyToCommand(
                $"[hd] {p.Team,-17} {p.PlayerName,-16} pos={Fmt(pawn.AbsOrigin)} goal={Fmt(bot.GoalPosition)} " +
                $"goalEnt={bot.GoalEntity.Value?.DesignerName ?? "-"} taskEnt={bot.TaskEntity.Value?.DesignerName ?? "-"} " +
                $"path={bot.PathIndex} escort={bot.HostageEscortCount} waitHostage={bot.IsWaitingForHostage}");
        }
    }
}
