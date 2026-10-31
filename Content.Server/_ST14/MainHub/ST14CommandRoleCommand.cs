using System.Threading.Tasks;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Database;
using Content.Server.Players.JobWhitelist;
using Content.Shared._ST14.MainHub;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._ST14.MainHub;

/// <summary>
/// Gives or takes the command of a side in the galactic war. The role is a job whitelist, so it is kept in the database,
/// works for players who are offline, and a player who is online sees the planning panel right away
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class ST14CommandRoleCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private JobWhitelistManager _whitelist = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _player = default!;

    private static readonly string[] Sides = { "federation", "bugs" };

    public string Command => "st14_command";
    public string Description => Loc.GetString("st14-command-role-description");
    public string Help => Loc.GetString("st14-command-role-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _ = ExecuteAsync(shell, args);
    }

    /// <summary>
    /// The database lookups are awaited here, so a failure ends up in front of the admin instead of being lost
    /// </summary>
    private async Task ExecuteAsync(IConsoleShell shell, string[] args)
    {
        try
        {
            await Run(shell, args);
        }
        catch (Exception e)
        {
            Logger.GetSawmill("st14.war").Error($"st14_command failed: {e}");
            shell.WriteError(Loc.GetString("st14-command-role-failed", ("error", e.Message)));
        }
    }

    private async Task Run(IConsoleShell shell, string[] args)
    {
        if (args.Length != 3 || args[0] is not ("give" or "take") || !Sides.Contains(args[2]))
        {
            shell.WriteError(Help);
            return;
        }

        var give = args[0] == "give";
        var side = args[2] == "bugs" ? ST14GalaxyFaction.Arachnid : ST14GalaxyFaction.Federation;
        var job = ST14GalaxyWarRules.CommandOf(side);
        var sideName = Loc.GetString($"st14-galaxy-command-{args[2]}");

        var data = await _locator.LookupIdByNameAsync(args[1].Trim());
        if (data == null)
        {
            shell.WriteError(Loc.GetString("st14-command-role-no-player", ("player", args[1])));
            return;
        }

        var has = await _db.IsJobWhitelisted(data.UserId, job);
        if (has == give)
        {
            shell.WriteLine(Loc.GetString(give ? "st14-command-role-already" : "st14-command-role-not-in",
                ("player", data.Username), ("side", sideName)));
            return;
        }

        if (give)
            await _whitelist.AddWhitelistAsync(data.UserId, job);
        else
            await _whitelist.RemoveWhitelistAsync(data.UserId, job);

        // the player sees the planning panel appear or go without reopening the map
        if (_player.TryGetSessionById(data.UserId, out var session))
            _entMan.System<ST14GalaxyWarSystem>().Resend(session);

        shell.WriteLine(Loc.GetString(give ? "st14-command-role-given" : "st14-command-role-taken",
            ("player", data.Username), ("side", sideName)));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromOptions(new[] { "give", "take" }),
            2 => CompletionResult.FromHintOptions(_player.Sessions.Select(s => s.Name), Loc.GetString("st14-command-role-arg-player")),
            3 => CompletionResult.FromOptions(Sides),
            _ => CompletionResult.Empty,
        };
    }
}
