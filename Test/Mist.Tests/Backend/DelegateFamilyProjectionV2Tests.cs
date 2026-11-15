// ambient-type-provider-v2 Property 3, Requirements 4.1, 4.2, 4.4.

using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Family = Mist.Tests.InteropFixtures.DelegateFamily;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class DelegateFamilyProjectionV2Tests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    private const string Signatures = "Mist.Tests.InteropFixtures.DelegateFamily." + nameof(Family.DelegateFamilySignatures);

    public static TheoryData<int> FuncArities => [.. Enumerable.Range(1, 17)];

    public static TheoryData<int> ActionArities => [.. Enumerable.Range(0, 17)];

    [Theory]
    [MemberData(nameof(FuncArities))]
    public void SystemFuncOfEveryArity_ProjectsAsTheOneVariadicStandardFunc(int arity)
    {
        var projected = fixture.Provider.ProjectionOf(SystemDelegate("Func", arity));

        Assert.Same(StandardType("Func<,>"), projected);
    }

    [Theory]
    [MemberData(nameof(ActionArities))]
    public void SystemActionOfEveryArity_ProjectsAsTheOneVariadicStandardAction(int arity)
    {
        var projected = fixture.Provider.ProjectionOf(SystemDelegate("Action", arity));

        Assert.Same(StandardType("Action<>"), projected);
    }

    [Fact]
    public void AClosedSystemFunc_ProjectsAsItsDefinitionsStandardFunc()
    {
        Assert.Same(StandardType("Func<,>"), fixture.Provider.ProjectionOf(typeof(Func<int, string>)));
        Assert.Same(StandardType("Action<>"), fixture.Provider.ProjectionOf(typeof(Action<int, long>)));
    }

    // A generic family has no one name a written use could mean, so it is reached through Standard alone.
    [Theory]
    [InlineData("System.Func`1")]
    [InlineData("System.Func`3")]
    [InlineData("System.Action")]
    [InlineData("System.Action`2")]
    public void TheSystemDelegateFamily_IsNotNamedByItsClrName(string clrName)
    {
        Assert.Null(fixture.Model.FindQualifiedType(clrName));
    }

    [Fact]
    public void ADelegateNamedFuncOutsideSystem_ProjectsAsItsOwnAmbientType()
    {
        var projected = fixture.Provider.ProjectionOf(typeof(Family.Func<>));

        Assert.NotNull(projected);
        Assert.NotSame(StandardType("Func<,>"), projected);
        Assert.Equal("Mist.Tests.InteropFixtures.DelegateFamily.Func<>", projected!.FullyQualifiedName);
        Assert.Equal(MistAssemblyKind.Ambient, projected.Assembly.Kind);
    }

    // Neither is in the family: with no assembly declaring them loaded, nothing projects them.
    [Theory]
    [InlineData(typeof(Delegate))]
    [InlineData(typeof(MulticastDelegate))]
    public void TheDelegateBaseTypes_AreNotProjectedAsTheFamily(Type clrType)
    {
        Assert.Null(fixture.Provider.ProjectionOf(clrType));
    }

    [Fact]
    public void ASystemFuncParameter_ProjectsAsTheFunctionTypeOfItsArguments()
    {
        var produce = Assert.IsType<FunctionType>(ParameterOf("Produce"));
        Assert.Empty(produce.ParameterTypes);
        Assert.Same(StandardType("Int32"), produce.ReturnType);

        var test = Assert.IsType<FunctionType>(ParameterOf("Test"));
        AssertStandardTypes(test.ParameterTypes, "Int32", "Int64");
        Assert.Same(StandardType("Boolean"), test.ReturnType);
    }

    [Fact]
    public void ASystemActionParameter_ProjectsAsAFunctionTypeReturningVoid()
    {
        var run = Assert.IsType<FunctionType>(ParameterOf("Run"));
        Assert.Empty(run.ParameterTypes);
        Assert.Same(StandardType("Void"), run.ReturnType);

        var feed = Assert.IsType<FunctionType>(ParameterOf("Feed"));
        AssertStandardTypes(feed.ParameterTypes, "Int32", "Int64");
        Assert.Same(StandardType("Void"), feed.ReturnType);
    }

    [Fact]
    public void ASystemFuncReturn_ProjectsAsTheFunctionTypeOfItsArguments()
    {
        var positive = Assert.IsType<FunctionType>(MethodOf("Positive").ReturnType);

        AssertStandardTypes(positive.ParameterTypes, "Int32");
        Assert.Same(StandardType("Boolean"), positive.ReturnType);
    }

    private static Type SystemDelegate(string family, int arity)
    {
        var name = arity == 0 ? $"System.{family}" : $"System.{family}`{arity}";
        return typeof(Action).Assembly.GetType(name, throwOnError: true)!;
    }

    private IMistType StandardType(string name)
    {
        return fixture.Model.TypesByFQN[$"Standard.{name}"];
    }

    private void AssertStandardTypes(IReadOnlyList<IMistType> actual, params string[] names)
    {
        Assert.Equal(names.Length, actual.Count);
        for(var i = 0; i < names.Length; i++)
        {
            Assert.Same(StandardType(names[i]), actual[i]);
        }
    }

    private Method MethodOf(string name)
    {
        var signatures = (MemberType)fixture.Model.TypesByFQN[Signatures];
        return signatures.MethodsList.Single(method => method.Name == name);
    }

    private IMistType ParameterOf(string methodName)
    {
        return Assert.Single(MethodOf(methodName).ParametersList).Type;
    }
}