using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ConstructorChainAccessibilityTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData(false, Accessibility.Private, true)]
    [InlineData(true, Accessibility.Private, true)]
    [InlineData(false, Accessibility.Public, false)]
    [InlineData(true, Accessibility.Public, false)]
    [InlineData(false, Accessibility.Protected, false)]
    [InlineData(true, Accessibility.Protected, false)]
    public void WrittenBaseChainUsesTheSelectedConstructorsAccessDomain(bool isStruct, Accessibility access, bool refused)
    {
        var fragment = new DeclarationFragments();
        var owner = Type(fragment, "Derived", isStruct);
        var parent = Type(fragment, "Base", isStruct);
        owner.BaseType = parent;
        var target = Declare(parent, access);
        var constructor = Chain(owner, ConstructorChainKind.Base, target);

        Assert.Same(refused ? target : null, AccessibilityAnalyzer.InaccessibleChain(constructor));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateThisChainStaysWithinItsDeclaringType(bool isStruct)
    {
        var fragment = new DeclarationFragments();
        var owner = Type(fragment, "Owner", isStruct);
        var target = Declare(owner, Accessibility.Private);
        var constructor = Chain(owner, ConstructorChainKind.This, target);

        Assert.Null(AccessibilityAnalyzer.InaccessibleChain(constructor));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedConstructorOfAConstructedBaseAdmitsTheDerivedType(bool isStruct)
    {
        var fragment = new DeclarationFragments();
        var owner = Type(fragment, "Derived", isStruct);
        var parent = Type(fragment, "Base", isStruct);
        _ = DeclarationFragments.Parameter(parent, parent.Scope, "T");
        owner.BaseType = new ConstructedType(parent, [fragment.Built.Type("Int32")]);
        var target = Declare(parent, Accessibility.Protected);
        var constructor = Chain(owner, ConstructorChainKind.Base, target);

        Assert.Null(AccessibilityAnalyzer.InaccessibleChain(constructor));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(false)]
    [InlineData(true)]
    public void InternalBaseConstructorUsesTheOwnersAssembly(bool differentAssembly)
    {
        var fragment = new DeclarationFragments();
        var owner = Type(fragment, "Derived", false);
        var parent = Type(fragment, "Base", false);
        owner.BaseType = parent;
        parent.Assembly = new MistAssembly("BaseAssembly", MistAssemblyKind.Reference);
        owner.Assembly = differentAssembly ? new MistAssembly("DerivedAssembly", MistAssemblyKind.Self) : parent.Assembly;
        var target = Declare(parent, Accessibility.Internal);
        var constructor = Chain(owner, ConstructorChainKind.Base, target);

        Assert.Same(differentAssembly ? target : null, AccessibilityAnalyzer.InaccessibleChain(constructor));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void InaccessibleDeclaringTypeLeavesTheFindingToItsTypeNaming()
    {
        var fragment = new DeclarationFragments();
        var outer = fragment.Class("Outer");
        var parent = fragment.Class("Hidden", outer, Accessibility.Private);
        var owner = fragment.Class("Derived");
        owner.BaseType = parent;
        var target = Declare(parent, Accessibility.Private);
        var constructor = Chain(owner, ConstructorChainKind.Base, target);

        Assert.Null(AccessibilityAnalyzer.InaccessibleChain(constructor));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateBaseChainReportsAtTheWrittenChainAndPreservesItsBoundCall(bool isStruct)
    {
        var fragment = new DeclarationFragments();
        var owner = Type(fragment, "Derived", isStruct);
        var parent = Type(fragment, "Base", isStruct);
        owner.BaseType = parent;
        var target = Declare(parent, Accessibility.Private);
        var integer = fragment.Built.Type("Int32");
        target.ParametersList.Add(new Parameter("value", integer));
        _ = Declare(parent, Accessibility.Public);
        var constructor = Chain(owner, ConstructorChainKind.Base, target);
        var argument = HandBuiltModel.Literal(integer, 7L, 60);
        constructor.ChainArgumentsList.Add(argument);
        constructor.ChainParameterTypes = [integer];
        var parameterTypes = constructor.ChainParameterTypes;

        var finding = Assert.Single(fragment.Run(new AccessibilityAnalyzer()));

        Assert.Equal("MIST0159", finding.Number.Value);
        Assert.Equal("hand-built.mist", finding.Location.Path);
        Assert.Equal(41, finding.Position().Start.Column);
        Assert.Equal("'Base' is inaccessible due to its protection level: private members are accessible only within the declaring type.", finding.Message);
        Assert.Same(target, constructor.ChainedConstructor);
        Assert.Same(argument, Assert.Single(constructor.ChainArgumentsList));
        Assert.Same(parameterTypes, constructor.ChainParameterTypes);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false, "MIST0285")]
    [InlineData(true, "MIST0147")]
    public void UnwrittenPrivateBaseChainKeepsItsImplicitChainDiagnostic(bool isStruct, string diagnosticId)
    {
        var fragment = new DeclarationFragments();
        var owner = Type(fragment, "Derived", isStruct);
        var parent = Type(fragment, "Base", isStruct);
        owner.BaseType = parent;
        _ = Declare(parent, Accessibility.Private);
        _ = Declare(owner, Accessibility.Public);

        Assert.Empty(fragment.Run(new AccessibilityAnalyzer()));
        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));
        Assert.Equal(diagnosticId, finding.Number.Value);
        Assert.Equal(11, finding.Position().Start.Column);
    }

    private static ConcreteType Type(DeclarationFragments fragment, string name, bool isStruct)
    {
        if(!isStruct)
        {
            return fragment.Class(name);
        }

        var type = new StructType(name, "Refusal", null, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
        {
            Modifiers = DeclarationFragments.Public,
        };
        fragment.Types.Add(type);
        return type;
    }

    private static Constructor Declare(ConcreteType owner, Accessibility access)
    {
        var constructor = new Constructor(owner.Name, owner, [], DeclarationFragments.Public with { Accessibility = access })
        {
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(10),
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private static Constructor Chain(ConcreteType owner, ConstructorChainKind kind, Constructor target)
    {
        var constructor = new Constructor(owner.Name, owner, [], DeclarationFragments.Public)
        {
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(10),
            ChainKind = kind,
            ChainNode = kind == ConstructorChainKind.This
                ? DeclarationFragments.At<ThisChainConstructorChainNode>(40)
                : DeclarationFragments.At<BaseChainConstructorChainNode>(40),
            ChainedConstructor = target,
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }
}