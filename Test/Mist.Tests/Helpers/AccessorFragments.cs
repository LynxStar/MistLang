using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class AccessorFragments
{
    private readonly HandBuiltModel model = new();

    public AccessorFragments(string name)
    {
        Name = name;
        Declaring = HandBuiltModel.Class("Counter");
        Owner = name.Contains("derived", StringComparison.Ordinal)
            ? HandBuiltModel.Class("Derived", Declaring)
            : name.StartsWith("init-only-property", StringComparison.Ordinal) || name.StartsWith("get-only-auto-property", StringComparison.Ordinal)
                || name == "static-get-only-auto-property-written-in-an-instance-constructor"
                ? Declaring : HandBuiltModel.Class("Probe");
        var missingGet = name.Contains("without-a-getter", StringComparison.Ordinal);
        var missingSet = name.Contains("without-a-setter", StringComparison.Ordinal)
            || name.Contains("get-only-auto-property", StringComparison.Ordinal)
            || name.StartsWith("static-get-only", StringComparison.Ordinal);
        Getter = missingGet ? null : new PropertyAccessor { IsAuto = name.Contains("auto-property", StringComparison.Ordinal) };
        Setter = missingSet ? null : new PropertyAccessor
        {
            IsInit = name.Contains("init-only", StringComparison.Ordinal) || name.Contains("private-init", StringComparison.Ordinal),
            Accessibility = name.Contains("private", StringComparison.Ordinal) ? Accessibility.Private
                : name.StartsWith("clr-protected", StringComparison.Ordinal) ? Accessibility.Protected : Accessibility.Public,
        };
        Reads = missingGet || name.Contains("coalesc", StringComparison.Ordinal) || name.Contains("reassigned-from-itself", StringComparison.Ordinal);
        Writes = !name.Contains("read-without", StringComparison.Ordinal) && !name.Contains("read-by-name", StringComparison.Ordinal)
            && name != "property-pattern-without-a-getter" && name != "selected-indexer-without-a-getter";
        Initializing = IsInitializer(name);
        MemberAccessibility = name.Contains("protected-property", StringComparison.Ordinal) ? Accessibility.Protected : Accessibility.Public;
    }

    public string Name { get; }

    public ClassType Declaring { get; }

    public ClassType Owner { get; }

    public PropertyAccessor? Getter { get; }

    public PropertyAccessor? Setter { get; }

    public bool Reads { get; }

    public bool Writes { get; }

    public bool Initializing { get; }

    public Accessibility MemberAccessibility { get; }

    public IReadOnlyList<MistDiagnostic> Decide()
    {
        var diagnostics = new List<MistDiagnostic>();
        if(Reads && AccessorDecision.Read(Getter, Owner, MistAssembly.Synthetic, MemberAccessibility, Declaring) is { } read)
        {
            diagnostics.Add(read);
        }

        if(Writes && AccessorDecision.Write(Setter, false, Initializing, Owner, MistAssembly.Synthetic, MemberAccessibility, Declaring) is { } written)
        {
            diagnostics.Add(written);
        }

        return diagnostics;
    }

    public IReadOnlyList<Diagnostic> Analyze()
    {
        var integer = model.Type("Int32");
        var property = new Property("Count", integer, new MemberModifiers
        {
            Accessibility = MemberAccessibility,
            IsStatic = Name.StartsWith("static-get-only", StringComparison.Ordinal),
        })
        {
            DeclaringType = Declaring,
            Getter = Getter,
            Setter = Setter,
        };
        Declaring.PropertiesList.Add(property);
        var constructor = Name.Contains("in-its-constructor", StringComparison.Ordinal)
            || Name.Contains("in-a-constructor", StringComparison.Ordinal)
            || Name.Contains("derived-constructor", StringComparison.Ordinal)
            || Name.Contains("instance-constructor", StringComparison.Ordinal);
        var nestedLambda = Name.Contains("in-a-lambda", StringComparison.Ordinal);
        var nestedFunction = Name.Contains("in-a-local-function", StringComparison.Ordinal);
        var initAccessor = Name.Contains("init-accessor", StringComparison.Ordinal);
        var anotherInstance = Name.Contains("another-instance", StringComparison.Ordinal);
        Expression access()
        {
            if(Name.StartsWith("index-", StringComparison.Ordinal) || Name.StartsWith("selected-indexer", StringComparison.Ordinal))
            {
                var indexer = new Indexer(integer, [new Parameter("slot", integer)], new MemberModifiers { Accessibility = Accessibility.Public })
                {
                    DeclaringType = Declaring,
                    Getter = Getter,
                    Setter = Setter,
                };
                Declaring.IndexersList.Add(indexer);
                if(Name.StartsWith("selected-indexer", StringComparison.Ordinal))
                {
                    Declaring.IndexersList.Add(new Indexer(model.Type("String"), [new Parameter("key", model.Type("String"))], default)
                    {
                        Getter = new PropertyAccessor { IsAuto = true },
                        Setter = new PropertyAccessor { IsAuto = true },
                    });
                }

                return new IndexExpression(HandBuiltModel.Node(37), HandBuiltModel.Literal(Declaring), HandBuiltModel.Literal(integer, 0L), integer) { Indexer = indexer };
            }

            if(Name.Contains("by-name", StringComparison.Ordinal) || Name.StartsWith("static-get-only", StringComparison.Ordinal)
                || (Owner == Declaring && !anotherInstance && !Name.Contains("get-accessor", StringComparison.Ordinal) && !Name.Contains("set-accessor", StringComparison.Ordinal)))
            {
                return new PropertyReferenceExpression(HandBuiltModel.Node(37), property);
            }

            Expression receiver = anotherInstance || Owner != Declaring
                ? new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("other", Declaring))
                : new ThisExpression(HandBuiltModel.Node(), Declaring);
            return new MemberAccessExpression(HandBuiltModel.Node(37), receiver, "Count", integer, member: new PropertyResult(property, Declaring, []));
        }

        object root;
        object? member;
        if(Initializing)
        {
            var entry = new ObjectInitializerAssignment(HandBuiltModel.Node(37), "Count", HandBuiltModel.Literal(integer, 5L), integer,
                member: new PropertyResult(property, Declaring, []));
            root = new NewExpression(HandBuiltModel.Node(), Declaring, [], null, [entry], []);
            member = null;
        }
        else if(Name == "property-pattern-without-a-getter")
        {
            root = new PropertyPattern(HandBuiltModel.Node(), Declaring,
                [new PropertySubpattern(HandBuiltModel.Node(37), "Count", new CatchAllPattern(HandBuiltModel.Node(), integer, null), integer, new PropertyResult(property, Declaring, []))]);
            member = null;
        }
        else
        {
            var target = access();
            var expression = target;
            if(Writes)
            {
                var value = Name.Contains("reassigned-from-itself", StringComparison.Ordinal)
                    ? new BinaryExpression(HandBuiltModel.Node(), access(), BinaryOperatorKind.Add, HandBuiltModel.Literal(integer, 1L), integer)
                    : (Expression)HandBuiltModel.Literal(integer, 1L);
                expression = new AssignmentExpression(HandBuiltModel.Node(), target, value, integer, Name.Contains("coalesc", StringComparison.Ordinal));
            }

            var body = HandBuiltModel.Block(new ExpressionStatement(expression));
            if(nestedLambda)
            {
                body = HandBuiltModel.Block(new ExpressionStatement(new LambdaExpression(HandBuiltModel.Node(), [], null, body, false,
                    model.Interner.GetOrCreateFunctionType([], integer))));
            }
            else if(nestedFunction)
            {
                body = HandBuiltModel.Block(new LocalFunctionStatement(new Method("Seed", integer, [], default) { Body = body }, []));
            }

            root = body;
            if(constructor)
            {
                member = new Constructor("Counter", Owner, [], default) { Body = body, DeclaringType = Owner };
            }
            else if(initAccessor || Name.Contains("get-accessor", StringComparison.Ordinal) || Name.Contains("set-accessor", StringComparison.Ordinal))
            {
                var enclosing = new Property("Depth", integer, default)
                {
                    DeclaringType = Owner,
                    Getter = Name.Contains("get-accessor", StringComparison.Ordinal) ? new PropertyAccessor { Body = body } : null,
                    Setter = new PropertyAccessor { IsInit = initAccessor || Name.Contains("beside-an-init", StringComparison.Ordinal), Body = Name.Contains("get-accessor", StringComparison.Ordinal) ? HandBuiltModel.Block() : body },
                };
                member = enclosing;
            }
            else
            {
                member = new Method("Probe", integer, [], default) { Body = body, DeclaringType = Owner };
            }
        }

        return model.Analyze(new AccessorAnalyzer(), root, owner: Owner, member: member);
    }

    private static bool IsInitializer(string name)
    {
        return name.StartsWith("initializer-entry", StringComparison.Ordinal)
            || name == "private-init-accessor-called-by-an-initializer-outside-its-type";
    }
}