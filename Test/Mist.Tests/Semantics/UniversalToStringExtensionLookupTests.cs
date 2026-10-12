using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class UniversalToStringExtensionLookupTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FormattingOverloadsKeepAUniversalZeroArgumentCandidate(int arity)
    {
        var fixture = new Fixture();
        var text = fixture.Fragments.Built.Type("String");
        var provider = HandBuiltModel.Class("IFormatProvider");
        var parameterTypes = arity == 1 ? new IMistType[] { text } : [text, provider];
        var formatting = BindingFragments.Member(fixture.Temp, "ToString", text, parameterTypes);

        var plain = fixture.Call();
        var formatted = fixture.Call(parameterTypes);

        Assert.Same(fixture.Universal, plain.Method);
        Assert.Same(formatting, formatted.Method);
        Assert.Same(text, plain.Type);
        Assert.Same(text, formatted.Type);
        Assert.Empty(fixture.Fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ADeclaredZeroArgumentImplementationKeepsItsIdentity()
    {
        var fixture = new Fixture();
        var text = fixture.Fragments.Built.Type("String");
        _ = BindingFragments.Member(fixture.Temp, "ToString", text, text);
        var own = BindingFragments.Member(fixture.Temp, "ToString", text);

        Assert.Same(own, fixture.Call().Method);
        Assert.DoesNotContain(fixture.Universal, fixture.Lookup().Methods);
        Assert.Empty(fixture.Fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnInheritedZeroArgumentImplementationKeepsItsIdentity()
    {
        var fixture = new Fixture();
        var parent = HandBuiltModel.Class("Parent");
        fixture.Temp.BaseType = parent;
        var text = fixture.Fragments.Built.Type("String");
        var inherited = BindingFragments.Member(parent, "ToString", text);
        _ = BindingFragments.Member(fixture.Temp, "ToString", text, text);

        Assert.Same(inherited, fixture.Call().Method);
        Assert.DoesNotContain(fixture.Universal, fixture.Lookup().Methods);
        Assert.Empty(fixture.Fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void MergingTheUniversalCandidatePreservesInheritedGenericPaths()
    {
        var fixture = new Fixture();
        var parent = HandBuiltModel.Class("Parent<>");
        var parameter = new TypeParameter("T", 0, parent.Scope, parent);
        parent.TypeParametersList.Add(parameter);
        var integer = fixture.Fragments.Built.Type("Int32");
        var construction = fixture.Fragments.Built.Interner.GetOrCreateConstructedType(parent, [integer]);
        fixture.Temp.BaseType = construction;
        var formatting = BindingFragments.Member(parent, "ToString", fixture.Fragments.Built.Type("String"), parameter);

        var group = fixture.Lookup();
        Assert.NotNull(group.Paths);
        Assert.Same(construction, Assert.Single(group.Paths[formatting]));
        Assert.False(group.Paths.ContainsKey(fixture.Universal));

        var call = fixture.Call(integer);

        Assert.Same(formatting, call.Method);
        Assert.Same(integer, Assert.Single(call.ParameterTypes!));
        Assert.Same(fixture.Universal, fixture.Call().Method);
        Assert.Empty(fixture.Fragments.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("primitive")]
    [InlineData("unknown")]
    public void ReceiversWithoutOwnMembersKeepUniversalLookup(string receiverKind)
    {
        var fixture = new Fixture();
        IMistType receiver = receiverKind == "primitive" ? fixture.Fragments.Built.Type("Double") : UnknownType.Instance;
        _ = fixture.Fragments.Local("value", receiver);

        var call = fixture.CallOn("value", "ToString");

        Assert.Same(fixture.Universal, call.Method);
        Assert.Empty(fixture.Fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void TheMergeAddsOnlyTheUniversalZeroArgumentShape()
    {
        var fixture = new Fixture();
        var text = fixture.Fragments.Built.Type("String");
        var own = BindingFragments.Member(fixture.Temp, "ToString", text, text);
        _ = fixture.Extension(UnknownType.Instance, parameters: [text]);
        _ = fixture.Extension(fixture.Temp);

        Assert.Equal(new[] { own, fixture.Universal }, fixture.Lookup().Methods);
    }

    [Fact]
    public void AnUnimportedUniversalBlockDoesNotWidenOwnLookup()
    {
        var fixture = new Fixture();
        fixture.Fragments.Types.WithdrawImport("Standard");
        var text = fixture.Fragments.Built.Type("String");
        var own = BindingFragments.Member(fixture.Temp, "ToString", text, text);

        Assert.Same(own, Assert.Single(fixture.Lookup().Methods));
    }

    [Fact]
    public void StaticLookupKeepsOnlyDeclaredStaticMembers()
    {
        var fixture = new Fixture();
        var text = fixture.Fragments.Built.Type("String");
        var own = new Method("ToString", text, [new Parameter("format", text)], DeclarationFragments.Public with { IsStatic = true })
        {
            DeclaringType = fixture.Temp,
        };
        fixture.Temp.MethodsList.Add(own);

        Assert.Same(own, Assert.Single(fixture.Lookup(visibility: MemberVisibility.StaticOnly).Methods));
    }

    [Fact]
    public void OtherNamesKeepOrdinaryOwnMemberPrecedence()
    {
        var fixture = new Fixture();
        var text = fixture.Fragments.Built.Type("String");
        var own = BindingFragments.Member(fixture.Temp, "Describe", text, text);
        _ = fixture.Extension(UnknownType.Instance, "Describe");

        Assert.Same(own, Assert.Single(fixture.Lookup("Describe").Methods));
    }

    private sealed class Fixture
    {
        public BindingFragments Fragments { get; } = new();
        public ClassType Temp { get; } = HandBuiltModel.Class("Temp");
        public Method Universal { get; }

        public Fixture()
        {
            Fragments.Register(Temp);
            Fragments.Types.DeclareImport("Standard");
            _ = Fragments.Local("temp", Temp);
            Universal = Extension(UnknownType.Instance);
        }

        public Method Extension(IMistType receiver, string name = "ToString", params IMistType[] parameters)
        {
            var block = new ExtensionBlock("self", "Standard", new Scope(null), null) { ResolvedReceiverType = receiver };
            var method = new Method(name, Fragments.Built.Type("String"),
                parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(), DeclarationFragments.Public)
            {
                DeclaringBlock = block,
            };
            block.MethodsList.Add(method);
            var registered = Fragments.Built.Model.GetOrAddNamespace("Standard").Extensions;
            if(!registered.TryGetValue(receiver, out var blocks))
            {
                blocks = [];
                registered.Add(receiver, blocks);
            }

            blocks.Add(block);
            return method;
        }

        public MethodGroupResult Lookup(string name = "ToString", MemberVisibility visibility = MemberVisibility.InstanceOnly)
        {
            return Assert.IsType<MethodGroupResult>(MemberResolution.Resolve(
                Temp, name, visibility, HandBuiltModel.Node(), Fragments.Builder, Fragments.Scope).Found);
        }

        public InvocationExpression Call(params IMistType[] parameters)
        {
            var arguments = parameters.Select((type, i) =>
            {
                var name = $"argument{i}";
                _ = Fragments.Local(name, type);
                return BindingFragments.Expression(BindingFragments.Identifier(name));
            }).ToArray();
            return CallOn("temp", "ToString", arguments);
        }

        public InvocationExpression CallOn(string receiver, string name, params ExpressionNode[] arguments)
        {
            return Assert.IsType<InvocationExpression>(Fragments.Bind(BindingFragments.Identifier(receiver), new MethodCallSuffixPostfixSuffixNode
            {
                Name = BindingFragments.Token(name),
                Arguments = BindingFragments.Arguments(arguments),
            }));
        }
    }
}