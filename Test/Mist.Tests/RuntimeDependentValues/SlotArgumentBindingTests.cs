using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "3")]
public sealed class SlotArgumentBindingTests : ProgramModelProbe
{
    public SlotArgumentBindingTests()
        : base("MistSlotArguments_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private const string Source = """
        namespace Probe;

        public class Width
        {
        }

        public class Tagged<T, Capacity:int>
        {
        }

        public class Holder
        {
            int n;
            Tagged<Width, n> held;
        }

        public class Probe
        {
            static int Width = 4;

            public static void Run()
            {
                Tagged<Width, Width> tagged = new Tagged<Width, Width>();
            }
        }
        """;

    [Fact]
    public void ANameIsAValueInADependentSlotAndATypeInATypeSlot()
    {
        var model = Build(Source, out _);
        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run");
        var declaration = Assert.IsType<LocalDeclarationStatement>(Assert.Single(run.Body!.Statements));
        var written = Assert.IsType<ConstructedType>(declaration.Local.Type);
        var created = Assert.IsType<NewExpression>(declaration.Initializer);

        Assert.Same(model.TypesByFQN["Probe.Width"], written.TypeArguments[0]);
        var slot = Assert.IsType<CapturedValue>(written.TypeArguments[1]);
        Assert.Same(slot.Capture, Assert.Single(declaration.Captures));
        Assert.Equal("Width", Assert.IsType<FieldReferenceExpression>(slot.Capture.Initializer).Field.Name);
        Assert.True(slot.Capture.IsReadonly);
        Assert.Same(Assert.Single(created.Captures).Value, Assert.IsType<ConstructedType>(created.Type).TypeArguments[1]);
        Assert.NotSame(slot, created.Captures[0].Value);
    }

    [Fact]
    public void AValuesNameInAFieldTypesSlotIsRefused()
    {
        _ = Build(Source, out var diagnostics);
        var refused = Assert.Single(Errors(diagnostics, "MIST0487"));

        Assert.Contains("cannot name the value 'n'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACreationSizedByAValueAndTheVarDeclaredFromItHoldTheCapture()
    {
        var model = StandardModelProbe.Build(
            """
            ZeroOrPositiveInt n = 4;
            var a = new int[n];
            var f = (int k) => new int[n];
            """,
            out var diagnostics);
        var statements = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!
            .Statements.OfType<LocalDeclarationStatement>().ToList();
        var creation = Assert.IsType<ArrayCreationExpression>(statements[1].Initializer);
        var length = Assert.Single(creation.Captures);
        var lambda = Assert.IsAssignableFrom<IFunctionType>(statements[2].Local.Type);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error);
        Assert.Same(creation.Size, length.Initializer);
        Assert.Same(length.Value, Assert.IsType<ConstructedType>(creation.Type).TypeArguments[1]);
        Assert.Same(creation.Type, statements[1].Local.Type);
        Assert.Equal("int[n]", TypeDisplay.Mist(statements[1].Local.Type));
        Assert.Equal("int[]", TypeDisplay.Mist(lambda.ReturnType));
        _ = Assert.IsType<UnknownValueType>(Assert.IsType<ConstructedType>(lambda.ReturnType).TypeArguments[1]);
    }

    [Fact]
    public void AnInferredParameterOverAnUnknownLengthReadsTheArgumentsSlotIntoACaptureOnTheCall()
    {
        var model = StandardModelProbe.Build(
            """
            int[] a = [1, 2, 3];
            var n = Api.Len(a);
            var m = Api.Len(Api.Make());
            """,
            out var diagnostics,
            extraTypes: """
            public class Api
            {
                public static int Len<N:ZeroOrPositiveInt>(Array<int, N> a)
                {
                    return 0;
                }

                public static int[] Make()
                {
                    return [1];
                }
            }
            """);
        var statements = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!
            .Statements.OfType<LocalDeclarationStatement>().ToList();
        var call = Assert.IsType<InvocationExpression>(statements[1].Initializer);
        var capture = Assert.Single(call.Captures);
        var read = Assert.IsType<MemberAccessExpression>(capture.Initializer);
        var made = Assert.Single(Assert.IsType<InvocationExpression>(statements[2].Initializer).Captures);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal("a.Length", capture.Name);
        Assert.Equal("a", Assert.IsType<LocalReferenceExpression>(read.Receiver).Local.Name);
        Assert.Equal("Length", Assert.IsType<TypeParameter>(Assert.IsType<TypeResult>(read.Member).Type).Name);
        Assert.Same(capture.Value, Assert.IsType<ConstructedType>(call.ParameterTypes![0]).TypeArguments[1]);
        Assert.Equal("Api.Make().Length", made.Name);
        _ = Assert.IsType<InvocationExpression>(Assert.IsType<MemberAccessExpression>(made.Initializer).Receiver);
    }

    [Fact]
    public void ACaptureIsNamedByItsWholeSizeExpressionAndNoCommonTypeNamesItAsWritten()
    {
        var model = StandardModelProbe.Build(
            """
            ZeroOrPositiveInt n = 4;
            ZeroOrPositiveInt m = 5;
            int[] source = [1, 2];
            var read = new int[source.Length];
            var called = new int[Sizes.Next()];
            var stack = stackalloc int[source.Length];
            var a = new int[n];
            var c = new int[m];
            var jag = [a, c];
            var picked = n switch { 1 => a, _ => c };
            """,
            out var diagnostics,
            extraTypes: """
            public class Sizes
            {
                public static ZeroOrPositiveInt Next()
                {
                    return 3;
                }
            }
            """);
        var statements = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!
            .Statements.OfType<LocalDeclarationStatement>().ToList();

        Assert.Equal("int[source.Length]", TypeDisplay.Mist(statements[3].Local.Type));
        Assert.Equal("int[Sizes.Next()]", TypeDisplay.Mist(statements[4].Local.Type));
        Assert.Equal("source.Length", Assert.Single(Assert.IsType<StackallocExpression>(statements[5].Initializer).Captures).Name);
        Assert.Contains("('int[n]' and 'int[m]')", Assert.Single(Errors(diagnostics, "MIST0376")).Message, StringComparison.Ordinal);
        Assert.Contains("('int[n]' and 'int[m]')", Assert.Single(Errors(diagnostics, "MIST0347")).Message, StringComparison.Ordinal);
    }
}