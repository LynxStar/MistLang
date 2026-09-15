// Tier 3 (Component): what a conversion running a Clone body emits (newtypes spec Requirement 10). Asserted
// on the emitted text, since a member written by name where C# refuses it fails only in Roslyn, and a static
// read from the wrong type prints a plausible count either way.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class CloneConversionEmissionTests
{
    private const string Types = """
        public class Cat
        {
            protected int lives;

            public Cat() { this.lives = 9; }
        }

        public class Tiger : Cat, IClonable<Tiger>
        {
            private static int made = 0;
            private int stripes;

            public string Name { get; set; }

            public Tiger(string name) : base() { this.Name = name; }

            private Tiger(int stripes) : base()
            {
                this.Name = "";
                this.stripes = stripes;
            }

            private int Doubled() { return stripes * 2; }

            public owned Tiger Clone()
            {
                made = made + 1;
                var copy = new Tiger(Doubled() + lives);
                copy.Name = Name;
                return new Tiger(Name) { stripes = copy.stripes };
            }
        }

        public class Lion : IClonable<Lion>
        {
            public Lion? Mate;

            public Lion() { }

            public owned Lion Clone() { return new Lion { Mate = Mate }; }
        }

        public class Cell<T> : IClonable<Cell<T>>
        {
            private static int made = 0;
            public static int Cloned = 0;
            private T content;

            public Cell(T content) { this.content = content; }

            private Cell(T content, int serial) { this.content = content; }

            public owned Cell<T> Clone()
            {
                made = made + 1;
                Cloned = Cloned + 1;
                return new Cell<T>(content, made);
            }
        }

        public abstract IShow
        {
            string Show();
        }

        public class Word : IShow
        {
            public Word() { }

            public string Show() { return "word"; }
        }

        public class Lynx : IClonable<Lynx>
        {
            private int spots;
            private string? note;

            private string? Note
            {
                get { return note; }
                set { note = value; }
            }

            private int this[int index]
            {
                get { return spots + index; }
                set { spots = value - index; }
            }

            public Lynx(int spots) { this.spots = spots; }

            private T Echo<T>(T value) { return value; }

            private int Three() { return 3; }

            private void Bump() { spots = spots + 1; }

            private string Shown<U>(U item) where U : IShow { return item.Show(); }

            public owned Lynx Clone()
            {
                var copy = new Lynx(Echo<int>(spots));
                Lynx? maybe = copy;
                int? read = maybe?.spots;
                maybe?.Bump();
                Note ??= "self";
                copy[1] = this[1] + 10;
                func<int> three = Three;
                func<int> copyThree = copy.Three;
                var shown = Shown<Word>(new Word());
                return copy;
            }
        }

        public class Serval : IClonable<Serval>
        {
            public int marks;

            private int this[int index]
            {
                set { marks = value + index; }
            }

            public Serval(int marks) { this.marks = marks; }

            public owned Serval Clone()
            {
                var copy = new Serval(marks);
                copy[1] = 2;
                return copy;
            }
        }

        public type Jaguar = new Tiger;
        public type Puma = new Lion;
        public type IntCell = new Cell<int>;
        public type Ocelot = new Lynx;
        public type Caracal = new Serval;
        """;

    private const string Casts = """
        var jaguar = (Jaguar)new Tiger("Shere");
        var tiger = (Tiger)jaguar;
        var intCell = (IntCell)new Cell<int>(1);
        var cell = (Cell<int>)intCell;
        var ocelot = (Ocelot)new Lynx(1);
        var lynx = (Lynx)ocelot;
        var caracal = (Caracal)new Serval(1);
        var serval = (Serval)caracal;
        """;

    [Fact]
    public void ACastToTheNewtype_ReadsTheSourcesPrivateAndProtectedMembersThroughAccessors()
    {
        var emitted = EmitDeclaration("Jaguar");

        Assert.Contains("public static explicit operator Probe.Jaguar(Probe.Tiger @this)", emitted);
        Assert.Contains("Probe.Jaguar.made = Probe.Jaguar.made + 1;", emitted);
        Assert.Contains("new Probe.Jaguar(__In_Probe_Tiger_Doubled(@this) + __In_Probe_Cat_lives(@this))", emitted);
        Assert.Contains("copy.Name = @this.Name;", emitted);
        Assert.Contains("private static extern ref int __In_Probe_Cat_lives(Probe.Cat __target);", emitted);
    }

    [Fact]
    public void ACastToTheSource_ConstructsItAndWritesItsPrivateStateThroughAccessors()
    {
        var emitted = EmitDeclaration("Jaguar");

        Assert.Contains("public static explicit operator Probe.Tiger(Probe.Jaguar @this)", emitted);
        Assert.Contains("__Out_Probe_Tiger_made(null) = __Out_Probe_Tiger_made(null) + 1;", emitted);
        Assert.Contains("__Out_Probe_Tiger_new(@this.Doubled() + @this.lives)", emitted);
        Assert.Contains(
            "__Out_Initialized(new Probe.Tiger(@this.Name), __initialized0 => { __Out_Probe_Tiger_stripes(__initialized0) = __Out_Probe_Tiger_stripes(copy); })",
            emitted);
        Assert.Contains("UnsafeAccessorKind.Constructor)]", emitted);
    }

    [Fact]
    public void AConstructedSourcesPrivateState_IsReachedThroughAHolderOverItsDefinitionsParameters()
    {
        var emitted = EmitDeclaration("IntCell");

        Assert.Contains("new Probe.IntCell(__In_Probe_Cell_Access<int>.__content(@this), Probe.IntCell.made)", emitted);
        Assert.Contains("private static class __In_Probe_Cell_Access<T>", emitted);
        Assert.Contains("public static extern ref T __content(Probe.Cell<T> __target);", emitted);
    }

    [Fact]
    public void ACastToAConstructedSource_SpellsItsStaticsAndConstructorOnTheConstruction()
    {
        var emitted = EmitDeclaration("IntCell");

        Assert.Contains("Probe.Cell<int>.Cloned = Probe.Cell<int>.Cloned + 1;", emitted);
        Assert.Contains("__Out_Probe_Cell_Access<int>.__made(null) = __Out_Probe_Cell_Access<int>.__made(null) + 1;", emitted);
        Assert.Contains("return __Out_Probe_Cell_Access<int>.__new(@this.content, __Out_Probe_Cell_Access<int>.__made(null));", emitted);
        Assert.Contains("public static extern Probe.Cell<T> __new(T content, int serial);", emitted);
    }

    [Fact]
    public void AConditionalAccessToAnUnreachableMember_NarrowsItsReceiverBeforeTheAccessor()
    {
        var emitted = EmitDeclaration("Ocelot");

        Assert.Matches(@"int\? read = \(maybe is \{ \} (__reached\d+) \? \(int\?\)__Out_Probe_Lynx_spots\(\1\) : null\);", emitted);
        Assert.Matches(@"__Out_When\(maybe, (__reached\d+) => __Out_Probe_Lynx_Bump\(\1\)\);", emitted);
        Assert.Contains("private static extern void __Out_Probe_Lynx_Bump(Probe.Lynx __target);", emitted);
    }

    [Fact]
    public void ACoalescingWriteThroughAComputedProperty_ReadsAndWritesThroughItsAccessors()
    {
        var emitted = EmitDeclaration("Ocelot");

        Assert.Contains(
            "__In_Coalesced(@this, (object?)null, (__t, __k) => __In_Probe_Lynx_get_Note(__t), (__t, __k, __v) => __In_Probe_Lynx_set_Note(__t, __v), () => \"self\");",
            emitted);
    }

    [Fact]
    public void AGenericMethod_IsCalledThroughAGenericAccessor()
    {
        var emitted = EmitDeclaration("Ocelot");

        Assert.Contains("new Probe.Ocelot(__In_Probe_Lynx_Echo<int>(@this, __In_Probe_Lynx_spots(@this)))", emitted);
        Assert.Contains("private static extern T __In_Probe_Lynx_Echo<T>(Probe.Lynx __target, T value);", emitted);
        Assert.Contains("private static extern string __In_Probe_Lynx_Shown<U>(Probe.Lynx __target, U item)", emitted);
        Assert.Contains("    where U : Probe.IShow;", emitted);
    }

    [Fact]
    public void AnIndexer_IsReadAndWrittenThroughItsItemAccessors()
    {
        var emitted = EmitDeclaration("Ocelot");

        Assert.Contains("copy[1] = __In_Probe_Lynx_get_Item(@this, 1) + 10;", emitted);
        Assert.Contains("__Out_Probe_Lynx_set_Item(copy, 1, (@this[1] + 10));", emitted);
        Assert.Contains("private static extern void __Out_Probe_Lynx_set_Item(Probe.Lynx __target, int index, int value);", emitted);
    }

    [Fact]
    public void AWriteThroughASetOnlyIndexer_DeclaresNoGetter()
    {
        var emitted = EmitDeclaration("Caracal");

        Assert.Contains("__Out_Probe_Serval_set_Item(copy, 1, 2);", emitted);
        Assert.DoesNotContain("get_Item", emitted);
    }

    [Fact]
    public void AMethodGroup_IsALambdaOverItsAccessorWithAnExplicitReceiverBoundOnce()
    {
        var emitted = EmitDeclaration("Ocelot");

        Assert.Contains("System.Func<int> three = ((System.Func<int>)(() => __In_Probe_Lynx_Three(@this)));", emitted);
        Assert.Contains("System.Func<int> copyThree = __Out_Bound(copy, __t => (System.Func<int>)(() => __Out_Probe_Lynx_Three(__t)));", emitted);
        Assert.Contains("System.Func<int> three = @this.Three;", emitted);
    }

    [Fact]
    public void AConversionNoCastCalls_IsNotEmitted()
    {
        Assert.DoesNotContain("explicit operator", EmitDeclaration("Puma"));
    }

    private static string EmitDeclaration(string typeName)
    {
        var model = StandardModelProbe.Build(Casts, out var buildDiagnostics, Types);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(
            type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);

        Assert.Empty(emitDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}