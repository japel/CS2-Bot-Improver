using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace HostageDirector;

// Phase B: helps CT bots on hostage maps (covered pickup) + diagnostics for scripted tests.
public partial class HostageDirector : BasePlugin
{
    public override string ModuleName => "HostageDirector";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "japel";

    public override void Load(bool hotReload)
    {
        StartCoveredPickup();
        StartEscort();
    }

    // Utilities.GetPlayers() only scans slots < Server.MaxPlayers, which is 1 on a listen
    // server started with `map` from the console, so it misses every bot. Scan controllers instead.
    internal static List<CCSPlayerController> Players() =>
        Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller")
            .Where(p => p.IsValid && !p.IsHLTV && p.Connected == PlayerConnectedState.Connected)
            .ToList();

    // CCSBot layout (Linux, libserver c7741272): m_moveToState at +0x2D8, its goal at +0x2E0,
    // current state pointer m_state at +0x5B8 (compared against &m_idleState=+0x400 in MoveTo).
    private static string MoveToGoal(nint bot)
    {
        if (bot == nint.Zero) return "-";
        float x = BitConverter.Int32BitsToSingle(System.Runtime.InteropServices.Marshal.ReadInt32(bot + 0x2E0));
        float y = BitConverter.Int32BitsToSingle(System.Runtime.InteropServices.Marshal.ReadInt32(bot + 0x2E4));
        return $"{x:F0},{y:F0}";
    }

    private static string StateName(nint bot)
    {
        if (bot == nint.Zero) return "-";
        nint st = System.Runtime.InteropServices.Marshal.ReadIntPtr(bot + 0x5B8);
        long off = (long)st - (long)bot;
        return off is >= 0 and < 0x2000 ? $"+0x{off:X}" : "ext";
    }

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

        var all = Players();
        command.ReplyToCommand(
            $"{p} players={all.Count} bots={all.Count(pl => pl.IsBot)} alive={all.Count(pl => pl.PawnIsAlive)} " +
            $"pawns={all.Count(pl => pl.PlayerPawn.Value != null)} botsvc={all.Count(pl => pl.PlayerPawn.Value?.Bot != null)}");

        foreach (var player in all.Where(pl => pl.IsBot && pl.PawnIsAlive))
        {
            var pawn = player.PlayerPawn.Value;
            var bot = pawn?.Bot;
            if (pawn == null || bot == null) continue;
            command.ReplyToCommand(
                $"{p} {player.Team,-17} {player.PlayerName,-16} pos={Fmt(pawn.AbsOrigin)} goal={Fmt(bot.GoalPosition)} " +
                $"goalEnt={bot.GoalEntity.Value?.DesignerName ?? "-"} taskEnt={bot.TaskEntity.Value?.DesignerName ?? "-"} " +
                $"path={bot.PathIndex} escort={bot.HostageEscortCount} waitHostage={bot.IsWaitingForHostage} " +
                $"following={bot.IsFollowing} leader={bot.Leader.Value?.Index} " +
                $"mt={MoveToGoal(bot.Handle)} state={StateName(bot.Handle)}");
        }
    }
}
