using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class FacadeTypeSelectionTests
{
    private const string DataManifest = "/repo/App.Data/mist.toml";

    private static readonly DeclarationOrigin Data = new(DeclarationOriginKind.ComposedProject, DataManifest);

    [Fact]
    public void APublicTypeOfTheComposedProjectIsForwarded()
    {
        var selected = FacadeTypeSelection.Select([new("App.Data", "StoreContext", true, Data)], DataManifest);

        Assert.Equal([new ForwardedType("App.Data", "StoreContext")], selected.AsEnumerable());
    }

    [Fact]
    public void AnInternalTypeIsNotForwarded()
    {
        var selected = FacadeTypeSelection.Select([new("App.Data", "Cache", false, Data)], DataManifest);

        Assert.Empty(selected);
    }

    [Fact]
    public void ATypeOfAnotherComposedProjectOrOfTheRootIsNotForwarded()
    {
        ForwardingCandidate[] candidates =
        [
            new("App.Core", "Clock", true, new(DeclarationOriginKind.ComposedProject, "/repo/App.Core/mist.toml")),
            new("App", "Program", true, new(DeclarationOriginKind.ComposedProject, null)),
        ];

        Assert.Empty(FacadeTypeSelection.Select(candidates, DataManifest));
    }

    [Fact]
    public void StandardLibraryAndSupportTypesAreForwarded()
    {
        ForwardingCandidate[] candidates =
        [
            new("Standard", "IClonable`1", true, DeclarationOrigin.StandardLibrary),
            new("", "__Void", true, DeclarationOrigin.SupportFile),
            new("App.Core", "Pair`2", true, DeclarationOrigin.SupportFile),
        ];

        Assert.Equal(
            [new ForwardedType("", "__Void"), new ForwardedType("App.Core", "Pair`2"), new ForwardedType("Standard", "IClonable`1")],
            FacadeTypeSelection.Select(candidates, DataManifest).AsEnumerable());
    }

    [Fact]
    public void ANestedTypeIsForwardedUnderItsEncloserWhateverItsOwnAccessibility()
    {
        ForwardingCandidate[] candidates =
        [
            new("App.Data", "StoreContext", true, Data),
            new("App.Data", "StoreContext/Options", true, Data),
            new("App.Data", "StoreContext/Options/Kind", false, Data),
        ];

        Assert.Equal(
            [
                new ForwardedType("App.Data", "StoreContext"),
                new ForwardedType("App.Data", "StoreContext/Options"),
                new ForwardedType("App.Data", "StoreContext/Options/Kind"),
            ],
            FacadeTypeSelection.Select(candidates, DataManifest).AsEnumerable());
    }

    [Fact]
    public void ANestedTypeOfATypeThatIsNotForwardedProducesNoEntry()
    {
        ForwardingCandidate[] candidates =
        [
            new("App.Data", "Cache", false, Data),
            new("App.Data", "Cache/Entry", true, Data),
            new("App.Core", "Clock/Tick", true, new(DeclarationOriginKind.ComposedProject, "/repo/App.Core/mist.toml")),
        ];

        Assert.Empty(FacadeTypeSelection.Select(candidates, DataManifest));
    }

    [Fact]
    public void ATypeDeclaredInTwoFilesIsForwardedOnce()
    {
        ForwardingCandidate[] candidates =
        [
            new("App.Data", "StoreContext", true, Data),
            new("App.Data", "StoreContext", true, DeclarationOrigin.SupportFile),
        ];

        Assert.Equal([new ForwardedType("App.Data", "StoreContext")], FacadeTypeSelection.Select(candidates, DataManifest).AsEnumerable());
    }

    [Fact]
    public void AnAssemblysOriginIsItsManifestUnlessItIsTheStandardLibrary()
    {
        Assert.Equal(
            Data,
            FacadeTypeSelection.OriginOf(new MistAssembly("App.Data", MistAssemblyKind.Reference, DataManifest)));
        Assert.Equal(
            new DeclarationOrigin(DeclarationOriginKind.ComposedProject, null),
            FacadeTypeSelection.OriginOf(new MistAssembly("App", MistAssemblyKind.Self)));
        Assert.Equal(
            DeclarationOrigin.StandardLibrary,
            FacadeTypeSelection.OriginOf(new MistAssembly(MistAssembly.StandardName, MistAssemblyKind.Standard)));
    }

    [Fact]
    public void AnEmittedFileThatIsNoMistUnitIsASupportFile()
    {
        var origins = FacadeTypeSelection.Origins(
            ["/out/App.Data/Store.cs", "/out/__Unions.g.cs"],
            new Dictionary<string, DeclarationOrigin> { ["/out/App.Data/Store.cs"] = Data });

        Assert.Equal(Data, origins["/out/App.Data/Store.cs"]);
        Assert.Equal(DeclarationOrigin.SupportFile, origins["/out/__Unions.g.cs"]);
    }
}