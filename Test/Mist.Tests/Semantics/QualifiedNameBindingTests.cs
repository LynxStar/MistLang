using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;
using SourceSpan = Storm.Runtime.SourceSpan;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Semantics;

/// <summary>
/// A qualified name in expression position is refused at the segment that resolves to nothing, rather
/// than left for the backend to call unsupported, and a Standard intrinsic named by its CLR namespace and
/// name is that intrinsic in expression and type position alike.
/// </summary>
[Trait("Tier", "2")]
public sealed class QualifiedNameBindingTests
{
    private static readonly ParsedOrigin Source = new(new SourceText("hand-built.mist", new string(' ', 80)));

    [Fact]
    public void AQualifiedNameThatResolvesToNothingIsRefusedAtItsSegment()
    {
        var f = Fragments();

        var bound = f.Bind(BindingFragments.Identifier("System"), Access("NoSuchType", 20));

        _ = Assert.IsType<ErrorExpression>(bound);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_IDENTIFIER", diagnostic.Number.Value);
        Assert.Equal("The name 'System.NoSuchType' could not be found in this scope.", diagnostic.Message);
        Assert.Equal(22, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void ACallThroughAnUnresolvedQualificationIsRefusedOnce()
    {
        var f = Fragments();
        var call = At<MethodCallSuffixPostfixSuffixNode>(31);
        call.Name = MistTypeGenerator.MakeToken("Parse", 32);

        _ = f.Bind(BindingFragments.Identifier("System"), Access("NoSuchType", 20), call);

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_IDENTIFIER", diagnostic.Number.Value);
        Assert.Equal(22, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void AnIntrinsicClrNameInExpressionPositionIsTheStandardType()
    {
        var f = Fragments();

        var bound = f.Bind(BindingFragments.Identifier("System"), Access("Int32", 20));

        Assert.Same(f.Built.Type("Int32"), Assert.IsType<TypeLiteralExpression>(bound).ReferencedType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AMemberTheIntrinsicLacksIsRefusedNamingTheStandardType()
    {
        var f = Fragments();

        _ = f.Bind(BindingFragments.Identifier("System"), Access("Int32", 20), Access("MaxValue", 40));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_MEMBER", diagnostic.Number.Value);
        Assert.Equal("'int' has no member named 'MaxValue'.", diagnostic.Message);
    }

    [Fact]
    public void AnIntrinsicClrNameInTypePositionIsTheStandardType()
    {
        var f = Fragments();
        var name = new QualifiedIdentifierNode
        {
            IDENTIFIER = BindingFragments.Token("System"),
            IDENTIFIERs = [BindingFragments.Token("Int32")],
        };
        var reference = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode { Name = name } };

        var resolved = f.Builder.Resolver.Resolve(reference, f.Scope);

        Assert.Same(f.Built.Type("Int32"), resolved);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static BindingFragments Fragments()
    {
        var f = new BindingFragments();
        _ = f.Built.Model.GetOrAddNamespace("System");
        f.Built.Model.RegisterClrName("System.Int32", f.Built.Type("Int32"));
        return f;
    }

    private static MemberAccessSuffixPostfixSuffixNode Access(string name, int offset)
    {
        var access = At<MemberAccessSuffixPostfixSuffixNode>(offset);
        access.Name = MistTypeGenerator.MakeToken(name, offset + 1);
        return access;
    }

    private static T At<T>(int offset) where T : SyntaxNode, new()
    {
        return new T { Span = new SourceSpan(offset, 1), Origin = Source };
    }
}