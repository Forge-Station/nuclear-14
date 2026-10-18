using System.Diagnostics;
using System.IO.Compression;
using Robust.Packaging;
using Robust.Packaging.AssetProcessing;
using Robust.Packaging.AssetProcessing.Passes;
using Robust.Packaging.Utility;
using Robust.Shared.Timing;

namespace Content.Packaging;

public static class ClientPackaging
{

    // Forge-Secrets-Start
    private static readonly string SecretsClientProject =
        Path.Combine("FalloutSecrets", "Content.Secrets.Client", "Content.Secrets.Client.csproj");

    public static bool UseSecrets => File.Exists(SecretsClientProject);
    // Forge-Secrets-End
    
    /// <summary>
    /// Be advised this can be called from server packaging during a HybridACZ build.
    /// </summary>
    public static async Task PackageClient(bool skipBuild, string configuration, IPackageLogger logger)
    {
        logger.Info("Building client...");

        if (!skipBuild)
        {
            // Forge-Secrets-Start
            if (UseSecrets)
            {
                logger.Info("Secrets found, building secret client project...");
                await ProcessHelpers.RunCheck(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    ArgumentList =
                    {
                        "build",
                        SecretsClientProject,
                        "-c", configuration,
                        "--nologo",
                        "/v:m",
                        "/p:FullRelease=true",
                        "/m"
                    }
                });
            }
            // Forge-Secrets-End
            await ProcessHelpers.RunCheck(new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList =
                {
                    "build",
                    Path.Combine("Content.Client", "Content.Client.csproj"),
                    "-c", configuration,
                    "--nologo",
                    "/v:m",
                    "/t:Rebuild",
                    "/p:FullRelease=true",
                    "/m"
                }
            });
        }

        logger.Info("Packaging client...");

        var sw = RStopwatch.StartNew();
        {
            await using var zipFile =
                File.Open(Path.Combine("release", "SS14.Client.zip"), FileMode.Create, FileAccess.ReadWrite);
            using var zip = new ZipArchive(zipFile, ZipArchiveMode.Update);
            var writer = new AssetPassZipWriter(zip);

            await WriteResources("", writer, logger, default);
            await writer.FinishedTask;
        }

        logger.Info($"Finished packaging client in {sw.Elapsed}");
    }

    public static async Task WriteResources(
        string contentDir,
        AssetPass pass,
        IPackageLogger logger,
        CancellationToken cancel)
    {
        var graph = new RobustClientAssetGraph();
        pass.Dependencies.Add(new AssetPassDependency(graph.Output.Name));

        AssetGraph.CalculateGraph(graph.AllPasses.Append(pass).ToArray(), logger);

        var inputPass = graph.Input;

        // Forge-Secrets-Start
        var clientAssemblies = new List<string> { "Content.Client", "Content.Shared", "Content.Shared.Database" };
        if (UseSecrets)
        {
            clientAssemblies.Add("Content.Secrets.Shared");
            clientAssemblies.Add("Content.Secrets.Client");
        }
        // Forge-Secrets-End

        await RobustSharedPackaging.WriteContentAssemblies(
            inputPass,
            contentDir,
            "Content.Client",
            clientAssemblies,
            cancel: cancel);

        await RobustClientPackaging.WriteClientResources(contentDir, pass, cancel);

        inputPass.InjectFinished();
    }
}
