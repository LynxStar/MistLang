using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PuritySpecFragments
{
    private IReadOnlyList<Diagnostic>? Getters(string name)
    {
        return name switch
        {
            "getter-calling-a-declared-pure-method" => Refined(stable: true, (jar, a) => Sum(jar, Pure, Arithmetic(Own(a), BinaryOperatorKind.Add, Own(B(jar))))),
            "getter-calling-an-inferred-pure-method" => Refined(stable: true, (jar, a) => Sum(jar, Public, Arithmetic(Own(a), BinaryOperatorKind.Add, Own(B(jar))))),
            "getter-calling-a-method-that-calls-another-pure-method" => Refined(
                stable: true, (jar, a) => Sum(jar, Public, Arithmetic(Call(Twice(jar), null, 0, Own(a)), BinaryOperatorKind.Add, Own(B(jar))))),
            "getter-calling-a-static-pure-method" => Refined(stable: true, (jar, _) => Sum(jar, PureStatic, Number(3))),
            "getter-reading-a-mutable-field" => Refined(stable: false, (_, a) => Own(a)),
            "getter-calling-a-method-reading-a-mutable-field" => Refined(stable: false, (jar, a) => Call(Method(jar, "Cur", integer, Public, Returning(Own(a))))),
            "getter-calling-a-local-readonly-method-reading-a-mutable-field" => Refined(
                stable: false, (jar, a) => Call(Method(jar, "Cur", integer, LocalReadonly, Returning(Own(a))))),
            "getter-adding-a-mutable-static" => Refined(stable: true, (_, a) => Arithmetic(Own(a), BinaryOperatorKind.Add, StaticRead(counter))),
            "getter-calling-a-method-reading-a-mutable-static" => Refined(
                stable: true, (jar, a) => Arithmetic(Own(a), BinaryOperatorKind.Add, Call(Method(jar, "Now", integer, Public, Returning(StaticRead(counter)))))),
            _ => null,
        };
    }

    private IReadOnlyList<Diagnostic> Refined(bool stable, Func<ClassType, Field, Expression> both)
    {
        var jar = HandBuiltModel.Class("Jar");
        var a = Field(jar, "A", integer, isReadonly: stable);
        var property = new Property("Both", integer, Public) { DeclaringType = jar, Getter = new PropertyAccessor { Body = Returning(both(jar, a)) } };
        jar.PropertiesList.Add(property);
        var predicate = new PropertyPattern(Node(), jar,
        [
            new PropertySubpattern(
                Node(37), "Both", new ComparisonPattern(Node(), integer, ComparisonKind.GreaterThan, Number(1)), integer, new PropertyResult(property, jar, [])),
        ]);
        var rich = new TypeAlias("Rich", "Probe", new Scope(null), null, DeclarationFragments.At<TypeAliasDeclarationNode>())
        {
            AliasedType = new RefinedType(jar, Node(), "Rich") { Predicate = predicate },
            Modifiers = Public,
        };
        return model.Analyze(new DeclarationShapeAnalyzer(), types: [jar, rich]);
    }

    private InvocationExpression Sum(ClassType jar, MemberModifiers modifiers, Expression value)
    {
        return Call(Method(jar, "Sum", integer, modifiers, Returning(value)));
    }

    private Field B(ClassType jar)
    {
        return Field(jar, "B", integer, isReadonly: true);
    }

    private Method Twice(ClassType jar)
    {
        var n = Input("n", integer);
        return Method(jar, "Twice", integer, Public, Returning(Arithmetic(Ref(n), BinaryOperatorKind.Multiply, Number(2))), n);
    }
}