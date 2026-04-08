using Content.Server.Administration;
using Content.Server._Forge.Rendering.Cache;
using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.Console;

namespace Content.Server._Forge.Rendering.Cache.Commands;

[UsedImplicitly]
[AdminCommand(AdminFlags.Host)]
public sealed class TexCacheRefreshAllCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "texcacherefreshall";
    public string Description => "Requests texture cache validation from all connected clients.";
    public string Help => "Usage: texcacherefreshall [includeOverlay=true|false]";

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
        var result = _entities.System<TextureCacheValidationSystem>().RequestCaptureAll(requestedBy, includeUi);

        shell.WriteLine(
            $"Requested {result.RequestedCount} cache refreshes. Dir: {result.OutputDirectory}");
    }
}
