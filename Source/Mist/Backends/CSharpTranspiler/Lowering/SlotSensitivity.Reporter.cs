using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
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

internal sealed partial class SlotSensitivity
{
    public static void Report(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        Discover(model).Report(SourceBodies.Of(model), SourceDeclared(model), SourceTypes.ExtensionsOf(model), diagnostics);
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
        return Judge(parameter, argument, new HashSet<(TypeParameter, IMistType)>());
    }

    private (string Reason, ConstructedType Closing)? Judge(TypeParameter parameter, IMistType argument, HashSet<(TypeParameter, IMistType)> visited)
    {
        var routed = RoutedClosings(argument);

        if(routed.Count == 0 || !visited.Add((parameter, argument)) || !reasons.TryGetValue(parameter, out var found))
        {
            return null;
        }

        foreach(var reason in found)
        {
            switch(reason.Kind)
            {
                case Kind.TypeValue:
                    return ($"reads a type that mentions '{parameter.Name}' as a type value, and a CLR type cannot carry the slot's value", routed[0]);

                case Kind.Contained:
                    return ($"tests a value against a type that nests '{parameter.Name}' in a CLR container, which has nowhere to keep the slot's value", routed[0]);

                case Kind.Test when ContainedClosing(argument, contained: false) is { } nested:
                    return ($"tests a value against '{parameter.Name}', and the argument nests '{TypeDisplay.Mist(nested)}' in a CLR container, which has nowhere to keep the slot's value", nested);

                case Kind.Clr when Substituted(reason.Through!, parameter, argument) is var reached && StructStamp.Needed(reached):
                    return ($"passes a type that mentions '{parameter.Name}' to a CLR generic, whose body no description of the closing reaches and which may hand back a zeroed value", StructStamp.HeldByValue(reached) ?? routed[0]);

                case Kind.Forwarded:
                    if(Judge(reason.Target!, Substituted(reason.Through ?? parameter, parameter, argument), visited) is var (inner, closing))
                    {
                        return ($"passes a type that mentions '{parameter.Name}' to '{reason.Target!.Name}' of '{SlotReach.Owner(reason.Target)}', which {inner}", closing);
                    }

                    break;

                case Kind.Answered:
                    if(Judge(reason.Target!, argument, visited) is var (answering, answered))
                    {
                        return ($"is answered by '{SlotReach.Owner(reason.Target!)}' of '{Host(reason.Target!)}', which {answering}", answered);
                    }

                    break;
            }
        }

        return null;
    }

    internal static string ClrReason(TypeParameter parameter)
    {
        return $"is a CLR generic, whose body no description of the closing reaches and which may hand back a zeroed value for '{parameter.Name}'";
    }

    private static IMistType Substituted(IMistType through, TypeParameter parameter, IMistType argument)
    {
        return TypeSubstitution.ForParameters([parameter], [argument]).Apply(through, interner: null);
    }

    private static ConstructedType? ContainedClosing(IMistType type, bool contained)
    {
        return Unaliased(type) switch
        {
            ConstructedType constructed when SlotRoutes.ValueRouted(constructed.Definition).Count > 0 && contained => constructed,
            ConstructedType constructed => constructed.TypeArguments
                .Select(argument => ContainedClosing(argument, contained || IsContainer(constructed)))
                .FirstOrDefault(found => found is not null),
            FunctionType function => function.ParameterTypes.Append(function.ReturnType)
                .Select(part => ContainedClosing(part, contained: true))
                .FirstOrDefault(found => found is not null),
            var other => Parts(other).Select(part => ContainedClosing(part, contained)).FirstOrDefault(found => found is not null),
        };
    }

    private static string Host(TypeParameter parameter)
    {
        return MistType.StripArity(parameter.DeclaringMethod?.DeclaringType?.Name ?? SlotReach.Owner(parameter));
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
            var clr = IsClrGeneric(method);

            for(var i = 0; i < arguments.Count; i++)
            {
                if(i < filled.Count)
                {
                    if(clr)
                    {
                        RefuseClr(filled[i], arguments[i], method.Name, node);
                    }
                    else
                    {
                        Refuse(filled[i], arguments[i], node);
                    }
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

                var clr = IsClrGeneric(generic);

                for(var i = 0; i < constructed.TypeArguments.Count && i < generic.TypeParametersList.Count; i++)
                {
                    if(clr)
                    {
                        RefuseClr(generic.TypeParametersList[i], constructed.TypeArguments[i], MistType.StripArity(constructed.Definition.Name), node);
                    }
                    else
                    {
                        Refuse(generic.TypeParametersList[i], constructed.TypeArguments[i], node);
                    }
                }
            }
        }

        private void Refuse(TypeParameter filled, IMistType argument, SyntaxNode node)
        {
            if(sensitivity.IsSensitive(filled) && !reported.Contains((node, filled)) && sensitivity.Judge(filled, argument) is var (reason, closing))
            {
                Report(filled, SlotReach.Owner(filled), argument, closing, reason, node);
            }
        }

        private void RefuseClr(TypeParameter filled, IMistType argument, string declaration, SyntaxNode node)
        {
            if(!reported.Contains((node, filled)) && StructStamp.Needed(argument) && (StructStamp.HeldByValue(argument) ?? RoutedClosings(argument).FirstOrDefault()) is { } zeroed)
            {
                Report(filled, declaration, argument, zeroed, ClrReason(filled), node);
            }
        }

        private void Report(TypeParameter filled, string declaration, IMistType argument, ConstructedType closing, string reason, SyntaxNode node)
        {
            _ = reported.Add((node, filled));
            diagnostics.ReportAtNodeTyped(
                BackendDiagnostics.SlotSensitiveClosingArgument, node, null,
                DiagnosticSnapshot.Type(argument), new NameValue(filled.Name), new NameValue(declaration),
                DiagnosticSnapshot.Type(closing), new NameValue(SlotRoutes.ValueRouted(closing.Definition)[0].Name), new EnumValue(reason));
        }
    }
}