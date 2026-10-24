using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class ReferenceKeyMismatchTests
{
    [Theory]
    [InlineData(ReferenceKind.MistProject, "/work/Lib/mist.toml")]
    [InlineData(ReferenceKind.Assembly, "/work/libs/App.Data.dll")]
    [InlineData(ReferenceKind.CSharpProject, "/work/App.Migrations/App.Migrations.csproj")]
    public void AKeyEqualToTheProducedName_IsNoMismatch(ReferenceKind kind, string target)
    {
        Assert.Null(ProjectReferenceResolver.KeyMismatch("App.Data", kind, "App.Data", target));
    }

    [Theory]
    [InlineData(ReferenceKind.MistProject, "Mist project")]
    [InlineData(ReferenceKind.Assembly, "assembly")]
    [InlineData(ReferenceKind.CSharpProject, "C# project")]
    public void AMismatch_NamesTheKindAndTheNameTheTargetProduces(ReferenceKind kind, string kindText)
    {
        const string target = "/work/App.Migrations/App.Migrations.csproj";

        var diagnostic = ProjectReferenceResolver.KeyMismatch("Migrations", kind, "App.Migrations", target);

        Assert.NotNull(diagnostic);
        Assert.Equal(DriverDiagnostics.ProjectReferenceNameMismatch.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(target, diagnostic.CompilationUnitPath);
        Assert.Equal($"Reference key 'Migrations' does not match 'App.Migrations', the name the {kindText} '{target}' produces.", diagnostic.Message);
    }

    [Fact]
    public void KeysCompareWithCase()
    {
        Assert.NotNull(ProjectReferenceResolver.KeyMismatch("app.data", ReferenceKind.Assembly, "App.Data", "/work/libs/App.Data.dll"));
    }
}