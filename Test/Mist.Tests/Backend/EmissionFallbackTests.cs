using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class EmissionFallbackTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void AnUnhandledStatementReportsOneInternalErrorAndRetainsItsComment()
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var writer = new CSharpWriter();
        BodyEmitter.EmitStatement(new NewStatement(), writer, diagnostics);

        _ = AssertFallback(diagnostics, "statement");
        Assert.Contains("/* unsupported statement: NewStatement */", writer.ToString());
    }

    [Theory]
    [InlineData(false, "/* unknown op */")]
    [InlineData(true, "/* unknown unary */")]
    public void AnUnknownOperatorReportsOneInternalErrorAndRetainsItsComment(bool unary, string comment)
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var writer = new CSharpWriter();
        var value = HandBuiltModel.Literal(built.Type("Int32"), 1L);
        var expression = unary
            ? new UnaryExpression(HandBuiltModel.Node(), (UnaryOperatorKind)999, value, value.Type)
            : (Expression)new BinaryExpression(HandBuiltModel.Node(), value, (BinaryOperatorKind)999, value, value.Type);
        BodyEmitter.EmitExpression(expression, writer, diagnostics);

        var occurrence = AssertFallback(diagnostics, unary ? "unary operator" : "binary operator");
        _ = Assert.IsType<SourceRange>(occurrence.Location);
        Assert.Contains(comment, writer.ToString());
    }

    [Theory]
    [InlineData(false, "/* error */")]
    [InlineData(true, "/* anonymous */")]
    public void AnUnspellableTypeReportsOneInternalErrorAndRetainsItsComment(bool anonymous, string comment)
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var type = anonymous ? new AnonymousType([], []) : (IMistType)ErrorType.Instance;

        Assert.Equal(comment, TypeNameEmitter.Emit(type, diagnostics));
        _ = AssertFallback(diagnostics, "type");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMissingDefaultWitnessReportsOneInternalErrorForCallsAndMethodGroups(bool group)
    {
        var owner = HandBuiltModel.Class("Factory");
        var method = new Method("Make", built.Type("Void"), [], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        var parameter = new TypeParameter("T", 0, new Scope(null), owner) { DeclaringMethod = method };
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Default));
        method.TypeParametersList.Add(parameter);
        var receiver = new TypeReferenceExpression(HandBuiltModel.Node(), owner);
        var expression = group
            ? new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, method, new FunctionType([], built.Type("Void")))
            { TypeArguments = [UnknownType.Instance] }
            : (Expression)new InvocationExpression(HandBuiltModel.Node(),
                new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Make", built.Type("Void"), [UnknownType.Instance]),
                [], built.Type("Void"), method, []);
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var writer = new CSharpWriter();
        BodyEmitter.EmitExpression(expression, writer, diagnostics);

        _ = AssertFallback(diagnostics, "default constraint witness");
        Assert.Contains("/* unsupported: default constraint */", writer.ToString());
    }

    [Fact]
    public void NestedReportingScopesRestoreTheirOwnerAfterAnException()
    {
        var outer = new DiagnosticBag(PipelineStage.Backend);
        var inner = new DiagnosticBag(PipelineStage.Backend);
        using(EmissionDiagnostics.Enter(outer))
        {
            _ = Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var nested = EmissionDiagnostics.Enter(inner);
                _ = TypeNameEmitter.Emit(ErrorType.Instance);
                throw new InvalidOperationException();
            }));
            _ = TypeNameEmitter.Emit(ErrorType.Instance);
        }

        _ = AssertFallback(outer, "type");
        _ = AssertFallback(inner, "type");
    }

    private static Diagnostic AssertFallback(DiagnosticBag diagnostics, string kind)
    {
        var occurrence = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(BackendDiagnostics.EmissionFallback.Number, occurrence.Number);
        Assert.Equal(DiagnosticCategory.Ice, occurrence.Descriptor.Category);
        Assert.Equal(DiagnosticSeverity.Error, occurrence.Severity);
        Assert.Equal(kind, Assert.IsType<EnumValue>(occurrence.Arguments[0]).Token);
        Assert.True(diagnostics.HasErrors);
        return occurrence;
    }

    private sealed class NewStatement : Statement;
}