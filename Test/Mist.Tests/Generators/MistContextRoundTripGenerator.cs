// Feature: structured-context, Property 1: Pretty-printer round-trip idempotence
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.5, 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 13.1**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class ContextRoundTripScenario
{
    public string Source { get; }

    public string Description { get; }

    public ContextRoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class MistContextRoundTripGenerator
{
    private static readonly string[] ClassNames =
        ["Service", "Handler", "Manager", "Provider", "Worker"];

    private static readonly string[] MethodNames =
        ["Execute", "Process", "Handle", "Run", "Compute"];

    private static readonly string[] CtorParamNames =
        ["value", "data", "input"];

    private static readonly string[] VarNames =
        ["ctx", "svc", "cfg", "env", "state"];

    private static readonly string[] ContextTypeNames =
        ["Config", "Logger", "DbConn", "AuthToken", "Settings"];

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool"];

    private static readonly string[] Expressions =
        ["42", "\"hello\"", "true", "new Config()", "new Logger()"];

    private static readonly string[] AbstractNames =
        ["IService", "IHandler", "IProcessor"];

    private static Gen<string> GenSimpleExpression()
    {
        return Gen.Elements(Expressions);
    }

    private static Gen<string> GenVarName()
    {
        return Gen.Elements(VarNames);
    }

    private static Gen<string> GenTypeRef()
    {
        return Gen.OneOf(
            Gen.Elements(PrimitiveTypes),
            Gen.Elements(ContextTypeNames));
    }

    private static Gen<string> GenWithVarStatement()
    {
        return from name in GenVarName()
               from expr in GenSimpleExpression()
               select $"with var {name} = {expr};";
    }

    private static Gen<string> GenWithTypedStatement()
    {
        return from type in GenTypeRef()
               from name in GenVarName()
               from expr in GenSimpleExpression()
               select $"with {type} {name} = {expr};";
    }

    private static Gen<string> GenWithDiscardStatement()
    {
        return from type in GenTypeRef()
               from expr in GenSimpleExpression()
               select $"with {type} _ = {expr};";
    }

    private static Gen<string> GenWithPushStatement()
    {
        return from expr in GenSimpleExpression()
               select $"with {expr};";
    }

    private static Gen<string> GenWithBlockStatement()
    {
        return from expr in GenSimpleExpression()
               from innerStmtCount in Gen.Choose(0, 2)
               from innerStmts in Gen.ArrayOf(GenSimpleBodyStatement(), innerStmtCount)
               let body = string.Join("\n        ", innerStmts)
               let bodySection = innerStmts.Length > 0 ? $"\n        {body}\n    " : " "
               select $"with ({expr}) {{{bodySection}}}";
    }

    private static Gen<string> GenSimpleBodyStatement()
    {
        return Gen.OneOf(
            Gen.Constant("var x = 1;"),
            Gen.Constant("var y = \"test\";"),
            Gen.Constant("var z = true;"));
    }

    private static Gen<string> GenAnyContextStatement()
    {
        return Gen.OneOf(
            GenWithVarStatement(),
            GenWithTypedStatement(),
            GenWithDiscardStatement(),
            GenWithPushStatement(),
            GenWithBlockStatement());
    }

    private static Gen<string> GenWithClause()
    {
        return from type in GenTypeRef()
               from name in GenVarName()
               select $"with {type} {name}";
    }

    private static Gen<string> GenWithClauses()
    {
        return from count in Gen.Choose(1, 3)
               from clauses in Gen.ArrayOf(GenWithClause(), count)
               select string.Join(" ", clauses);
    }

    private static Gen<string> GenMethodWithContextBody()
    {
        return from name in Gen.Elements(MethodNames)
               from hasWithClause in Gen.Elements(true, false)
               from withClauses in GenWithClauses()
               from stmtCount in Gen.Choose(1, 3)
               from stmts in Gen.ArrayOf(GenAnyContextStatement(), stmtCount)
               let withPart = hasWithClause ? $" {withClauses}" : ""
               let body = string.Join("\n        ", stmts)
               select $"    void {name}(){withPart} {{\n        {body}\n    }}";
    }

    private static Gen<string> GenMethodWithClausesOnly()
    {
        return from name in Gen.Elements(MethodNames)
               from withClauses in GenWithClauses()
               select $"    void {name}() {withClauses} {{\n    }}";
    }

    private static Gen<string> GenConstructorWithClauses(string className)
    {
        return from hasParam in Gen.Elements(true, false)
               from paramType in Gen.Elements(PrimitiveTypes)
               from paramName in Gen.Elements(CtorParamNames)
               from withClauses in GenWithClauses()
               from stmtCount in Gen.Choose(0, 2)
               from stmts in Gen.ArrayOf(GenAnyContextStatement(), stmtCount)
               let paramList = hasParam ? $"{paramType} {paramName}" : ""
               let body = stmts.Length > 0
                   ? string.Join("\n        ", stmts)
                   : ""
               let bodySection = stmts.Length > 0
                   ? $"\n        {body}\n    "
                   : "\n    "
               select $"    {className}({paramList}) {withClauses} {{{bodySection}}}";
    }

    private static Gen<ContextRoundTripScenario> GenClassWithContextMethods()
    {
        return from className in Gen.Elements(ClassNames)
               from methodCount in Gen.Choose(1, 3)
               from methods in Gen.ArrayOf(
                   Gen.OneOf(GenMethodWithContextBody(), GenMethodWithClausesOnly()),
                   methodCount)
               let members = string.Join("\n\n", methods)
               let source = $"class {className} {{\n{members}\n}}\n"
               select new ContextRoundTripScenario(source,
                   $"class {className} with {methodCount} context methods");
    }

    private static Gen<ContextRoundTripScenario> GenClassWithConstructorClauses()
    {
        return from className in Gen.Elements(ClassNames)
               from ctor in GenConstructorWithClauses(className)
               from hasMethod in Gen.Elements(true, false)
               from method in GenMethodWithContextBody()
               let members = hasMethod ? $"{ctor}\n\n{method}" : ctor
               let source = $"class {className} {{\n{members}\n}}\n"
               select new ContextRoundTripScenario(source,
                   $"class {className} with constructor with-clauses");
    }

    private static Gen<ContextRoundTripScenario> GenAbstractWithClauses()
    {
        return from abstractName in Gen.Elements(AbstractNames)
               from methodCount in Gen.Choose(1, 3)
               from methods in Gen.ArrayOf(GenAbstractMethodWithClauses(), methodCount)
               let members = string.Join("\n", methods)
               let source = $"abstract {abstractName} {{\n{members}\n}}\n"
               select new ContextRoundTripScenario(source,
                   $"abstract {abstractName} with {methodCount} with-clause methods");
    }

    private static Gen<string> GenAbstractMethodWithClauses()
    {
        return from retType in Gen.Elements("void", "int", "string", "bool")
               from name in Gen.Elements(MethodNames)
               from hasParam in Gen.Elements(true, false)
               from paramType in Gen.Elements(PrimitiveTypes)
               from paramName in Gen.Elements(CtorParamNames)
               from withClauses in GenWithClauses()
               let paramList = hasParam ? $"{paramType} {paramName}" : ""
               select $"    {retType} {name}({paramList}) {withClauses};";
    }

    private static Gen<ContextRoundTripScenario> GenMixedProgram()
    {
        return from className in Gen.Elements(ClassNames)
               from abstractName in Gen.Elements(AbstractNames)
               from method in GenMethodWithContextBody()
               from ctor in GenConstructorWithClauses(className)
               from abstractMethod in GenAbstractMethodWithClauses()
               let abstractBlock = $"abstract {abstractName} {{\n{abstractMethod}\n}}\n"
               let classBlock = $"class {className} {{\n{ctor}\n\n{method}\n}}\n"
               let source = $"{abstractBlock}\n{classBlock}"
               select new ContextRoundTripScenario(source,
                   $"mixed: abstract {abstractName} + class {className}");
    }

    private static Gen<ContextRoundTripScenario> GenDenseContextBody()
    {
        return from className in Gen.Elements(ClassNames)
               from name in Gen.Elements(MethodNames)
               from withClauses in GenWithClauses()
               from stmt1 in GenWithVarStatement()
               from stmt2 in GenWithTypedStatement()
               from stmt3 in GenWithDiscardStatement()
               from stmt4 in GenWithPushStatement()
               from blockExpr in GenSimpleExpression()
               let blockBody = $"with ({blockExpr}) {{\n            var inner = 1;\n        }}"
               let body = string.Join("\n        ", stmt1, stmt2, stmt3, stmt4, blockBody)
               let source = $"class {className} {{\n    void {name}() {withClauses} {{\n        {body}\n    }}\n}}\n"
               select new ContextRoundTripScenario(source,
                   $"dense context body in {className}.{name}");
    }

    public static Gen<ContextRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenClassWithContextMethods(),
            GenClassWithConstructorClauses(),
            GenAbstractWithClauses(),
            GenMixedProgram(),
            GenDenseContextBody());
    }

    public static Arbitrary<ContextRoundTripScenario> ArbitraryContextRoundTripScenario()
    {
        return GenScenario().ToArbitrary();
    }
}