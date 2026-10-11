using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;

namespace HostageDirector;

// Native CCSBot::MoveTo(const Vector& goal, RouteType route): the legacy bot's own
// "go there" order (pathfinding, fighting on the way, back to idle on arrival).
// Found via the IdleState hostage branch, which calls it right before
// PrintIfWatched("I'm collecting hostages"). Linux libserver c7741272...: VA 0xc1c150.
//   mov rax,[rsi]; mov [rdi+2E0h],rax; mov eax,[rsi+8]; lea rsi,[rdi+2D8h] ...
public partial class HostageDirector
{
    private const string MoveToSig = "48 8B 06 48 89 87 E0 02 00 00 8B 46 08 48 8D B7 D8 02 00 00";

    public enum RouteType { Default = 0, Fastest = 1, Safest = 2, Retreat = 3 }

    private MemoryFunctionVoid<nint, nint, int>? _moveTo;
    private bool _moveToResolved;

    private bool TryMoveTo(CCSPlayerController bot, Vector goal, RouteType route)
    {
        if (!_moveToResolved)
        {
            _moveToResolved = true;
            try { _moveTo = new MemoryFunctionVoid<nint, nint, int>(MoveToSig); }
            catch (Exception ex) { Console.WriteLine($"[hd] CCSBot::MoveTo signature not found: {ex.Message}"); }
        }
        var csbot = bot.PlayerPawn.Value?.Bot;
        if (_moveTo == null || csbot == null || csbot.Handle == nint.Zero || !bot.PawnIsAlive) return false;
        _moveTo.Invoke(csbot.Handle, goal.Handle, (int)route);
        return true;
    }

    [ConsoleCommand("css_hd_moveto", "Send a bot somewhere: <bot name> <x> <y> <z> [fastest|safest]")]
    [CommandHelper(minArgs: 4, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnMoveTo(CCSPlayerController? caller, CommandInfo command)
    {
        var bot = Players().FirstOrDefault(p => p.IsBot && p.PlayerName == command.GetArg(1));
        if (bot == null) { command.ReplyToCommand("[hd] no such bot"); return; }
        var goal = new Vector(float.Parse(command.GetArg(2)), float.Parse(command.GetArg(3)), float.Parse(command.GetArg(4)));
        var route = command.ArgCount > 5 && command.GetArg(5) == "safest" ? RouteType.Safest : RouteType.Fastest;
        bool ok = TryMoveTo(bot, goal, route);
        command.ReplyToCommand($"[hd] moveto {bot.PlayerName} -> {Fmt(goal)} {route}: {(ok ? "ok" : "FAILED")}");
    }
}
