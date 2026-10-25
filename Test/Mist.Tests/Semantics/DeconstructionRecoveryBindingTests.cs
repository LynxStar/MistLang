using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class DeconstructionRecoveryBindingTests
{
    private const string DeclarationPath = "Probe/Deconstruction.mist";

    [Theory]
    [InlineData(false, "missing", "MIST_PM_UNRESOLVED_IDENTIFIER")]
    [InlineData(true, "missing", "MIST_PM_UNRESOLVED_IDENTIFIER")]
    [InlineData(false, "reported-local", null)]
    [InlineData(true, "reported-local", null)]
    public void AReportedSourceFailureKeepsEveryNameWithoutClaimingSuccess(bool typed, string source, string? diagnosticId)
    {
        var f = new BindingFragments();
        if(source == "reported-local")
        {
            _ = f.Local(source, ErrorType.Instance, failed: true);
        }

        _ = Assert.IsType<UnsupportedStatement>(Bind(f, Declaration(typed, source)));
        ReadAndCall(f, "first", failed: true);
        ReadAndCall(f, "second", failed: true);

        string[] expected = diagnosticId is null ? [] : [diagnosticId];
        Assert.Equal(expected, f.Diagnostics.ToImmutableArray().Select(d => d.Number.Value));
        Assert.Equal(typed ? "Int32" : "<error>", f.Scope.Locals["first"].Type.Name);
        if(diagnosticId is not null)
        {
            Assert.Equal(31, Assert.Single(f.Diagnostics.ToImmutableArray()).Position().Start.Column);
        }
    }

    [Theory]
    [InlineData(false, "arity", "MIST_PM_DECONSTRUCTION_ARITY")]
    [InlineData(true, "arity", "MIST_PM_DECONSTRUCTION_ARITY")]
    [InlineData(false, "non-tuple", "MIST_PM_DECONSTRUCTION_SOURCE")]
    [InlineData(true, "non-tuple", "MIST_PM_DECONSTRUCTION_SOURCE")]
    [InlineData(false, "unfinished", "MIST_PM_DECONSTRUCTION_SOURCE")]
    [InlineData(true, "unfinished", "MIST_PM_DECONSTRUCTION_SOURCE")]
    public void AShapeRefusalReportsOnceAtTheDeclarationAndKeepsRecoveryNames(bool typed, string source, string diagnosticId)
    {
        var f = new BindingFragments();
        var intType = f.Built.Type("Int32");
        _ = f.Local("source", source switch
        {
            "arity" => f.Built.Interner.GetOrCreateTupleType([intType, intType, intType]),
            "unfinished" => ErrorType.Instance,
            _ => intType,
        });

        _ = Assert.IsType<UnsupportedStatement>(Bind(f, Declaration(typed, "source")));
        ReadAndCall(f, "first", failed: true);
        ReadAndCall(f, "second", failed: true);

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(DeclarationPath, diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 13), new LinePosition(1, 14)), diagnostic.Position());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AValidTupleKeepsItsPositionalTypesAndDoesNotRefuseCalls(bool typed)
    {
        var f = new BindingFragments();
        _ = f.Local("source", f.Built.Interner.GetOrCreateTupleType([f.Built.Type("Int32"), f.Built.Type("String")]));

        var statement = Assert.IsType<TupleDeconstructionStatement>(Bind(f, Declaration(typed, "source")));
        Assert.Equal(["Int32", "String"], statement.Targets.Select(target => target.Type.Name));
        ReadAndCall(f, "first", failed: false);
        ReadAndCall(f, "second", failed: false);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARepeatedRecoveryNameStillReportsTheGenuineDuplicateOnce(bool typed)
    {
        var f = new BindingFragments();
        _ = f.Local("source", ErrorType.Instance, failed: true);

        _ = Assert.IsType<UnsupportedStatement>(Bind(f, Declaration(typed, "source", "same", "same")));
        ReadAndCall(f, "same", failed: true);

        Assert.Equal("MIST_PM_DUPLICATE_LOCAL", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
        _ = Assert.Single(f.Scope.Locals, pair => pair.Key == "same");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void ATupleElementCarriesPositiveFailureButAnUnfinishedElementRemainsReportable(bool typed, bool reported)
    {
        var f = new BindingFragments();
        _ = f.Local("bad", ErrorType.Instance, failed: reported);
        _ = f.Local("good", f.Built.Type("String"));
        var syntax = Declaration(typed, "ignored");
        SetInitializer(syntax, BindingFragments.Expression(new TupleLiteralExpressionPrimaryNode
        {
            First = new UnnamedTupleLiteralElementTupleLiteralElementNode { Value = BindingFragments.Expression(BindingFragments.Identifier("bad")) },
            Rest = new UnnamedTupleLiteralElementTupleLiteralElementNode { Value = BindingFragments.Expression(BindingFragments.Identifier("good")) },
        }));

        _ = Assert.IsType<TupleDeconstructionStatement>(Bind(f, syntax));
        ReadAndCall(f, "first", failed: reported);
        ReadAndCall(f, "second", failed: false);

        string[] expected = reported || typed ? [] : ["MIST_PM_UNRESOLVABLE_OVERLOAD"];
        Assert.Equal(expected, f.Diagnostics.ToImmutableArray().Select(d => d.Number.Value));
    }

    [Fact]
    public void AReportedDeclaredTargetTypeFailureIsCarriedByThatTargetOnly()
    {
        var f = new BindingFragments();
        _ = f.Local("source", f.Built.Interner.GetOrCreateTupleType([f.Built.Type("Int32"), f.Built.Type("String")]));
        var syntax = Declaration(typed: true, "source");
        var typed = Assert.IsType<TypedTupleDeconstructionVariableDeclarationStatementNode>(syntax.VariableDeclaration);
        typed.First.Type = BindingFragments.TypeName("MissingType");

        _ = Assert.IsType<UnsupportedStatement>(Bind(f, syntax));
        ReadAndCall(f, "first", failed: true);
        ReadAndCall(f, "second", failed: false);

        Assert.Equal("MIST_PM_UNRESOLVED_TYPE", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    private static void ReadAndCall(BindingFragments f, string name, bool failed)
    {
        var read = Assert.IsType<LocalReferenceExpression>(f.Bind(BindingFragments.Identifier(name)));
        Assert.Equal(failed, PriorFailure.Of(read));
        if(!f.Scope.Locals.ContainsKey("printer"))
        {
            var printer = HandBuiltModel.Class("Printer");
            _ = BindingFragments.Member(printer, "Print", f.Built.Type("Void"), f.Built.Type("Int32"));
            _ = BindingFragments.Member(printer, "Print", f.Built.Type("Void"), f.Built.Type("String"));
            _ = f.Local("printer", printer);
        }

        _ = f.Bind(BindingFragments.Identifier("printer"), new MethodCallSuffixPostfixSuffixNode
        {
            Name = BindingFragments.Token("Print"),
            Arguments = BindingFragments.Arguments(BindingFragments.Expression(BindingFragments.Identifier(name))),
        });
    }

    private static Statement Bind(BindingFragments f, VariableDeclarationStatementStatementNode syntax)
    {
        return StatementBinder.BindStatement(syntax, f.Binding, f.Builder);
    }

    private static VariableDeclarationStatementStatementNode Declaration(bool typed, string source, string first = "first", string second = "second")
    {
        var location = HandBuiltModel.Node(12, DeclarationPath);
        VariableDeclarationStatementNode declaration = typed
            ? new TypedTupleDeconstructionVariableDeclarationStatementNode
            {
                First = new TupleDeconstructionElementNode { Type = BindingFragments.TypeName("Int32"), Name = BindingFragments.Token(first) },
                Rest = new TupleDeconstructionElementNode { Type = BindingFragments.TypeName("String"), Name = BindingFragments.Token(second) },
            }
            : new VarTupleDeconstructionVariableDeclarationStatementNode
            {
                FirstName = BindingFragments.Token(first),
                RestNames = [BindingFragments.Token(second)],
            };
        declaration.Span = location.Span;
        declaration.Origin = location.Origin;
        var statement = new VariableDeclarationStatementStatementNode { VariableDeclaration = declaration };
        SetInitializer(statement, BindingFragments.Expression(BindingFragments.Identifier(source)));
        return statement;
    }

    private static void SetInitializer(VariableDeclarationStatementStatementNode statement, ExpressionNode initializer)
    {
        if(statement.VariableDeclaration is TypedTupleDeconstructionVariableDeclarationStatementNode typed)
        {
            typed.Initializer = initializer;
        }
        else
        {
            ((VarTupleDeconstructionVariableDeclarationStatementNode)statement.VariableDeclaration).Initializer = initializer;
        }
    }
}