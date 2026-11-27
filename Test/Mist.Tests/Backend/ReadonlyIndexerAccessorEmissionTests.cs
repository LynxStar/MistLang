using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ReadonlyIndexerAccessorEmissionTests
{
    [Fact]
    public void WritableIndexerMarksOnlyTheGetterReadonly()
    {
        Assert.Contains("""
                public int this[int index]
                {
                    readonly get
                    {
                        return this.stored;
                    }
                    set
                    {
                        this.stored = value;
                    }
                }
            """.ReplaceLineEndings("\n"), EmitIndexer(writable: true), StringComparison.Ordinal);
    }

    [Fact]
    public void GetterOnlyIndexerMarksTheDeclarationReadonly()
    {
        Assert.Contains("""
                public readonly int this[int index]
                {
                    get
                    {
                        return this.stored;
                    }
                }
            """.ReplaceLineEndings("\n"), EmitIndexer(writable: false), StringComparison.Ordinal);
    }

    private static string EmitIndexer(bool writable)
    {
        var integer = new HandBuiltModel().Type("Int32");
        var owner = HandBuiltModel.Struct("Slot");
        var field = new Field("stored", integer, OwnershipKind.Unrestricted,
            new MemberModifiers { Accessibility = Accessibility.Private })
        {
            DeclaringType = owner,
        };
        owner.FieldsList.Add(field);

        var getter = new PropertyAccessor
        {
            Body = HandBuiltModel.Block(new ReturnStatement(new FieldReferenceExpression(HandBuiltModel.Node(), field), integer)),
        };
        var write = new AssignmentExpression(HandBuiltModel.Node(),
            new FieldReferenceExpression(HandBuiltModel.Node(), field),
            new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", integer)), integer);
        owner.IndexersList.Add(new Indexer(integer, [new Parameter("index", integer)],
            new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = owner,
            Getter = getter,
            Setter = writable ? new PropertyAccessor { Body = HandBuiltModel.Block(new ExpressionStatement(write)) } : null,
        });

        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        TypeEmitter.EmitTypeBody(owner, new ConstraintClosureMap(), new VariadicUseRegistry(), writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().ReplaceLineEndings("\n");
    }
}