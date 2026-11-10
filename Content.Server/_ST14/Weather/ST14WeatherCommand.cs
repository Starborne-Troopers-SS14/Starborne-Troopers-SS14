using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Server._ST14.Sectors;
using Content.Shared._ST14.Weather;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.Weather;

/// <summary>
/// Admin tool for the weather of the sector the admin stands in: start or stop an event, set the hour of the day
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ST14WeatherCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public string Command => "st14_weather";
    public string Description => Loc.GetString("st14-weather-command-description");
    public string Help => Loc.GetString("st14-weather-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteError(Help);
            return;
        }

        if (args[0] == "list")
        {
            foreach (var listed in _prototype.EnumeratePrototypes<ST14WeatherEventPrototype>().OrderBy(p => p.ID))
            {
                shell.WriteLine($"{listed.ID} - {Loc.GetString(listed.Name)}");
            }

            return;
        }

        if (shell.Player?.AttachedEntity is not { } player ||
            _entMan.GetComponent<TransformComponent>(player).MapUid is not { } map ||
            !_entMan.System<ST14SectorSystem>().LoadedMaps.Values.Contains(map))
        {
            shell.WriteError(Loc.GetString("st14-weather-command-no-sector"));
            return;
        }

        var weather = _entMan.System<ST14SectorWeatherSystem>();
        var day = _entMan.System<ST14DayNightSystem>();

        switch (args[0])
        {
            case "stop":
                shell.WriteLine(weather.Stop(map) ? Loc.GetString("st14-weather-command-stopped") : Loc.GetString("st14-weather-command-none"));
                return;

            case "hour":
                if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hour))
                {
                    shell.WriteError(Help);
                    return;
                }

                day.SetHour(map, Math.Clamp(hour, 0f, 24f));
                shell.WriteLine(Loc.GetString("st14-weather-command-hour", ("hour", hour)));
                return;

            case "status":
                shell.WriteLine(Loc.GetString("st14-weather-command-status",
                    ("height", MathF.Round(day.StarHeight(map), 2)),
                    ("event", weather.Current(map) is { } current ? Loc.GetString(current.Name) : "-")));
                return;
        }

        if (!_prototype.TryIndex<ST14WeatherEventPrototype>(args[0], out var proto))
        {
            shell.WriteError(Loc.GetString("st14-weather-command-unknown", ("id", args[0])));
            return;
        }

        TimeSpan? duration = null;
        if (args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            duration = TimeSpan.FromSeconds(seconds);

        var full = args.Length > 2 && args[2] == "full";
        weather.Start(map, proto, duration, full);
        shell.WriteLine(Loc.GetString("st14-weather-command-started", ("name", Loc.GetString(proto.Name))));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var options = _prototype.EnumeratePrototypes<ST14WeatherEventPrototype>().Select(p => p.ID)
            .Concat(new[] { "list", "stop", "hour", "status" });
        return CompletionResult.FromHintOptions(options, Loc.GetString("st14-weather-command-arg"));
    }
}
