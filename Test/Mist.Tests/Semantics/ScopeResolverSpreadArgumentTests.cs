using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ScopeResolverSpreadArgumentTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ARefusedSpreadBecomesAnErrorAtItsOwnPositionAndFailsTheResolution(int position)
    {
        var (f, _) = Fixture();
        var pair = Generic(f, "Pair<,>", TypePackKind.None, TypePackKind.None);
        var reference = BindingFragments.At<NamedSingleTypeRefSingleTypeRefNode>(10);
        reference.Name = BindingFragments.Name("Pair");
        reference.TypeArgs = Arguments(spreadAt: position, count: 2);

        var resolution = f.Builder.Resolver.ResolveWithFailure(new TypeReferenceNode { First = reference }, f.Types);

        Assert.True(resolution.IsFailed);
        var constructed = Assert.IsType<ConstructedType>(resolution.Type);
        Assert.Same(pair, constructed.Definition);
        Assert.Same(ErrorType.Instance, constructed.TypeArguments[position]);
        Assert.Same(f.Built.Type("Int32"), constructed.TypeArguments[1 - position]);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0421", diagnostic.Number.Value);
        Assert.Equal(
            "'Ts...' cannot fill a fixed type-argument slot of 'Pair'. A symbolic spread requires a pack slot that accepts every permitted width.",
            diagnostic.Message);
        Assert.Equal(SpreadOffset(position) + 1, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void ASpreadIsJudgedAgainstTheWrittenArgumentCountNotTheSlotCount(int spreadAt, bool admitted)
    {
        var (f, ts) = Fixture();
        var lead = Generic(f, "Lead<,>", TypePackKind.None, TypePackKind.Unbracketed);

        var arguments = f.Builder.Resolver.ResolveTypeArguments(Arguments(spreadAt, count: 3), f.Types, lead);

        Assert.Equal(3, arguments.Count);
        if(admitted)
        {
            var spread = Assert.IsType<SpreadType>(arguments[spreadAt]);
            Assert.Same(ts, spread.Operand);
            Assert.Empty(f.Diagnostics.ToImmutableArray());
        }
        else
        {
            Assert.Same(ErrorType.Instance, arguments[spreadAt]);
            Assert.Equal("MIST0421", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
        }
    }

    [Fact]
    public void ADefinitionWithoutTypeParametersLeavesTheResolvedArgumentsUnjudged()
    {
        var (f, ts) = Fixture();

        var arguments = f.Builder.Resolver.ResolveTypeArguments(Arguments(spreadAt: 0, count: 2), f.Types, ErrorType.Instance);

        Assert.Equal(2, arguments.Count);
        Assert.Same(ts, Assert.IsType<SpreadType>(arguments[0]).Operand);
        Assert.Same(f.Built.Type("Int32"), arguments[1]);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static (BindingFragments F, TypeParameter Ts) Fixture()
    {
        var f = new BindingFragments();
        var ts = new TypeParameter("Ts", 0, f.Types, null) { PackKind = TypePackKind.Unbracketed };
        f.Register(ts);
        return (f, ts);
    }

    private static ClassType Generic(BindingFragments f, string name, params TypePackKind[] kinds)
    {
        var generic = HandBuiltModel.Class(name);
        for(var i = 0; i < kinds.Length; i++)
        {
            generic.TypeParametersList.Add(new TypeParameter($"T{i}", i, generic.Scope, generic)
            {
                PackKind = kinds[i],
                Domain = kinds[i] == TypePackKind.None ? null : f.Built.Type("Type"),
            });
        }

        f.Register(generic);
        return generic;
    }

    private static int SpreadOffset(int position)
    {
        return 40 + 10 * position;
    }

    private static TypeArgumentListNode Arguments(int spreadAt, int count)
    {
        var arguments = Enumerable.Range(0, count).Select(position =>
        {
            var argument = BindingFragments.At<TypeReferenceTypeArgumentTypeArgumentNode>(SpreadOffset(position) - 5);
            if(position == spreadAt)
            {
                var spread = BindingFragments.At<SpreadSingleTypeRefSingleTypeRefNode>(SpreadOffset(position));
                spread.Name = BindingFragments.Name("Ts");
                argument.Type = new TypeReferenceNode { First = spread };
            }
            else
            {
                argument.Type = BindingFragments.TypeName("Int32");
            }

            return (TypeArgumentNode)argument;
        }).ToList();

        return new TypeArgumentListNode { TypeArgument = arguments[0], TypeArguments = [.. arguments.Skip(1)] };
    }
}