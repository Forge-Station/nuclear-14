using System.Linq;
using Content.Server.Administration;
using Content.Server._Forge.Photo.Sync.Systems;
using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._Forge.Photo.Sync.Commands;

[UsedImplicitly]
[AdminCommand(AdminFlags.Host)]
public sealed class PhotoSyncCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public string Command => "photoframe";
    public string Description => "Requests one photo frame from target player.";
    public string Help => "Usage: photoframe <playerNameOrCkey> [includeUi=true|false]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Help);
            return;
        }

        if (!TryFindSession(args[0], out var target))
        {
            shell.WriteError($"Player '{args[0]}' is not online.");
            return;
        }

        var includeUi = true;
        if (args.Length == 2 && !bool.TryParse(args[1], out includeUi))
        {
            shell.WriteError("Second argument must be a boolean: true or false.");
            return;
        }

        var requestedBy = shell.Player?.Name ?? "server-console";
        var result = _entities.System<PhotoSyncSystem>().RequestPhoto(target, requestedBy, includeUi);

        shell.WriteLine(
            $"Requested photo frame #{result.RequestId} from {target.Name}. Output dir: {result.OutputDirectory}");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            _players.Sessions.Select(session => session.Name).OrderBy(name => name).ToArray(),
            "<playerName>");
    }

    private bool TryFindSession(string query, out ICommonSession target)
    {
        if (_players.TryGetSessionByUsername(query, out var byName))
        {
            target = byName;
            return true;
        }

        var ckey = ToCkey(query);
        foreach (var session in _players.Sessions)
        {
            if (ToCkey(session.Name) != ckey)
                continue;

            target = session;
            return true;
        }

        target = default!;
        return false;
    }

    private static string ToCkey(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(' ', '_');
    }
}
