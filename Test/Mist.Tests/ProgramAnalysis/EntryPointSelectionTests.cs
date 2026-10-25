using System.Collections.Immutable;
using System.Globalization;
using Mist.ProgramAnalysis;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// The rows of top-level-statements Requirements 1.6 and 2 to 4 and attributes-and-test-command's library
/// rule, over the four facts the decision reads: the files holding top-level statements (each followed by
/// the block namespaces it declares in brackets, <c>Program.mist[Foo,Baz]</c>), the class declaring each
/// <c>Main</c> (named twice when it declares two), the manifest's <c>entry_point</c>, and whether the
/// project is a library. Lists are separated by <c>;</c>. A refusal is asserted as its whole message
/// because Requirements 1.6, 2.2, 3.3 and 3.4 are claims about what the message names.
/// </summary>
[Trait("Tier", "1")]
public sealed class EntryPointSelectionTests
{
    [Theory]
    [InlineData("Program.mist", "", "statements Program.mist")]
    [InlineData("", "Probe.A", "main Probe.A")]
    public void TheOnlyCandidate_IsTheEntryPointWithNoSetting(string files, string mains, string expected)
    {
        Assert.Equal(expected, Decide(files, mains, setting: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Probe.A")]
    [InlineData("Probe.A;Probe.B")]
    [InlineData("Probe.A;Probe.A")]
    public void TheTopLevelSetting_SelectsTheStatementsWhateverMainsExist(string mains)
    {
        Assert.Equal("statements Program.mist", Decide("Program.mist", mains, "top-level"));
    }

    [Theory]
    [InlineData("Probe.A", "Probe.A")]
    [InlineData("Probe.A;Probe.B", "Probe.A")]
    [InlineData("Probe.A;Probe.B", "Probe.B")]
    [InlineData("Probe.A;Probe.A;Probe.B", "Probe.B")]
    public void AClassSetting_SelectsThatClassAmongTheMains(string mains, string setting)
    {
        Assert.Equal($"main {setting}", Decide("", mains, setting));
    }

    [Fact]
    public void AnExecutableDeclaringNothing_StartsAtNothing()
    {
        Assert.Equal("none", Decide("", "", setting: null));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("Lib.A", null)]
    [InlineData("Lib.A;Lib.B", null)]
    [InlineData("Lib.A;Lib.A", null)]
    [InlineData("Lib.A;Lib.B", "Lib.B")]
    public void ALibrary_StartsAtNothingHoweverManyMainsItDeclares(string mains, string? setting)
    {
        Assert.Equal("none", Decide("", mains, setting, isLibrary: true));
    }

    [Theory]
    [InlineData("First.mist;Second.mist", "", null)]
    [InlineData("Second.mist;First.mist", "", "top-level")]
    [InlineData("First.mist;Second.mist", "Probe.A", null)]
    [InlineData("First.mist;Second.mist", "Probe.A", "Probe.A")]
    public void SeveralTopLevelFiles_AreRefusedNamingEachFile(string files, string mains, string? setting)
    {
        Assert.Equal(
            "MIST_EP_0001: Multiple files contain top-level statements. Only one file per project may have top-level statements. "
            + "Files: First.mist, Second.mist",
            Decide(files, mains, setting));
    }

    [Theory]
    [InlineData("Probe.A", "'Probe.A'")]
    [InlineData("Probe.B;Probe.A", "'Probe.A', 'Probe.B'")]
    public void TopLevelStatementsBesideAMain_WithNoSetting_AreRefusedNamingEveryCandidate(string mains, string named)
    {
        Assert.Equal(
            $"MIST_EP_0002: Project has both top-level statements (in 'Program.mist') and a static void Main (in {named}). "
            + "Add entry_point = \"top-level\" to mist.toml to resolve this conflict.",
            Decide("Program.mist", mains, setting: null));
    }

    [Theory]
    [InlineData("Probe.A")]
    [InlineData("Probe.B")]
    [InlineData("")]
    public void AClassSetting_BesideTopLevelStatements_IsRefusedWhetherOrNotTheClassHasAMain(string mains)
    {
        Assert.Equal(
            "MIST_EP_0003: Cannot specify entry_point = \"Probe.A\" when a top-level statement file exists. "
            + "A top-level statement file must always be the entry point. Use entry_point = \"top-level\" instead.",
            Decide("Program.mist", mains, "Probe.A"));
    }

    [Theory]
    [InlineData("Probe.A;Probe.B")]
    [InlineData("Probe.B;Probe.A")]
    [InlineData("Probe.B;Probe.A;Probe.A")]
    public void SeveralMains_WithNoSetting_AreRefusedSuggestingOne(string mains)
    {
        Assert.Equal(
            "MIST_EP_0004: Multiple classes contain static void Main: Probe.A, Probe.B. "
            + "Add entry_point = \"Probe.A\" to mist.toml to specify which one to use.",
            Decide("", mains, setting: null));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Probe.A", false)]
    [InlineData("Probe.A", true)]
    public void TheTopLevelSetting_WithNoTopLevelFile_IsRefused(string mains, bool isLibrary)
    {
        Assert.Equal(
            "MIST_EP_0005: entry_point = \"top-level\" specified in mist.toml but no file contains top-level statements.",
            Decide("", mains, "top-level", isLibrary));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Probe.A", false)]
    [InlineData("Probe.A;Probe.B", false)]
    [InlineData("Probe.A", true)]
    public void AClassSetting_NamingNoClassWithAMain_IsRefusedNamingIt(string mains, bool isLibrary)
    {
        Assert.Equal(
            "MIST_EP_0007: entry_point specifies 'Probe.NoSuch' but no class with that name contains a static void Main method.",
            Decide("", mains, "Probe.NoSuch", isLibrary));
    }

    [Theory]
    [InlineData("Program.mist", null, "Program.mist")]
    [InlineData("Program.mist", "top-level", "Program.mist")]
    [InlineData("Program.mist", "Lib.A", "Program.mist")]
    [InlineData("Second.mist;First.mist", null, "First.mist, Second.mist")]
    public void ALibraryWithTopLevelStatements_IsRefusedWhateverTheSetting(string files, string? setting, string named)
    {
        Assert.Equal(
            $"MIST_EP_0008: Library projects cannot have top-level statements. Files: {named}",
            Decide(files, "Lib.A", setting, isLibrary: true));
    }

    [Theory]
    [InlineData("Probe.A;Probe.A", null)]
    [InlineData("Probe.A;Probe.A;Probe.B", "Probe.A")]
    public void TheEntryClass_DeclaringTwoMains_IsRefused(string mains, string? setting)
    {
        Assert.Equal(
            "MIST_PM_DUPLICATE_ENTRY_POINT: 'Probe.A' is the program's entry point but declares more than one 'Main' that can be one. "
            + "A program can have only one entry point.",
            Decide("", mains, setting));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("", "top-level")]
    [InlineData("Probe.A", "top-level")]
    [InlineData("Probe.A;Probe.B", "top-level")]
    public void TheSelectedFile_DeclaringABlockNamespace_IsRefusedNamingTheFileAndTheNamespace(string mains, string? setting)
    {
        Assert.Equal(
            "MIST_EP_0006: File 'Program.mist' has both top-level statements and a block namespace declaration: namespace Foo { ... }. "
            + "Top-level statements cannot coexist with block namespace declarations in the same file. "
            + "Use a file-scoped namespace (namespace X;) instead.",
            Decide("Program.mist[Foo]", mains, setting));
    }

    [Theory]
    [InlineData("Program.mist[Foo,Baz]", "namespace Foo { ... }, namespace Baz { ... }")]
    [InlineData("Program.mist[Foo.Inner,Baz,Foo.Inner]", "namespace Foo.Inner { ... }, namespace Baz { ... }")]
    public void EveryBlockNamespaceIsNamedOnce_InDeclarationOrder(string files, string named)
    {
        Assert.StartsWith(
            $"MIST_EP_0006: File 'Program.mist' has both top-level statements and a block namespace declaration: {named}. ",
            Decide(files, "", setting: null),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Program.mist[Foo]", "Lib.A", null, true, "MIST_EP_0008")]
    [InlineData("Program.mist[Foo]", "", "top-level", true, "MIST_EP_0008")]
    [InlineData("First.mist[Foo];Second.mist", "", null, false, "MIST_EP_0001")]
    [InlineData("First.mist[Foo];Second.mist[Baz]", "", "top-level", false, "MIST_EP_0001")]
    [InlineData("Program.mist[Foo]", "Probe.A", null, false, "MIST_EP_0002")]
    [InlineData("Program.mist[Foo]", "Probe.A", "Probe.A", false, "MIST_EP_0003")]
    [InlineData("Program.mist[Foo]", "", "Probe.A", false, "MIST_EP_0003")]
    public void ABlockNamespace_InStatementsTheProjectWouldNotStartAt_IsNotWhatIsRefused(
        string files, string mains, string? setting, bool isLibrary, string refusal)
    {
        Assert.StartsWith($"{refusal}: ", Decide(files, mains, setting, isLibrary), StringComparison.Ordinal);
    }

    private static string Decide(string files, string mains, string? setting, bool isLibrary = false)
    {
        return EntryPointSelection.Decide([.. Split(files).Select(TopLevelFile)], Split(mains), setting, isLibrary) switch
        {
            EntryPointSelection.TopLevelStatements statements => $"statements {statements.File}",
            EntryPointSelection.Main main => $"main {main.Class}",
            EntryPointSelection.Refused refused =>
                $"{refused.Descriptor.Number.Value}: "
                + string.Format(CultureInfo.InvariantCulture, refused.Descriptor.Template, [.. refused.Arguments]),
            _ => "none",
        };
    }

    private static string[] Split(string list)
    {
        return list.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    private static EntryPointSelection.TopLevelFile TopLevelFile(string entry)
    {
        var bracket = entry.IndexOf('[', StringComparison.Ordinal);
        if(bracket < 0)
        {
            return new EntryPointSelection.TopLevelFile(entry, []);
        }

        var namespaces = entry[(bracket + 1)..entry.IndexOf(']', StringComparison.Ordinal)].Split(',');
        return new EntryPointSelection.TopLevelFile(entry[..bracket], ImmutableArray.Create(namespaces));
    }
}