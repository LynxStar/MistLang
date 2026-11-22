using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// The interner's own identity rules, asserted on direct input. A union is the canonical SET of its
/// constituents: nested unions are flattened before duplicates are dropped, so a nested union that
/// repeats a constituent is the flat union, and a set that leaves one constituent is that constituent
/// bare, carried by no union. The emitter discovers carrier structs by reading
/// <see cref="TypeInterner.UnionTypes"/>, so each of those is also a claim about the emitted carriers.
/// A function type is keyed on its parameters AND its result.
/// </summary>
[Trait("Tier", "1")]
public sealed class UnionAndFunctionTypeInterningTests
{
    private static readonly ClassType Integer = new("Int32", "Standard", new Scope(null), null, null);
    private static readonly ClassType Text = new("String", "Standard", new Scope(null), null, null);
    private static readonly ClassType Flag = new("Boolean", "Standard", new Scope(null), null, null);

    [Fact]
    public void ANestedUnionRepeatingAConstituent_IsTheFlatUnionAndOneCarrier()
    {
        var interner = new TypeInterner();
        var flat = interner.GetOrCreateUnionType([Integer, Text]);

        var nested = interner.GetOrCreateUnionType([flat, Text]);

        Assert.Same(flat, nested);
        Assert.Same(flat, Assert.Single(interner.UnionTypes));
    }

    [Fact]
    public void ASetLeavingOneConstituent_IsThatTypeBareAndNoCarrier()
    {
        var interner = new TypeInterner();

        Assert.Same(Integer, interner.GetOrCreateUnionType([Integer, Integer]));
        Assert.Empty(interner.UnionTypes);
    }

    [Fact]
    public void FunctionTypesSharingTheirParameters_AreDistinctByTheirResult()
    {
        var interner = new TypeInterner();
        var toFlag = interner.GetOrCreateFunctionType([Integer], Flag);

        var toText = interner.GetOrCreateFunctionType([Integer], Text);

        Assert.NotSame(toFlag, toText);
        Assert.Same(Text, toText.ReturnType);
    }
}