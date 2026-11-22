using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// An indexer reached through a value reads that value's own slot (runtime-dependent-values Requirement 7.6):
/// through a plain read the binder substitutes a capture of the receiver's slot read into the key and the
/// result, and through any other receiver the type's closing stays, for Stage 5 to meet through the receiver.
/// </summary>
[Trait("Tier", "3")]
public sealed class ReceiverSlotBindingTests : ProgramModelProbe
{
    public ReceiverSlotBindingTests()
        : base("MistReceiverSlots_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private const string Source = """
        namespace Probe;

        public class Keyed<Capacity:int> where Capacity >= 0
        {
            public int[Capacity] this[int[Capacity] key]
            {
                get { return key; }
            }

            public Keyed<Capacity> Copy()
            {
                Keyed<Capacity> copy = this;
                return copy;
            }
        }

        public class Probe
        {
            public static void Run(int n, int m)
            {
                Keyed<n> q = new Keyed<m>();
                var plain = q[new int[m]];
                var copied = q.Copy()[new int[m]];
            }
        }
        """;

    private IndexExpression Indexed(string name)
    {
        var model = Build(Source, out _);
        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run");
        var declaration = run.Body!.Statements.OfType<LocalDeclarationStatement>().Single(statement => statement.Local.Name == name);
        return Assert.IsType<IndexExpression>(declaration.Initializer);
    }

    private static IMistType Length(IMistType? array)
    {
        return Assert.IsType<ConstructedType>(array).TypeArguments[1];
    }

    [Fact]
    public void AnIndexerThroughAPlainReadTakesAndReturnsTheReceiversOwnSlot()
    {
        var index = Indexed("plain");
        var own = Assert.Single(index.Captures);
        var read = Assert.IsType<MemberAccessExpression>(own.Initializer);

        Assert.Equal("q", Assert.IsType<LocalReferenceExpression>(read.Receiver).Local.Name);
        Assert.Equal("Capacity", read.MemberName);
        Assert.Same(own, Assert.IsType<CapturedValue>(Length(index.ParameterType)).Capture);
        Assert.Same(own, Assert.IsType<CapturedValue>(Length(index.Type)).Capture);
    }

    [Fact]
    public void AnIndexerThroughACallsResultKeepsTheTypesClosing()
    {
        var index = Indexed("copied");
        var receiver = Assert.IsType<ConstructedType>(index.Receiver.Type);

        Assert.Empty(index.Captures);
        Assert.Same(receiver.TypeArguments[0], Length(index.ParameterType));
    }
}