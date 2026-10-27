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

        var admission = ProjectReferenceResolver.KeyMismatch("Migrations", kind, "App.Migrations", target);

        _ = Assert.NotNull(admission);
        Assert.Empty(admission.Value.Breaches);
        var diagnostic = admission.Value.Occurrence;
        Assert.Equal(DriverDiagnostics.ProjectReferenceNameMismatch.Number, diagnostic.Number);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(target, diagnostic.Location.Path);
        Assert.Equal($"Reference key 'Migrations' does not match 'App.Migrations', the name the {kindText} '{target}' produces.", diagnostic.Message);
    }

    [Fact]
    public void KeysCompareWithCase()
    {
        _ = Assert.NotNull(ProjectReferenceResolver.KeyMismatch("app.data", ReferenceKind.Assembly, "App.Data", "/work/libs/App.Data.dll"));
    }
}