using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ImplicitChainsTests
{
    private readonly HandBuiltModel built = new();
    private readonly Dictionary<Constructor, string> specs = new(ReferenceEqualityComparer.Instance);

    [Theory]
    [InlineData("", "the base itself")]
    [InlineData("static:none", "the base itself")]
    [InlineData("public:none", "public:none")]
    [InlineData("public:params", "public:params")]
    [InlineData("public:params public:none", "public:none")]
    [InlineData("protected:none public:int", "protected:none")]
    [InlineData("internal:none", "internal:none")]
    [InlineData("private:none public:params", "public:params")]
    public void UnchainedConstructor_RunsWhatAnImplicitBaseCallReaches(string constructors, string expected)
    {
        var (derived, baseType) = Derivation(constructors);

        Assert.Equal(expected, Describe(ImplicitChains.Of(derived), baseType));
        Assert.Null(ImplicitChains.UnsatisfiedBase(derived.ConstructorsList[0]));
    }

    [Theory]
    [InlineData("public:int")]
    [InlineData("public:int public:int,int")]
    [InlineData("private:none")]
    [InlineData("private:none public:int")]
    public void UnchainedConstructorOverABaseItCanCallNothingIn_IsUnsatisfied(string constructors)
    {
        var (derived, baseType) = Derivation(constructors);

        Assert.Null(ImplicitChains.Of(derived));
        Assert.Same(baseType, ImplicitChains.UnsatisfiedBase(derived.ConstructorsList[0]));
    }

    [Fact]
    public void InternalParameterlessConstructorOfAnotherAssembly_IsUnsatisfied()
    {
        var (derived, baseType) = Derivation("internal:none");
        baseType.Assembly = new MistAssembly("Gauges", MistAssemblyKind.Reference);

        Assert.Same(baseType, ImplicitChains.UnsatisfiedBase(derived.ConstructorsList[0]));
    }

    [Fact]
    public void PrivateParameterlessConstructor_IsReachedFromADerivationNestedInTheBase()
    {
        var (derived, baseType) = Derivation("private:none public:int");
        var nested = new ClassType("Needle", "Probe", new Scope(null), baseType, null) { BaseType = baseType };
        nested.ConstructorsList.Add(Unchained(nested));

        Assert.Same(baseType.ConstructorsList[0], ImplicitChains.Of(nested));
        Assert.Null(ImplicitChains.UnsatisfiedBase(nested.ConstructorsList[0]));
        Assert.Same(baseType, ImplicitChains.UnsatisfiedBase(derived.ConstructorsList[0]));
    }

    [Theory]
    [InlineData("base-chain")]
    [InlineData("this-chain")]
    [InlineData("static-constructor")]
    [InlineData("ambient-base")]
    [InlineData("intrinsic-base")]
    [InlineData("derived-struct")]
    public void ConstructorThisRuleDoesNotJudge_IsNeverUnsatisfied(string shape)
    {
        var (derived, baseType) = Derivation("public:int");
        var constructor = shape switch
        {
            "base-chain" => new Constructor("Dial", derived, [], Public) { DeclaringType = derived, ChainKind = ConstructorChainKind.Base },
            "this-chain" => new Constructor("Dial", derived, [], Public) { DeclaringType = derived, ChainKind = ConstructorChainKind.This },
            "static-constructor" => new Constructor("Dial", derived, [], Public with { IsStatic = true }) { DeclaringType = derived },
            _ => derived.ConstructorsList[0],
        };

        if(shape == "ambient-base")
        {
            baseType.Assembly = MistAssembly.Ambient;
        }

        if(shape == "intrinsic-base")
        {
            baseType.Modifiers = Public with { IsIntrinsic = true };
        }

        if(shape == "derived-struct")
        {
            var structBase = HandBuiltModel.Struct("Meter");
            structBase.ConstructorsList.Add(BaseConstructor(structBase, "public:int"));
            var structDerived = HandBuiltModel.Struct("Dial");
            structDerived.BaseType = structBase;
            constructor = Unchained(structDerived);
        }

        Assert.Null(ImplicitChains.UnsatisfiedBase(constructor));
    }

    [Theory]
    [InlineData("public:int", true)]
    [InlineData("public:none", false)]
    public void ConstructedGenericBase_IsJudgedByItsDefinitionsConstructors(string constructors, bool unsatisfied)
    {
        var definition = HandBuiltModel.Class("BaseContainer");
        definition.ConstructorsList.Add(BaseConstructor(definition, constructors));
        var derived = HandBuiltModel.Class("DerivedContainer");
        var parameter = DeclarationFragments.Parameter(derived, derived.Scope, "T");
        var constructed = built.Interner.GetOrCreateConstructedType(definition, [parameter]);
        derived.BaseType = constructed;
        derived.ConstructorsList.Add(Unchained(derived));

        if(unsatisfied)
        {
            Assert.Same(constructed, ImplicitChains.UnsatisfiedBase(derived.ConstructorsList[0]));
            Assert.Null(ImplicitChains.Of(derived));
        }
        else
        {
            Assert.Null(ImplicitChains.UnsatisfiedBase(derived.ConstructorsList[0]));
            Assert.Same(definition.ConstructorsList[0], ImplicitChains.Of(derived));
        }
    }

    [Theory]
    [InlineData("public:pack", "[]", "public:pack")]
    [InlineData("public:pack", "[int]", "nothing")]
    [InlineData("public:pack", "[int, string]", "nothing")]
    [InlineData("public:pack", "Us...", "nothing")]
    [InlineData("public:pack", "Us", "nothing")]
    [InlineData("public:pack public:none", "[int]", "public:none")]
    [InlineData("public:int public:pack", "[]", "public:pack")]
    [InlineData("private:pack", "[]", "nothing")]
    public void UnchainedConstructorOverAPackTakingBase_RunsItOnlyWhereTheBaseClauseClosesThePackEmpty(
        string constructors, string closing, string expected)
    {
        var (derived, baseType, written) = PackDerivation(constructors, closing);

        Assert.Equal(expected, Describe(ImplicitChains.Of(derived), baseType));

        if(expected == "nothing")
        {
            Assert.Same(written, ImplicitChains.UnsatisfiedBase(derived.ConstructorsList[0]));
        }
        else
        {
            Assert.Null(ImplicitChains.UnsatisfiedBase(derived.ConstructorsList[0]));
        }
    }

    private static MemberModifiers Public => DeclarationFragments.Public;

    private (ClassType Derived, ClassType Base, ConstructedType Written) PackDerivation(string constructors, string closing)
    {
        var baseType = new ClassType("Meter<>", "Probe", new Scope(null), null, null);
        var pack = new TypeParameter("Ts", 0, baseType.Scope, baseType) { PackKind = TypePackKind.Unbracketed };
        baseType.TypeParametersList.Add(pack);

        foreach(var spec in constructors.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            baseType.ConstructorsList.Add(BaseConstructor(baseType, spec, pack));
        }

        var derived = HandBuiltModel.Class("Dial");
        var own = new TypeParameter("Us", 0, derived.Scope, derived) { PackKind = TypePackKind.Unbracketed };
        IMistType argument = closing switch
        {
            "Us" => own,
            "Us..." => Pack([new SpreadType(own)]),
            _ => Pack([.. closing.Trim('[', ']').Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(name => built.Type(name == "int" ? "Int32" : "String"))]),
        };

        if(closing.StartsWith("Us", StringComparison.Ordinal))
        {
            derived.TypeParametersList.Add(own);
        }

        var written = built.Interner.GetOrCreateConstructedType(baseType, [argument]);
        derived.BaseType = written;
        derived.ConstructorsList.Add(Unchained(derived));
        return (derived, baseType, written);
    }

    private LiftedType Pack(List<IMistType> elements)
    {
        return new LiftedType(built.Type("Type"), new TypePackValue(elements));
    }

    private (ClassType Derived, ClassType Base) Derivation(string constructors)
    {
        var baseType = HandBuiltModel.Class("Meter");

        foreach(var spec in constructors.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            baseType.ConstructorsList.Add(BaseConstructor(baseType, spec));
        }

        var derived = HandBuiltModel.Class("Dial", baseType);
        derived.ConstructorsList.Add(Unchained(derived));
        return (derived, baseType);
    }

    private Constructor BaseConstructor(ConcreteType owner, string spec, TypeParameter? pack = null)
    {
        var access = spec.Split(':')[0];
        var shape = spec.Split(':')[1];
        var integer = built.Type("Int32");
        List<Parameter> parameters = shape switch
        {
            "none" => [],
            "params" => [new Parameter("readings", integer) { IsFlattened = true }],
            "pack" => [new Parameter("readings", new TupleType([new SpreadType(pack!)])) { IsFlattened = true }],
            _ => [.. shape.Split(',').Select((_, i) => new Parameter($"reading{i}", integer))],
        };

        var modifiers = access == "static"
            ? Public with { IsStatic = true }
            : Public with { Accessibility = Enum.Parse<Accessibility>(access, ignoreCase: true) };

        var constructor = new Constructor(owner.Name, owner, parameters, modifiers) { DeclaringType = owner };
        specs[constructor] = spec;
        return constructor;
    }

    private static Constructor Unchained(ConcreteType owner)
    {
        return new Constructor(owner.Name, owner, [], Public) { DeclaringType = owner };
    }

    private string Describe(object? runs, ClassType baseType)
    {
        return runs switch
        {
            ClassType type when ReferenceEquals(type, baseType) => "the base itself",
            Constructor constructor => specs[constructor],
            _ => "nothing",
        };
    }
}