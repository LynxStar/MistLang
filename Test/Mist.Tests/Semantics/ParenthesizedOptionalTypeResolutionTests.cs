using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ParenthesizedOptionalTypeResolutionTests
{
    private readonly ProgramModelClass programModel = new();
    private readonly ClassType widget = new("Widget", "Probe", new Scope(null), null, null);
    private readonly ClassType gadget = new("Gadget", "Probe", new Scope(null), null, null);
    private readonly ClassType voidType = new("Void", "Standard", new Scope(null), null, null);
    private readonly DiagnosticBag diagnostics = new();
    private readonly ScopeResolver resolver;
    private readonly FileScope scope;

    public ParenthesizedOptionalTypeResolutionTests()
    {
        _ = programModel.AddSelfAssembly("Test");
        var context = new BuilderContext(programModel, diagnostics, NullAmbientTypeProvider.Instance);
        _ = programModel.GetOrAddNamespace("Probe");
        _ = programModel.GetOrAddNamespace("Standard");
        _ = programModel.RegisterType(widget);
        _ = programModel.RegisterType(gadget);
        _ = programModel.RegisterType(voidType);
        var array = new ClassType(WellKnownTypes.Array.Name, WellKnownTypes.Array.Namespace, new Scope(null), null, null);
        array.TypeParametersList.Add(new TypeParameter("TValue", 0, array.Scope, array));
        array.TypeParametersList.Add(new TypeParameter("Length", 1, array.Scope, array) { IsDependent = true });
        _ = programModel.RegisterType(array);
        scope = new FileScope(context.ScopeFor(programModel.SelfAssembly!), programModel, diagnostics);
        scope.DeclareImport("Probe");
        resolver = new ScopeResolver(context);
    }

    [Fact]
    public void AnOptionalParenthesizedFunctionIsTheOptionalFunction()
    {
        var bare = Func();
        bare.Optional = BindingFragments.Token("?");

        var parenthesized = Resolve(Parenthesized(Func(), optional: true));

        Assert.Same(Resolve(bare), parenthesized);
        var union = Assert.IsAssignableFrom<IUnionType>(parenthesized);
        Assert.Contains(union.Constituents, constituent => constituent is IFunctionType);
        Assert.Contains(voidType, union.Constituents);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnOptionalOnAParenthesizedArrayWrapsTheArrayNotItsElements()
    {
        var array = Parenthesized(Func(), optional: false);
        array.ArrayRank = new UnsizedArrayRankArrayRankSuffixNode();
        var optionalArray = Parenthesized(Func(), optional: true);
        optionalArray.ArrayRank = new UnsizedArrayRankArrayRankSuffixNode();

        var union = Assert.IsAssignableFrom<IUnionType>(Resolve(optionalArray));

        Assert.Equal(2, union.Constituents.Count);
        Assert.Contains(Resolve(array), union.Constituents);
        Assert.Contains(voidType, union.Constituents);
    }

    [Fact]
    public void TheOptionalApplicationIsRecordedAtTheParentheses()
    {
        var parenthesized = Parenthesized(Func(), optional: true);

        _ = Resolve(parenthesized);

        var application = Assert.Single(programModel.OptionalApplications.Applications);
        Assert.Same(parenthesized, application.Node);
    }

    [Fact]
    public void AParenthesizedTypeWithoutTheSuffixStaysRequired()
    {
        _ = Assert.IsAssignableFrom<IFunctionType>(Resolve(Parenthesized(Func(), optional: false)));
        Assert.Empty(programModel.OptionalApplications.Applications);
    }

    private IMistType Resolve(SingleTypeRefNode node)
    {
        return resolver.Resolve(new TypeReferenceNode { First = node }, scope);
    }

    private static FuncSingleTypeRefSingleTypeRefNode Func()
    {
        return new FuncSingleTypeRefSingleTypeRefNode { TypeArgs = BindingFragments.TypeArguments("Widget", "Gadget") };
    }

    private static ParenthesizedTypeRefSingleTypeRefNode Parenthesized(SingleTypeRefNode inner, bool optional)
    {
        return new ParenthesizedTypeRefSingleTypeRefNode
        {
            Inner = new TypeReferenceNode { First = inner },
            Optional = optional ? BindingFragments.Token("?") : null,
        };
    }
}