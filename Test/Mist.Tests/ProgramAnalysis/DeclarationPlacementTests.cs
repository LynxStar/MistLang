using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class DeclarationPlacementTests
{
    private static MemberModifiers Static => DeclarationFragments.Public with { IsStatic = true };

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bodiless-method", "method", "Make")]
    [InlineData("bodied-method", "method", "Make")]
    [InlineData("property", "property", "Count")]
    [InlineData("indexer", "indexer", "this[]")]
    public void AStaticMemberOfAnAbstractIsRefused(string member, string kind, string name)
    {
        var fragment = new DeclarationFragments();
        var abstraction = fragment.Abstract("IMake");
        Declare(fragment, abstraction, member, Static);

        var refused = Assert.Single(DeclarationPlacementAnalyzer.StaticMembers(abstraction));

        Assert.Equal(kind, refused.Kind);
        Assert.Equal(name, refused.Name);
        Assert.NotNull(refused.Declaration);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bodiless-method")]
    [InlineData("bodied-method")]
    [InlineData("property")]
    [InlineData("indexer")]
    public void AnInstanceMemberOfAnAbstractIsAdmitted(string member)
    {
        var fragment = new DeclarationFragments();
        var abstraction = fragment.Abstract("IMake");
        Declare(fragment, abstraction, member, DeclarationFragments.Public);

        Assert.Empty(DeclarationPlacementAnalyzer.StaticMembers(abstraction));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AStaticMemberOfAProjectedClrInterfaceIsAdmitted()
    {
        var fragment = new DeclarationFragments();
        var projected = fragment.Abstract("IParsable");
        projected.Modifiers = projected.Modifiers with { IsClrProjected = true };
        Declare(fragment, projected, "bodiless-method", Static);

        Assert.Empty(DeclarationPlacementAnalyzer.StaticMembers(projected));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void EachStaticMemberOfAnAbstractIsReportedOnceAtItsDeclaration()
    {
        var fragment = new DeclarationFragments();
        var abstraction = fragment.Abstract("IMake");
        Declare(fragment, abstraction, "bodied-method", Static, offset: 30);
        Declare(fragment, abstraction, "property", Static, offset: 50);
        Declare(fragment, abstraction, "bodiless-method", DeclarationFragments.Public, offset: 70);

        var diagnostics = fragment.Run(new DeclarationPlacementAnalyzer());

        Assert.Equal(["MIST_PM_STATIC_ON_ABSTRACT_MEMBER", "MIST_PM_STATIC_ON_ABSTRACT_MEMBER"], diagnostics.Select(d => d.Id.Value));
        Assert.Equal([31, 51], diagnostics.Select(d => d.Location.Start.Column).Order());
        Assert.Contains(diagnostics, d => d.Message.Contains("method 'Make' of abstract type 'IMake'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Message.Contains("property 'Count' of abstract type 'IMake'", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("binary-first", "a")]
    [InlineData("binary-second", "b")]
    [InlineData("binary-both", "a,b")]
    [InlineData("unary", "a")]
    [InlineData("implicit-cast", "a")]
    [InlineData("explicit-cast", "a")]
    public void ABareRefOperatorParameterIsRefused(string callable, string refused)
    {
        var vec = HandBuiltModel.Struct("Vec");

        var parameters = DeclarationPlacementAnalyzer.RefParameters(Callable(vec, callable, ByRef));

        Assert.Equal(refused.Split(','), parameters.Select(parameter => parameter.Name));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("binary-first")]
    [InlineData("unary")]
    [InlineData("implicit-cast")]
    public void AByValueOperatorParameterIsAdmitted(string callable)
    {
        var vec = HandBuiltModel.Struct("Vec");

        Assert.Empty(DeclarationPlacementAnalyzer.RefParameters(Callable(vec, callable, default)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AReadonlyRefOperatorParameterIsRefused()
    {
        var vec = HandBuiltModel.Struct("Vec");

        var refused = Assert.Single(DeclarationPlacementAnalyzer.RefParameters(Callable(vec, "binary-first", ReadonlyRef)));

        Assert.Equal("a", refused.Name);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AReadonlyRefOperatorParameterIsReportedWithItsOwnDiagnostic()
    {
        var fragment = new DeclarationFragments();
        var vec = HandBuiltModel.Struct("Vec");
        fragment.Types.Add(vec);
        _ = Callable(vec, "implicit-cast", ReadonlyRef, offset: 30);

        var diagnostic = Assert.Single(fragment.Run(new DeclarationPlacementAnalyzer()));

        Assert.Equal("MIST_PM_READONLY_REF_ON_OPERATOR_PARAMETER", diagnostic.Id.Value);
        Assert.Equal(31, diagnostic.Location.Start.Column);
        Assert.Contains("parameter 'a' of the implicit cast operator to 'int'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ARefMethodParameterIsAdmitted()
    {
        var vec = HandBuiltModel.Struct("Vec");
        var bump = new Method("Bump", vec, [new Parameter("v", vec, ByRef)], Static) { DeclaringType = vec };

        Assert.Empty(DeclarationPlacementAnalyzer.RefParameters(bump));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void EachRefOperatorParameterIsReportedOnceAtTheParameter()
    {
        var fragment = new DeclarationFragments();
        var vec = HandBuiltModel.Struct("Vec");
        fragment.Types.Add(vec);
        _ = Callable(vec, "binary-both", ByRef, offset: 30);
        _ = Callable(vec, "explicit-cast", ByRef, offset: 60);

        var diagnostics = fragment.Run(new DeclarationPlacementAnalyzer());

        Assert.All(diagnostics, d => Assert.Equal("MIST_PM_REF_ON_OPERATOR_PARAMETER", d.Id.Value));
        Assert.Equal([31, 41, 61], diagnostics.Select(d => d.Location.Start.Column).Order());
        Assert.Contains(diagnostics, d => d.Message.Contains("parameter 'b' of operator '+'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Message.Contains("parameter 'a' of the explicit cast operator to 'int'", StringComparison.Ordinal));
    }

    private static ParameterModifier ByRef => new(IsReadonly: false, IsRef: true, OwnershipKind.Unrestricted, ByRefDirection.Ref);

    private static ParameterModifier ReadonlyRef => new(IsReadonly: true, IsRef: true, OwnershipKind.Unrestricted, ByRefDirection.In);

    private static Method Callable(StructType vec, string callable, ParameterModifier modifier, int offset = 20)
    {
        var first = callable is "binary-second" ? default : modifier;
        var second = callable is "binary-second" or "binary-both" ? modifier : default;
        var a = new Parameter("a", vec, first) { DeclarationNode = DeclarationFragments.At<ParameterNode>(offset) };
        var b = new Parameter("b", vec, second) { DeclarationNode = DeclarationFragments.At<ParameterNode>(offset + 10) };

        if(callable.EndsWith("-cast", StringComparison.Ordinal))
        {
            var cast = new CastOperator(new HandBuiltModel().Type("Int32"), a, callable == "implicit-cast", Static) { DeclaringType = vec };
            vec.CastOperatorsList.Add(cast);
            return cast;
        }

        var op = callable == "unary"
            ? new Operator(OperatorKind.Minus, vec, [a], Static) { DeclaringType = vec }
            : new Operator(OperatorKind.Plus, vec, [a, b], Static) { DeclaringType = vec };
        vec.OperatorsList.Add(op);
        return op;
    }

    private static void Declare(DeclarationFragments fragment, AbstractType owner, string member, MemberModifiers modifiers, int offset = 20)
    {
        var integer = fragment.Built.Type("Int32");

        switch(member)
        {
            case "bodiless-method":
            case "bodied-method":
                owner.MethodsList.Add(new Method("Make", integer, [], modifiers)
                {
                    DeclaringType = owner,
                    DeclarationNode = HandBuiltModel.Node(offset),
                    Body = member == "bodied-method" ? DeclarationFragments.Returning(HandBuiltModel.Literal(integer, 5L)) : null,
                });
                break;

            case "property":
                owner.PropertiesList.Add(new Property("Count", integer, modifiers)
                {
                    DeclaringType = owner,
                    DeclarationNode = HandBuiltModel.Node(offset),
                    Getter = new PropertyAccessor(),
                });
                break;

            default:
                owner.IndexersList.Add(new Indexer(integer, [new Parameter("i", integer)], modifiers)
                {
                    DeclaringType = owner,
                    DeclarationNode = HandBuiltModel.Node(offset),
                });
                break;
        }
    }
}