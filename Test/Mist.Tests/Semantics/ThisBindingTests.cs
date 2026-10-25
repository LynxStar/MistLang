// Tier 2 (Functional): what the binder makes of `this` over hand-built types and scopes. Inside a
// generic declaration it is a value of the interned self construction -- Tag<T>, Bundle<[Ts...]>, or a
// nested definition over its own parameter -- which is what lets it reach a parameter declared the same
// way with no conversion manufactured; a receiver a scope supplies already typed is kept; and a static
// member or a chain with no type is refused with its own diagnostic.

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ThisBindingTests
{
    private readonly HandBuiltModel built = new();

    private readonly DiagnosticBag diagnostics = new();

    private readonly BuilderContext context;

    public ThisBindingTests()
    {
        context = new BuilderContext(built.Model, diagnostics, new EmptyAmbient());
    }

    [Fact]
    public void ThisInAGenericBodyIsTheInternedSelfConstruction()
    {
        var tag = Generic("Tag<>");

        var self = Assert.IsType<ThisExpression>(Bind(InstanceBody(tag.Scope)));

        Assert.Same(built.Interner.GetOrCreateConstructedType(tag, [tag.TypeParametersList[0]]), self.Type);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ThisInANonGenericBodyIsTheDefinition()
    {
        var plain = HandBuiltModel.Class("Plain");

        Assert.Same(plain, Assert.IsType<ThisExpression>(Bind(InstanceBody(plain.Scope))).Type);
    }

    [Fact]
    public void ThisInAPackOwnersBodyFillsTheSlotWithItsOwnSpread()
    {
        var bundle = HandBuiltModel.Class("Bundle<>");
        var domain = built.Type("TypeArray");
        var pack = new TypeParameter("Ts", 0, bundle.Scope, bundle) { PackKind = TypePackKind.Unbracketed, Domain = domain };
        bundle.TypeParametersList.Add(pack);
        var written = built.Interner.GetOrCreateConstructedType(
            bundle, [built.Interner.LiftPack(domain, new TypePackValue([new SpreadType(pack)]))]);

        Assert.Same(written, Assert.IsType<ThisExpression>(Bind(InstanceBody(bundle.Scope))).Type);
    }

    [Fact]
    public void ThisInANestedGenericIsTheNestedDefinitionOverItsOwnParameter()
    {
        var outer = Generic("Outer<>");
        var inner = new ClassType("Inner<>", "Probe", outer.Scope, outer, null);
        inner.TypeParametersList.Add(new TypeParameter("U", 0, inner.Scope, inner));

        var self = Assert.IsType<ConstructedType>(Assert.IsType<ThisExpression>(Bind(InstanceBody(inner.Scope))).Type);

        Assert.Same(inner, self.Definition);
        Assert.Same(inner.TypeParametersList[0], Assert.Single(self.TypeArguments));
        Assert.Null(self.Enclosing);
    }

    [Fact]
    public void ThisReachesAParameterDeclaredAsTheSelfConstruction()
    {
        var tag = Generic("Tag<>");
        var self = built.Interner.GetOrCreateConstructedType(tag, [tag.TypeParametersList[0]]);
        var same = new Method("Same", self, [new Parameter("other", self)], new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            DeclaringType = tag,
            Scope = new MethodScope(tag.Scope) { IsStatic = true },
        };
        tag.MethodsList.Add(same);
        var call = new PostfixNode
        {
            Target = BindingFragments.Identifier("Same"),
            Suffix = [new InvocationSuffixPostfixSuffixNode { Arguments = BindingFragments.Arguments(BindingFragments.Expression(This())) }],
        };

        var invocation = Assert.IsType<InvocationExpression>(PostfixBinder.BindPostfix(call, InstanceBody(tag.Scope), context));

        Assert.Same(same, invocation.Method);
        Assert.Same(self, Assert.IsType<ThisExpression>(Assert.Single(invocation.Arguments)).Type);
        Assert.Same(self, Assert.Single(invocation.ParameterTypes!));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ASuppliedReceiverIsKeptAsTyped()
    {
        var box = Generic("Box<>");
        var supplied = built.Interner.GetOrCreateConstructedType(box, [built.Type("Int32")]);
        var body = new BindingContext(new BodyScope(new MethodScope(new ReceiverScope(box.Scope, supplied))), null);

        Assert.Same(supplied, Assert.IsType<ThisExpression>(Bind(body)).Type);
    }

    [Fact]
    public void ThisInAStaticMemberIsRefused()
    {
        var tag = Generic("Tag<>");
        var body = new BindingContext(new BodyScope(new MethodScope(tag.Scope) { IsStatic = true }), null);

        _ = Assert.IsType<ErrorExpression>(Bind(body));
        Assert.Equal("MIST0372", Assert.Single(diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void ThisOutsideAnyTypeIsRefused()
    {
        var body = new BindingContext(new BodyScope(new MethodScope(new Scope(null))), null);

        _ = Assert.IsType<ErrorExpression>(Bind(body));
        Assert.Equal("MIST0371", Assert.Single(diagnostics.ToImmutableArray()).Number.Value);
    }

    private Expression Bind(BindingContext body)
    {
        return PostfixBinder.BindPostfix(new PostfixNode { Target = This(), Suffix = [] }, body, context);
    }

    private static ThisExpressionPrimaryNode This()
    {
        return BindingFragments.At<ThisExpressionPrimaryNode>();
    }

    private static BindingContext InstanceBody(Scope typeScope)
    {
        return new BindingContext(new BodyScope(new MethodScope(typeScope)), null);
    }

    private static ClassType Generic(string name)
    {
        var type = HandBuiltModel.Class(name);
        type.TypeParametersList.Add(new TypeParameter("T", 0, type.Scope, type));
        return type;
    }

    private sealed class EmptyAmbient : IAmbientTypeProvider
    {
        public void BuildAndRegister(ProgramModel.Model.ProgramModel programModel) { }
    }
}