using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Member shapes a type body writes that C# spells differently from Mist: an interface method's
// constraint clauses around its terminator or body, and a generic member C# cannot declare generically,
// written once per instantiation under the name its call sites use.
[Trait("Tier", "2")]
public sealed class TypeEmitterMemberShapeTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers PublicStatic = Public with { IsStatic = true };

    private readonly HandBuiltModel built = new();

    [Fact]
    public void ASlotTerminatesOnlyItsLastConstraintClause()
    {
        var pick = Pick(body: false, ("T", TypeParameterConstraintKind.Class), ("U", TypeParameterConstraintKind.Struct));

        Assert.Equal(
            ["string Pick<T, U>(T first, U second)", "where T : class", "where U : struct;"],
            InterfaceLines(pick));
    }

    [Fact]
    public void ADefaultImplementationWritesItsConstraintClausesBetweenItsSignatureAndBody()
    {
        var pick = Pick(body: true, ("T", TypeParameterConstraintKind.Class));

        var lines = InterfaceLines(pick);

        Assert.Equal(["string Pick<T>(T first)", "where T : class", "{"], lines.Take(3));
        Assert.Equal("}", lines[^1]);
        Assert.DoesNotContain(lines, line => line.EndsWith(';') && line.StartsWith("where", StringComparison.Ordinal));
    }

    [Fact]
    public void AGenericOperatorIsDeclaredOncePerCalledInstance_UnderItsCallSitesName()
    {
        var meter = HandBuiltModel.Class("Meter");
        var cell = HandBuiltModel.Class("Cell<>");
        cell.TypeParametersList.Add(new TypeParameter("V", 0, cell.Scope, cell));
        var parameter = new TypeParameter("T", 0, new Scope(meter.Scope), declaringType: null);
        var plus = new Operator(OperatorKind.Plus, meter,
            [new Parameter("a", meter), new Parameter("b", new ConstructedType(cell, [parameter]))], PublicStatic)
        { DeclaringType = meter };
        plus.TypeParametersList.Add(parameter);
        meter.OperatorsList.Add(plus);
        built.Model.GenericCalls.Record(plus, [built.Type("String")]);
        built.Model.GenericCalls.Record(plus, [built.Type("Boolean")]);

        var headers = Headers(meter, "op_Addition");

        Assert.Equal(2, headers.Count);
        Assert.Matches(@"^public static \S+ op_Addition__Standard_String\(\S+ a, \S*Cell<string> b\)$", headers[0]);
        Assert.Matches(@"^public static \S+ op_Addition__Standard_Boolean\(\S+ a, \S*Cell<bool> b\)$", headers[1]);
    }

    [Fact]
    public void AGenericMethodWritingAUnionOverItsParameters_IsDeclaredPerCall_AndNeverGenerically()
    {
        var api = HandBuiltModel.Class("Api");
        var scope = new Scope(api.Scope);
        var first = new TypeParameter("T", 0, scope, declaringType: null);
        var second = new TypeParameter("R", 1, scope, declaringType: null);
        var integer = built.Type("Int32");
        var pick = new Method("Pick", integer, [new Parameter("value", built.Interner.GetOrCreateUnionType([first, second]))], PublicStatic)
        {
            DeclaringType = api,
            Body = HandBuiltModel.Block(new ReturnStatement(HandBuiltModel.Literal(integer, 1), integer)),
        };
        pick.TypeParametersList.AddRange([first, second]);
        api.MethodsList.Add(pick);
        built.Model.GenericCalls.Record(pick, [built.Type("Boolean"), integer]);
        built.Model.GenericCalls.Record(pick, [built.Type("String"), built.Type("Int64")]);

        var headers = Headers(api, "Pick");

        Assert.Equal(2, headers.Count);
        Assert.StartsWith("public static int Pick__Standard_Boolean__Standard_Int32(", headers[0], StringComparison.Ordinal);
        Assert.StartsWith("public static int Pick__Standard_String__Standard_Int64(", headers[1], StringComparison.Ordinal);
    }

    private Method Pick(bool body, params (string Name, TypeParameterConstraintKind Kind)[] parameters)
    {
        var contract = new AbstractType("IPicker", "Probe", new Scope(null), null, null);
        var scope = new Scope(contract.Scope);
        var typeParameters = parameters.Select((parameter, ordinal) =>
        {
            var declared = new TypeParameter(parameter.Name, ordinal, scope, declaringType: null);
            declared.ConstraintsList.Add(new KeywordConstraint(parameter.Kind));
            return declared;
        }).ToList();
        string[] names = ["first", "second"];
        var text = built.Type("String");
        var method = new Method("Pick", text, [.. typeParameters.Select((parameter, i) => new Parameter(names[i], parameter))], Public)
        {
            DeclaringType = contract,
            Body = body ? HandBuiltModel.Block(new ReturnStatement(HandBuiltModel.Literal(text, "x"), text)) : null,
        };
        method.TypeParametersList.AddRange(typeParameters);
        contract.MethodsList.Add(method);
        return method;
    }

    private static List<string> InterfaceLines(Method method)
    {
        var writer = new CSharpWriter();

        TypeEmitter.EmitInterfaceMethod(method, writer, new DiagnosticBag());

        return [.. writer.ToString().Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
    }

    private List<string> Headers(ClassType type, string name)
    {
        _ = built.Model.RegisterType(type);
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        using(UnionSpecialization.Enter(built.Model, diagnostics))
        {
            TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(built.Model, diagnostics), built.Model.VariadicUses, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return [.. writer.ToString().Split('\n').Select(line => line.Trim()).Where(line => line.Contains($" {name}", StringComparison.Ordinal))];
    }
}