using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PuritySpecFragments
{
    private IReadOnlyList<Diagnostic>? Functions(string name)
    {
        return name switch
        {
            "lambda-adding-one-to-its-parameter" => ConvertingALambda(n => Arithmetic(Ref(n), BinaryOperatorKind.Add, Number(1))),
            "lambda-reading-a-readonly-static" => ConvertingALambda(n => Arithmetic(Ref(n), BinaryOperatorKind.Add, StaticRead(limit))),
            "pure-lambda-adding-one-to-its-parameter" => ConvertingALambda(n => Arithmetic(Ref(n), BinaryOperatorKind.Add, Number(1)), declared: true),
            "lambda-writing-an-array-it-allocated" => ConvertingABlockLambda(FillingAnArrayWith),
            "lambda-passed-to-a-pure-function-parameter" => PassingALambda(n => Returning(Arithmetic(Ref(n), BinaryOperatorKind.Add, Number(1)))),
            "lambda-writing-a-static" => ConvertingABlockLambda(WritingAStaticWith),
            "pure-lambda-writing-a-static" => ConvertingABlockLambda(WritingAStaticWith, declared: true),
            "lambda-passed-to-a-pure-function-parameter-writing-a-static" => PassingALambda(WritingAStaticWith),
            "lambda-reading-a-mutable-static" => ConvertingALambda(n => Arithmetic(Ref(n), BinaryOperatorKind.Add, StaticRead(counter, 37))),
            "pure-lambda-reading-a-mutable-static" => ConvertingALambda(n => Arithmetic(Ref(n), BinaryOperatorKind.Add, StaticRead(counter, 37)), declared: true),
            "lambda-capturing-a-readonly-local" => CapturingALocal(isReadonly: true),
            "lambda-capturing-a-local-nothing-reassigns" => CapturingALocal(),
            "pure-lambda-capturing-a-readonly-local" => CapturingALocal(isReadonly: true, declared: true),
            "lambda-capturing-a-local-reassigned-before-the-capture" => CapturingALocal(reassignedBefore: true),
            "lambda-capturing-a-local-reassigned-after-the-capture" => CapturingALocal(reassignedAfter: true),
            "method-group-of-a-declared-pure-method" => ConvertingAMethodGroup(ops => ops.Inc),
            "method-group-of-an-inferred-pure-method" => ConvertingAMethodGroup(ops => ops.Plain),
            "method-group-passed-to-a-pure-function-parameter" => PassingAMethodGroup(ops => ops.Inc),
            "method-group-of-an-impure-method" => ConvertingAMethodGroup(ops => ops.Bump),
            "method-group-of-an-impure-method-passed-to-a-pure-function-parameter" => PassingAMethodGroup(ops => ops.Bump),
            "pure-method-invoking-a-pure-function-value" => Judge(new PurityAnalyzer(), Ops().Twice),
            "pure-method-invoking-a-plain-function-value" => InvokingAPlainFunctionValue(),
            "local-readonly-method-invoking-a-pure-function-value" => InvokingAPureFunctionValueFromALocalReadonlyMethod(),
            _ => null,
        };
    }

    private IReadOnlyList<Diagnostic> ConvertingALambda(Func<LocalVariable, Expression> result, bool declared = false)
    {
        var n = Variable("n", integer);
        var f = Variable("f", Function(pure: true, integer, integer));
        return InMain(Declare(f, Lambda(n, result(n), null, f.Type, declared)));
    }

    private IReadOnlyList<Diagnostic> ConvertingABlockLambda(Func<LocalVariable, Block> body, bool declared = false)
    {
        var n = Variable("n", integer);
        var f = Variable("f", Function(pure: true, integer, integer));
        return InMain(Declare(f, Lambda(n, null, body(n), f.Type, declared)));
    }

    private IReadOnlyList<Diagnostic> PassingALambda(Func<LocalVariable, Block> body)
    {
        var ops = Ops();
        var n = Variable("n", integer);
        var r = Variable("r", integer);
        return InMain(Declare(r, Call(ops.Twice, null, 0, Lambda(n, null, body(n), Function(pure: true, integer, integer)), Number(3))));
    }

    private IReadOnlyList<Diagnostic> CapturingALocal(bool isReadonly = false, bool declared = false, bool reassignedBefore = false, bool reassignedAfter = false)
    {
        var k = Variable("k", integer, isReadonly: isReadonly);
        var n = Variable("n", integer);
        var f = Variable("f", Function(pure: true, integer, integer));
        var statements = new List<Statement> { Declare(k, Number(1)) };

        if(reassignedBefore)
        {
            statements.Add(Set(Ref(k), Number(2)));
        }

        statements.Add(Declare(f, Lambda(n, Arithmetic(Ref(n), BinaryOperatorKind.Add, Ref(k, 37)), null, f.Type, declared)));

        if(reassignedAfter)
        {
            statements.Add(Set(Ref(k), Number(2)));
        }

        return InMain([.. statements]);
    }

    private IReadOnlyList<Diagnostic> ConvertingAMethodGroup(Func<Operations, Method> pick)
    {
        var ops = Ops();
        var m = Variable("m", Function(pure: true, integer, integer));
        return InMain(Declare(m, Group(ops.Type, pick(ops), m.Type)));
    }

    private IReadOnlyList<Diagnostic> PassingAMethodGroup(Func<Operations, Method> pick)
    {
        var ops = Ops();
        var r = Variable("r", integer);
        return InMain(Declare(r, Call(ops.Twice, null, 0, Group(ops.Type, pick(ops), Function(pure: true, integer, integer)), Number(3))));
    }

    private IReadOnlyList<Diagnostic> InvokingAPlainFunctionValue()
    {
        var f = Input("f", Function(pure: false, integer, integer));
        var x = Input("x", integer);
        var apply = Method(host, "Apply", integer, PureStatic, Returning(Invoke(f, 37, Ref(x))), f, x);
        return Judge(new PurityAnalyzer(), apply);
    }

    private IReadOnlyList<Diagnostic> InvokingAPureFunctionValueFromALocalReadonlyMethod()
    {
        var n = Field(host, "n", integer);
        var f = Input("f", Function(pure: true, integer, integer));
        var apply = Method(host, "Apply", integer, LocalReadonly, Returning(Invoke(f, 37, Own(n))), f);
        return Judge(new PurityAnalyzer(), apply);
    }

    private IReadOnlyList<Diagnostic> InMain(params Statement[] statements)
    {
        var main = Method(host, "Main", unit, Static, HandBuiltModel.Block(statements));
        return Judge(new PurityAnalyzer(), main);
    }

    private Operations Ops()
    {
        var ops = HandBuiltModel.Class("Ops");
        var a = Input("n", integer);
        var inc = Method(ops, "Inc", integer, PureStatic, Returning(Arithmetic(Ref(a), BinaryOperatorKind.Add, Number(1))), a);
        var b = Input("n", integer);
        var plain = Method(ops, "Plain", integer, Static, Returning(Arithmetic(Ref(b), BinaryOperatorKind.Add, Number(1))), b);
        var c = Input("n", integer);
        var bump = Method(ops, "Bump", integer, Static, HandBuiltModel.Block(Set(StaticRead(counter), Ref(c)), Return(Ref(c))), c);
        var f = Input("f", Function(pure: true, integer, integer));
        var x = Input("x", integer);
        var twice = Method(ops, "Twice", integer, PureStatic, Returning(Invoke(f, 0, Invoke(f, 0, Ref(x)))), f, x);
        return new Operations(ops, inc, plain, bump, twice);
    }

    private Block WritingAStaticWith(LocalVariable n)
    {
        return HandBuiltModel.Block(Set(StaticRead(counter, 37), Ref(n)), Return(Ref(n)));
    }

    private Block FillingAnArrayWith(LocalVariable n)
    {
        var xs = Variable("xs", ArrayOfInt());
        return HandBuiltModel.Block(
            Declare(xs, new ArrayCreationExpression(Node(), integer, Number(1), xs.Type)),
            Set(Element(xs, integer), Ref(n)),
            Return(Element(xs, integer)));
    }

    private static LambdaExpression Lambda(LocalVariable parameter, Expression? result, Block? body, IMistType type, bool declared = false)
    {
        return new LambdaExpression(Node(), [parameter], result, body, false, type) { IsPure = declared };
    }

    private static FunctionReferenceExpression Group(ClassType owner, Method method, IMistType type)
    {
        return new FunctionReferenceExpression(Node(37), new TypeReferenceExpression(Node(), owner), method, type);
    }

    private sealed record Operations(ClassType Type, Method Inc, Method Plain, Method Bump, Method Twice);
}