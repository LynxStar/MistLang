// Tier 3 (Component): what an operator expression and an index access RECORD, asserted against a real
// ProgramModelBuilder run over minimal source. Emission re-writes both as source spelled them and lets
// Roslyn resolve them again, so a rule about the member reached -- whether it is readonly -- can only
// read it off the model. An intrinsic indexer is deliberately not recorded: an array's or a span's
// element access has no accessor to judge. See ProgramModelProbe for the harness.

using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class SelectionRecordingTests : ProgramModelProbe
{
    public SelectionRecordingTests()
        : base("MistSelectionRecording_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct Vec
        {
            public int x;

            public Vec(int x)
            {
                this.x = x;
            }

            public static Vec operator +(Vec a, Vec b)
            {
                return new Vec(a.x + b.x);
            }

            public static Vec operator -(Vec a)
            {
                return new Vec(-a.x);
            }
        }

        public class Grid
        {
            public int cell;

            public int this[int i]
            {
                get
                {
                    return cell;
                }
                set
                {
                    cell = value;
                }
            }
        }

        public class Table
        {
            public int this[int i] { get { return i; } }

            public string this[string key] { get { return key; } }
        }

        public class Ledger
        {
            public int Count { get { return 2; } }

            public string this[string key] { get { return key; } }

            public int this[int i] { get { return i; } }
        }

        public class Keyed<K>
        {
            public int this[int i] { get { return i; } }

            public K this[K key] { get { return key; } }
        }
        """;

    protected override string Preamble { get; } = """
        var a = new Vec(1);
        var b = new Vec(2);
        var grid = new Grid();
        var table = new Table();
        var ledger = new Ledger();
        var keyed = new Keyed<string>();
        int[] cells = [1, 2, 3];
        """;

    [Fact]
    public void ABinaryExpressionRecordsTheOperatorItSelected()
    {
        var sum = InitializerOf<BinaryExpression>("var sum = a + b;");

        Assert.NotNull(sum.Operator);
        Assert.Equal(OperatorKind.Plus, sum.Operator.Kind);
    }

    [Fact]
    public void AUnaryExpressionRecordsTheOperatorItSelected()
    {
        var negated = InitializerOf<UnaryExpression>("var negated = -a;");

        Assert.NotNull(negated.Operator);
        Assert.Equal(OperatorKind.Minus, negated.Operator.Kind);
    }

    [Fact]
    public void AnIndexAccessRecordsTheUserIndexer()
    {
        var read = InitializerOf<IndexExpression>("var read = grid[0];");

        Assert.NotNull(read.Indexer);
        Assert.Same(read.Indexer, ((ClassType)read.Receiver.Type).IndexersList[0]);
    }

    [Fact]
    public void AnIndexAccessRecordsTheIndexerItsArgumentSelects_NotTheFirstDeclared()
    {
        var read = InitializerOf<IndexExpression>("var read = table[\"a\"];");

        Assert.Same(((ClassType)read.Receiver.Type).IndexersList[1], read.Indexer);
        Assert.Equal("Standard.String", read.ParameterType?.FullyQualifiedName);
        Assert.Equal("Standard.String", read.Type.FullyQualifiedName);
    }

    [Fact]
    public void AnIndexIntoAConstructedReceiverRecordsTheSelectedSlotSubstituted()
    {
        var read = InitializerOf<IndexExpression>("var read = keyed[\"a\"];");
        var definition = (ClassType)((ConstructedType)read.Receiver.Type).Definition;

        Assert.Same(definition.IndexersList[1], read.Indexer);
        Assert.Equal("Standard.String", read.ParameterType?.FullyQualifiedName);
        Assert.Equal("Standard.String", read.Type.FullyQualifiedName);
    }

    [Fact]
    public void AFromEndIndexRecordsThePositionalIndexerAndNoSlot()
    {
        var read = InitializerOf<IndexExpression>("var read = ledger[^1];");

        Assert.Same(((ClassType)read.Receiver.Type).IndexersList[1], read.Indexer);
        Assert.Null(read.ParameterType);
    }

    [Fact]
    public void AnArrayElementAccessRecordsNoIndexer()
    {
        Assert.Null(InitializerOf<IndexExpression>("var first = cells[0];").Indexer);
    }
}