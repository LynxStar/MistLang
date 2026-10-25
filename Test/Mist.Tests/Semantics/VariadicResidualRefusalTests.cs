using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class VariadicResidualRefusalTests
{
    [Fact]
    public void SpreadSuffixOnValueParameterIsRefusedAtItsDeclaration()
    {
        var f = Fixture();
        var node = BindingFragments.At<ParameterNode>(20);
        node.Name = BindingFragments.Token("args");
        node.Type = Spread();

        var parameter = Assert.Single(new ParameterBuilder(f.Builder.Resolver, f.Builder.Attributes, f.Diagnostics)
            .Resolve(new ParameterListNode { Parameter = node }, f.Types));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_SPREAD_VALUE_PARAMETER", diagnostic.Number.Value);
        Assert.Contains("params", diagnostic.Message);
        Assert.Equal(21, diagnostic.Location.Start.Column);
        Assert.True(parameter.IsFailed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TypeArgumentSpreadNeedsAnAbsorbingSlot(bool acceptsPack, bool construction)
    {
        var f = Fixture();
        var family = HandBuiltModel.Class("Family<>");
        family.TypeParametersList.Add(new TypeParameter("T", 0, family.Scope, family)
        {
            PackKind = acceptsPack ? TypePackKind.Unbracketed : TypePackKind.None,
            Domain = f.Built.Type("Type"),
        });
        f.Register(family);
        var syntax = BindingFragments.At<NamedSingleTypeRefSingleTypeRefNode>();
        syntax.Name = BindingFragments.Name("Family");
        syntax.TypeArgs = new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = Spread() },
        };

        if(construction)
        {
            var creation = BindingFragments.At<ObjectCreationExpressionPrimaryNode>();
            creation.Type = syntax.Name;
            creation.TypeArgs = syntax.TypeArgs;
            _ = f.Bind(creation);
        }
        else
        {
            _ = f.Builder.Resolver.Resolve(new TypeReferenceNode { First = syntax }, f.Types);
        }

        if(acceptsPack)
        {
            Assert.Empty(f.Diagnostics.ToImmutableArray());
        }
        else
        {
            var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
            Assert.Equal("MIST_PM_SPREAD_NON_PACK_ARGUMENT", diagnostic.Number.Value);
            Assert.Equal(41, diagnostic.Location.Start.Column);
        }
    }

    [Theory]
    [InlineData("call")]
    [InlineData("group")]
    [InlineData("member-call")]
    [InlineData("member-group")]
    [InlineData("optional-call")]
    public void MethodTypeArgumentSpreadIsRefusedAtTheSpread(string route)
    {
        var f = Fixture();
        var owner = HandBuiltModel.Class("Api");
        var method = BindingFragments.Member(owner, "One", f.Built.Type("Int32"));
        method.TypeParametersList.Add(new TypeParameter("T", 0, method.Scope!, null));
        f.Binding.Scope = new BodyScope(new TypeScope(f.Scope, owner));
        _ = f.Local("api", route == "optional-call"
            ? f.Built.Interner.GetOrCreateUnionType([owner, f.Built.Type("Void")])
            : owner);
        var arguments = new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = Spread() },
        };
        using var target = f.Binding.PushTargetType(new FunctionType([], f.Built.Type("Int32")));

        var result = route switch
        {
            "call" => f.Bind(new GenericInvocationExpressionPrimaryNode
            {
                Name = BindingFragments.Token("One"),
                TypeArgs = arguments,
            }),
            "group" => f.Bind(new GenericNameExpressionPrimaryNode
            {
                Name = BindingFragments.Token("One"),
                TypeArgs = arguments,
            }),
            "member-call" => f.Bind(BindingFragments.Identifier("api"), new MethodCallSuffixPostfixSuffixNode
            {
                Name = BindingFragments.Token("One"),
                TypeArgs = arguments,
            }),
            "member-group" => f.Bind(BindingFragments.Identifier("api"), new GenericMethodGroupSuffixPostfixSuffixNode
            {
                Name = BindingFragments.Token("One"),
                TypeArgs = arguments,
            }),
            _ => f.Bind(BindingFragments.Identifier("api"), new OptionalMethodCallSuffixPostfixSuffixNode
            {
                Name = BindingFragments.Token("One"),
                TypeArgs = arguments,
            }),
        };

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_SPREAD_NON_PACK_ARGUMENT", diagnostic.Number.Value);
        Assert.Equal(41, diagnostic.Location.Start.Column);
        Assert.True(PriorFailure.Of(result));
        Assert.Empty(f.Built.Model.GenericCalls.All());
    }

    [Fact]
    public void MethodSpreadKeepsItsClosingSymbolicBesideAFixedOverload()
    {
        var f = Fixture();
        var owner = HandBuiltModel.Class("Api");
        var fixedMethod = BindingFragments.Member(owner, "One", f.Built.Type("Int32"), f.Built.Type("Int32"));
        fixedMethod.TypeParametersList.Add(new TypeParameter("T", 0, fixedMethod.Scope!, null));
        var packMethod = BindingFragments.Member(owner, "One", f.Built.Type("Int32"));
        packMethod.TypeParametersList.Add(new TypeParameter("Us", 0, packMethod.Scope!, null)
        {
            PackKind = TypePackKind.Unbracketed,
        });
        f.Binding.Scope = new BodyScope(new TypeScope(f.Scope, owner));

        var result = f.Bind(new GenericInvocationExpressionPrimaryNode
        {
            Name = BindingFragments.Token("One"),
            TypeArgs = new TypeArgumentListNode
            {
                TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = Spread() },
            },
        });

        var invocation = Assert.IsType<InvocationExpression>(result);
        Assert.Same(packMethod, invocation.Method);
        var recorded = Assert.Single(f.Built.Model.GenericCalls.All());
        var closing = Assert.IsType<LiftedType>(Assert.Single(recorded.TypeArguments));
        var elements = Assert.IsType<TypePackValue>(closing.Value).Elements;
        _ = Assert.IsType<SpreadType>(Assert.Single(elements));
        Assert.Empty(f.Built.Model.VariadicUses.All());
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static BindingFragments Fixture()
    {
        var f = new BindingFragments();
        f.Register(new TypeParameter("Ts", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed });
        return f;
    }

    private static TypeReferenceNode Spread()
    {
        var spread = BindingFragments.At<SpreadSingleTypeRefSingleTypeRefNode>(40);
        spread.Name = BindingFragments.Name("Ts");
        return new TypeReferenceNode { First = spread };
    }
}