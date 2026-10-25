#nullable enable

using System;
using System.IO;
using System.Reflection;
using Content.Client._Forge.Paper;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Content.Tests._Forge.Newspapers;

[TestFixture]
public sealed class NewspaperSandboxTests
{
    [Test]
    public void ClientAssemblyPassesEngineSandboxAndIlVerification()
    {
        // Use the actual engine checker: compiling and loading a unit test alone
        // does not enforce the content sandbox's API whitelist.
        var checkerType = typeof(IResourceManager).Assembly.GetType(
            "Robust.Shared.ContentPack.AssemblyTypeChecker", throwOnError: true)!;
        using var logs = new LogManager();
        var checker = Activator.CreateInstance(checkerType,
            new object?[] { null, logs.GetSawmill("test.sandbox") })!;
        var resolverType = checkerType.GetNestedType("Resolver", BindingFlags.NonPublic)!;
        using var resolver = (IDisposable)Activator.CreateInstance(resolverType,
            new object[] { checker, new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(object).Assembly.Location)! },
                Array.Empty<ResPath>() })!;
        using var assembly = File.OpenRead(typeof(PaperSurfacePanel).Assembly.Location);
        var check = checkerType.GetMethod("CheckAssembly", new[] { typeof(Stream), resolverType })!;
        Assert.That(check.Invoke(checker, new object[] { assembly, resolver }), Is.EqualTo(true),
            "Content.Client must pass the same sandbox and IL checks used during startup.");
    }
}
