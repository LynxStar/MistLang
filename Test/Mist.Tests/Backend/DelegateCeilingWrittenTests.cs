using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Backend;

// The walk that refuses a function written past the ceiling before anything is emitted (variadics
// Requirement 16.2), over every place a model can write a type: each kind of member signature, an alias or
// newtype, an extension block, and a body's locals, local functions and expressions. One refusal per member,
// at that member's own declaration, under an identity that tells the members apart.
[Trait("Tier", "2")]
public sealed class DelegateCeilingWrittenTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly HandBuiltModel built = new();
    private readonly CompilationUnit unit = new() { SourcePath = "Probe.mist" };
    private readonly ClassType box = new("Box", "Probe", new Scope(null), null, null);
    private readonly FunctionType wide;

    public DelegateCeilingWrittenTests()
    {
        wide = new FunctionType([.. Enumerable.Repeat<IMistType>(built.Type("Int32"), 17)], built.Type("String"));
        built.Model.AddSelfAssembly("Probe", isLibrary: true).CompilationUnits.Add(unit);
        unit.Types.Add(box);
    }

    [Theory]
    [InlineData("property", 4, "Box.Map")]
    [InlineData("field", 5, "Box.map")]
    [InlineData("indexer-parameter", 6, "Box.this[")]
    [InlineData("alias", 7, "Mapper")]
    [InlineData("newtype", 8, "Mapping")]
    [InlineData("extension-method", 9, "Map")]
    [InlineData("extension-property", 10, ".Map")]
    [InlineData("local-function", 12, "Run")]
    [InlineData("uninitialized-local", 2, "Run")]
    [InlineData("initialized-local", 13, "Run")]
    [InlineData("expression", 14, "Run")]
    public void AFunctionWrittenPastTheCeiling_IsRefusedWhereItsMemberIsDeclared(string place, int line, string member)
    {
        Write(place);

        var refusal = Assert.Single(Judge());

        Assert.Equal(("MIST0028", line), (refusal.Number.Value, refusal.Position().Start.Line));
        Assert.Contains("takes 17 inputs, and System.Func and System.Action hold at most 16", refusal.Message);
        Assert.Contains(member, Subject(refusal).Identity.Split('|')[1]);
    }

    [Fact]
    public void EachMemberIsRefusedOnce_UnderAnIdentityOfItsOwn()
    {
        var map = new Method("Map", built.Type("Int32"), [new Parameter("f", wide), new Parameter("g", wide)], Public)
        {
            DeclaringType = box,
            DeclarationNode = AtLine(2),
            Body = HandBuiltModel.Block(new ExpressionStatement(new LiteralExpression(AtLine(3), null, wide))),
        };
        box.MethodsList.Add(map);
        box.PropertiesList.Add(new Property("Fold", wide, Public) { DeclaringType = box, DeclarationNode = AtLine(4) });
        box.PropertiesList.Add(new Property("Scan", wide, Public) { DeclaringType = box, DeclarationNode = AtLine(5) });
        box.FieldsList.Add(new Field("zip", wide, OwnershipKind.Unrestricted, Public) { DeclaringType = box, DeclarationNode = AtLine(6) });

        var refusals = Judge();

        Assert.Equal([2, 4, 5, 6], refusals.Select(refusal => refusal.Position().Start.Line));
        Assert.Equal(4, refusals.Select(refusal => Subject(refusal).Identity).Distinct().Count());
    }

    [Fact]
    public void AWrittenFunctionWithNoDeclarationToPointAt_IsLeftForEmissionToRefuse()
    {
        box.PropertiesList.Add(new Property("Map", wide, Public) { DeclaringType = box });
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        using(DelegateCeiling.Enter(built.Model, diagnostics))
        {
            Assert.Empty(diagnostics.ToImmutableArray());

            using(DelegateCeiling.Emitting(AtLine(3), "Box", "Probe.Box"))
            {
                DelegateCeiling.Spelled(wide, 17);
            }
        }

        Assert.Equal(3, Assert.Single(diagnostics.ToImmutableArray()).Position().Start.Line);
    }

    private void Write(string place)
    {
        var int32 = built.Type("Int32");

        switch(place)
        {
            case "property":
                box.PropertiesList.Add(new Property("Map", wide, Public) { DeclaringType = box, DeclarationNode = AtLine(4) });
                break;

            case "field":
                box.FieldsList.Add(new Field("map", wide, OwnershipKind.Unrestricted, Public) { DeclaringType = box, DeclarationNode = AtLine(5) });
                break;

            case "indexer-parameter":
                box.IndexersList.Add(new Indexer(int32, [new Parameter("f", wide)], Public) { DeclaringType = box, DeclarationNode = AtLine(6) });
                break;

            case "alias":
                unit.Types.Add(new TypeAlias("Mapper", "Probe", new Scope(null), null, At<TypeAliasDeclarationNode>(7)) { AliasedType = wide });
                break;

            case "newtype":
                unit.Types.Add(new Newtype("Mapping", "Probe", new Scope(null), null, At<NewtypeDeclarationNode>(8)) { SourceType = wide });
                break;

            case "extension-method":
                Extension().MethodsList.Add(new Method("Map", int32, [new Parameter("f", wide)], Public) { DeclarationNode = AtLine(9) });
                break;

            case "extension-property":
                Extension().PropertiesList.Add(new Property("Map", wide, Public) { DeclarationNode = AtLine(10) });
                break;

            case "local-function":
                Run(new LocalFunctionStatement(
                    new Method("apply", int32, [new Parameter("f", wide)], default) { DeclarationNode = AtLine(12), Body = HandBuiltModel.Block() }, []));
                break;

            case "uninitialized-local":
                Run(new LocalDeclarationStatement(new LocalVariable("f", wide), null));
                break;

            case "initialized-local":
                Run(new LocalDeclarationStatement(new LocalVariable("f", wide), new LiteralExpression(AtLine(13), 1L, int32)));
                break;

            case "expression":
                Run(new ExpressionStatement(new LiteralExpression(AtLine(14), null, wide)));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(place));
        }
    }

    private ExtensionBlock Extension()
    {
        var block = new ExtensionBlock("self", "Probe", new Scope(null), null) { ResolvedReceiverType = built.Type("Int32") };
        unit.Extensions.Add(block);
        return block;
    }

    private void Run(Statement statement)
    {
        box.MethodsList.Add(new Method("Run", built.Type("Int32"), [], Public)
        {
            DeclaringType = box,
            DeclarationNode = AtLine(2),
            Body = HandBuiltModel.Block(statement),
        });
    }

    private IReadOnlyList<Diagnostic> Judge()
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        using(DelegateCeiling.Enter(built.Model, diagnostics))
        {
        }

        return diagnostics.ToImmutableArray();
    }

    private static SymbolValue Subject(Diagnostic refusal)
    {
        return Assert.IsType<SymbolValue>(refusal.Arguments[0]);
    }

    private static SyntaxNode AtLine(int line)
    {
        return At<TrueLiteralExpressionPrimaryNode>(line);
    }

    private static TNode At<TNode>(int line)
        where TNode : SyntaxNode, new()
    {
        return new TNode
        {
            Span = new Storm.Runtime.SourceSpan(line - 1, 1),
            Origin = new ParsedOrigin(new SourceText("Probe.mist", new string('\n', line - 1) + "x")),
        };
    }
}