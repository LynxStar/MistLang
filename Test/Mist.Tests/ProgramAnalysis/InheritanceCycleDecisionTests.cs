using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class InheritanceCycleDecisionTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("abstract")]
    public void ATypeCannotInheritFromItself(string kind)
    {
        var type = Type(kind, "Self");
        Inherit(type, type);

        var cycle = Assert.Single(InheritanceCycleAnalyzer.Decide([type]));

        Assert.Same(type, cycle.Owner);
        Assert.Same(type, cycle.Base);
        Assert.Same(Site(type), cycle.Node);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("abstract")]
    public void AnIndirectCycleReportsTheBaseThatClosesTheActivePath(string kind)
    {
        var first = Type(kind, "First");
        var second = Type(kind, "Second");
        Inherit(first, second);
        Inherit(second, first);

        var cycle = Assert.Single(InheritanceCycleAnalyzer.Decide([first, second]));

        Assert.Same(second, cycle.Owner);
        Assert.Same(first, cycle.Base);
        Assert.Same(Site(second), cycle.Node);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("abstract")]
    public void ConstructedArgumentsCannotHideACycleInTheDefinitions(string kind)
    {
        var first = Type(kind, "First<>");
        var second = Type(kind, "Second<>");
        var a = new TypeParameter("A", 0, first.Scope, first);
        var b = new TypeParameter("B", 0, second.Scope, second);
        first.TypeParametersList.Add(a);
        second.TypeParametersList.Add(b);
        var firstBase = new ConstructedType(second, [a]);
        var closingBase = new ConstructedType(first, [new ConstructedType(second, [b])]);
        Inherit(first, firstBase);
        Inherit(second, closingBase);

        var cycle = Assert.Single(InheritanceCycleAnalyzer.Decide([first, second]));

        Assert.Same(second, cycle.Owner);
        Assert.Same(closingBase, cycle.Base);
        Assert.Same(Site(second), cycle.Node);
    }

    [Fact]
    public void ASharedAbstractAncestorInADiamondIsNotACycle()
    {
        var root = Type("abstract", "Root");
        var left = Type("abstract", "Left");
        var right = Type("abstract", "Right");
        var shared = Type("abstract", "Shared");
        Inherit(root, left);
        Inherit(root, right);
        Inherit(left, shared);
        Inherit(right, shared);

        Assert.Empty(InheritanceCycleAnalyzer.Decide([root, left, right, shared]));
    }

    [Fact]
    public void DistinctConstructionsOfASharedDefinitionAreNotCycles()
    {
        var root = Type("abstract", "Root");
        var shared = Type("abstract", "Shared<>");
        var built = new HandBuiltModel();
        Inherit(root, new ConstructedType(shared, [built.Type("Int32")]));
        Inherit(root, new ConstructedType(shared, [built.Type("Boolean")]));

        Assert.Empty(InheritanceCycleAnalyzer.Decide([root, shared]));
    }

    [Fact]
    public void ATypeLeadingIntoACycleDoesNotHideTheCyclicBase()
    {
        var tail = Type("class", "Tail");
        var first = Type("class", "First");
        var second = Type("class", "Second");
        Inherit(tail, first);
        Inherit(first, second);
        Inherit(second, first);

        var cycle = Assert.Single(InheritanceCycleAnalyzer.Decide([tail]));

        Assert.Same(second, cycle.Owner);
        Assert.Same(first, cycle.Base);
    }

    [Fact]
    public void EveryDisjointCycleIsReported()
    {
        var first = Type("class", "First");
        var second = Type("struct", "Second");
        Inherit(first, first);
        Inherit(second, second);

        var cycles = InheritanceCycleAnalyzer.Decide([first, second]);

        Assert.Equal([first, second], cycles.Select(cycle => cycle.Owner));
    }

    [Fact]
    public void AnAbstractCycleReachedFromAConcreteImplementationIsRefused()
    {
        var root = Type("class", "Root");
        var first = Type("abstract", "First");
        var second = Type("abstract", "Second");
        root.BaseAbstractTypes.Add(first);
        Inherit(first, second);
        Inherit(second, first);

        var cycle = Assert.Single(InheritanceCycleAnalyzer.Decide([root]));

        Assert.Same(second, cycle.Owner);
        Assert.Same(first, cycle.Base);
    }

    internal static MemberType Type(string kind, string name)
    {
        var first = BindingFragments.At<BaseTypeRefNode>();
        first.Name = BindingFragments.Name("Base");
        var bases = BindingFragments.At<BaseListNode>(20);
        bases.First = first;
        bases.BaseTypeRefs = [];
        var declaration = BindingFragments.At<BodyTypeTypeDeclarationNode>(10);
        declaration.TypeBody = new TypeBodyDeclarationNode { BaseList = bases };
        return kind switch
        {
            "class" => new ClassType(name, "Probe", new Scope(null), null, declaration),
            "struct" => new StructType(name, "Probe", new Scope(null), null, declaration),
            "abstract" => new AbstractType(name, "Probe", new Scope(null), null, declaration),
            _ => throw new ArgumentException("Unknown type kind", nameof(kind)),
        };
    }

    private static void Inherit(MemberType type, MistType inherited)
    {
        if(type is ConcreteType concrete)
        {
            concrete.BaseType = inherited;
        }
        else
        {
            type.BaseAbstractTypes.Add(inherited);
        }
    }

    private static Storm.Runtime.SyntaxNode Site(MemberType type)
    {
        var bases = ((BodyTypeTypeDeclarationNode)type.DeclarationNode!).TypeBody.BaseList!;
        return type is ConcreteType ? bases.First : bases;
    }
}