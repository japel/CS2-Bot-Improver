using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace HostageDirector;

// Escort: when a CT bot picks up a hostage it radios "follow me", so nearby teammates use
// the legacy bot follow behaviour (CCSBot::Follow via RespondToRadioCommands) to cover it.
public partial class HostageDirector
{
    // Off by default: neither bot- nor human-sent playerradio followme made legacy bots follow
    // (IsFollowing stayed false) in tests on 2026-10-11.
    private string _escortRadio = "off";            // followme|coverme|off
    private int _escortCalls;

    private void StartEscort()
    {
        RegisterEventHandler<EventHostageFollows>((e, _) =>
        {
            var carrier = e.Userid;
            if (_escortRadio == "off" || carrier is not { IsValid: true, IsBot: true }
                || carrier.Team != CsTeam.CounterTerrorist) return HookResult.Continue;
            carrier.ExecuteClientCommandFromServer(RadioCommand(_escortRadio));
            _escortCalls++;
            return HookResult.Continue;
        });
    }

    // CS2 has no followme/coverme console commands; radio goes through playerradio with a
    // response token from an allow list plus its localized title.
    private static string RadioCommand(string response) => response switch
    {
        "coverme" => "playerradio coverme \"#Cstrike_TitlesTXT_Cover_me\"",
        _ => "playerradio followme \"#Cstrike_TitlesTXT_Follow_me\"",
    };

    [ConsoleCommand("css_hd_botcmd", "Run a client command as a bot: <bot name> <command...>")]
    [CommandHelper(minArgs: 2, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBotCmd(CCSPlayerController? caller, CommandInfo command)
    {
        var bot = Players().FirstOrDefault(p => p.IsBot && p.PlayerName == command.GetArg(1));
        if (bot == null) { command.ReplyToCommand("[hd] no such bot"); return; }
        string cmd = command.ArgString[command.GetArg(1).Length..].Trim();
        if (cmd is "followme" or "coverme") cmd = RadioCommand(cmd);
        bot.ExecuteClientCommandFromServer(cmd);
        command.ReplyToCommand($"[hd] {bot.PlayerName} <- {cmd}");
    }

    [ConsoleCommand("css_hd_escort", "Radio command a CT bot sends on hostage pickup (followme|coverme|off)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnEscort(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount > 1) _escortRadio = command.GetArg(1);
        int following = Players().Count(p => p.IsBot && p.PawnIsAlive && p.PlayerPawn.Value?.Bot?.IsFollowing == true);
        command.ReplyToCommand($"[hd] escort={_escortRadio} calls={_escortCalls} following_now={following}");
    }
}
