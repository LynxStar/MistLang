using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;

namespace Mist.Tests.Integration;

public sealed class PropertyPatternRuntimeBoundaryTests
{
    [Fact]
    public void ModelToEmitterToRoslynToRuntime_PreservesTypeGatesBindingsAndScopes()
    {
        var animal = PropertyPatternEmissionTests.Type("Animal");
        var boolean = PropertyPatternEmissionTests.Type("Boolean", "Standard");
        var integer = PropertyPatternEmissionTests.Type("Int32", "Standard");
        var text = PropertyPatternEmissionTests.Type("String", "Standard");
        var value = PropertyPatternEmissionTests.Reference("value", animal);
        var typed = PropertyPatternEmissionTests.Property(typed: true, withMember: true);
        var whole = typed.Binding!;
        var breed = typed.Subpatterns[0].Pattern.Binding!;
        var condition = new IsPatternExpression(null!, value, typed, boolean);
        var guarded = PropertyPatternEmissionTests.Binary(condition, BinaryOperatorKind.LogicalAnd,
            PropertyPatternEmissionTests.Guard(whole, breed), boolean);
        var switched = new SwitchExpression(null!, value,
            [new SwitchArm(null!, typed, PropertyPatternEmissionTests.Guard(whole, breed), PropertyPatternEmissionTests.Result(whole, breed)),
             new SwitchArm(null!, new CatchAllPattern(null!, animal, null), null, PropertyPatternEmissionTests.Number(-1))], integer);
        var untypedPattern = PropertyPatternEmissionTests.Property(typed: false, withMember: true);
        var untyped = PropertyPatternEmissionTests.Binary(new IsPatternExpression(null!, value, untypedPattern, boolean),
            BinaryOperatorKind.LogicalAnd,
            PropertyPatternEmissionTests.Binary(
                new MemberAccessExpression(null!, new LocalReferenceExpression(null!, untypedPattern.Binding!), "Breed", text),
                BinaryOperatorKind.Equal, new LocalReferenceExpression(null!, untypedPattern.Subpatterns[0].Pattern.Binding!), boolean), boolean);
        var constantPattern = new PropertyPattern(null!, typed.NarrowedType,
            [new PropertySubpattern(null!, "Breed",
                new ComparisonPattern(null!, text, ComparisonKind.Equal, new LiteralExpression(null!, "collie", text)), text,
                typed.Subpatterns[0].Member)], typed.Binding, typed.TypeTest);
        var constant = new IsPatternExpression(null!, value, constantPattern, boolean);
        var union = new UnionType([animal, text]);
        var unionValue = PropertyPatternEmissionTests.Reference("value", union);
        var unionPattern = PropertyPatternEmissionTests.Property(typed: true, withMember: true, constituent: animal);
        var unionGuard = PropertyPatternEmissionTests.Guard(unionPattern.Binding!, unionPattern.Subpatterns[0].Pattern.Binding!);
        var unionCondition = PropertyPatternEmissionTests.Binary(new IsPatternExpression(null!, unionValue, unionPattern, boolean),
            BinaryOperatorKind.LogicalAnd, unionGuard, boolean);
        var unionSwitch = new SwitchExpression(null!, unionValue,
            [new SwitchArm(null!, unionPattern, unionGuard,
                PropertyPatternEmissionTests.Result(unionPattern.Binding!, unionPattern.Subpatterns[0].Pattern.Binding!)),
             new SwitchArm(null!, new CatchAllPattern(null!, union, null), null, PropertyPatternEmissionTests.Number(-1))], integer);
        var empty = new IsPatternExpression(null!, value,
            PropertyPatternEmissionTests.Property(typed: true, withMember: false), boolean);
        var bodyWriter = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitBlock(PropertyPatternEmissionTests.ScopedBody(), ["value"], bodyWriter, diagnostics);
        Assert.False(diagnostics.HasErrors);

        var source = $$"""
            public class Animal
            {
                public static int Reads;
                public virtual string Breed { get { Reads++; throw new System.InvalidOperationException("unexpected getter"); } }
            }
            public sealed class Dog : Animal
            {
                private readonly string breed;
                public Dog(string breed) { this.breed = breed; }
                public override string Breed { get { Reads++; return breed; } }
                public int Rank { get { return 2; } }
            }
            public sealed class Cat : Animal { }
            public sealed class Carrier
            {
                public Carrier(Animal animal) { AsAnimalOrNull = animal; }
                public Animal AsAnimalOrNull { get; }
            }
            public static class Probe
            {
                private static bool Typed(Animal value) { return {{PropertyPatternEmissionTests.Emit(guarded)}}; }
                private static int Switched(Animal value) { return {{PropertyPatternEmissionTests.Emit(switched)}}; }
                private static bool Untyped(Animal value) { return {{PropertyPatternEmissionTests.Emit(untyped)}}; }
                private static bool Empty(Animal value) { return {{PropertyPatternEmissionTests.Emit(empty)}}; }
                private static bool Constant(Animal value) { return {{PropertyPatternEmissionTests.Emit(constant)}}; }
                private static bool UnionCondition(Carrier value) { return {{PropertyPatternEmissionTests.Emit(unionCondition)}}; }
                private static int UnionSwitch(Carrier value) { return {{PropertyPatternEmissionTests.Emit(unionSwitch)}}; }
                private static int Scoped(Animal value) { {{bodyWriter}} }
                public static int[] Run()
                {
                    var results = new System.Collections.Generic.List<int>();
                    Animal.Reads = 0;
                    results.Add(Typed(null) ? 1 : 0);
                    results.Add(Typed(new Cat()) ? 1 : 0);
                    results.Add(Animal.Reads);
                    results.Add(Typed(new Dog("collie")) ? 1 : 0);
                    results.Add(Animal.Reads);
                    results.Add(Typed(new Dog("poodle")) ? 1 : 0);
                    results.Add(Animal.Reads);
                    Animal.Reads = 0;
                    results.Add(Switched(null));
                    results.Add(Switched(new Cat()));
                    results.Add(Animal.Reads);
                    results.Add(Switched(new Dog("collie")));
                    results.Add(Switched(new Dog("poodle")));
                    results.Add(Animal.Reads);
                    results.Add(Untyped(null) ? 1 : 0);
                    results.Add(Untyped(new Dog("collie")) ? 1 : 0);
                    Animal.Reads = 0;
                    results.Add(Empty(null) ? 1 : 0);
                    results.Add(Empty(new Cat()) ? 1 : 0);
                    results.Add(Empty(new Dog("collie")) ? 1 : 0);
                    results.Add(Animal.Reads);
                    Animal.Reads = 0;
                    results.Add(Constant(null) ? 1 : 0);
                    results.Add(Constant(new Cat()) ? 1 : 0);
                    results.Add(Animal.Reads);
                    results.Add(Constant(new Dog("poodle")) ? 1 : 0);
                    results.Add(Constant(new Dog("collie")) ? 1 : 0);
                    results.Add(Animal.Reads);
                    results.Add(Scoped(new Dog("collie")));
                    results.Add(Scoped(new Cat()));
                    Animal.Reads = 0;
                    results.Add(UnionCondition(new Carrier(null)) ? 1 : 0);
                    results.Add(UnionCondition(new Carrier(new Cat())) ? 1 : 0);
                    results.Add(UnionSwitch(new Carrier(null)));
                    results.Add(UnionSwitch(new Carrier(new Cat())));
                    results.Add(Animal.Reads);
                    results.Add(UnionCondition(new Carrier(new Dog("collie"))) ? 1 : 0);
                    results.Add(UnionSwitch(new Carrier(new Dog("collie"))));
                    results.Add(Animal.Reads);
                    return results.ToArray();
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "PropertyPatternRuntime_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
            CSharpCompilationDriver.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Select(d => d.ToString())));
        stream.Position = 0;
        var context = new AssemblyLoadContext("PropertyPatternRuntime", isCollectible: true);

        try
        {
            var assembly = context.LoadFromStream(stream);
            var run = assembly.GetType("Probe")!.GetMethod("Run")!;
            var results = Assert.IsType<int[]>(run.Invoke(null, null));

            Assert.Equal([0, 0, 0, 1, 1, 0, 2, -1, -1, 0, 8, -1, 2, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 2, 8, 6, 0, 0, -1, -1, 0, 1, 8, 2], results);
        }
        finally
        {
            context.Unload();
        }
    }
}