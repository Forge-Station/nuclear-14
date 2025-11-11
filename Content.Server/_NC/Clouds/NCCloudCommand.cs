using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._NC.Clouds;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._NC.Clouds;

/// <summary>
///     Console command that allows admins to manually start or stop NC cloud cover events.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class NCCloudCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    public string Command => "nccloud";

    public string Description => Loc.GetString("cmd-nccloud-desc");

    public string Help => Loc.GetString("cmd-nccloud-help");

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(CompletionHelper.MapIds(_entManager), Loc.GetString("cmd-nccloud-hint-map"));
            case 2:
                return CompletionResult.FromHintOptions(new[] {"start", "stop"}, Loc.GetString("cmd-nccloud-hint-action"));
            case 3:
                if (args[1].Equals("start", StringComparison.OrdinalIgnoreCase))
                    return CompletionResult.FromHint(Loc.GetString("cmd-nccloud-hint-duration"));
                break;
        }

        return CompletionResult.Empty;
    }

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Loc.GetString("cmd-nccloud-error-args"));
            shell.WriteLine(Help);
            return;
        }

        if (!int.TryParse(args[0], out var mapInt))
        {
            shell.WriteError(Loc.GetString("cmd-nccloud-error-map-id", ("value", args[0])));
            return;
        }

        var mapId = new MapId(mapInt);

        var cloudSystem = _entManager.System<NCCloudLayerSystem>();

        if (!cloudSystem.TryGetCloudLayer(mapId, out var mapUid, out var component))
        {
            if (!_mapManager.MapExists(mapId))
            {
                shell.WriteError(Loc.GetString("cmd-nccloud-error-map-missing", ("mapId", mapId)));
            }
            else
            {
                shell.WriteError(Loc.GetString("cmd-nccloud-error-no-component", ("mapId", mapId)));
            }
            return;
        }

        var action = args[1];
        if (action.Equals("start", StringComparison.OrdinalIgnoreCase))
        {
            TimeSpan? duration = null;

            if (args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2]))
            {
                if (!float.TryParse(args[2], out var seconds) || seconds <= 0f)
                {
                    shell.WriteError(Loc.GetString("cmd-nccloud-error-duration", ("value", args[2])));
                    return;
                }

                duration = TimeSpan.FromSeconds(seconds);
            }

            cloudSystem.ForceStartClouds(mapUid, component, duration);

            if (duration.HasValue)
            {
                shell.WriteLine(Loc.GetString("cmd-nccloud-start-duration", ("mapId", mapId), ("seconds", duration.Value.TotalSeconds)));
            }
            else
            {
                shell.WriteLine(Loc.GetString("cmd-nccloud-start", ("mapId", mapId)));
            }
            return;
        }

        if (action.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            cloudSystem.ForceStopClouds(mapUid, component);
            shell.WriteLine(Loc.GetString("cmd-nccloud-stop", ("mapId", mapId)));
            return;
        }

        shell.WriteError(Loc.GetString("cmd-nccloud-error-action", ("value", args[1])));
    }
}
