using Content.Server.Administration;
using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.Console;

namespace Content.Server._Forge.Photo.Commands;

[UsedImplicitly]
[AdminCommand(AdminFlags.Host)]
public sealed class PhotoStatsCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "photostats";
    public string Description => "Shows current photo memory usage on the server.";
    public string Help => "Usage: photostats";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var system = _entities.System<PhotoSystem>();
        var used = system.StoredImageBytes;
        var limit = system.MaxStoredImageBytes;
        var usagePercent = limit > 0 ? used * 100.0 / limit : 0;

        shell.WriteLine($"Stored photos: {system.StoredImageCount}");
        shell.WriteLine($"Photo memory: {FormatBytes(used)} / {FormatBytes(limit)} ({usagePercent:F1}%)");
    }

    private static string FormatBytes(long bytes)
    {
        const double kib = 1024d;
        const double mib = kib * 1024d;

        if (bytes >= mib)
            return $"{bytes / mib:F2} MiB";

        if (bytes >= kib)
            return $"{bytes / kib:F2} KiB";

        return $"{bytes} B";
    }
}
