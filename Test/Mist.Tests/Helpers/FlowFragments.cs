using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class FlowFragments
{
    private readonly HandBuiltModel model = new();
    private readonly StructType integer = new("Int32", "Standard", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsIntrinsic = true } };
    private readonly ClassType text = new("String", "Standard", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsIntrinsic = true } };

    public IReadOnlyList<Mist.Common.Diagnostics.Diagnostic> Analyze(string name)
    {
        if(name.StartsWith("bare-array-", StringComparison.Ordinal) || name.StartsWith("stackalloc-", StringComparison.Ordinal))
        {
            var element = Element(name);
            var array = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [element, model.Interner.GetOrCreateUnknownValue(integer)]);
            Expression root = name.StartsWith("bare-array-", StringComparison.Ordinal)
                ? new ArrayCreationExpression(HandBuiltModel.Node(37), element, HandBuiltModel.Literal(integer, 3L), array)
                : new StackallocExpression(HandBuiltModel.Node(37), element, HandBuiltModel.Literal(integer, 2L), array);
            return model.Analyze(new StampabilityAnalyzer(), root);
        }

        if(name.StartsWith("stampable-", StringComparison.Ordinal))
        {
            var declared = DeclaredStruct("Box");
            declared.Modifiers = new MemberModifiers { IsStampable = true };
            if(name == "stampable-declaration-with-reference-storage")
            {
                declared.PropertiesList.Add(new Property("Item", text, default) { Getter = new PropertyAccessor { IsAuto = true } });
            }
            else
            {
                var parameter = new TypeParameter("T", 0, declared.Scope, declared);
                declared.TypeParametersList.Add(parameter);
                declared.FieldsList.Add(Field(declared, "Item", parameter));
            }

            return model.Analyze(new StampabilityAnalyzer(), types: [declared]);
        }

        if(name.StartsWith("field-", StringComparison.Ordinal) || name.StartsWith("auto-property-", StringComparison.Ordinal)
            || name == "struct-field-read-through-this-before-assignment")
        {
            return Constructor(name);
        }

        return Local(name);
    }

    private IMistType Element(string name)
    {
        var suffix = name[(name.IndexOf("-of-", StringComparison.Ordinal) + 4)..];
        return suffix switch
        {
            "a-class" => HandBuiltModel.Class("Dog"),
            "a-function" => model.Interner.GetOrCreateFunctionType([integer], integer),
            "a-newtype" => HandBuiltModel.Newtype("Actor", HandBuiltModel.Class("Entity")),
            "a-refinement" => HandBuiltModel.Refined(integer, "PositiveInt"),
            "a-type-parameter" => new TypeParameter("T", 0, new Scope(null), null),
            "an-enum" => HandBuiltModel.Enum("Color", integer),
            "string" => text,
            "a-struct-with-a-reference-field" => NamedStruct(),
            "a-struct-with-an-inherited-reference-field" => new StructType("Derived", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode()) { BaseType = NamedStruct() },
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private StructType NamedStruct()
    {
        var named = DeclaredStruct("Named");
        named.FieldsList.Add(Field(named, "label", text));
        return named;
    }

    private IReadOnlyList<Mist.Common.Diagnostics.Diagnostic> Constructor(string name)
    {
        ConcreteType owner = name == "struct-field-read-through-this-before-assignment" ? DeclaredStruct("Slot") : HandBuiltModel.Class("Account");
        var dog = HandBuiltModel.Class("Dog");
        dog.ConstructorsList.Add(new Constructor("Dog", dog, [new Parameter("name", text)], default));
        Expression target;
        Expression read;
        if(name.StartsWith("auto-property-", StringComparison.Ordinal))
        {
            var property = new Property("Pet", dog, default) { Getter = new PropertyAccessor { IsAuto = true }, DeclaringType = owner };
            owner.PropertiesList.Add(property);
            target = new PropertyReferenceExpression(HandBuiltModel.Node(), property);
            read = new MemberAccessExpression(HandBuiltModel.Node(37), new ThisExpression(HandBuiltModel.Node(), owner), "Pet", dog,
                member: new PropertyResult(property, owner, []));
        }
        else
        {
            var field = Field(owner, "owner", dog);
            owner.FieldsList.Add(field);
            target = new FieldReferenceExpression(HandBuiltModel.Node(), field);
            read = Access(new ThisExpression(HandBuiltModel.Node(), owner), field, 37);
        }

        var write = Write(target, HandBuiltModel.Literal(dog));
        var statements = new List<Statement>();
        switch(name)
        {
            case "field-not-assigned-on-all-paths":
                statements.Add(new IfStatement(Condition(), HandBuiltModel.Block(write), null));
                break;
            case "field-assigned-only-inside-lambda":
                statements.Add(new ExpressionStatement(new LambdaExpression(HandBuiltModel.Node(), [], null,
                    HandBuiltModel.Block(write), false, model.Interner.GetOrCreateFunctionType([], integer))));
                break;
            case "field-read-through-this-after-partial-assignment":
                statements.Add(new IfStatement(Condition(), HandBuiltModel.Block(write), null));
                statements.Add(new ExpressionStatement(read));
                statements.Add(write);
                break;
            case "field-read-through-this-inside-loop":
                statements.Add(new WhileStatement(Condition(), HandBuiltModel.Block(new ExpressionStatement(read), write)));
                statements.Add(write);
                break;
            case "field-read-through-this-before-assignment":
            case "auto-property-read-through-this-before-assignment":
            case "struct-field-read-through-this-before-assignment":
                statements.Add(new ExpressionStatement(read));
                statements.Add(write);
                break;
            case "field-never-assigned":
            case "auto-property-not-assigned":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }

        var body = HandBuiltModel.Block([.. statements]);
        var constructor = new Constructor(owner.Name, owner, [], default)
        {
            Body = body,
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(37),
        };
        owner.ConstructorsList.Add(constructor);
        return model.Analyze(new DefiniteAssignmentAnalyzer(), body, [owner], owner, constructor);
    }

    private IReadOnlyList<Mist.Common.Diagnostics.Diagnostic> Local(string name)
    {
        var point = DeclaredStruct("Point");
        var x = Field(point, "x", integer);
        var y = Field(point, "y", integer);
        point.FieldsList.Add(x);
        point.FieldsList.Add(y);
        var nested = DeclaredStruct("Nested");
        var inner = Field(nested, "inner", point);
        var other = Field(nested, "other", integer);
        nested.FieldsList.Add(inner);
        nested.FieldsList.Add(other);
        IMistType type = name.StartsWith("struct-local-", StringComparison.Ordinal) ? point : integer;
        if(name.StartsWith("struct-local-nested-", StringComparison.Ordinal))
        {
            type = nested;
        }

        if(name.StartsWith("struct-local-generic-", StringComparison.Ordinal))
        {
            var wrapper = DeclaredStruct("Wrapper<>");
            var parameter = new TypeParameter("T", 0, wrapper.Scope, wrapper);
            wrapper.TypeParametersList.Add(parameter);
            inner = Field(wrapper, "inner", parameter);
            other = Field(wrapper, "other", integer);
            wrapper.FieldsList.Add(inner);
            wrapper.FieldsList.Add(other);
            type = model.Interner.GetOrCreateConstructedType(wrapper, [point]);
        }

        if(name.StartsWith("struct-local-inherited-", StringComparison.Ordinal))
        {
            var derived = DeclaredStruct("ExtendedPoint");
            derived.BaseType = point;
            other = Field(derived, "z", integer);
            derived.FieldsList.Add(other);
            type = derived;
        }

        if(name == "struct-local-unassigned-opaque-ambient-value")
        {
            type = new StructType("DateTime", "System", new Scope(null), null, null);
        }

        Field? referenceValue = null;
        if(name is "struct-local-reference-write-does-not-assign-sibling" or "struct-local-unassigned-reference-field-write")
        {
            var box = HandBuiltModel.Class("Box");
            referenceValue = Field(box, "value", integer);
            box.FieldsList.Add(referenceValue);
            var holder = DeclaredStruct("Holder");
            inner = Field(holder, "box", box);
            other = Field(holder, "other", integer);
            holder.FieldsList.Add(inner);
            holder.FieldsList.Add(other);
            type = holder;
        }

        var local = new LocalVariable("p", type);
        var localRead = new LocalReferenceExpression(HandBuiltModel.Node(37), local);
        var parent = type == nested || type is ConstructedType ? Access(localRead, inner, type: point) : (Expression)localRead;
        Expression first = name.StartsWith("struct-local-", StringComparison.Ordinal) ? Access(parent, x) : localRead;
        Expression missing = name.StartsWith("struct-local-", StringComparison.Ordinal) ? Access(parent, y, 37) : localRead;
        if(referenceValue is not null)
        {
            first = name == "struct-local-reference-write-does-not-assign-sibling" ? Access(localRead, inner) : Access(localRead, other);
            missing = name == "struct-local-reference-write-does-not-assign-sibling"
                ? Access(localRead, other, 37)
                : Access(Access(localRead, inner, 37), referenceValue, 37);
        }

        var statements = new List<Statement> { new LocalDeclarationStatement(local, null) };
        if(name.StartsWith("struct-local-", StringComparison.Ordinal)
            && name is not ("struct-local-field-before-any-assignment" or "struct-local-field-reset-on-loop-entry" or "struct-local-unassigned-opaque-ambient-value"))
        {
            statements.Add(Write(first));
        }

        switch(name)
        {
            case "local-assigned-on-one-branch":
            case "local-read-after-loop-body-declares-it":
                statements.Add(new IfStatement(Condition(), HandBuiltModel.Block(Write(localRead)), null));
                break;
            case "local-assigned-only-in-for":
            case "struct-local-field-assigned-only-in-for":
                statements.Add(new ForStatement(null, Condition(), null, HandBuiltModel.Block(Write(missing))));
                break;
            case "local-assigned-only-in-foreach":
            case "struct-local-field-assigned-only-in-foreach":
                var array = model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [integer]);
                statements.Add(new ForEachStatement(new LocalVariable("item", integer), HandBuiltModel.Literal(array), HandBuiltModel.Block(Write(missing))));
                break;
            case "local-assigned-only-in-while":
            case "struct-local-field-assigned-only-in-while":
                statements.Add(new WhileStatement(Condition(), HandBuiltModel.Block(Write(missing))));
                break;
            case "local-assigned-only-inside-lambda":
            case "struct-local-field-assigned-only-in-lambda":
                statements.Add(Lambda(Write(missing)));
                break;
            case "local-assigned-only-inside-async-block":
                statements.Add(Async(Write(missing)));
                break;
            case "struct-local-field-assigned-on-one-branch":
                statements.Clear();
                statements.Add(new LocalDeclarationStatement(local, null));
                statements.Add(Write(missing));
                statements.Add(new IfStatement(Condition(), HandBuiltModel.Block(Write(first)), null));
                missing = first;
                break;
            case "struct-local-different-fields-on-each-branch":
                statements.RemoveAt(1);
                statements.Add(new IfStatement(Condition(), HandBuiltModel.Block(Write(first)), HandBuiltModel.Block(Write(missing))));
                missing = localRead;
                break;
            case "struct-local-inherited-partial-whole-copy":
            case "struct-local-inherited-unassigned-field":
            case "struct-local-generic-nested-whole-copy":
                statements.Add(Write(Access(localRead, other)));
                if(name.EndsWith("whole-copy", StringComparison.Ordinal))
                {
                    missing = localRead;
                }

                break;
            case "struct-local-nested-partial-whole-copy":
                missing = parent;
                break;
            case "struct-local-partial-whole-copy":
            case "struct-local-partial-whole-argument":
            case "struct-local-partial-whole-return":
                missing = localRead;
                break;
            case "struct-local-nested-parent-unassigned-sibling":
                statements.RemoveAt(1);
                statements.Add(Write(parent));
                missing = Access(localRead, other, 37);
                break;
            case "struct-local-declaration-initializer-does-not-assign":
                x.Initializer = HandBuiltModel.Literal(integer, 7L);
                statements.RemoveAt(1);
                statements.Add(Write(missing));
                missing = Access(parent, x, 37);
                break;
            case "struct-local-auto-property-needs-whole-receiver":
            case "struct-local-auto-property-write-needs-whole-receiver":
                _ = point.FieldsList.Remove(y);
                var property = new Property("Value", integer, default) { Getter = new PropertyAccessor { IsAuto = true } };
                point.PropertiesList.Add(property);
                missing = new MemberAccessExpression(HandBuiltModel.Node(37), localRead, "Value", integer, member: new PropertyResult(property, point, []));
                if(name.Contains("write-", StringComparison.Ordinal))
                {
                    missing = new AssignmentExpression(HandBuiltModel.Node(37), missing, HandBuiltModel.Literal(integer, 2L), integer);
                }

                break;
            case "struct-local-reference-write-does-not-assign-sibling":
                statements.Add(Write(Access(Access(localRead, inner), referenceValue!)));
                break;
            case "struct-local-unassigned-reference-field-write":
                statements.Add(Write(missing));
                return model.Analyze(new DefiniteAssignmentAnalyzer(), HandBuiltModel.Block([.. statements]));
            case "struct-local-static-write-does-not-assign-instance":
                statements.RemoveAt(1);
                var shared = new Field("shared", integer, default, new MemberModifiers { IsStatic = true }) { DeclaringType = point };
                point.FieldsList.Add(shared);
                statements.Add(Write(new FieldReferenceExpression(HandBuiltModel.Node(), shared)));
                missing = Access(localRead, x, 37);
                break;
            case "struct-local-field-before-any-assignment":
                missing = Access(parent, x, 37);
                break;
            case "struct-local-field-reset-on-loop-entry":
                var loopBody = HandBuiltModel.Block(new LocalDeclarationStatement(local, null), Write(first), new ExpressionStatement(missing), Write(missing));
                return model.Analyze(new DefiniteAssignmentAnalyzer(), HandBuiltModel.Block(new WhileStatement(Condition(), loopBody)));
            case "struct-local-missing-member-does-not-cascade":
                missing = new MemberAccessExpression(HandBuiltModel.Node(37), localRead, "missing", ErrorType.Instance, isFailed: true);
                break;
            case "struct-local-unassigned-opaque-ambient-value":
                missing = localRead;
                break;
        }

        if(name.Contains("local-function", StringComparison.Ordinal))
        {
            LocalFunction(statements, name, missing);
        }
        else if(name is "local-captured-before-assignment" || name.Contains("captured-by-lambda", StringComparison.Ordinal))
        {
            statements.Add(Lambda(new ExpressionStatement(missing)));
            statements.Add(Write(missing));
        }
        else if(name.Contains("captured-by-async", StringComparison.Ordinal))
        {
            statements.Add(Async(new ExpressionStatement(missing)));
            statements.Add(Write(missing));
        }
        else if(name.Contains("read-modify-write", StringComparison.Ordinal))
        {
            statements.Add(Write(missing, new BinaryExpression(HandBuiltModel.Node(37), missing, BinaryOperatorKind.Add, HandBuiltModel.Literal(integer, 2L), integer)));
        }
        else if(name == "local-read-through-interpolation")
        {
            statements.Add(new ExpressionStatement(new InterpolatedStringExpression(HandBuiltModel.Node(), [new InterpolatedStringHole(missing)], text)));
        }
        else if(name == "struct-local-partial-whole-return")
        {
            statements.Add(new ReturnStatement(missing, type));
        }
        else if(name == "struct-local-partial-whole-argument")
        {
            statements.Add(new ExpressionStatement(new InvocationExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(integer), [missing], integer, null, [type])));
        }
        else
        {
            statements.Add(new ExpressionStatement(missing));
        }

        return model.Analyze(new DefiniteAssignmentAnalyzer(), HandBuiltModel.Block([.. statements]));
    }

    private void LocalFunction(List<Statement> statements, string name, Expression missing)
    {
        var body = name == "local-function-reads-its-own-local-before-assignment"
            ? HandBuiltModel.Block(new LocalDeclarationStatement(((LocalReferenceExpression)missing).Local, null), new ReturnStatement(missing, integer))
            : HandBuiltModel.Block(new ReturnStatement(missing, integer));
        if(name == "local-function-reads-its-own-local-before-assignment")
        {
            statements.Clear();
        }

        var function = new Method("Read", integer, [], default) { Body = body };
        statements.Add(new LocalFunctionStatement(function, []));
        if(name.Contains("through-another", StringComparison.Ordinal) || name.Contains("transitive", StringComparison.Ordinal))
        {
            var forward = new Method("Forward", integer, [], default)
            {
                Body = HandBuiltModel.Block(new ReturnStatement(Call(function), integer)),
            };
            statements.Add(new LocalFunctionStatement(forward, []));
            function = forward;
        }

        Expression entry = name.Contains("reference", StringComparison.Ordinal) || name.Contains("named-", StringComparison.Ordinal)
            ? new FunctionReferenceExpression(HandBuiltModel.Node(37), null, function, model.Interner.GetOrCreateFunctionType([], integer))
            : Call(function, 37);
        statements.Add(new ExpressionStatement(entry));
        statements.Add(Write(missing));
    }

    private InvocationExpression Call(Method function, int offset = 0)
    {
        return new InvocationExpression(HandBuiltModel.Node(offset), new MethodReferenceExpression(HandBuiltModel.Node(), function), [], integer, function, []);
    }

    private ExpressionStatement Lambda(params Statement[] statements)
    {
        return new ExpressionStatement(new LambdaExpression(HandBuiltModel.Node(37), [], null, HandBuiltModel.Block(statements), false,
            model.Interner.GetOrCreateFunctionType([], integer)));
    }

    private ExpressionStatement Async(params Statement[] statements)
    {
        return new ExpressionStatement(new AsyncBlockExpression(HandBuiltModel.Node(37), HandBuiltModel.Block(statements), model.Type("Task"), false));
    }

    private Expression Condition()
    {
        return HandBuiltModel.Literal(model.Type("Boolean"));
    }

    private Statement Write(Expression target, Expression? value = null)
    {
        return new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), target, value ?? HandBuiltModel.Literal(integer, 1L), target.Type));
    }

    private static MemberAccessExpression Access(Expression receiver, Field field, int offset = 0, IMistType? type = null)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(offset), receiver, field.Name, type ?? field.Type,
            member: new FieldResult(field, receiver.Type, []));
    }

    private static Field Field(ConcreteType owner, string name, IMistType type)
    {
        return new Field(name, type, default, default) { DeclaringType = owner };
    }

    private static StructType DeclaredStruct(string name)
    {
        return new StructType(name, "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode());
    }
}