using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class LocalStorageFlowTests
{
    [Theory]
    [InlineData("imported")]
    [InlineData("unknown")]
    [InlineData("intrinsic")]
    [InlineData("tuple")]
    [InlineData("named-tuple")]
    [InlineData("closed-pack")]
    [InlineData("fixed-pack")]
    public void KnownWritesAssignOnlyTheirOwnStorage(string shape)
    {
        var fixture = new Fixture(shape);
        var findings = fixture.Analyze(Fixture.Write(fixture.First), Fixture.Read(fixture.First), Fixture.Read(fixture.Second));

        var finding = Assert.Single(findings);
        Assert.Equal("MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT", finding.Id.Value);
    }

    [Theory]
    [InlineData("imported", true)]
    [InlineData("unknown", false)]
    [InlineData("intrinsic", false)]
    [InlineData("tuple", true)]
    [InlineData("named-tuple", true)]
    [InlineData("closed-pack", true)]
    [InlineData("fixed-pack", true)]
    [InlineData("hidden", false)]
    [InlineData("auto-property", false)]
    [InlineData("unresolved-base", false)]
    public void AllVisibleWritesPermitWholeReadOnlyWithoutRemainder(string shape, bool assigned)
    {
        var fixture = new Fixture(shape);
        var findings = fixture.Analyze(Fixture.Write(fixture.First), Fixture.Write(fixture.Second), Fixture.Read(fixture.Local));

        Assert.Equal(assigned ? 0 : 1, findings.Count);
    }

    [Theory]
    [InlineData("imported")]
    [InlineData("unknown")]
    [InlineData("named-tuple")]
    [InlineData("closed-pack")]
    public void WholeAndElementWritesMeetInOneSlotUniverse(string shape)
    {
        var fixture = new Fixture(shape);
        var branch = new IfStatement(fixture.Condition,
            HandBuiltModel.Block(Fixture.Write(fixture.Local)),
            HandBuiltModel.Block(Fixture.Write(fixture.First), Fixture.Write(fixture.Second)));

        Assert.Empty(fixture.Analyze(branch, Fixture.Read(fixture.First), Fixture.Read(fixture.Second)));
        Assert.Empty(fixture.Analyze(Fixture.Write(fixture.Local), Fixture.Read(fixture.Local)));
    }

    [Theory]
    [InlineData("tuple")]
    [InlineData("imported")]
    [InlineData("closed-pack")]
    public void DifferentBranchWritesDoNotCombine(string shape)
    {
        var fixture = new Fixture(shape);
        var branch = new IfStatement(fixture.Condition,
            HandBuiltModel.Block(Fixture.Write(fixture.First)),
            HandBuiltModel.Block(Fixture.Write(fixture.Second)));

        _ = Assert.Single(fixture.Analyze(branch, Fixture.Read(fixture.Local)));
    }

    [Theory]
    [InlineData("imported")]
    [InlineData("named-tuple")]
    public void LoopBodyWritesDoNotAssignOnTheZeroIterationPath(string shape)
    {
        var fixture = new Fixture(shape);
        var loop = new WhileStatement(fixture.Condition, HandBuiltModel.Block(Fixture.Write(fixture.Second)));

        _ = Assert.Single(fixture.Analyze(Fixture.Write(fixture.First), loop, Fixture.Read(fixture.Second)));
    }

    [Fact]
    public void NamedTupleAndPositionalAliasesShareTheirSlot()
    {
        var fixture = new Fixture("named-tuple");
        var named = Assert.IsType<NamedTupleType>(fixture.Local.Type);
        var positional = Fixture.Access(fixture.Local, named.Underlying.PositionalFields[0]);

        Assert.Empty(fixture.Analyze(Fixture.Write(fixture.First), Fixture.Read(positional)));
        Assert.Empty(fixture.Analyze(Fixture.Write(positional), Fixture.Read(fixture.First)));
    }

    [Fact]
    public void FreshVariadicProjectionObjectsShareAggregateElementStorage()
    {
        var fixture = new Fixture("closed-pack");
        var first = Assert.IsType<FieldResult>(Assert.IsType<MemberAccessExpression>(fixture.First).Member).Field;
        var repeated = Fixture.Access(fixture.Local, new Field(first.Name, first.Type, default, default)
        {
            StorageSource = first.StorageSource,
            StorageElementIndex = first.StorageElementIndex,
        });

        Assert.Empty(fixture.Analyze(Fixture.Write(fixture.First), Fixture.Read(repeated)));
        _ = Assert.Single(fixture.Analyze(Fixture.Write(repeated), Fixture.Read(fixture.Second)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TuplePackProjectionAssignsOnlyItsSelectedElement(bool named)
    {
        var model = new HandBuiltModel();
        var pack = new TypeParameter("Ts", 0, new Scope(null), null)
        {
            PackKind = TypePackKind.Unbracketed,
            PackLength = 2,
        };
        var aggregateType = new TupleType([new SpreadType(pack)]);
        var tuple = new TupleType([aggregateType, model.Type("Reference")]);
        var namedTuple = new NamedTupleType(tuple, ["values", "other"]);
        IMistType type = named ? namedTuple : tuple;
        var variable = new LocalVariable("value", type);
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), variable);
        var aggregate = named ? namedTuple.Fields[2] : tuple.PositionalFields[0];
        var first = Fixture.Access(receiver, new Field($"{aggregate.Name}1", pack.ElementAt(0), default, default)
        {
            StorageSource = aggregate,
            StorageElementIndex = 0,
        });
        var second = Fixture.Access(receiver, new Field($"{aggregate.Name}2", pack.ElementAt(1), default, default)
        {
            StorageSource = aggregate,
            StorageElementIndex = 1,
        });
        var sibling = Fixture.Access(receiver, named ? namedTuple.Fields[3] : tuple.PositionalFields[1]);
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(variable, null),
            Fixture.Write(first), Fixture.Read(first), Fixture.Read(second), Fixture.Read(sibling));

        var findings = model.Analyze(new DefiniteAssignmentAnalyzer(), body);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal("MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT", finding.Id.Value));
    }

    [Fact]
    public void CapturedReadRequiresOnlyItsElementButStillRejectsMissingSibling()
    {
        var fixture = new Fixture("tuple");
        var functionType = new HandBuiltModel().Interner.GetOrCreateFunctionType([], fixture.First.Type);
        var capture = new ExpressionStatement(new LambdaExpression(HandBuiltModel.Node(), [], null,
            HandBuiltModel.Block(Fixture.Read(fixture.Second)), false, functionType));

        _ = Assert.Single(fixture.Analyze(Fixture.Write(fixture.First), capture));
        Assert.Empty(fixture.Analyze(Fixture.Write(fixture.Second), capture));
    }

    [Fact]
    public void ObservedRecursivePathsKeepAFiniteWholeWriteUniverse()
    {
        var recursive = HandBuiltModel.Struct("Recursive");
        var next = new Field("next", recursive, default, default);
        recursive.FieldsList.Add(next);
        var local = new LocalVariable("value", recursive);
        Expression path = new LocalReferenceExpression(HandBuiltModel.Node(), local);
        for(var index = 0; index < 8; index++)
        {
            path = Fixture.Access(path, next);
        }

        var slots = new LocalAssignmentSlots([local], new ExpressionStatement(path));

        _ = Assert.Single(slots.All);
        Assert.Equal(slots.Required(LocalStoragePath.Of(path)), slots.Written(new LocalStoragePath(local)));
    }

    private sealed class Fixture
    {
        private readonly HandBuiltModel model = new();
        private readonly IMistType integer = new StructType("Int32", "Standard", new Scope(null), null, null)
        {
            Modifiers = new MemberModifiers { IsIntrinsic = true },
        };

        public Fixture(string shape)
        {
            var first = new Field("first", integer, default, default);
            var second = new Field("second", integer, default, default);
            IMistType type;
            if(shape is "tuple" or "named-tuple")
            {
                var tuple = new TupleType([integer, integer]);
                if(shape == "named-tuple")
                {
                    var named = new NamedTupleType(tuple, ["left", "right"]);
                    type = named;
                    first = named.Fields[2];
                    second = named.Fields[3];
                }
                else
                {
                    type = tuple;
                    first = tuple.PositionalFields[0];
                    second = tuple.PositionalFields[1];
                }
            }
            else if(shape is "closed-pack" or "fixed-pack")
            {
                var bundle = HandBuiltModel.Struct("Bundle<>");
                var pack = new TypeParameter("Ts", 0, bundle.Scope, bundle)
                {
                    PackKind = TypePackKind.Unbracketed,
                    PackLength = shape == "fixed-pack" ? 2 : null,
                };
                bundle.TypeParametersList.Add(pack);
                var aggregate = new Field("Item", new TupleType([new SpreadType(pack)]), default, default);
                bundle.FieldsList.Add(aggregate);
                type = shape == "fixed-pack" ? bundle : new ConstructedType(bundle,
                    [new LiftedType(model.Type("Type"), new TypePackValue([integer, integer]))]);
                first = new Field("Item1", integer, default, default) { StorageSource = aggregate, StorageElementIndex = 0 };
                second = new Field("Item2", integer, default, default) { StorageSource = aggregate, StorageElementIndex = 1 };
            }
            else
            {
                var structure = HandBuiltModel.Struct("Pair");
                structure.FieldsList.AddRange([first, second]);
                var fields = new List<StorageField> { new(first.Name, integer, first), new(second.Name, integer, second) };
                if(shape == "hidden")
                {
                    fields.Add(new StorageField("hidden", HandBuiltModel.Class("Reference")));
                }

                if(shape is "imported" or "unknown" or "hidden")
                {
                    structure.ClrMetadataName = "Probe.Pair";
                    structure.ImportedStorage = new StorageLayout(shape != "unknown", shape == "unknown" ? [] : fields);
                }

                if(shape == "intrinsic")
                {
                    structure.Modifiers = new MemberModifiers { IsIntrinsic = true };
                }

                if(shape == "auto-property")
                {
                    structure.PropertiesList.Add(new Property("Item", integer, default) { Getter = new PropertyAccessor { IsAuto = true } });
                }

                structure.BaseUnresolved = shape == "unresolved-base";
                type = structure;
            }

            Variable = new LocalVariable("value", type);
            Local = new LocalReferenceExpression(HandBuiltModel.Node(), Variable);
            First = Access(Local, first);
            Second = Access(Local, second);
        }

        public LocalVariable Variable { get; }

        public Expression Local { get; }

        public Expression First { get; }

        public Expression Second { get; }

        public Expression Condition => HandBuiltModel.Literal(model.Type("Boolean"));

        public IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
        {
            return model.Analyze(new DefiniteAssignmentAnalyzer(),
                HandBuiltModel.Block([new LocalDeclarationStatement(Variable, null), .. statements]));
        }

        public static Statement Write(Expression target)
        {
            return new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), target, HandBuiltModel.Literal(target.Type), target.Type));
        }

        public static Statement Read(Expression expression)
        {
            return new ExpressionStatement(expression);
        }

        public static Expression Access(Expression receiver, Field field)
        {
            return new MemberAccessExpression(HandBuiltModel.Node(), receiver, field.Name, field.Type,
                member: new FieldResult(field, receiver.Type, []));
        }
    }
}