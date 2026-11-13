using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Which type parameters a shared C# body cannot fill with a closing of a value-routed type, decided once over
/// the whole program beside <see cref="SlotRoutes"/> (runtime-dependent-values design, <em>A closing as a type
/// argument</em>). Once a slot leaves the C# type, every closing of its type is one C# type, so a generic body
/// shared by all closings of its parameter answers for the wrong one wherever it tells closings apart: a test
/// against a type that mentions the parameter, storage of such a type that starts from the CLR zero, static
/// storage of the declaring type, a type value, or a closing passed on to a parameter of another declaration
/// that does one of these, to a fixed point. A test is exempt when the value's own type admits only the tested
/// closing, the parameter itself or a union holding it, because the C# test then decides the same question the
/// slot would; the emitter writes no slot comparison for such a test either. The zeroed-storage reason refuses
/// only a struct closing, a zeroed reference being null whichever closing it was declared as. Everything else a
/// body does with its parameter holds, passes, returns or calls through an object that carries its own value.
/// </summary>
internal sealed class SlotSensitivity
{
    public enum Kind
    {
        Test,
        Storage,
        Static,
        TypeValue,
        Forwarded,
    }

    public sealed record Reason(Kind Kind, TypeParameter Parameter, TypeParameter? Target = null);

    private readonly Dictionary<TypeParameter, List<Reason>> reasons = new(ReferenceEqualityComparer.Instance);

    private SlotSensitivity()
    {
    }

    public bool IsSensitive(TypeParameter parameter)
    {
        return reasons.ContainsKey(parameter);
    }

    public IReadOnlyList<Reason> ReasonsOf(TypeParameter parameter)
    {
        return reasons.GetValueOrDefault(parameter) ?? [];
    }

    public IReadOnlyCollection<TypeParameter> Sensitive => reasons.Keys;

    public static bool AdmitsOnly(IMistType? subject, IMistType tested)
    {
        if(subject is null)
        {
            return false;
        }

        var target = Unaliased(tested);
        var source = Unaliased(subject);

        return ReferenceEquals(source, target)
            || (source is UnionType union && union.Constituents.Any(constituent => ReferenceEquals(Unaliased(constituent), target)));
    }

    public static SlotSensitivity Discover(ProgramModelClass model)
    {
        var roots = SourceBodies.Of(model).Select(root => root.Root).ToList();

        return Discover(roots, SourceDeclared(model), Closings(model, roots));
    }

    public static SlotSensitivity Discover(
        IEnumerable<object> roots,
        IEnumerable<MemberType> types,
        IEnumerable<(IHasTypeParameters Definition, IReadOnlyList<IMistType> Arguments)> closings)
    {
        var found = new SlotSensitivity();

        foreach(var root in roots)
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root))
            {
                found.Mark(node);
            }
        }

        foreach(var type in types)
        {
            if(!HasStaticStorage(type))
            {
                continue;
            }

            for(var owner = type; owner is not null; owner = owner.DeclaringType as MemberType)
            {
                foreach(var parameter in owner.TypeParametersList)
                {
                    _ = found.Add(new Reason(Kind.Static, parameter));
                }
            }
        }

        var fills = closings.ToList();
        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var (definition, arguments) in fills)
            {
                for(var i = 0; i < arguments.Count && i < definition.TypeParametersList.Count; i++)
                {
                    var filled = definition.TypeParametersList[i];

                    if(!found.IsSensitive(filled))
                    {
                        continue;
                    }

                    foreach(var mentioned in Parameters(arguments[i]))
                    {
                        if(!ReferenceEquals(mentioned, filled))
                        {
                            changed |= found.Add(new Reason(Kind.Forwarded, mentioned, filled));
                        }
                    }
                }
            }
        }

        return found;
    }

    public static void Report(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        var sensitivity = Discover(model);

        if(sensitivity.reasons.Count == 0)
        {
            return;
        }

        sensitivity.Report(SourceBodies.Of(model), SourceDeclared(model), SourceTypes.ExtensionsOf(model), diagnostics);
    }

    public void Report(IEnumerable<SourceRoot> bodies, IEnumerable<MemberType> types, IEnumerable<ExtensionBlock> blocks, DiagnosticBag diagnostics)
    {
        var reporter = new Reporter(this, diagnostics);

        foreach(var root in bodies)
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root.Root))
            {
                reporter.Body(node);
            }
        }

        foreach(var type in types)
        {
            reporter.Signatures(type);
        }

        foreach(var block in blocks)
        {
            reporter.Signatures(block);
        }
    }

    internal (string Reason, ConstructedType Closing)? Judge(TypeParameter parameter, IMistType argument)
    {
        var routed = RoutedClosings(argument);

        return routed.Count == 0 ? null : Judge(parameter, routed, new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance));
    }

    private (string Reason, ConstructedType Closing)? Judge(TypeParameter parameter, IReadOnlyList<ConstructedType> routed, HashSet<TypeParameter> visited)
    {
        if(!visited.Add(parameter) || !reasons.TryGetValue(parameter, out var found))
        {
            return null;
        }

        foreach(var reason in found)
        {
            switch(reason.Kind)
            {
                case Kind.Storage:
                    if(routed.FirstOrDefault(closing => closing.Definition is StructType) is { } zeroed)
                    {
                        return ($"creates storage of a type that mentions '{parameter.Name}' from the CLR zero, which holds no value of the slot", zeroed);
                    }

                    break;

                case Kind.Forwarded:
                    if(Judge(reason.Target!, routed, visited) is var (inner, closing))
                    {
                        return ($"passes a type that mentions '{parameter.Name}' to '{reason.Target!.Name}' of '{SlotReach.Owner(reason.Target)}', which {inner}", closing);
                    }

                    break;

                case Kind.Test:
                    return ($"tests a value against a type that mentions '{parameter.Name}', which the shared body cannot tell from another closing's", routed[0]);

                case Kind.Static:
                    return ("declares static storage, which every closing of it would share", routed[0]);

                default:
                    return ($"reads a type that mentions '{parameter.Name}' as a type value, which stands for every closing at once", routed[0]);
            }
        }

        return null;
    }

    private bool Add(Reason reason)
    {
        if(!reasons.TryGetValue(reason.Parameter, out var found))
        {
            found = [];
            reasons[reason.Parameter] = found;
        }

        if(found.Exists(existing => existing.Kind == reason.Kind && ReferenceEquals(existing.Target, reason.Target)))
        {
            return false;
        }

        found.Add(reason);
        return true;
    }

    private void Mark(object node)
    {
        switch(node)
        {
            case IsPatternExpression test:
                MarkPattern(test.Pattern, test.Scrutinee.Type);
                break;

            case SwitchExpression switched:
                foreach(var arm in switched.Arms)
                {
                    MarkPattern(arm.Pattern, switched.Scrutinee.Type);
                }

                break;

            case AsExpression conversion:
                MarkTest(conversion.Operand.Type, conversion.TargetType);
                break;

            case ArrayCreationExpression array:
                MarkEach(Kind.Storage, array.ElementType);
                break;

            case StackallocExpression allocation:
                MarkEach(Kind.Storage, allocation.ElementType);
                break;

            case DefaultExpression zero when !OptionalUnion.IsOptional(zero.Type):
                MarkEach(Kind.Storage, zero.Type);
                break;

            case TypeLiteralExpression literal:
                MarkEach(Kind.TypeValue, literal.ReferencedType);
                break;
        }
    }

    private void MarkPattern(Pattern pattern, IMistType? subject)
    {
        switch(pattern)
        {
            case TypeTestPattern typeTest:
                MarkTest(subject, typeTest.NarrowedType);
                break;

            case PropertyPattern property:
                MarkTest(subject, property.TypeTest?.NarrowedType ?? property.NarrowedType);

                foreach(var subpattern in property.Subpatterns)
                {
                    MarkPattern(subpattern.Pattern, subpattern.MemberType);
                }

                break;

            case NegatedPattern negated:
                MarkPattern(negated.Operand, subject);
                break;

            case ConjunctionPattern conjunction:
                foreach(var operand in conjunction.Operands)
                {
                    MarkPattern(operand, subject);
                }

                break;

            case DisjunctionPattern disjunction:
                foreach(var operand in disjunction.Operands)
                {
                    MarkPattern(operand, subject);
                }

                break;

            case TuplePattern tuple:
                if(tuple.TestedType is { } tested)
                {
                    MarkTest(subject, tested);
                }

                foreach(var element in tuple.Elements)
                {
                    MarkPattern(element, subject: null);
                }

                break;

            case ListPattern list:
                foreach(var element in list.Elements)
                {
                    if(element.Subpattern is { } subpattern)
                    {
                        MarkPattern(subpattern, subject: null);
                    }
                }

                break;
        }
    }

    private void MarkTest(IMistType? subject, IMistType tested)
    {
        if(!AdmitsOnly(subject, tested))
        {
            MarkEach(Kind.Test, tested);
        }
    }

    private void MarkEach(Kind kind, IMistType type)
    {
        foreach(var parameter in Parameters(type))
        {
            _ = Add(new Reason(kind, parameter));
        }
    }

    private static IEnumerable<TypeParameter> Parameters(IMistType type)
    {
        foreach(var parameter in TypeParameterReach.Collect(type))
        {
            var filled = parameter is PackElementType element ? element.Pack : parameter;

            if(!filled.IsDependent)
            {
                yield return filled;
            }
        }
    }

    private static bool HasStaticStorage(MemberType type)
    {
        if(type is ConcreteType concrete && concrete.FieldsList.Exists(field => field.Modifiers is { IsStatic: true, IsIntrinsic: false }))
        {
            return true;
        }

        return type.PropertiesList.Exists(property => property.Modifiers is { IsStatic: true, IsIntrinsic: false }
            && (property.UsesBackingField || property.Initializer is not null || property.Getter is { IsAuto: true } || property.Setter is { IsAuto: true }));
    }

    private static IEnumerable<MemberType> SourceDeclared(ProgramModelClass model)
    {
        return model.TypesByFQN.Values.OfType<MemberType>().Where(type => type.DeclarationNode is not null && !ClrProjection.Projects(type));
    }

    private static IEnumerable<(IHasTypeParameters Definition, IReadOnlyList<IMistType> Arguments)> Closings(ProgramModelClass model, IReadOnlyList<object> roots)
    {
        foreach(var constructed in model.TypeInterner.ConstructedTypes)
        {
            if(constructed.Definition is IHasTypeParameters generic)
            {
                yield return (generic, constructed.TypeArguments);
            }
        }

        foreach(var (method, typeArguments) in model.GenericCalls.All())
        {
            yield return (method, typeArguments);
        }

        foreach(var root in roots)
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root))
            {
                if(SlotRoutes.ClosedThroughReceiver(node) is var (block, arguments))
                {
                    yield return (block, arguments);
                }
            }
        }
    }

    private static IMistType Unaliased(IMistType type)
    {
        return type is TypeAlias { AliasedType: { } aliased } ? Unaliased(aliased) : type;
    }

    private static List<ConstructedType> RoutedClosings(IMistType argument)
    {
        return [.. Constructed(argument).Where(closing => SlotRoutes.ValueRouted(closing.Definition).Count > 0)];
    }

    internal static IEnumerable<ConstructedType> Constructed(IMistType type)
    {
        var found = new List<ConstructedType>();
        Collect(type, found, new HashSet<IMistType>(ReferenceEqualityComparer.Instance));
        return found;
    }

    private static void Collect(IMistType type, List<ConstructedType> found, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return;
        }

        switch(type)
        {
            case TypeAlias { AliasedType: { } aliased }:
                Collect(aliased, found, seen);
                break;

            case RefinedType refined:
                Collect(refined.BaseType, found, seen);
                break;

            case ConstructedType constructed:
                found.Add(constructed);

                foreach(var argument in constructed.TypeArguments)
                {
                    Collect(argument, found, seen);
                }

                if(constructed.Enclosing is { } enclosing)
                {
                    Collect(enclosing, found, seen);
                }

                break;

            case UnionType union:
                CollectAll(union.Constituents, found, seen);
                break;

            case NamedTupleType named:
                CollectAll(named.ElementTypes, found, seen);
                break;

            case TupleType tuple:
                CollectAll(tuple.ElementTypes, found, seen);
                break;

            case FunctionType function:
                Collect(function.ReturnType, found, seen);
                CollectAll(function.ParameterTypes, found, seen);
                break;

            case LiftedType lifted:
                Collect(lifted.Type, found, seen);

                if(lifted.Value is TypePackValue pack)
                {
                    CollectAll(pack.Elements, found, seen);
                }

                break;

            case SpreadType spread:
                Collect(spread.Operand, found, seen);
                break;
        }
    }

    private static void CollectAll(IReadOnlyList<IMistType> types, List<ConstructedType> found, HashSet<IMistType> seen)
    {
        foreach(var type in types)
        {
            Collect(type, found, seen);
        }
    }

    private sealed class Reporter(SlotSensitivity sensitivity, DiagnosticBag diagnostics)
    {
        private readonly HashSet<(SyntaxNode Node, TypeParameter Parameter)> reported = [];

        public void Body(object node)
        {
            switch(node)
            {
                case NewExpression created:
                    Check(created.Type, created.Node);
                    break;

                case TypeReferenceExpression reference:
                    Check(reference.ReferencedType, reference.Node);
                    break;

                case TypeLiteralExpression literal:
                    Check(literal.ReferencedType, literal.Node);
                    break;

                case ArrayCreationExpression array:
                    Check(array.Type, array.Node);
                    break;

                case StackallocExpression allocation:
                    Check(allocation.Type, allocation.Node);
                    break;

                case AsExpression conversion:
                    Check(conversion.TargetType, conversion.Node);
                    break;

                case CastExpression cast:
                    Check(cast.TargetType, cast.Node);
                    break;

                case DefaultExpression zero:
                    Check(zero.Type, zero.Node);
                    break;

                case TypeTestPattern typeTest:
                    Check(typeTest.NarrowedType, typeTest.Node);
                    break;

                case PropertyPattern property:
                    Check(property.NarrowedType, property.Node);
                    break;

                case TuplePattern { TestedType: { } tested } tuple:
                    Check(tested, tuple.Node);
                    break;

                case LambdaExpression lambda:
                    foreach(var parameter in lambda.Parameters)
                    {
                        Check(parameter.Type, lambda.Node);
                    }

                    break;

                case LocalDeclarationStatement { Initializer: { } initializer } declaration:
                    Check(declaration.Local.Type, initializer.Node);
                    break;

                case LocalFunctionStatement { Function.DeclarationNode: { } at } local:
                    Signature(local.Function, at);
                    break;

                case InvocationExpression { Method: { } method } invocation:
                    Call(method, invocation.Receiver switch
                    {
                        MemberAccessExpression access => access.TypeArguments,
                        MethodReferenceExpression reference => reference.TypeArguments,
                        _ => [],
                    }, invocation.Node);
                    break;

                case FunctionReferenceExpression group:
                    Call(group.Method, group.TypeArguments, group.Node);
                    break;
            }
        }

        public void Signatures(MemberType type)
        {
            if(type.DeclarationNode is not { } node)
            {
                return;
            }

            if(type is ConcreteType { BaseType: { } baseType })
            {
                Check(baseType, node);
            }

            foreach(var abstractType in type.BaseAbstractTypes)
            {
                Check(abstractType, node);
            }

            if(type is ConcreteType concrete)
            {
                foreach(var conformance in concrete.ConformancesList)
                {
                    Check(conformance.AbstractType, conformance.DeclarationNode);
                }

                foreach(var field in concrete.FieldsList)
                {
                    Check(field.Type, field.DeclarationNode ?? node);
                }
            }

            Constraints(type.TypeParametersList, node);

            foreach(var method in type.EnumerateDelegates())
            {
                Signature(method, node);
            }

            foreach(var property in type.PropertiesList)
            {
                Check(property.Type, property.DeclarationNode ?? node);
            }

            foreach(var indexer in type.IndexersList)
            {
                var at = indexer.DeclarationNode ?? node;
                Check(indexer.Type, at);

                foreach(var parameter in indexer.ParametersList)
                {
                    Check(parameter.Type, parameter.DeclarationNode ?? at);
                }
            }
        }

        public void Signatures(ExtensionBlock block)
        {
            if(block.DeclarationNode is not { } node)
            {
                return;
            }

            if(block.ResolvedReceiverType is { } receiver)
            {
                Check(receiver, node);
            }

            Constraints(block.TypeParametersList, node);

            foreach(var method in block.MethodsList)
            {
                Signature(method, node);
            }

            foreach(var property in block.PropertiesList)
            {
                Check(property.Type, property.DeclarationNode ?? node);
            }
        }

        private void Signature(Method method, SyntaxNode fallback)
        {
            var at = method.DeclarationNode ?? fallback;
            Check(method.ReturnType, at);
            Constraints(method.TypeParametersList, at);

            foreach(var parameter in method.ParametersList)
            {
                Check(parameter.Type, parameter.DeclarationNode ?? at);
            }
        }

        private void Constraints(IReadOnlyList<TypeParameter> parameters, SyntaxNode node)
        {
            foreach(var parameter in parameters)
            {
                foreach(var constraint in parameter.ConstraintsList.OfType<TypeConstraint>())
                {
                    Check(constraint.ConstraintType, node);
                }
            }
        }

        private void Call(Method method, IReadOnlyList<IMistType> written, SyntaxNode node)
        {
            var (arguments, parameters) = ExtensionBlockEmitter.CallTypeArguments(method, written);
            var filled = parameters ?? method.TypeParametersList;

            for(var i = 0; i < arguments.Count; i++)
            {
                if(i < filled.Count)
                {
                    Refuse(filled[i], arguments[i], node);
                }

                Check(arguments[i], node);
            }
        }

        private void Check(IMistType type, SyntaxNode node)
        {
            foreach(var constructed in Constructed(type))
            {
                if(constructed.Definition is not IHasTypeParameters generic)
                {
                    continue;
                }

                for(var i = 0; i < constructed.TypeArguments.Count && i < generic.TypeParametersList.Count; i++)
                {
                    Refuse(generic.TypeParametersList[i], constructed.TypeArguments[i], node);
                }
            }
        }

        private void Refuse(TypeParameter filled, IMistType argument, SyntaxNode node)
        {
            if(!sensitivity.IsSensitive(filled) || reported.Contains((node, filled)) || sensitivity.Judge(filled, argument) is not var (reason, closing))
            {
                return;
            }

            _ = reported.Add((node, filled));
            diagnostics.ReportAtNodeTyped(
                BackendDiagnostics.SlotSensitiveClosingArgument, node, null,
                DiagnosticSnapshot.Type(argument), new NameValue(filled.Name), new NameValue(SlotReach.Owner(filled)),
                DiagnosticSnapshot.Type(closing), new NameValue(SlotRoutes.ValueRouted(closing.Definition)[0].Name), new EnumValue(reason));
        }
    }
}