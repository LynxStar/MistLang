// Tier 3 (Component): which declaration a call reaches when two share a closed signature, asserted
// on the recorded method because emission lets Roslyn resolve the call again.
// DeclarationShapeAnalyzer admits these inheritance fixtures except the row that pins a refused
// hide: inherited declarations answer, while a conformer answering an abstract slot supplies the
// implementation.

using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class SignatureHidingTests : ProgramModelProbe
{
    private const string AmbiguousId = "MIST_PM_AMBIGUOUS_OVERLOAD";

    public SignatureHidingTests()
        : base("MistSignatureHiding_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Left
        {
            public class Widget { }
        }

        public class Right
        {
            public class Widget { }
        }

        public class Base
        {
            public string ByRef(int a) { return "base"; }

            public string Nested(Left.Widget w) { return "base"; }

            public string this[int i] { get { return "base"; } }
        }

        public class Derived : Base
        {
            public string ByRef(ref int a) { return "derived"; }

            public string Nested(Right.Widget w) { return "derived"; }

            public string this[string s] { get { return "derived"; } }
        }

        public class Slots<T>
        {
            public string this[T key] { get { return "base"; } }
        }

        public class IntSlots : Slots<int>
        {
        }

        abstract IIndexed
        {
            string this[int i] { get; }
        }

        abstract IReadable : IIndexed
        {
        }

        public class Row : IReadable
        {
            public string this[int i] { get { return "row"; } }
        }

        abstract IShape
        {
            string Sides(int a);
        }

        public class Square : IShape
        {
            public string Sides(int a) { return "square"; }
        }

        abstract IBox<T>
        {
            string Closed(T a);
        }

        public class IntBox : IBox<int>
        {
            public string Closed(int a) { return "conformer"; }
        }

        public class Repeated
        {
            public static string Take<T>(T first) { return "first"; }

            public static string Take<U>(U second) { return "second"; }
        }

        public class Keyed<K>
        {
            public string Take(K key) { return "key"; }

            public string Take(int i) { return "int"; }
        }
        """;

    protected override string Preamble { get; } = """
        int x = 1;
        var derived = new Derived();
        var holder = new IntBox();
        """;

    [Theory]
    [InlineData("var chosen = holder.Closed(x);", "IntBox")]
    [InlineData("var chosen = new Square().Sides(x);", "Square")]
    [InlineData("var chosen = derived.ByRef(x);", "Base")]
    [InlineData("var chosen = derived.ByRef(ref x);", "Derived")]
    [InlineData("var chosen = derived.Nested(new Left.Widget());", "Base")]
    [InlineData("var chosen = derived.Nested(new Right.Widget());", "Derived")]
    public void ADerivedDeclaration_HidesOnlyAnInheritedOneOfTheSameSignature(string statement, string expectedOwner)
    {
        var invocation = InitializerOf<InvocationExpression>(statement);

        Assert.Equal(expectedOwner, invocation.Method?.DeclaringType?.Name);
    }

    [Theory]
    [InlineData("var chosen = derived[1];", "Base")]
    [InlineData("var chosen = derived[\"x\"];", "Derived")]
    public void AnInheritedIndexer_IsACandidateBesideTheDerivedOne(string statement, string expectedOwner)
    {
        var index = InitializerOf<IndexExpression>(statement);

        Assert.Equal(expectedOwner, index.Indexer?.DeclaringType?.Name);
    }

    [Fact]
    public void AnIndexerInheritedFromAGenericBase_TakesTheSlotItsClosingProduces()
    {
        var index = InitializerOf<IndexExpression>("var chosen = new IntSlots()[1];");

        Assert.Equal("Standard.Int32", index.ParameterType?.FullyQualifiedName);
    }

    [Fact]
    public void AnIndexerReachedThroughAnAbstract_IsACandidate()
    {
        var index = InitializerOf<IndexExpression>("IReadable readable = new Row(); var chosen = readable[1];");

        Assert.Equal("IIndexed", index.Indexer?.DeclaringType?.Name);
    }

    [Fact]
    public void AMemberThatWouldHideAnInheritedOne_LeavesTheUseBoundToTheInherited()
    {
        var invocation = InitializerOf<InvocationExpression>(
            "var chosen = new Leaf().Take(1);",
            "public class Root { public int Take(int a) { return 1; } } public class Leaf : Root { public int Take(int b) { return 2; } }");

        Assert.Equal("Root", invocation.Method?.DeclaringType?.Name);
    }

    [Fact]
    public void ALaterDeclarationRepeatingASignature_LeavesTheUseBoundToTheFirst()
    {
        Assert.Empty(Errors(BuildRun("var chosen = Repeated.Take(1);"), AmbiguousId));

        var invocation = InitializerOf<InvocationExpression>("var chosen = Repeated.Take(1);");
        Assert.Equal("first", invocation.Method?.ParametersList[0].Name);
    }

    [Fact]
    public void OverloadsAReceiversArgumentMakesIdentical_AreNotRepeats()
    {
        var invocation = InitializerOf<InvocationExpression>("var chosen = new Keyed<int>().Take(3);");

        Assert.Equal("i", invocation.Method?.ParametersList[0].Name);
    }
}