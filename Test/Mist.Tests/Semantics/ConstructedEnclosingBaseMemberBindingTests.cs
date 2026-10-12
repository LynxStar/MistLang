using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ConstructedEnclosingBaseMemberBindingTests
{
    [Theory]
    [InlineData(false, "First", "Int32")]
    [InlineData(false, "Second", "Boolean")]
    [InlineData(false, "FirstProperty", "Int32")]
    [InlineData(false, "SecondProperty", "Boolean")]
    [InlineData(true, "First", "Int32")]
    [InlineData(true, "Second", "Boolean")]
    [InlineData(true, "FirstProperty", "Int32")]
    [InlineData(true, "SecondProperty", "Boolean")]
    public void InheritedStorageUsesItsEnclosingOrOwnArgument(bool isStruct, string memberName, string expected)
    {
        var f = new Fragments(isStruct);

        var access = Assert.IsType<MemberAccessExpression>(f.Bind(
            new MemberAccessSuffixPostfixSuffixNode { Name = BindingFragments.Token(memberName) }));

        Assert.Same(f.Built.Type(expected), access.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInheritedCallUsesBothEnclosingAndOwnArguments(bool isStruct)
    {
        var f = new Fragments(isStruct);

        var call = Assert.IsType<InvocationExpression>(f.Bind(new MethodCallSuffixPostfixSuffixNode
        {
            Name = BindingFragments.Token("Echo"),
            Arguments = BindingFragments.Arguments(BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode())),
        }));

        Assert.Same(f.Echo, call.Method);
        Assert.Same(f.Built.Type("Int32"), call.Type);
        Assert.Same(f.Built.Type("Boolean"), Assert.Single(call.ParameterTypes!));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnInheritedBareNameInsideTheDeriverUsesTheEnclosingArgument()
    {
        var f = new Fragments(isStruct: false);
        var scope = new BodyScope(f.Derived.Scope);

        var reference = Assert.IsType<FieldReferenceExpression>(IdentifierBinder.BindIdentifier(
            BindingFragments.Identifier("First"), new BindingContext(scope, null), f.Context));

        Assert.Same(f.Built.Type("Int32"), reference.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private sealed class Fragments
    {
        public HandBuiltModel Built { get; } = new();

        public DiagnosticBag Diagnostics { get; } = new();

        public BuilderContext Context { get; }

        public BindingContext Binding { get; }

        public ConcreteType Derived { get; }

        public Method Echo { get; }

        public Fragments(bool isStruct)
        {
            Context = new BuilderContext(Built.Model, Diagnostics, NullAmbientTypeProvider.Instance);
            var scope = new BodyScope(new ProgramScope(Built.Model));
            Binding = new BindingContext(scope, null);
            _ = Built.Model.RegisterType(Built.Type("Int32"));
            _ = Built.Model.RegisterType(Built.Type("Boolean"));
            var outer = new ClassType("Outer<>", "Probe", scope, null, null);
            var outerParameter = new TypeParameter("T", 0, outer.Scope, outer);
            outer.TypeParametersList.Add(outerParameter);
            var inner = Type(isStruct, "Inner<>", outer.Scope, outer);
            var ownParameter = new TypeParameter("U", 0, inner.Scope, inner);
            inner.TypeParametersList.Add(ownParameter);
            var modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
            inner.FieldsList.Add(new Field("First", outerParameter, OwnershipKind.Unrestricted, modifiers) { DeclaringType = inner });
            inner.FieldsList.Add(new Field("Second", ownParameter, OwnershipKind.Unrestricted, modifiers) { DeclaringType = inner });
            inner.PropertiesList.Add(new Property("FirstProperty", outerParameter, modifiers) { DeclaringType = inner });
            inner.PropertiesList.Add(new Property("SecondProperty", ownParameter, modifiers) { DeclaringType = inner });
            Echo = new Method("Echo", outerParameter, [new Parameter("value", ownParameter)], modifiers)
            {
                DeclaringType = inner,
                Scope = inner.Scope,
            };
            inner.MethodsList.Add(Echo);
            var outerConstruction = Built.Interner.GetOrCreateConstructedType(outer, [Built.Type("Int32")]);
            Derived = Type(isStruct, "Derived", scope, null);
            Derived.BaseType = Built.Interner.GetOrCreateConstructedType(inner, [Built.Type("Boolean")], outerConstruction);
            scope.DeclareLocal(new LocalVariable("value", Derived), HandBuiltModel.Node(), Diagnostics);
        }

        public Expression Bind(params PostfixSuffixNode[] suffixes)
        {
            return PostfixBinder.BindPostfix(new PostfixNode { Target = BindingFragments.Identifier("value"), Suffix = [.. suffixes] }, Binding, Context);
        }

        private static ConcreteType Type(bool isStruct, string name, Scope scope, MistType? declaring)
        {
            return isStruct
                ? new StructType(name, "Probe", scope, declaring, null)
                : new ClassType(name, "Probe", scope, declaring, null);
        }
    }
}