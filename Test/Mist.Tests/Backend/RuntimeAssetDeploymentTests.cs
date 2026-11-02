using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class RuntimeAssetDeploymentTests
{
    private const string Bin = "/repo/App/bin";
    private const string LibClient = "/nuget/client/1.0.0/lib/net8.0/Client.dll";
    private const string WinClient = "/nuget/client/1.0.0/runtimes/win/lib/net8.0/Client.dll";
    private const string UnixClient = "/nuget/client/1.0.0/runtimes/unix/lib/net8.0/Client.dll";
    private const string PackageSqlite = "/nuget/sqlitepclraw.lib.e_sqlite3/2.1.12/runtimes/osx-arm64/native/libe_sqlite3.dylib";
    private const string OtherSqlite = "/nuget/other.e_sqlite3/1.0.0/runtimes/osx/native/libe_sqlite3.dylib";
    private const string MigrationsOutput = "/repo/App.Migrations/bin/Debug/net10.0/App.Migrations.dll";

    private static readonly ImmutableArray<string> OsxArm64 = ["osx-arm64", "osx", "unix", "any"];
    private static readonly DateTime Earlier = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    private readonly StubFiles files = new();

    [Fact]
    public void AReferenceIsCopiedButNotCountedAsARuntimeAsset()
    {
        files.Times[LibClient] = Earlier;

        var plan = Plan([LibClient], []);

        Assert.Equal([new FileCopy(LibClient, InBin("Client.dll"))], plan.Copies.AsEnumerable());
        Assert.Equal(0, plan.RuntimeAssetsDeployed);
    }

    [Fact]
    public void ARidSpecificManagedAssemblyReplacesTheCopyFromAPackageLibFolder()
    {
        files.Times[LibClient] = Earlier;
        files.Times[WinClient] = Earlier;

        var plan = Plan([LibClient], [Managed(WinClient, "win")]);

        Assert.Equal([new FileCopy(WinClient, InBin("Client.dll"))], plan.Copies.AsEnumerable());
        Assert.Equal(1, plan.RuntimeAssetsDeployed);
    }

    [Fact]
    public void TheReplacementIsCopiedEvenWhenTheCopyInBinIsNewer()
    {
        files.Times[LibClient] = Later;
        files.Times[WinClient] = Earlier;
        files.Times[InBin("Client.dll")] = Later;

        var plan = Plan([LibClient], [Managed(WinClient, "win")]);

        Assert.Equal([new FileCopy(WinClient, InBin("Client.dll"))], plan.Copies.AsEnumerable());
    }

    [Fact]
    public void ASecondRidSpecificAssemblyOfTheSameNameDoesNotReplaceTheFirst()
    {
        files.Times[LibClient] = Earlier;
        files.Times[WinClient] = Earlier;
        files.Times[UnixClient] = Earlier;

        var plan = Plan([LibClient], [Managed(WinClient, "win"), Managed(UnixClient, "unix")]);

        Assert.Equal([new FileCopy(WinClient, InBin("Client.dll"))], plan.Copies.AsEnumerable());
        Assert.Equal(1, plan.RuntimeAssetsDeployed);
    }

    [Fact]
    public void ANativeAssetDoesNotReplaceAReferenceOfTheSameName()
    {
        const string nativeClient = "/nuget/client.native/1.0.0/runtimes/win-x64/native/Client.dll";
        files.Times[LibClient] = Earlier;
        files.Times[nativeClient] = Earlier;

        var plan = Plan([LibClient], [Native(nativeClient, "win-x64")]);

        Assert.Equal([new FileCopy(LibClient, InBin("Client.dll"))], plan.Copies.AsEnumerable());
        Assert.Equal(0, plan.RuntimeAssetsDeployed);
    }

    [Fact]
    public void TheFirstOfTwoAssetsSharingAFileNameWins()
    {
        files.Times[PackageSqlite] = Earlier;
        files.Times[OtherSqlite] = Later;

        var plan = Plan([], [Native(PackageSqlite, "osx-arm64"), Native(OtherSqlite, "osx")]);

        Assert.Equal([new FileCopy(PackageSqlite, InBin("libe_sqlite3.dylib"))], plan.Copies.AsEnumerable());
        Assert.Equal(1, plan.RuntimeAssetsDeployed);
    }

    [Fact]
    public void AnUpToDateCopyIsNotCopiedAgainButStillCountsAsDeployed()
    {
        files.Times[PackageSqlite] = Earlier;
        files.Times[InBin("libe_sqlite3.dylib")] = Earlier;

        var plan = Plan([], [Native(PackageSqlite, "osx-arm64")]);

        Assert.Empty(plan.Copies);
        Assert.Equal(1, plan.RuntimeAssetsDeployed);
    }

    [Fact]
    public void ACopyOlderThanItsSourceIsRefreshed()
    {
        files.Times[PackageSqlite] = Later;
        files.Times[InBin("libe_sqlite3.dylib")] = Earlier;

        var plan = Plan([], [Native(PackageSqlite, "osx-arm64")]);

        Assert.Equal([new FileCopy(PackageSqlite, InBin("libe_sqlite3.dylib"))], plan.Copies.AsEnumerable());
    }

    [Fact]
    public void AnAssetWhoseSourceIsMissingIsNeitherCopiedNorCounted()
    {
        var plan = Plan([], [Native(PackageSqlite, "osx-arm64")]);

        Assert.Empty(plan.Copies);
        Assert.Equal(0, plan.RuntimeAssetsDeployed);
    }

    [Fact]
    public void AReferencedCSharpProjectContributesOnlyItsSelectedClosureAssets()
    {
        var selected = InMigrations("runtimes/osx-arm64/native/libe_sqlite3.dylib");
        files.Times[selected] = Earlier;
        files.Times[InMigrations("runtimes/linux-x64/native/libe_sqlite3.so")] = Earlier;
        files.Runtimes[MigrationsDirectory] = ["runtimes/linux-x64/native/libe_sqlite3.so", "runtimes/osx-arm64/native/libe_sqlite3.dylib"];

        var plan = Plan([], [], [Migrations(ReferenceKind.CSharpProject)]);

        Assert.Equal([new FileCopy(selected, InBin("libe_sqlite3.dylib"))], plan.Copies.AsEnumerable());
        Assert.Equal(1, plan.RuntimeAssetsDeployed);
    }

    [Fact]
    public void APackageAssetWinsOverAClosureAssetOfTheSameName()
    {
        files.Times[PackageSqlite] = Earlier;
        files.Times[InMigrations("runtimes/osx-arm64/native/libe_sqlite3.dylib")] = Later;
        files.Runtimes[MigrationsDirectory] = ["runtimes/osx-arm64/native/libe_sqlite3.dylib"];

        var plan = Plan([], [Native(PackageSqlite, "osx-arm64")], [Migrations(ReferenceKind.CSharpProject)]);

        Assert.Equal([new FileCopy(PackageSqlite, InBin("libe_sqlite3.dylib"))], plan.Copies.AsEnumerable());
        Assert.Equal(1, plan.RuntimeAssetsDeployed);
    }

    [Fact]
    public void AClosureManagedAssemblyReplacesTheProjectOutputCopy()
    {
        var outputCopy = InMigrations("Client.dll");
        var ridCopy = InMigrations("runtimes/unix/lib/net8.0/Client.dll");
        files.Times[outputCopy] = Earlier;
        files.Times[ridCopy] = Earlier;
        files.Runtimes[MigrationsDirectory] = ["runtimes/unix/lib/net8.0/Client.dll"];

        var plan = Plan([outputCopy], [], [Migrations(ReferenceKind.CSharpProject)]);

        Assert.Equal([new FileCopy(ridCopy, InBin("Client.dll"))], plan.Copies.AsEnumerable());
    }

    [Fact]
    public void AnAssemblyReferencedByPathContributesNoClosureAssets()
    {
        files.Times[InMigrations("runtimes/osx-arm64/native/libe_sqlite3.dylib")] = Earlier;
        files.Runtimes[MigrationsDirectory] = ["runtimes/osx-arm64/native/libe_sqlite3.dylib"];

        var plan = Plan([], [], [Migrations(ReferenceKind.Assembly)]);

        Assert.Empty(plan.Copies);
        Assert.Equal(0, plan.RuntimeAssetsDeployed);
    }

    private static string MigrationsDirectory => Path.GetDirectoryName(MigrationsOutput)!;

    private DeploymentPlan Plan(
        ImmutableArray<string> references,
        ImmutableArray<RuntimeAsset> assets,
        ImmutableArray<ExternalAssembly> externals = default)
    {
        return RuntimeAssetDeployment.Plan(Bin, references, assets, externals, OsxArm64, files);
    }

    private static string InBin(string name)
    {
        return Path.Combine(Bin, name);
    }

    private static string InMigrations(string relativePath)
    {
        return Path.Combine(MigrationsDirectory, relativePath);
    }

    private static RuntimeAsset Native(string path, string rid)
    {
        return new RuntimeAsset(path, rid, RuntimeAssetKind.Native);
    }

    private static RuntimeAsset Managed(string path, string rid)
    {
        return new RuntimeAsset(path, rid, RuntimeAssetKind.Managed);
    }

    private static ExternalAssembly Migrations(ReferenceKind kind)
    {
        return new ExternalAssembly("App.Migrations", MigrationsOutput, [], null, kind);
    }

    private sealed class StubFiles : IDeploymentFileSource
    {
        public Dictionary<string, DateTime> Times { get; } = [];

        public Dictionary<string, ImmutableArray<string>> Runtimes { get; } = [];

        public bool Exists(string path)
        {
            return Times.ContainsKey(path);
        }

        public DateTime LastWriteTimeUtc(string path)
        {
            return Times[path];
        }

        public ImmutableArray<string> RuntimesListing(string outputDirectory)
        {
            return Runtimes.GetValueOrDefault(outputDirectory, []);
        }
    }
}