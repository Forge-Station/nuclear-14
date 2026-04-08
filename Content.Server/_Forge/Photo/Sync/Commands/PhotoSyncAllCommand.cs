using Content.Server.Administration;
using Content.Server._Forge.Photo.Sync.Systems;
using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.Console;

namespace Content.Server._Forge.Photo.Sync.Commands;

[UsedImplicitly]
[AdminCommand(AdminFlags.Host)]
public sealed class PhotoSyncAllCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "photoframeall";
    public string Description => "Requests remote photo frames from all connected players.";
    public string Help => "Usage: photoframeall [includeUi=true|false]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }

        var includeUi = true;
        if (args.Length == 1 && !bool.TryParse(args[0], out includeUi))
        {
            shell.WriteError("Argument must be a boolean: true or false.");
            return;
        }

        var requestedBy = shell.Player?.Name ?? "server-console";
        var result = _entities.System<PhotoSyncSystem>().RequestPhotoAll(requestedBy, includeUi);

        shell.WriteLine(
            $"Requested {result.RequestedCount} photo frames. Output dir: {result.OutputDirectory}");
    }
}
