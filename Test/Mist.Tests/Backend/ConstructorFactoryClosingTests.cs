using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// The factory a parameterized constructor constraint appends to a GENERIC closing definition, here
// `Crate(X held)` on Crate of X and `Pair(X x, Y y)` on Pair of X and Y, over hand-built types.
public sealed class ConstructorFactoryClosingTests
{
    private static readonly MistAssembly Source = new("Probe", MistAssemblyKind.Self);

    private readonly HandBuiltModel built = new();

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("closed", "Int32", "X", "(X)(object)arg0")]
    [InlineData("open", "X", "X", "arg0")]
    [InlineData("closed", "Int32", "Int32", "arg0")]
    public void AClosedPositionConvertsToTheConstructorsOwnType_AnOpenOneTakesIt(string position, string factory, string declared, string argument)
    {
        var x = new TypeParameter("X", 0, new Scope(null), null);
        var open = new TypeParameter("A", 1, new Scope(null), null);
        IMistType named(string name)
        {
            return name == "X" ? x : built.Type(name);
        }


        var witness = new ConstructorWitness(
            [position == "open" ? open : built.Type("Int32")], [built.Type("Int32")], [named(declared)]);

        Assert.Equal(factory, Assert.Single(witness.Factory).Name);
        Assert.Equal(argument, ConstructorConstraintInterfaces.Arguments(witness));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AGenericClosingImplementsTheInterfaceInItsOwnTerms()
    {
        var crate = Generic("Crate", "X");
        var factory = Constrained("Factory", [built.Type("Int32")]);
        Close(factory, Construct(crate, built.Type("Int32")));

        var lines = Emit(crate);

        Assert.Contains("public class Crate<X> : Mist.Emission.CSharp.__IConstruct_int<Crate<X>>", lines);
        Assert.Contains("public static Crate<X> __New(int arg0)", lines);
        Assert.Contains("return new Crate<X>((X)(object)arg0);", lines);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TwoInstantiationsOfAnOpenSignatureShareOneFactory_TwoClosedSignaturesHaveTheirOwn()
    {
        var crate = Generic("Crate", "X");
        var argument = new TypeParameter("A", 1, new Scope(null), null);
        var open = Constrained("Open", [argument], argument);
        Close(open, Construct(crate, built.Type("Int32")), built.Type("Int32"));
        Close(open, Construct(crate, built.Type("String")), built.Type("String"));
        Close(Constrained("Ints", [built.Type("Int32")]), Construct(crate, built.Type("Int32")));
        Close(Constrained("Texts", [built.Type("String")]), Construct(crate, built.Type("String")));

        var lines = Emit(crate);

        _ = Assert.Single(lines, line => line.StartsWith("public class Crate<X>", StringComparison.Ordinal)
            && line.Contains("__IConstruct_0<Crate<X>, X>", StringComparison.Ordinal)
            && line.Split("__IConstruct_0<").Length == 2);
        _ = Assert.Single(lines, line => line == "public static Crate<X> __New(X arg0)");
        _ = Assert.Single(lines, line => line == "public static Crate<X> __New(int arg0)");
        _ = Assert.Single(lines, line => line == "public static Crate<X> __New(string arg0)");
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AMixedSignatureConvertsOnlyItsClosedPosition()
    {
        var pair = Generic("Pair", "X", "Y");
        var argument = new TypeParameter("A", 1, new Scope(null), null);
        var mixed = Constrained("Mixed", [built.Type("Int32"), argument], argument);
        Close(mixed, Construct(pair, built.Type("Int32"), built.Type("String")), built.Type("String"));

        var lines = Emit(pair);

        Assert.Contains("public class Pair<X, Y> : Mist.Emission.CSharp.__IConstruct_int_1<Pair<X, Y>, Y>", lines);
        Assert.Contains("public static Pair<X, Y> __New(int arg0, Y arg1)", lines);
        Assert.Contains("return new Pair<X, Y>((X)(object)arg0, arg1);", lines);
    }

    private static ClassType Generic(string name, params string[] parameters)
    {
        var type = new ClassType($"{name}<{new string(',', parameters.Length - 1)}>", "Probe", new Scope(null), null, null)
        {
            Assembly = Source,
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public },
        };

        foreach(var parameter in parameters)
        {
            type.TypeParametersList.Add(new TypeParameter(parameter, type.TypeParametersList.Count, type.Scope, type));
        }

        type.ConstructorsList.Add(new Constructor(
            type.Name, type, [.. type.TypeParametersList.Select(parameter => new Parameter(parameter.Name.ToLowerInvariant(), parameter))],
            new MemberModifiers { Accessibility = Accessibility.Public })
        { DeclaringType = type });
        return type;
    }

    private static ClassType Constrained(string name, IReadOnlyList<IMistType> signature, TypeParameter? sibling = null)
    {
        var type = new ClassType(sibling is null ? $"{name}<>" : $"{name}<,>", "Probe", new Scope(null), null, null) { Assembly = Source };
        var product = new TypeParameter("T", 0, type.Scope, type);
        product.ConstraintsList.Add(new ConstructorConstraint(signature));
        type.TypeParametersList.Add(product);

        if(sibling is not null)
        {
            type.TypeParametersList.Add(sibling);
        }

        return type;
    }

    private ConstructedType Construct(ClassType definition, params IMistType[] arguments)
    {
        return built.Interner.GetOrCreateConstructedType(definition, arguments);
    }

    private void Close(ClassType constrained, IMistType product, IMistType? sibling = null)
    {
        built.Model.ConstructorConstraintClosures.RecordType(constrained, sibling is null ? [product] : [product, sibling], HandBuiltModel.Node());
    }

    private List<string> Emit(ClassType type)
    {
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(built.Model, diagnostics), built.Model.VariadicUses, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return [.. writer.ToString().Split('\n').Select(line => line.Trim())];
    }
}