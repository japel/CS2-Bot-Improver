using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace HostageDirector;

// Orders from a human to bot teammates (upstream #68), built on native MoveTo. This is the
// command surface a voice / tool-calling front end (e.g. a local model) would drive.
//   css_bots follow [n]        n nearest bots follow you
//   css_bots hold <A|B> [n]    n bots go to a bomb site and stay near it
//   css_bots hostage [n]       n bots go to the nearest free hostage
//   css_bots free              cancel your orders
// Orders last until the round ends; bots still fight whenever they see an enemy.
public partial class HostageDirector
{
    private enum OrderKind { Follow, Hold, Hostage }
    private record Order(OrderKind Kind, uint Leader, Vector? Point);
    private readonly Dictionary<uint, Order> _orders = [];      // bot pawn index -> order
    private const float FollowKeep = 220f, HoldKeep = 300f;

    private void StartOrders()
    {
        RegisterEventHandler<EventRoundStart>((_, _) => { _orders.Clear(); return HookResult.Continue; });
        AddTimer(0.5f, UpdateOrders, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        RegisterListener<Listeners.OnMapStart>(_ =>
            AddTimer(0.5f, UpdateOrders, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE));
    }

    private void UpdateOrders()
    {
        if (_orders.Count == 0) return;
        float now = Server.CurrentTime;
        var byPawn = Players().Where(p => p.PlayerPawn.Value != null)
            .ToDictionary(p => p.PlayerPawn.Value!.Index, p => p);
        foreach (var (pawnIndex, order) in _orders.ToList())
        {
            if (!byPawn.TryGetValue(pawnIndex, out var bot) || !bot.PawnIsAlive) { _orders.Remove(pawnIndex); continue; }
            var pawn = bot.PlayerPawn.Value!;
            if (pawn.Bot is not { } b || pawn.AbsOrigin is not { } pos) continue;
            if (b.IsEnemyVisible || now - b.LastSawEnemyTimestamp < 2f) continue;   // fight first

            Vector? goal = order.Point;
            float keep = HoldKeep;
            if (order.Kind == OrderKind.Follow)
            {
                if (!byPawn.TryGetValue(order.Leader, out var leader) || !leader.PawnIsAlive) { _orders.Remove(pawnIndex); continue; }
                goal = leader.PlayerPawn.Value!.AbsOrigin;
                keep = FollowKeep;
            }
            else if (order.Kind == OrderKind.Hostage)
            {
                goal = NearestFreeHostage(pos);
                if (goal == null) { _orders.Remove(pawnIndex); continue; }
                keep = 60f;
            }
            if (goal != null && Dist(pos, goal) > keep)
                TryMoveTo(bot, new Vector(goal.X, goal.Y, goal.Z), RouteType.Fastest);
        }
    }

    private static Vector? NearestFreeHostage(Vector from) =>
        Utilities.FindAllEntitiesByDesignerName<CHostage>("hostage_entity")
            .Where(h => h.IsValid && h.Leader.Value == null && h.AbsOrigin != null)
            .Select(h => h.AbsOrigin!)
            .OrderBy(h => Dist(h, from))
            .FirstOrDefault();

    [ConsoleCommand("css_bots", "Order bot teammates: follow [n] | hold <A|B> [n] | hostage [n] | free")]
    [CommandHelper(minArgs: 1, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnBots(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller?.PlayerPawn.Value is not { AbsOrigin: { } me } myPawn || !caller.PawnIsAlive)
        {
            Reply(caller, command, "you need to be alive on a team");
            return;
        }
        string verb = command.GetArg(1).ToLowerInvariant();
        if (verb == "free")
        {
            var mine = _orders.Where(kv => kv.Value.Leader == myPawn.Index).Select(kv => kv.Key).ToHashSet();
            _orders.RemoveWhere(kv => mine.Contains(kv.Key));
            var released = Players().Where(p => p.IsBot && p.PlayerPawn.Value != null && mine.Contains(p.PlayerPawn.Value.Index)).ToList();
            Acknowledge(released, "Roger, back to my own plan.");
            Reply(caller, command, released.Count == 0 ? "no bots were under orders" : $"released {string.Join(", ", released.Select(p => p.PlayerName))}");
            return;
        }

        Vector? point = null;
        int countArg = 2;
        if (verb == "hold")
        {
            string which = command.ArgCount > 2 ? command.GetArg(2).ToUpperInvariant() : "";
            point = BombSites().Where(s => (s.isB ? "B" : "A") == which).Select(s => s.center).FirstOrDefault();
            if (point == null) { Reply(caller, command, "usage: hold <A|B> [n] (bomb maps)"); return; }
            countArg = 3;
        }
        var kind = verb switch { "follow" => OrderKind.Follow, "hold" => OrderKind.Hold, "hostage" => OrderKind.Hostage, _ => (OrderKind?)null };
        if (kind == null) { Reply(caller, command, "follow [n] | hold <A|B> [n] | hostage [n] | free"); return; }
        int n = command.ArgCount > countArg && int.TryParse(command.GetArg(countArg), out int c) ? c : 1;

        var picks = Players()
            .Where(p => p.IsBot && p.PawnIsAlive && p.Team == caller.Team && p.PlayerPawn.Value?.AbsOrigin != null)
            .OrderBy(p => Dist(p.PlayerPawn.Value!.AbsOrigin!, point ?? me))
            .Take(Math.Max(1, n))
            .ToList();
        foreach (var p in picks) _orders[p.PlayerPawn.Value!.Index] = new Order(kind.Value, myPawn.Index, point);
        if (picks.Count == 0) { Reply(caller, command, "no bot teammates alive"); return; }
        Acknowledge(picks, kind switch
        {
            OrderKind.Follow => $"Roger, following you, {caller.PlayerName}.",
            OrderKind.Hold => $"Roger, holding {command.GetArg(2).ToUpperInvariant()}.",
            _ => "Roger, going for the hostage.",
        });
        command.ReplyToCommand($"[bots] {verb}: {string.Join(", ", picks.Select(p => p.PlayerName))}");
    }
}

// Feedback for orders given by voice (netcon): the bots that took the order answer like
// teammates (radio "Roger that" + a team chat line); failures go to the caller's screen.
partial class HostageDirector
{
    private static void Reply(CCSPlayerController? caller, CommandInfo command, string msg)
    {
        command.ReplyToCommand($"[bots] {msg}");
        if (caller?.IsValid != true) return;
        caller.PrintToChat($" {ChatColors.Green}[bots]{ChatColors.Default} {msg}");
        caller.PrintToCenter(msg);
    }

    private static void Acknowledge(List<CCSPlayerController> bots, string line)
    {
        for (int i = 0; i < bots.Count; i++)
        {
            if (i == 0) bots[i].ExecuteClientCommandFromServer("roger");   // one radio voice line is enough
            bots[i].ExecuteClientCommandFromServer($"say_team {line}");
        }
    }
}

internal static class DictionaryExtensions
{
    public static int RemoveWhere<TK, TV>(this Dictionary<TK, TV> d, Func<KeyValuePair<TK, TV>, bool> pred) where TK : notnull
    {
        var keys = d.Where(pred).Select(kv => kv.Key).ToList();
        foreach (var k in keys) d.Remove(k);
        return keys.Count;
    }
}
