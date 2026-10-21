using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class TypeParameterDistinctionTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void TwoMethodsOwnTsAreNamedWithTheirMethods()
    {
        var (pick, callee) = Generic(HandBuiltModel.Class("Api"), "Pick");
        var (_, outer) = Generic(HandBuiltModel.Class("Program"), "Outer");

        var spelled = TypeDisplay.Distinguished([callee, outer, Int]);

        Assert.Equal(["T of method Api.Pick", "T of method Program.Outer", "int"], spelled);
        Assert.Same(pick, callee.DeclaringMethod);
    }

    [Fact]
    public void ATypesTInsideAConstructedBoundIsNamedWithItsType()
    {
        var (_, callee) = Generic(HandBuiltModel.Class("Api"), "Pick");
        var box = HandBuiltModel.Class("Box<>");
        var boxed = new TypeParameter("T", 0, box.Scope, box);
        var list = built.Interner.GetOrCreateConstructedType(HandBuiltModel.Class("List<>"), [boxed]);

        var spelled = TypeDisplay.Distinguished([callee, list, Int]);

        Assert.Equal(["T of method Api.Pick", "List<T of type Box>", "int"], spelled);
    }

    [Fact]
    public void DistinctNamesKeepTheirPlainSpelling()
    {
        var (_, callee) = Generic(HandBuiltModel.Class("Api"), "Pick");
        var (_, other) = Generic(HandBuiltModel.Class("Program"), "Outer", parameterName: "U");

        Assert.Equal(["T", "U", "int"], TypeDisplay.Distinguished([callee, other, Int]));
        Assert.Equal("T", TypeDisplay.Mist(callee));
    }

    [Fact]
    public void OneParameterShownTwiceIsNotACollision()
    {
        var (_, callee) = Generic(HandBuiltModel.Class("Api"), "Pick");
        var list = built.Interner.GetOrCreateConstructedType(HandBuiltModel.Class("List<>"), [callee]);

        Assert.Equal(["T", "List<T>"], TypeDisplay.Distinguished([callee, list]));
    }

    [Fact]
    public void AConflictBetweenSameSpelledParametersNamesEachDeclaration()
    {
        var (pick, callee) = Generic(HandBuiltModel.Class("Api"), "Pick");
        var (_, outer) = Generic(HandBuiltModel.Class("Program"), "Outer");

        var diagnostic = Conflict(pick, new TypeArgumentConflict(callee, [outer, Int]));

        Assert.Equal("MIST_PM_TYPE_ARGUMENTS_CONFLICT", diagnostic.Id.Value);
        Assert.Equal(
            "Cannot infer the type arguments of method 'Pick': the arguments give 'T of method Api.Pick' the types "
            + "'T of method Program.Outer', 'int', and none of them is a type the others all convert to. "
            + "State all of them explicitly at the call: 'T of method Api.Pick'.",
            diagnostic.Message);
    }

    [Fact]
    public void AConflictWithoutACollisionKeepsItsConciseText()
    {
        var (pick, callee) = Generic(HandBuiltModel.Class("Api"), "Pick");

        var diagnostic = Conflict(pick, new TypeArgumentConflict(callee, [built.Type("String"), Int]));

        Assert.Equal(
            "Cannot infer the type arguments of method 'Pick': the arguments give 'T' the types 'string', 'int', "
            + "and none of them is a type the others all convert to. State all of them explicitly at the call: 'T'.",
            diagnostic.Message);
        Assert.Equal(31, diagnostic.Location.Start.Column);
    }

    private IMistType Int => built.Type("Int32");

    private (Method Method, TypeParameter Parameter) Generic(ClassType owner, string name, string parameterName = "T")
    {
        var method = new Method(name, Int, [], DeclarationFragments.Public) { DeclaringType = owner, Scope = new MethodScope(owner.Scope) };
        var parameter = new TypeParameter(parameterName, 0, method.Scope, owner) { DeclaringMethod = method };
        method.TypeParametersList.Add(parameter);
        method.ParametersList.Add(new Parameter("a", parameter));
        return (method, parameter);
    }

    private static Diagnostic Conflict(Method callee, TypeArgumentConflict conflict)
    {
        var diagnostics = new DiagnosticBag();
        OverloadFailure.Report(
            new OverloadChoice<Method> { Selected = null, Uninferred = [callee], UninferredConflict = conflict },
            [callee], [], $"method '{callee.Name}'", HandBuiltModel.Node(30), diagnostics);
        return Assert.Single(diagnostics.ToImmutableArray());
    }
}