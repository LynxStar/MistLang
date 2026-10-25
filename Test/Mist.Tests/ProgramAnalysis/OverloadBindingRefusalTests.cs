using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: select overloads and report their diagnostics on hand-built signatures and values.
[Trait("Tier", "2")]
public sealed class OverloadBindingRefusalTests
{
    [Theory]
    [InlineData("overload-ambiguous-between-signed-and-unsigned", "MIST0386")]
    [InlineData("index-overload-ambiguous-between-signed-and-unsigned", "MIST0386")]
    [InlineData("index-overload-arity-mismatch", "MIST0385")]
    [InlineData("index-overload-unresolvable", "MIST0390")]
    [InlineData("local-function-called-with-wrong-arity", "MIST0385")]
    [InlineData("type-argument-bounds-with-no-dominant", "MIST0392")]
    [InlineData("unknown-receiver-member-reaches-its-call", "MIST0390")]
    public void RefusedChoiceNamesItsCause(string refusal, string diagnosticId)
    {
        var f = new HandBuiltModel();
        var diagnostics = new DiagnosticBag();
        var intType = f.Type("Int32");
        IReadOnlyList<Expression> arguments;
        IReadOnlyList<Method> methods;
        if(refusal == "type-argument-bounds-with-no-dominant")
        {
            var method = Function("Pick", intType);
            var parameter = BindingFragments.Parameter(method, "T");
            method.ParametersList.AddRange([new Parameter("left", parameter), new Parameter("right", parameter)]);
            methods = [method];
            arguments = [HandBuiltModel.Literal(f.Type("String"), "x"), HandBuiltModel.Literal(intType, 1L)];
        }
        else if(refusal == "unknown-receiver-member-reaches-its-call")
        {
            methods = [Function("WriteLine", intType, intType), Function("WriteLine", intType, f.Type("String"))];
            arguments = [new UnresolvedExpression(HandBuiltModel.Node(30))];
        }
        else if(refusal.Contains("arity", StringComparison.Ordinal))
        {
            methods = [Function("Read", intType, intType, intType)];
            arguments = [HandBuiltModel.Literal(intType, 1L)];
        }
        else if(refusal == "index-overload-unresolvable")
        {
            methods = [Function("Read", intType, intType), Function("Read", intType, f.Type("String"))];
            arguments = [HandBuiltModel.Literal(f.Type("Double"), 1.5)];
        }
        else
        {
            methods = [Function("Read", intType, f.Type("UInt32")), Function("Read", intType, f.Type("Int16"))];
            arguments = [HandBuiltModel.Literal(f.Type("Byte"), 1L)];
        }

        if(refusal.StartsWith("index-", StringComparison.Ordinal))
        {
            var indexers = methods.Select(method => new Indexer(method.ReturnType, method.ParametersList, method.Modifiers)).ToArray();
            var choice = OverloadResolution.Select(indexers, arguments, null, null, f.Interner);
            Assert.Null(choice.Selected);
            OverloadFailure.Report(choice, indexers, arguments, "indexer 'Shelf.this[]'", HandBuiltModel.Node(30), diagnostics);
        }
        else
        {
            var choice = OverloadResolution.Select(methods, arguments, null, null, f.Interner);
            Assert.Null(choice.Selected);
            OverloadFailure.Report(choice, methods, arguments, "method 'Read'", HandBuiltModel.Node(30), diagnostics);
        }

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData("error-expression")]
    [InlineData("local-after-error-expression")]
    public void PreviouslyRefusedArgumentDoesNotCauseAnOverloadDiagnostic(string shape)
    {
        var f = new HandBuiltModel();
        var diagnostics = new DiagnosticBag();
        Expression failed = shape == "error-expression"
            ? new ErrorExpression(HandBuiltModel.Node(30))
            : new LocalReferenceExpression(HandBuiltModel.Node(30), new LocalVariable("failed", ErrorType.Instance) { IsFailed = true });
        var methods = new[] { Function("Read", f.Type("Void"), f.Type("Int32")), Function("Read", f.Type("Void"), f.Type("String")) };
        var choice = OverloadResolution.Select(methods, [failed], null, null, f.Interner);
        OverloadFailure.Report(choice, methods, [failed], "method 'Read'", HandBuiltModel.Node(30), diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    private static Method Function(string name, IMistType returns, params IMistType[] parameters)
    {
        return new(
        name, returns, parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(), new MemberModifiers())
        {
            Scope = new Scope(null),
        };
    }
}