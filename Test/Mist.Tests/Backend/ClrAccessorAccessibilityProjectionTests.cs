// Tier 2 (Functional): a projected CLR accessor carries its own accessibility, so a public property's protected
// setter is judged as protected at a use rather than read as the property's. Recorded on order 9 of
// backlog/analysis-v2-capability-progression.yaml.

using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

public sealed class ClrAccessorAccessibilityProjectionTests
{
    [Fact]
    public void AProtectedSetter_IsProjectedProtectedBesideAPublicGetter()
    {
        var level = Assert.Single(Project().PropertiesList, property => property.Name == nameof(ProtectedSetters.Level));

        Assert.Equal(Accessibility.Public, level.Modifiers.Accessibility);
        Assert.Equal(Accessibility.Public, level.Getter?.Accessibility);
        Assert.Equal(Accessibility.Protected, level.Setter?.Accessibility);
    }

    [Fact]
    public void AnIndexersProtectedSetter_IsProjectedProtectedBesideAPublicGetter()
    {
        var indexer = Assert.Single(Project().IndexersList);

        Assert.Equal(Accessibility.Public, indexer.Modifiers.Accessibility);
        Assert.Equal(Accessibility.Public, indexer.Getter?.Accessibility);
        Assert.Equal(Accessibility.Protected, indexer.Setter?.Accessibility);
    }

    private static MemberType Project()
    {
        var clrType = typeof(ProtectedSetters);
        var shell = (MemberType)TypeProjector.CreateShell(clrType, clrType.Namespace!);
        var resolver = new AmbientTypeResolver(
            new Dictionary<string, DeclaredType> { [clrType.FullName!] = shell },
            new Dictionary<string, IMistType> { ["System.Int32"] = TypeProjector.CreateShell(typeof(int), "System") },
            new TypeInterner(),
            new DiagnosticBag());

        PropertyProjector.Project(shell, clrType, resolver);
        IndexerProjector.Project(shell, clrType, resolver);

        return shell;
    }
}