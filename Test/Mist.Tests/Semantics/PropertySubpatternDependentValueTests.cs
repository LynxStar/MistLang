using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class PropertySubpatternDependentValueTests
{
    private static readonly ClassType Int32 = new("Int32", "Standard", new Scope(null), null, null);

    [Fact]
    public void ADependentParameterReadsAsItsDomainWithAnAliasExpanded()
    {
        var alias = new TypeAlias("ZeroOrPositiveInt", "Standard", new Scope(null), null, new TypeAliasDeclarationNode());
        var expanded = new ClassType("Expanded", "Probe", new Scope(null), null, null);

        Assert.Same(Int32, PatternBinder.DependentValue(new TypeResult(Parameter(Int32)), Unexpected));
        Assert.Same(expanded, PatternBinder.DependentValue(new TypeResult(Parameter(alias)), seen => ReferenceEquals(seen, alias) ? expanded : Unexpected(seen)));
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("pack")]
    [InlineData("property")]
    public void AnythingElseIsNotADependentValue(string found)
    {
        LookupResult result = found switch
        {
            "ordinary" => new TypeResult(Parameter(Int32, dependent: false)),
            "pack" => new TypeResult(Parameter(Int32, pack: true)),
            _ => new PropertyResult(new Property("Length", Int32, new MemberModifiers()), Int32, []),
        };

        Assert.Null(PatternBinder.DependentValue(result, Unexpected));
    }

    private static TypeParameter Parameter(MistType domain, bool dependent = true, bool pack = false)
    {
        var owner = new ClassType("Array<>", "Standard", new Scope(null), null, null);
        return new TypeParameter("Length", 0, owner.Scope, owner)
        {
            IsDependent = dependent,
            Domain = domain,
            PackKind = pack ? TypePackKind.Unbracketed : TypePackKind.None,
        };
    }

    private static IMistType Unexpected(TypeAlias alias)
    {
        throw new InvalidOperationException($"Expanded {alias.Name} unexpectedly.");
    }
}