using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ClrCallableFamilyNameTests
{
    [Theory]
    [InlineData("System.Func", 1, "Standard.Func")]
    [InlineData("System.Func", 17, "Standard.Func")]
    [InlineData("System.Action", 0, "Standard.Action")]
    [InlineData("System.Action", 16, "Standard.Action")]
    [InlineData("System.Func", 0, null)]
    [InlineData("System.Func", 18, null)]
    [InlineData("System.Action", 17, null)]
    [InlineData("System.Unknown", 1, null)]
    [InlineData("Other.Func", 2, null)]
    [InlineData("func", 2, null)]
    public void OnlyQualifiedClrCallableAritiesNameTheStandardFamily(string name, int arity, string? expected)
    {
        Assert.Equal(expected, ClrCallableNames.StandardFamily(name, arity));
    }
}

[Trait("Tier", "2")]
public sealed class ClrCallableNameResolutionTests
{
    private readonly BindingFragments fragments = new();

    public ClrCallableNameResolutionTests()
    {
        var array = new ClassType("Array<,>", "Standard", null, null, null);
        fragments.Register(array);
        var domain = fragments.Built.Interner.GetOrCreateConstructedType(array,
            [fragments.Built.Type("Type"), fragments.Built.Interner.GetOrCreateUnknownValue(fragments.Built.Type("Int32"))]);
        var function = new ClassType("Func<,>", "Standard", null, null, null);
        function.TypeParametersList.Add(new TypeParameter("TArgs", 0, function.Scope, function)
        {
            PackKind = TypePackKind.Unbracketed,
            Domain = domain,
        });
        function.TypeParametersList.Add(new TypeParameter("TResult", 1, function.Scope, function));
        fragments.Register(function);

        var action = new TypeAlias("Action<>", "Standard", null, null, BindingFragments.At<TypeAliasDeclarationNode>());
        var pack = new TypeParameter("TArgs", 0, action.Scope, action)
        {
            PackKind = TypePackKind.Unbracketed,
            Domain = domain,
        };
        action.TypeParametersList.Add(pack);
        action.AliasedType = new FunctionType([new SpreadType(pack)], fragments.Built.Type("Void"));
        fragments.Register(action);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void QualifiedFuncBindsTheSamePackAndResultAsStandardFunc(int parameters)
    {
        var arguments = Enumerable.Repeat("Int32", parameters).Append("Boolean").ToArray();
        var qualified = Assert.IsType<FunctionType>(Resolve("System.Func", arguments));
        var standard = Resolve("Standard.Func", arguments);

        Assert.Same(standard, qualified);
        Assert.Equal(parameters, qualified.ParameterTypes.Count);
        Assert.Same(fragments.Built.Type("Boolean"), qualified.ReturnType);
        Assert.All(qualified.ParameterTypes, parameter => Assert.Same(fragments.Built.Type("Int32"), parameter));
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void QualifiedActionExpandsTheSameAliasWithAVoidResult(int parameters)
    {
        var arguments = Enumerable.Repeat("Int32", parameters).ToArray();
        var qualified = Assert.IsType<FunctionType>(Resolve("System.Action", arguments));

        Assert.Same(Resolve("Standard.Action", arguments), qualified);
        Assert.Equal(parameters, qualified.ParameterTypes.Count);
        Assert.Same(fragments.Built.Type("Void"), qualified.ReturnType);
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ReservedFuncKeepsTheSameStructuralType()
    {
        var type = new FuncSingleTypeRefSingleTypeRefNode { TypeArgs = BindingFragments.TypeArguments("Int32", "Boolean") };

        Assert.Same(Resolve("System.Func", "Int32", "Boolean"), fragments.Builder.Resolver.Resolve(type, fragments.Scope));
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AQualifiedUnknownStillReportsItsWrittenName()
    {
        _ = Assert.IsType<ErrorType>(Resolve("System.MissingFunc", "Int32"));

        var diagnostic = Assert.Single(fragments.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_TYPE", diagnostic.Id.Value);
        Assert.Contains("System.MissingFunc<>", diagnostic.Message);
    }

    [Theory]
    [InlineData("System.Func", 0)]
    [InlineData("System.Func", 18)]
    [InlineData("System.Action", 17)]
    public void UnsupportedClrCallableAritiesRemainUnresolved(string name, int arity)
    {
        _ = Assert.IsType<ErrorType>(Resolve(name, Enumerable.Repeat("Int32", arity).ToArray()));

        var diagnostic = Assert.Single(fragments.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_TYPE", diagnostic.Id.Value);
        Assert.Contains(name, diagnostic.Message);
    }

    private ProgramModel.Contracts.Types.IMistType Resolve(string name, params string[] arguments)
    {
        return fragments.Builder.Resolver.ResolveNamed(
            BindingFragments.Name(name), arguments.Length == 0 ? null : BindingFragments.TypeArguments(arguments),
            fragments.Scope, HandBuiltModel.Node());
    }
}