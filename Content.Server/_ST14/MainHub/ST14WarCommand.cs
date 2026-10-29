using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._ST14.MainHub;

/// <summary>
/// Admin tool for the galactic war until a game mode reports the rounds: shows the state, applies a result to the target
/// sector, moves the target or starts the planet over
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed partial class ST14WarCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entMan = default!;

    public string Command => "st14_war";
    public string Description => Loc.GetString("st14-war-command-description");
    public string Help => Loc.GetString("st14-war-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var war = _entMan.System<ST14GalaxyWarSystem>();
        if (args.Length == 0)
        {
            shell.WriteError(Help);
            return;
        }

        // the planet is optional while there is only one war planet
        var planet = args.Length > 1 && args[0] != "target" ? args[^1] : war.Planets.FirstOrDefault();
        if (args[0] == "target" && args.Length > 2)
            planet = args[2];

        if (planet == null || !war.TryGetWar(planet, out var state, out var target))
        {
            shell.WriteError(Loc.GetString("st14-war-command-no-planet", ("planet", planet ?? "-")));
            return;
        }

        switch (args[0])
        {
            case "status":
                shell.WriteLine(Loc.GetString("st14-war-command-status",
                    ("planet", planet),
                    ("liberation", MathF.Round(state.Liberation)),
                    ("target", target >= 0 ? state.Cells[target].Code : "-"),
                    ("targetLiberation", target >= 0 ? state.Cells[target].Liberation : 0),
                    ("front", state.Cells.Count(c => c.Front))));
                return;

            case "win":
            case "objective":
            case "loss":
                if (!war.ApplyResult(planet, args[0] != "loss", args[0] == "objective"))
                {
                    shell.WriteError(Loc.GetString("st14-war-command-no-target"));
                    return;
                }

                shell.WriteLine(Loc.GetString("st14-war-command-done"));
                return;

            case "target":
                if (args.Length < 2 || !int.TryParse(args[1], out var cell) || !war.SetTarget(planet, cell))
                {
                    shell.WriteError(Loc.GetString("st14-war-command-bad-target"));
                    return;
                }

                shell.WriteLine(Loc.GetString("st14-war-command-done"));
                return;

            case "reset":
                war.Reset(planet);
                shell.WriteLine(Loc.GetString("st14-war-command-done"));
                return;

            default:
                shell.WriteError(Help);
                return;
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromOptions(new[] { "status", "win", "objective", "loss", "target", "reset" }),
            _ => CompletionResult.Empty,
        };
    }
}
