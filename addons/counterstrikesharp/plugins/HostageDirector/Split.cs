using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace HostageDirector;

// Split (bomb maps): legacy T bots tend to move as one pack (upstream #121; de_mirage
// baseline: >=4 Ts within 600u 51% of the time). At freeze end, N random T bots are sent
// to one bomb site with native MoveTo; the rest play normally. Orders are re-issued until
// the bot arrives, fights (enemy seen in the last 2 s) or the order expires.
public partial class HostageDirector
{
    // de_mirage A/B (40 rounds each): T wins 32 vs 24 with split 2, so on by default.
    private int _splitCount = 2;                // T bots sent to the split site; 0 = off
    private const float SplitTimeout = 35f;     // seconds after freeze end
    private const float SplitArrive = 350f;
    private readonly Dictionary<uint, (Vector goal, float until)> _split = [];   // pawn index -> order
    private int _splitRounds, _splitOrders;
    private readonly Random _rng = new();

    private void StartSplit()
    {
        RegisterEventHandler<EventRoundFreezeEnd>((_, _) => { AssignSplit(); return HookResult.Continue; });
        RegisterEventHandler<EventRoundStart>((_, _) => { _split.Clear(); return HookResult.Continue; });
        AddTimer(0.5f, UpdateSplit, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        RegisterListener<Listeners.OnMapStart>(_ =>
            AddTimer(0.5f, UpdateSplit, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE));
    }

    private static List<(bool isB, Vector center)> BombSites() =>
        Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target")
            .Where(t => t.IsValid && t.AbsOrigin != null && t.Collision != null)
            .Select(t =>
            {
                var o = t.AbsOrigin!; var mn = t.Collision.Mins; var mx = t.Collision.Maxs;
                return (t.IsBombSiteB, new Vector(o.X + (mn.X + mx.X) / 2, o.Y + (mn.Y + mx.Y) / 2, o.Z + mn.Z + 16));
            })
            .ToList();

    private void AssignSplit()
    {
        _split.Clear();
        if (_splitCount <= 0) return;
        var sites = BombSites();
        if (sites.Count < 2) return;
        var site = sites[_rng.Next(sites.Count)].center;
        var ts = Players()
            .Where(p => p.IsBot && p.PawnIsAlive && p.Team == CsTeam.Terrorist && p.PlayerPawn.Value != null)
            .OrderBy(_ => _rng.Next())
            .Take(_splitCount);
        float until = Server.CurrentTime + SplitTimeout;
        foreach (var p in ts) _split[p.PlayerPawn.Value!.Index] = (site, until);
        if (_split.Count > 0) _splitRounds++;
    }

    private void UpdateSplit()
    {
        if (_split.Count == 0) return;
        float now = Server.CurrentTime;
        foreach (var p in Players().Where(p => p.IsBot && p.PawnIsAlive))
        {
            var pawn = p.PlayerPawn.Value;
            if (pawn?.Bot is not { } bot || pawn.AbsOrigin is not { } pos) continue;
            if (!_split.TryGetValue(pawn.Index, out var order)) continue;
            if (now > order.until || Dist(pos, order.goal) < SplitArrive) { _split.Remove(pawn.Index); continue; }
            if (bot.IsEnemyVisible || now - bot.LastSawEnemyTimestamp < 2f) continue;
            if (TryMoveTo(p, new Vector(order.goal.X, order.goal.Y, order.goal.Z), RouteType.Fastest)) _splitOrders++;
        }
    }

    [ConsoleCommand("css_hd_split", "T bots sent to one bomb site at freeze end (0 = off; no arg: show)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnSplit(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount > 1 && int.TryParse(command.GetArg(1), out int n)) _splitCount = Math.Max(0, n);
        var sites = BombSites();
        command.ReplyToCommand($"[hd] split={_splitCount} rounds={_splitRounds} orders={_splitOrders} active={_split.Count} " +
                               $"sites={string.Join(" ", sites.Select(s => $"{(s.isB ? "B" : "A")}@{Fmt(s.center)}"))}");
    }
}
