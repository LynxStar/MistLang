// Tier 3 (Component): the parameterized constructor constraint lowering when the signature names a type
// parameter. Asserted on the emitted text, since an interface that spells a parameter no scope binds, or a
// closing type that lists one interface or factory twice, fails only in Roslyn.

using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ConstructorConstraintInterfaceEmissionTests
{
    private const string Types = """
        public class Box<V>
        {
            public V Held;

            public Box(V held) { this.Held = held; }
        }

        public class Widget
        {
            public Widget(int size) { }
            public Widget(string name) { }
            public Widget(int size, string name) { }
            public Widget(Box<int> box) { }
        }

        public class Factory<T, A> where T : new(A) { public T Make(A arg) { return new T(arg); } }
        public class Sized<T> where T : new(int) { public T Make(int arg) { return new T(arg); } }
        public class Mixed<T, A> where T : new(int, A) { public T Make(int size, A arg) { return new T(size, arg); } }
        public class Boxed<T, A> where T : new(Box<A>) { public T Make(A arg) { return new T(new Box<A>(arg)); } }
        public class Builder { public static T Build<T, A>(A arg) where T : new(A) { return new T(arg); } }
        """;

    private const string Closings = """
        var a = new Factory<Widget, int>().Make(5);
        var b = new Factory<Widget, string>().Make("w");
        var c = new Sized<Widget>().Make(6);
        var d = new Mixed<Widget, string>().Make(7, "m");
        var e = new Boxed<Widget, int>().Make(8);
        var f = Builder.Build<Widget, int>(9);
        """;

    private static readonly Lazy<ProgramModel.Model.ProgramModel> Model = new(() =>
    {
        var model = StandardModelProbe.Build(Closings, out var diagnostics, Types);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return model;
    });

    [Theory]
    [InlineData("Factory", "where T : Mist.Emission.CSharp.__IConstruct_0<T, A>")]
    [InlineData("Mixed", "where T : Mist.Emission.CSharp.__IConstruct_int_1<T, A>")]
    [InlineData("Boxed", "where T : Mist.Emission.CSharp.__IConstruct_0<T, Probe.Box<A>>")]
    [InlineData("Builder", "where T : Mist.Emission.CSharp.__IConstruct_0<T, A>")]
    [InlineData("Sized", "where T : Mist.Emission.CSharp.__IConstruct_int<T>")]
    public void AConstraintClosesTheInterfaceWithItsOwnOpenPositions(string typeName, string clause)
    {
        Assert.Contains(clause, Lines(EmitDeclaration(typeName)));
    }

    [Theory]
    [InlineData("Mist.Emission.CSharp.__IConstruct_0<Widget, int>")]
    [InlineData("Mist.Emission.CSharp.__IConstruct_0<Widget, string>")]
    [InlineData("Mist.Emission.CSharp.__IConstruct_int<Widget>")]
    [InlineData("Mist.Emission.CSharp.__IConstruct_int_1<Widget, string>")]
    [InlineData("Mist.Emission.CSharp.__IConstruct_0<Widget, Probe.Box<int>>")]
    public void AClosingTypeImplementsEachInterfaceItsUsesClose_Once(string closed)
    {
        var declaration = Lines(EmitDeclaration("Widget")).Single(line => line.StartsWith("public class Widget", StringComparison.Ordinal));

        _ = Assert.Single(Regex.Matches(declaration, $@"{Regex.Escape(closed)}(,|$)"));
    }

    [Theory]
    [InlineData("public static Widget __New(int arg0)")]
    [InlineData("public static Widget __New(string arg0)")]
    [InlineData("public static Widget __New(int arg0, string arg1)")]
    [InlineData("public static Widget __New(Probe.Box<int> arg0)")]
    public void AClosingTypeDeclaresEachFactoryItsInterfacesAskFor_Once(string factory)
    {
        _ = Assert.Single(Lines(EmitDeclaration("Widget")), line => line == factory);
    }

    [Theory]
    [InlineData("Factory", "__IConstruct_0<T, TArg0>", "TArg0 arg0")]
    [InlineData("Mixed", "__IConstruct_int_1<T, TArg1>", "int arg0, TArg1 arg1")]
    [InlineData("Boxed", "__IConstruct_0<T, TArg0>", "TArg0 arg0")]
    [InlineData("Sized", "__IConstruct_int<T>", "int arg0")]
    public void AnInterfaceIsDeclaredOverItsSignaturesOpenPositions(string typeName, string declared, string parameters)
    {
        var signature = Assert.Single(Declared(typeName).TypeParametersList[0].ConstraintsList.OfType<ConstructorConstraint>()).ParameterTypes;

        Assert.Equal(
            declared,
            $"{ConstructorConstraintInterfaces.InterfaceName(signature)}<{ConstructorConstraintInterfaces.TypeParameters(signature)}>");
        Assert.Equal(parameters, ConstructorConstraintInterfaces.DeclaredParameters(signature));
    }

    private static MemberType Declared(string typeName)
    {
        return Model.Value.TypesByFQN.Values.OfType<MemberType>()
            .Single(type => type.NamespaceName == "Probe" && MistType.StripArity(type.Name) == typeName);
    }

    private static string EmitDeclaration(string typeName)
    {
        var model = Model.Value;
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(
            Declared(typeName), ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }

    private static List<string> Lines(string emitted)
    {
        return [.. emitted.Split('\n').Select(line => line.Trim())];
    }
}