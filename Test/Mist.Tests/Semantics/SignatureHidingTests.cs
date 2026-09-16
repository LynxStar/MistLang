// Tier 3 (Component): which declaration a call reaches when two share a closed signature -- a derived
// type's hiding a base's, or a later declaration repeating an earlier one on the same type -- asserted on
// the recorded method, since emission lets Roslyn resolve the call again. Every INHERITANCE fixture here
// is a program order 6 admits: a member that repeats what its type inherits from a CLASS is refused
// instead, so what still hides is a conformer answering an abstract's slot, which has no body to hide.
// Why each verdict is the right one is recorded on order 6 of
// backlog/analysis-v2-capability-progression.yaml.

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
        }

        public class Derived : Base
        {
            public string ByRef(ref int a) { return "derived"; }

            public string Nested(Right.Widget w) { return "derived"; }
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
    [InlineData("var chosen = derived.ByRef(x);", "Base")]
    [InlineData("var chosen = derived.ByRef(ref x);", "Derived")]
    [InlineData("var chosen = derived.Nested(new Left.Widget());", "Base")]
    [InlineData("var chosen = derived.Nested(new Right.Widget());", "Derived")]
    public void ADerivedDeclaration_HidesOnlyAnInheritedOneOfTheSameSignature(string statement, string expectedOwner)
    {
        var invocation = InitializerOf<InvocationExpression>(statement);

        Assert.Equal(expectedOwner, invocation.Method?.DeclaringType?.Name);
    }

    [Fact]
    public void ALaterDeclarationRepeatingASignature_LeavesTheUseBoundToTheFirst()
    {
        Assert.Empty(Errors(BuildRun("var chosen = Repeated.Take(1);"), AmbiguousId));

        var invocation = InitializerOf<InvocationExpression>("var chosen = Repeated.Take(1);");
        Assert.Equal("first", invocation.Method?.ParametersList[0].Name);
    }

    [Fact]
    public void OverloadsAReceiversArgumentMakesIdentical_StayAmbiguous()
    {
        _ = Assert.Single(Errors(BuildRun("var chosen = new Keyed<int>().Take(3);"), AmbiguousId));
    }
}