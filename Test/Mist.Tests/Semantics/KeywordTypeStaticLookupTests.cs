using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class KeywordTypeStaticLookupTests
{
    [Fact]
    public void AKeywordAndItsNominalNameSelectTheSameStaticExtension()
    {
        var f = new BindingFragments();
        var method = ExtensionMethod(f, "Meaning", isStatic: true);

        var keyword = Assert.IsType<InvocationExpression>(f.Bind(Keyword(), Call("Meaning")));
        var nominal = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("Int32"), Call("Meaning")));

        Assert.Same(method, keyword.Method);
        Assert.Same(nominal.Method, keyword.Method);
        var receiver = Assert.IsType<TypeReferenceExpression>(Assert.IsType<MemberAccessExpression>(keyword.Receiver).Receiver);
        Assert.Same(f.Built.Type("Int32"), receiver.ReferencedType);
        Assert.Empty(keyword.Arguments);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ADeclaredStaticPropertyBeatsAnExtensionAndKeepsTheRepresentedReceiver()
    {
        var f = new BindingFragments();
        var represented = f.Built.Type("Int32");
        var property = new Property("Meaning", represented, DeclarationFragments.Public with { IsStatic = true })
        { DeclaringType = represented };
        represented.PropertiesList.Add(property);
        _ = ExtensionMethod(f, "Meaning", isStatic: true);

        var access = Assert.IsType<MemberAccessExpression>(f.Bind(Keyword(), Access("Meaning")));

        Assert.Same(property, Assert.IsType<PropertyResult>(access.Member).Property);
        Assert.Same(represented, Assert.IsType<TypeReferenceExpression>(access.Receiver).ReferencedType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AKeywordWithoutARepresentedStaticMemberKeepsReflectedProperties()
    {
        var f = new BindingFragments();
        var property = ReflectionName(f);

        var access = Assert.IsType<MemberAccessExpression>(f.Bind(Keyword(new StringTypePrimitiveTypeNode()), Access("Name")));

        Assert.Same(property, Assert.IsType<PropertyResult>(access.Member).Property);
        var literal = Assert.IsType<TypeLiteralExpression>(access.Receiver);
        Assert.Same(f.Built.Type("String"), literal.ReferencedType);
        Assert.Same(f.Built.Type("Type"), literal.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnInferredTypeValueLocalKeepsReflectionAndDoesNotBecomeAStaticReceiver()
    {
        var f = new BindingFragments();
        var property = ReflectionName(f);
        _ = ExtensionMethod(f, "Meaning", isStatic: true);
        var declaration = Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = BindingFragments.Token("t"),
                Initializer = BindingFragments.Expression(Keyword()),
            },
        }, f.Binding, f.Builder));

        Assert.Same(f.Built.Type("Type"), declaration.Local.Type);
        Assert.Same(f.Built.Type("Int32"), Assert.IsType<TypeLiteralExpression>(declaration.Initializer).ReferencedType);
        var reflected = Assert.IsType<MemberAccessExpression>(f.Bind(BindingFragments.Identifier("t"), Access("Name")));
        Assert.Same(property, Assert.IsType<PropertyResult>(reflected.Member).Property);
        Assert.Same(declaration.Local, Assert.IsType<LocalReferenceExpression>(reflected.Receiver).Local);
        Assert.Empty(f.Diagnostics.ToImmutableArray());

        var missed = Assert.IsType<MemberAccessExpression>(f.Bind(BindingFragments.Identifier("t"), Access("Meaning")));

        Assert.True(missed.IsFailed);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_MEMBER", diagnostic.Id.Value);
        Assert.Contains("'Type'", diagnostic.Message);
    }

    [Theory]
    [InlineData("declared")]
    [InlineData("extension")]
    [InlineData("property")]
    public void AKeywordStaticRouteDoesNotExposeTheRepresentedInstance(string memberKind)
    {
        var f = new BindingFragments();
        switch(memberKind)
        {
            case "declared":
                _ = BindingFragments.Member(f.Built.Type("Int32"), "OnlyInstance", f.Built.Type("Int32"));
                break;
            case "extension":
                _ = ExtensionMethod(f, "OnlyInstance", isStatic: false);
                break;
            case "property":
                _ = ExtensionProperty(f, "OnlyInstance", isStatic: false);
                break;
        }

        var access = Assert.IsType<MemberAccessExpression>(f.Bind(Keyword(), Access("OnlyInstance")));

        Assert.True(access.IsFailed);
        _ = Assert.IsType<NotFoundResult>(access.Member);
        _ = Assert.IsType<TypeReferenceExpression>(access.Receiver);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_MEMBER", diagnostic.Id.Value);
        Assert.Contains("'int'", diagnostic.Message);
    }

    [Fact]
    public void AStaticExtensionPropertyUsesTheRepresentedTypeReceiver()
    {
        var f = new BindingFragments();
        _ = ExtensionProperty(f, "Meaning", isStatic: false);
        var property = ExtensionProperty(f, "Meaning", isStatic: true);

        var access = Assert.IsType<ExtensionPropertyAccessExpression>(f.Bind(Keyword(), Access("Meaning")));

        Assert.Same(property, access.Property);
        Assert.Same(f.Built.Type("Int32"), Assert.IsType<TypeReferenceExpression>(access.Receiver).ReferencedType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AStaticExtensionPropertyIsExcludedFromAnInstanceReceiver()
    {
        var f = new BindingFragments();
        _ = ExtensionProperty(f, "Meaning", isStatic: true);
        _ = f.Local("value", f.Built.Type("Int32"));

        var access = Assert.IsType<MemberAccessExpression>(f.Bind(BindingFragments.Identifier("value"), Access("Meaning")));

        Assert.True(access.IsFailed);
        _ = Assert.IsType<NotFoundResult>(access.Member);
        Assert.Equal("MIST_PM_UNRESOLVED_MEMBER", Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Fact]
    public void AnUnknownKeywordMemberIsReportedOnceAtTheSuffix()
    {
        var f = new BindingFragments();
        var suffix = Access("NoSuchMember", 55);

        var access = Assert.IsType<MemberAccessExpression>(f.Bind(Keyword(), suffix));

        Assert.True(access.IsFailed);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_MEMBER", diagnostic.Id.Value);
        Assert.Contains("'int'", diagnostic.Message);
        Assert.Contains("'NoSuchMember'", diagnostic.Message);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 56), new LinePosition(1, 57)), diagnostic.Location);
    }

    [Fact]
    public void TheKeywordRouteDoesNotReclassifyOtherTypeLiteralShapes()
    {
        var f = new BindingFragments();
        _ = ExtensionMethod(f, "Meaning", isStatic: true);
        var named = BindingFragments.At<TypeLiteralExpressionPrimaryNode>();
        named.TypeRef = BindingFragments.TypeName("Int32");

        var access = Assert.IsType<MemberAccessExpression>(f.Bind(named, Access("Meaning")));

        _ = Assert.IsType<TypeLiteralExpression>(access.Receiver);
        Assert.True(access.IsFailed);
        Assert.Contains("'Type'", Assert.Single(f.Diagnostics.ToImmutableArray()).Message);
    }

    private static TypeLiteralExpressionPrimaryNode Keyword(PrimitiveTypeNode? primitive = null)
    {
        var node = BindingFragments.At<TypeLiteralExpressionPrimaryNode>();
        node.TypeRef = new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitive ?? new IntTypePrimitiveTypeNode() },
        };
        return node;
    }

    private static MemberAccessSuffixPostfixSuffixNode Access(string name, int offset = 40)
    {
        var node = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>(offset);
        node.Name = BindingFragments.Token(name);
        return node;
    }

    private static MethodCallSuffixPostfixSuffixNode Call(string name)
    {
        var node = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>(40);
        node.Name = BindingFragments.Token(name);
        return node;
    }

    private static Property ReflectionName(BindingFragments f)
    {
        var type = f.Built.Type("Type");
        var property = new Property("Name", f.Built.Type("String"), DeclarationFragments.Public) { DeclaringType = type };
        type.PropertiesList.Add(property);
        return property;
    }

    private static ExtensionBlock Extension(BindingFragments f)
    {
        var represented = f.Built.Type("Int32");
        var block = new ExtensionBlock("self", "", f.Types, null)
        { ResolvedReceiverType = represented, CarrierName = "IntExtensions" };
        var ns = f.Built.Model.GetOrAddNamespace("");
        if(!ns.Extensions.TryGetValue(represented, out var blocks))
        {
            blocks = [];
            ns.Extensions[represented] = blocks;
        }

        blocks.Add(block);
        return block;
    }

    private static Method ExtensionMethod(BindingFragments f, string name, bool isStatic)
    {
        var block = Extension(f);
        var method = new Method(name, f.Built.Type("Int32"), [], DeclarationFragments.Public with { IsStatic = isStatic })
        { Scope = block.Scope, DeclaringBlock = block };
        block.MethodsList.Add(method);
        return method;
    }

    private static Property ExtensionProperty(BindingFragments f, string name, bool isStatic)
    {
        var property = new Property(name, f.Built.Type("Int32"), DeclarationFragments.Public with { IsStatic = isStatic });
        Extension(f).PropertiesList.Add(property);
        return property;
    }
}