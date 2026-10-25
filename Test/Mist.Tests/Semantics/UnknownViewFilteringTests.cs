// Tier 3 (Component): the builder over minimal source, asserting on the model and its diagnostics,
// for the facts no single binder owns -- that the summaries the trial computes agree whatever order
// the declarations come in, and that a body-based writer, a reader overload and an extension method
// are judged through the real lookup chain. The decisions themselves are at Tiers 1 and 2.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "3")]
public sealed class UnknownViewFilteringTests : ProgramModelProbe
{
    private const string Hidden = "MIST0395";

    public UnknownViewFilteringTests()
        : base("MistUnknownView_")
    {
    }

    protected override string Fixtures { get; } = """
        public class MyList<T>
        {
            public int count;
            public T item0;

            public MyList()
            {
                this.count = 0;
            }

            public int Size()
            {
                return this.count;
            }

            public T GetAt(int index)
            {
                return this.item0;
            }

            public void Add(T value)
            {
                this.item0 = value;
            }

            public int Add(string label)
            {
                this.count = this.count + 1;
                return this.count;
            }

            public void Store(T value)
            {
                this.item0 = value;
            }

            public void Forward(T value)
            {
                this.Store(value);
            }
        }

        <E> extension(MyList<E> self)
        {
            public void Overwrite(E value)
            {
                self.item0 = value;
            }

            public E Look()
            {
                return self.item0;
            }
        }
        """;

    protected override string Preamble { get; } = """
        MyList<int> ints = new MyList<int>();
        MyList<unknown> widened = ints;
        """;

    [Fact]
    public void ABodyBasedWriterIsHiddenDirectlyAndTransitively()
    {
        var direct = BuildRun("widened.Store(99);");
        var transitive = BuildRun("widened.Forward(99);");

        Assert.Contains("'Store' cannot be called on 'MyList<unknown>'", Assert.Single(Errors(direct, Hidden)).Message, StringComparison.Ordinal);
        Assert.Contains("'Forward' cannot be called on 'MyList<unknown>'", Assert.Single(Errors(transitive, Hidden)).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>Add(T)</c> is hidden and <c>Add(string)</c> is not, so the call selects the reader on its own
    /// terms rather than refusing the name.
    /// </summary>
    [Fact]
    public void AReaderOverloadIsSelectedWhereTheWriterIsHidden()
    {
        var call = InitializerOf<InvocationExpression>("var labelled = widened.Add(\"x\");");

        Assert.Equal("string", TypeDisplay.Mist(call.Method!.ParametersList[0].Type));
    }

    [Fact]
    public void ReadersAndFieldReadsStayAvailable()
    {
        var size = InitializerOf<InvocationExpression>("var size = widened.Size();");
        var read = InitializerOf<MemberAccessExpression>("var first = widened.item0;");

        Assert.Equal("int", TypeDisplay.Mist(size.Type));
        Assert.Equal("unknown", TypeDisplay.Mist(read.Type));
    }

    [Fact]
    public void AnExtensionWriterIsHiddenAndAnExtensionReaderIsNot()
    {
        var hidden = BuildRun("widened.Overwrite(99);");
        var look = InitializerOf<InvocationExpression>("var seen = widened.Look();");

        Assert.Contains("'Overwrite' cannot be called on 'MyList<unknown>'", Assert.Single(Errors(hidden, Hidden)).Message, StringComparison.Ordinal);
        Assert.Equal("unknown", TypeDisplay.Mist(look.Type));
    }

    /// <summary>
    /// The site sits in a generic body declared BEFORE the type it closes, so its classification is
    /// demanded before that type's bodies are bound; the same program with the declarations swapped
    /// must report the same thing.
    /// </summary>
    [Fact]
    public void DeclarationOrderDoesNotChangeTheResult()
    {
        const string user = """
            public class Early<U>
            {
                public U held;

                public void Uses(Box<unknown> box, Early<unknown> other)
                {
                    box.Set(1);
                    unknown seen = box.Get();
                    other.Hold(2);
                }

                public void Hold(U value)
                {
                    this.held = value;
                }
            }
            """;
        const string box = """
            public class Box<T>
            {
                public T slot;

                public void Set(T value)
                {
                    this.slot = value;
                }

                public T Get()
                {
                    return this.slot;
                }
            }
            """;

        _ = Build($"namespace Probe;\n{user}\n{box}", out var userFirst);
        _ = Build($"namespace Probe;\n{box}\n{user}", out var boxFirst);

        Assert.Equal(
            ["'Set' cannot be called on 'Box<unknown>'", "'Hold' cannot be called on 'Early<unknown>'"],
            Errors(userFirst, Hidden).Select(diagnostic => diagnostic.Message[..diagnostic.Message.IndexOf(':', StringComparison.Ordinal)]));
        Assert.Equal(
            Errors(userFirst, Hidden).Select(diagnostic => diagnostic.Message),
            Errors(boxFirst, Hidden).Select(diagnostic => diagnostic.Message));
        Assert.Empty(userFirst.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Number.Value != Hidden));
    }
}