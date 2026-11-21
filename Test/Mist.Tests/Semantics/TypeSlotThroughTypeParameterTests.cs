using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class TypeSlotThroughTypeParameterTests
{
    private const int ReferenceOffset = 44;

    private readonly BindingFragments f = new();
    private readonly MemberScope member;
    private readonly TypeParameter parameter;
    private readonly AbstractType producer = Abstract("IProducer");
    private readonly AbstractType other = Abstract("IOther");
    private readonly TypeSlot output;
    private readonly TypeSlot pair;
    private readonly TypeSlot extra;

    public TypeSlotThroughTypeParameterTests()
    {
        member = new MemberScope(f.Types);
        parameter = new TypeParameter("T", 0, member, null);
        member.DeclareType("T", parameter);

        output = new TypeSlot("Output", 0, producer.Scope, producer);
        pair = new TypeSlot("Pair<>", 1, producer.Scope, producer);
        pair.TypeParametersList.Add(new TypeParameter("V", 0, pair.Scope, pair));
        producer.TypeSlotsList.AddRange([output, pair]);

        extra = new TypeSlot("Extra", 0, other.Scope, other);
        other.TypeSlotsList.Add(extra);
    }

    [Fact]
    public void ASlotNamedThroughATypeParameterItsAbstractConstrainsIsThatSlot()
    {
        parameter.ConstraintsList.Add(new TypeConstraint(producer));

        var resolution = Resolve(Reference("T", "Output"));

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.False(resolution.IsFailed);
        Assert.Same(output, resolution.Type);
    }

    [Fact]
    public void ASlotIsReachedThroughAConstraintThatConstructsItsAbstract()
    {
        var bag = Abstract("IBag<>");
        bag.TypeParametersList.Add(new TypeParameter("E", 0, bag.Scope, bag));
        var item = new TypeSlot("Item", 0, bag.Scope, bag);
        bag.TypeSlotsList.Add(item);
        parameter.ConstraintsList.Add(
            new TypeConstraint(f.Built.Interner.GetOrCreateConstructedType(bag, [f.Built.Type("Int32")])));

        var resolution = Resolve(Reference("T", "Item"));

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.False(resolution.IsFailed);
        Assert.Same(item, resolution.Type);
    }

    [Theory]
    [InlineData("Output")]
    [InlineData("Extra")]
    public void ASlotIsFoundOnWhicheverConstrainingAbstractDeclaresIt(string slotName)
    {
        parameter.ConstraintsList.AddRange(
        [
            new KeywordConstraint(TypeParameterConstraintKind.Class),
            new TypeConstraint(HandBuiltModel.Class("Plain")),
            new TypeConstraint(other),
            new TypeConstraint(producer),
        ]);

        var resolution = Resolve(Reference("T", slotName));

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.False(resolution.IsFailed);
        Assert.Same(slotName == "Output" ? output : extra, resolution.Type);
    }

    [Theory]
    [InlineData("none", "T.Output")]
    [InlineData("class", "T.Output")]
    [InlineData("abstract", "T.Absent")]
    [InlineData("abstract", "T.Output.Inner")]
    public void ADottedNameThatIsNoSlotOfAConstrainingAbstractIsAnUnresolvedType(string constraints, string written)
    {
        if(constraints == "class")
        {
            parameter.ConstraintsList.AddRange(
            [
                new KeywordConstraint(TypeParameterConstraintKind.Class),
                new TypeConstraint(HandBuiltModel.Class("Plain")),
            ]);
        }
        else if(constraints == "abstract")
        {
            parameter.ConstraintsList.Add(new TypeConstraint(producer));
        }

        var resolution = Resolve(Reference(written.Split('.')));

        AssertUnresolved(resolution, written);
    }

    [Fact]
    public void ASlotIsNotATypeParameterToNameItsBoundsSlotThrough()
    {
        output.ConstraintsList.Add(new TypeConstraint(other));
        member.DeclareType("Output", output);

        var resolution = Resolve(Reference("Output", "Extra"));

        AssertUnresolved(resolution, "Output.Extra");
    }

    [Theory]
    [InlineData("Output", 1)]
    [InlineData("Pair", 0)]
    public void ASlotNamedWithTypeArgumentsItDoesNotDeclareIsRefusedAndNeverThatSlot(string slotName, int typeArguments)
    {
        parameter.ConstraintsList.Add(new TypeConstraint(producer));
        var reference = Reference("T", slotName);
        reference.TypeArgs = typeArguments == 0 ? null : BindingFragments.TypeArguments("Int32");

        var resolution = Resolve(reference);

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Equal(ReferenceOffset, Assert.IsType<SourceRange>(finding.Location).Offset);
        Assert.True(resolution.IsFailed);
        Assert.Same(ErrorType.Instance, resolution.Type);
    }

    private void AssertUnresolved(TypeResolution resolution, string written)
    {
        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal("MIST0298", finding.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Equal(new SlotValue[] { new NameValue(written) }, finding.Arguments);
        Assert.Equal(ReferenceOffset, Assert.IsType<SourceRange>(finding.Location).Offset);
        Assert.True(resolution.IsFailed);
        Assert.Same(ErrorType.Instance, resolution.Type);
    }

    private TypeResolution Resolve(NamedSingleTypeRefSingleTypeRefNode reference)
    {
        return f.Builder.Resolver.ResolveWithFailure(reference, new BodyScope(member));
    }

    private static NamedSingleTypeRefSingleTypeRefNode Reference(params string[] parts)
    {
        var reference = BindingFragments.At<NamedSingleTypeRefSingleTypeRefNode>(ReferenceOffset);
        reference.Name = new QualifiedIdentifierNode
        {
            IDENTIFIER = BindingFragments.Token(parts[0]),
            IDENTIFIERs = [.. parts.Skip(1).Select(BindingFragments.Token)],
        };
        return reference;
    }

    private static AbstractType Abstract(string name)
    {
        return new AbstractType(name, "Probe", new Scope(null), null, null);
    }
}