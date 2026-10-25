using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 2: PostfixBinder over `Name<value>.Member` for hand-built `class Box<N:int>`, `type Alias<M:int> = Box<M>`,
// `type Outer<P:int> = Box<P>` (Alias<P> once expanded) and `type Plain<T> = Box<3>`.
[Trait("Tier", "2")]
public sealed class AliasDependentProjectionBindingTests
{
    private readonly BindingFragments f = new();
    private readonly ClassType box;
    private readonly TypeAlias alias;

    public AliasDependentProjectionBindingTests()
    {
        box = new ClassType("Box<>", "Probe", new Scope(null), null, null) { Modifiers = new MemberModifiers { Accessibility = Accessibility.Public } };
        _ = Dependent(box.TypeParametersList, "N", box);
        TypeParameterDomainResolver.ProjectValues(box);
        f.Register(box);

        alias = Alias("Alias<>", "M");
        _ = Alias("Outer<>", "P");
        var plain = new TypeAlias("Plain<>", "Probe", new Scope(null), null, null!);
        plain.TypeParametersList.Add(new TypeParameter("T", 0, plain.Scope, plain));
        plain.AliasedType = new ConstructedType(box, [new LiftedType(f.Built.Type("Int32"), 3L)]);
        f.Register(plain);
    }

    [Fact]
    public void TheAliasDeclaredNameReadsTheValueTheUseSupplies()
    {
        var access = Assert.IsType<MemberAccessExpression>(Bind("Alias", "7", "M"));
        var found = Assert.IsType<PropertyResult>(access.Member);

        Assert.Same(alias.TypeParametersList[0], found.Property.ProjectedParameter);
        Assert.Equal(7L, Assert.IsType<LiftedType>(
            TypeSubstitution.ForPath(receiver: null, found.BaseConstructions, interner: null).Apply(alias.TypeParametersList[0], interner: null)).Value);
        Assert.Same(f.Built.Type("Int32"), access.Type);
        Assert.Same(box, Assert.IsType<ConstructedType>(Assert.IsType<TypeReferenceExpression>(access.Receiver).ReferencedType).Definition);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void TheTargetsOwnProjectionIsStillReachedThroughTheAlias()
    {
        var access = Assert.IsType<MemberAccessExpression>(Bind("Alias", "7", "N"));

        Assert.Same(box.TypeParametersList[0], Assert.IsType<PropertyResult>(access.Member).Property.ProjectedParameter);
    }

    [Theory]
    [InlineData("Outer", "M")]
    [InlineData("Plain", "T")]
    public void ANameTheAliasDoesNotProject_IsNotFound(string aliasName, string member)
    {
        _ = Bind(aliasName, aliasName == "Plain" ? null : "8", member);

        Assert.Contains(f.Diagnostics.ToImmutableArray(), diagnostic => diagnostic.Number.Value == "MIST_PM_UNRESOLVED_MEMBER");
    }

    private TypeAlias Alias(string name, string parameter)
    {
        var declared = new TypeAlias(name, "Probe", new Scope(null), null, null!);
        var own = Dependent(declared.TypeParametersList, parameter, declared);
        declared.AliasedType = new ConstructedType(box, [own]);
        TypeParameterDomainResolver.ProjectValues(declared);
        f.Register(declared);
        return declared;
    }

    private TypeParameter Dependent(List<TypeParameter> parameters, string name, MistType owner)
    {
        var parameter = new TypeParameter(name, parameters.Count, owner.Scope, owner) { IsDependent = true, Domain = f.Built.Type("Int32") };
        parameters.Add(parameter);
        return parameter;
    }

    private Expression Bind(string typeName, string? value, string member)
    {
        var primary = BindingFragments.At<GenericTypeMemberAccessExpressionPrimaryNode>();
        primary.Type = BindingFragments.Name(typeName);
        primary.Member = BindingFragments.Token(member);
        primary.TypeArgs = value is null
            ? BindingFragments.TypeArguments("Int32")
            : new TypeArgumentListNode { TypeArgument = Literal(value), TypeArguments = [] };

        return f.Bind(primary);
    }

    private static IntegerLiteralTypeArgumentTypeArgumentNode Literal(string value)
    {
        var literal = BindingFragments.At<IntegerLiteralTypeArgumentTypeArgumentNode>();
        literal.Literal = BindingFragments.Token(value);
        return literal;
    }
}