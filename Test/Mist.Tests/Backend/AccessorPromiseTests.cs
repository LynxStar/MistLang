// Tier 3 (Component): purity Requirement 1.5 in the model -- an accessor's own promise is recorded on
// the accessor (PropertyAccessor.OwnModifiers), ahead of its accessibility, beside a declaration that
// keeps its own -- read off the real model builder over Standard's shells; what the judge makes of the
// promise is AccessorPromiseRefusalTests' claim.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class AccessorPromiseTests : ProgramModelProbe
{
    public AccessorPromiseTests()
        : base("MistAccessorPromise_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public class Box { public int n; public int P { local readonly get { return n; } local set { n = value; } } }")]
    [InlineData("public class Box { public int n; public int P { local readonly get; local set; } }")]
    public void ProgramModel_AnAccessorsOwnPromiseIsRecordedOnTheAccessor(string declaration)
    {
        var property = TypeIn(declaration).PropertiesList.Single();

        Assert.False(property.Modifiers.IsLocal);
        Assert.False(property.Modifiers.IsReadonly);
        AssertLocalReadonly(property.Getter);
        AssertLocal(property.Setter);
    }

    [Theory]
    [InlineData("public class Box { public int n; public int this[int i] { local readonly get { return n; } local set { n = value; } } }")]
    [InlineData("public class Box { public int n; public int this[int i] { local readonly get => n; local set => n = value; } }")]
    public void ProgramModel_AnIndexerAccessorsOwnPromiseIsRecordedOnTheAccessor(string declaration)
    {
        var indexer = TypeIn(declaration).IndexersList.Single();

        Assert.False(indexer.Modifiers.IsLocal);
        AssertLocalReadonly(indexer.Getter);
        AssertLocal(indexer.Setter);
    }

    [Fact]
    public void ProgramModel_APureAccessorIsPureAndReadonly()
    {
        var getter = TypeIn("public class Box { public int P { pure get { return 1; } } }").PropertiesList.Single().Getter;

        Assert.NotNull(getter);
        var own = Assert.NotNull(getter.OwnModifiers);
        Assert.True(own.IsPure);
        Assert.True(own.IsReadonly);
        Assert.False(own.IsLocal);
    }

    [Fact]
    public void ProgramModel_AnAccessorStatingNothingRecordsNoOwnPromise()
    {
        var property = TypeIn("public class Box { public int n; public int P { get { return n; } set { n = value; } } }").PropertiesList.Single();

        Assert.NotNull(property.Getter);
        Assert.NotNull(property.Setter);
        Assert.Null(property.Getter.OwnModifiers);
        Assert.Null(property.Setter.OwnModifiers);
    }

    [Fact]
    public void ProgramModel_APromiseStandsAheadOfTheAccessorsAccessibility()
    {
        var setter = TypeIn("public class Box { public int n; public int P { local readonly get { return n; } local private set { n = value; } } }").PropertiesList.Single().Setter;

        Assert.NotNull(setter);
        Assert.Equal(Accessibility.Private, setter.Accessibility);
        AssertLocal(setter);
    }

    [Fact]
    public void ProgramModel_ADeclarationsPromiseStaysOnTheDeclarationBesideAnAccessorsOwn()
    {
        var property = TypeIn("public class Box { public int n; public local int P { local readonly get { return n; } set { n = value; } } }").PropertiesList.Single();

        Assert.True(property.Modifiers.IsLocal);
        Assert.NotNull(property.Setter);
        AssertLocalReadonly(property.Getter);
        Assert.Null(property.Setter.OwnModifiers);
    }

    private static void AssertLocalReadonly(PropertyAccessor? getter)
    {
        Assert.NotNull(getter);
        var own = Assert.NotNull(getter.OwnModifiers);
        Assert.True(own.IsLocal);
        Assert.True(own.IsReadonly);
        Assert.False(own.IsPure);
    }

    private static void AssertLocal(PropertyAccessor? setter)
    {
        Assert.NotNull(setter);
        var own = Assert.NotNull(setter.OwnModifiers);
        Assert.True(own.IsLocal);
        Assert.False(own.IsReadonly);
        Assert.False(own.IsPure);
    }

    private ConcreteType TypeIn(string declaration)
    {
        var model = BuildModel("", declaration, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Probe.Box"]);
    }
}