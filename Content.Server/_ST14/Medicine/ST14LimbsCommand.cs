using System.Linq;
using Content.Server.Administration;
using Content.Shared._ST14.Medicine;
using Content.Shared.Administration;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Console;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// Admin tool to try the medicine out on the body you are in: tear a limb off, fit a prosthesis, open a wound,
/// treat a part, inject a drug, stop the heart, open the scanner on yourself
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class ST14LimbsCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entMan = default!;

    public string Command => "st14_limbs";
    public string Description => Loc.GetString("st14-limbs-command-description");
    public string Help => Loc.GetString("st14-limbs-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } body || args.Length == 0)
        {
            shell.WriteError(Help);
            return;
        }

        // "other ..." does it to the nearest other wounded body instead, to try the medic side out alone
        if (args[0] == "other")
        {
            var self = body;
            var lookup = _entMan.System<EntityLookupSystem>();
            var near = new HashSet<Entity<ST14WoundsComponent>>();
            lookup.GetEntitiesInRange(_entMan.GetComponent<TransformComponent>(self).Coordinates, 4f, near);
            near.RemoveWhere(e => e.Owner == self);
            if (near.Count == 0 || args.Length < 2)
            {
                shell.WriteError(Loc.GetString("st14-limbs-command-nothing"));
                return;
            }

            body = near.First().Owner;
            args = args[1..];
        }

        var wounds = _entMan.System<ST14WoundSystem>();
        var done = args[0] switch
        {
            "tear" => _entMan.TryGetComponent<ST14DismemberableComponent>(body, out var dismember) &&
                      _entMan.System<ST14DismembermentSystem>().TearOff((body, dismember), null),
            "fit" when args.Length > 1 => Fit(body, args[1]),
            "wound" when args.Length > 2 => Wound(body, args[1], args[2]),
            "treat" when args.Length > 2 => Treat(body, args[1], args[2]),
            "drug" when args.Length > 2 => Inject(body, args[1], args[2]),
            "arrest" => _entMan.TryGetComponent<ST14WoundsComponent>(body, out var heart) && Stop(wounds, (body, heart)),
            "scan" => Scan(body),
            _ => false,
        };

        if (!done)
            shell.WriteError(Loc.GetString("st14-limbs-command-nothing") + " " + Help);
    }

    private static bool Stop(ST14WoundSystem wounds, Entity<ST14WoundsComponent> ent)
    {
        wounds.StopHeart(ent);
        return true;
    }

    private bool Fit(EntityUid body, string prototype)
    {
        var item = _entMan.SpawnEntity(prototype, _entMan.GetComponent<TransformComponent>(body).Coordinates);
        if (_entMan.TryGetComponent<ST14ProsthesisItemComponent>(item, out var prosthesis) &&
            _entMan.System<ST14ProsthesisSystem>().Fit((item, prosthesis), body))
        {
            return true;
        }

        _entMan.DeleteEntity(item);
        return false;
    }

    private bool Wound(EntityUid body, string zone, string kind)
    {
        if (!Enum.TryParse<ST14BodyZone>(zone, true, out var part) || !Enum.TryParse<ST14WoundKind>(kind, true, out var wound) ||
            !_entMan.TryGetComponent<ST14WoundsComponent>(body, out var comp))
        {
            return false;
        }

        var bleed = wound switch
        {
            ST14WoundKind.Cut => 0.8f,
            ST14WoundKind.Puncture => 0.7f,
            _ => 0f,
        };

        _entMan.System<ST14WoundSystem>().AddWound((body, comp), wound, part, 0.7f, bleed, false);
        return true;
    }

    private bool Treat(EntityUid body, string kind, string zone)
    {
        if (!Enum.TryParse<ST14TreatmentKind>(kind, true, out var treatment) || !Enum.TryParse<ST14BodyZone>(zone, true, out var part) ||
            !_entMan.TryGetComponent<ST14WoundsComponent>(body, out var comp) || !ST14WoundSystem.Needs(comp, treatment, part))
        {
            return false;
        }

        _entMan.System<ST14WoundSystem>().Treat((body, comp), treatment, part);
        return true;
    }

    private bool Inject(EntityUid body, string reagent, string amount)
    {
        if (!float.TryParse(amount, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var units) ||
            !_entMan.TryGetComponent<BloodstreamComponent>(body, out var blood))
        {
            return false;
        }

        var solution = new Solution(reagent, FixedPoint2.New(units));
        return _entMan.System<BloodstreamSystem>().TryAddToBloodstream((body, blood), solution);
    }

    private bool Scan(EntityUid body)
    {
        var scanner = _entMan.SpawnEntity("ST14MedScanner", _entMan.GetComponent<TransformComponent>(body).Coordinates);
        _entMan.System<ST14MedScannerSystem>().Scan((scanner, _entMan.GetComponent<ST14MedScannerComponent>(scanner)), body, body);
        return true;
    }
}
