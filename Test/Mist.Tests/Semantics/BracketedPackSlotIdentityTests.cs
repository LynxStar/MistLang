using System.Collections.Immutable;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class BracketedPackSlotIdentityTests
{
    [Fact]
    public void EmptyBracketsBindOnePackSlotAndDistinguishTheConstructedType()
    {
        var fixture = new BindingFragments();
        var array = fixture.Built.Type("Array<,>");
        fixture.Register(array);
        var unknownLength = fixture.Built.Interner.GetOrCreateUnknownValue(fixture.Built.Type("Int32"));
        var packDomain = fixture.Built.Interner.GetOrCreateConstructedType(array, [fixture.Built.Type("Type"), unknownLength]);
        var family = HandBuiltModel.Class("Mixed<,,,>");
        family.TypeParametersList.AddRange([
            new TypeParameter("THead", 0, family.Scope, family),
            new TypeParameter("TFirstPack", 1, family.Scope, family) { PackKind = TypePackKind.Bracketed, Domain = packDomain },
            new TypeParameter("TSecondPack", 2, family.Scope, family) { PackKind = TypePackKind.Bracketed, Domain = packDomain },
            new TypeParameter("TTail", 3, family.Scope, family),
        ]);
        fixture.Register(family);

        var empty = Resolve(fixture, firstPack: null);
        var populated = Resolve(fixture, new BracketedPackListNode { TypeReference = BindingFragments.TypeName("Int64") });

        Assert.Same(family, empty.Definition);
        Assert.Collection(empty.TypeArguments,
            head => Assert.Same(fixture.Built.Type("Int32"), head),
            first => Assert.Empty(PackElements(first)),
            second => Assert.Collection(PackElements(second),
                element => Assert.Same(fixture.Built.Type("String"), element),
                element => Assert.Same(fixture.Built.Type("Boolean"), element)),
            tail => Assert.Same(fixture.Built.Type("Double"), tail));

        Assert.Same(family, populated.Definition);
        Assert.Collection(populated.TypeArguments,
            head => Assert.Same(empty.TypeArguments[0], head),
            first => Assert.Same(fixture.Built.Type("Int64"), Assert.Single(PackElements(first))),
            second => Assert.Same(empty.TypeArguments[2], second),
            tail => Assert.Same(empty.TypeArguments[3], tail));
        Assert.NotSame(empty, populated);
    }

    private static ConstructedType Resolve(BindingFragments fixture, BracketedPackListNode? firstPack)
    {
        var reference = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = BindingFragments.Name("Mixed"),
            TypeArgs = new TypeArgumentListNode
            {
                TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = BindingFragments.TypeName("Int32") },
                TypeArguments = [
                    new BracketedPackTypeArgumentTypeArgumentNode { PackElements = firstPack },
                    new BracketedPackTypeArgumentTypeArgumentNode
                    {
                        PackElements = new BracketedPackListNode
                        {
                            TypeReference = BindingFragments.TypeName("String"),
                            TypeReferences = [BindingFragments.TypeName("Boolean")],
                        },
                    },
                    new TypeReferenceTypeArgumentTypeArgumentNode { Type = BindingFragments.TypeName("Double") },
                ],
            },
        };

        return Assert.IsType<ConstructedType>(
            fixture.Builder.Resolver.Resolve(new TypeReferenceNode { First = reference }, fixture.Types));
    }

    private static ImmutableArray<IMistType> PackElements(IMistType argument)
    {
        return Assert.IsType<TypePackValue>(Assert.IsType<LiftedType>(argument).Value).Elements;
    }
}