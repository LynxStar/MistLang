using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The accessibility questions, from the full-accessibility-model spec: whether a name sits inside the
/// domain of the type it names, whether a use sits inside the member's -- <see cref="AccessDomains"/> --
/// and whether a declaration exposes a type less accessible than itself. A member of a type out of reach,
/// and a declaration exposing one, are left to the name that reached the type, so that one mistake is
/// one complaint.
/// </summary>
internal sealed class AccessibilityAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case MemberAccessExpression { Member: { } member } access:
                    JudgeStorage(context, access, access.Node, [access], access.MemberName, member);
                    break;

                case IndexExpression { Indexer: { DeclaringType: { } declaring } indexer } index:
                    Judge(context, index, index.Node, [index], "this[]", indexer.Modifiers, declaring);
                    JudgeCounted(context, index, index.Node, [index], index.FromEnd is { Count: var count } ? [count] : []);
                    break;

                case IndexExpression { Slice: { } slice } sliced:
                    JudgeCounted(context, sliced, sliced.Node, [sliced], CountedReads.Of(null, slice));
                    break;

                case ListPattern list:
                    JudgeCounted(context, list, list.Node, [], CountedReads.Of(list.Reads, list.Slice));
                    break;

                case ObjectInitializerAssignment { IsTargetFailed: false, MemberName: { } name, Member: { } member } entry:
                    JudgeStorage(context, entry, entry.Node, [], name, member);
                    break;

                case PropertySubpattern { IsMemberFailed: false } subpattern:
                    JudgeStorage(context, subpattern, subpattern.Node, [], subpattern.MemberName, subpattern.Member);
                    break;

                case InvocationExpression { Method: { DeclaringType: { } declaring } method } call:
                    Judge(context, call, call.Node, [call], method.Name, method.Modifiers, declaring);
                    break;

                case NewExpression { Constructor: { DeclaringType: { } declaring } constructor } creation:
                    Judge(context, creation, creation.Node, [creation], constructor.Name, constructor.Modifiers, declaring);
                    break;

                case FunctionReferenceExpression { Method: { DeclaringType: { } declaring } method } reference:
                    Judge(context, reference, reference.Node, [reference], method.Name, method.Modifiers, declaring);
                    break;
            }
        }

        JudgeNamings(context);

        foreach(var type in context.Types)
        {
            CheckExposure(context, type);

            foreach(var constructor in (type as ConcreteType)?.ConstructorsList ?? [])
            {
                if(InaccessibleChain(constructor) is { } target)
                {
                    var accessibility = target.Modifiers.Accessibility;
                    context.Report(
                        ProgramAnalysisDiagnostics.InaccessibleMember, constructor.ChainNode!, [],
                        target.Name, accessibility.ToString().ToLowerInvariant(), AccessDomains.Where(accessibility));
                }
            }
        }

        foreach(var declared in DeclaredPredicates.Of(context))
        {
            CheckPredicate(context, declared.Predicate, declared.Site);
        }
    }

    private static void JudgeNamings(AnalysisContext context)
    {
        var judged = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);

        foreach(var naming in context.Namings)
        {
            if(DecideNaming(naming) is { } refusal && judged.Add(naming.Node))
            {
                var blocking = refusal.Blocking;
                var accessibility = Declarations.ModifiersOf(blocking)?.Accessibility ?? Accessibility.Public;

                context.Report(
                    refusal.Descriptor, naming.Node, [],
                    Qualified(blocking), accessibility.ToString().ToLowerInvariant(), AccessDomains.Where(accessibility));
            }
        }
    }

    internal static (MistType Blocking, MistDiagnostic Descriptor)? DecideNaming(TypeNaming naming)
    {
        return AccessDomains.Blocking(naming.Owner, naming.Assembly, naming.Named) is { } blocking
            ? (blocking, ProgramAnalysisDiagnostics.InaccessibleType)
            : null;
    }

    internal static Constructor? InaccessibleChain(Constructor constructor)
    {
        if(constructor is not
            {
                ChainKind: ConstructorChainKind.This or ConstructorChainKind.Base,
                ChainNode: not null,
                ChainedConstructor: { DeclaringType: { } declaring } target,
                DeclaringType: { } owner,
            })
        {
            return null;
        }

        return AccessDomains.Reaches(owner, owner.Assembly, declaring)
            && !AccessDomains.Admits(owner, owner.Assembly, target.Modifiers.Accessibility, declaring)
            ? target
            : null;
    }

    private static string Qualified(MistType type)
    {
        return type.DeclaringType is { } outer ? $"{Qualified(outer)}.{TypeDisplay.Mist(type)}" : TypeDisplay.Mist(type);
    }

    private static void JudgeStorage(
        AnalysisContext context, object use, SyntaxNode at, IReadOnlyList<Expression> about, string name, LookupResult member)
    {
        if(Storage(member) is { } storage)
        {
            Judge(context, use, at, about, name, storage.Modifiers, storage.Declaring);
        }
    }

    private static void CheckPredicate(AnalysisContext context, Pattern predicate, PredicateSite site)
    {
        foreach(var node in BodyWalker.DescendantsAndSelf(predicate))
        {
            var (at, name, member) = node switch
            {
                PropertySubpattern { IsMemberFailed: false } subpattern => (subpattern.Node, subpattern.MemberName, subpattern.Member),
                MemberAccessExpression { Member: { } found } access => (access.Node, access.MemberName, found),
                _ => (null, "", null),
            };

            if(at is null || Storage(member) is not { Declaring: { } declaring } storage
                || !AccessDomains.Reaches(site.Owner, site.Assembly, declaring))
            {
                continue;
            }

            var accessibility = storage.Modifiers.Accessibility;

            if(!AccessDomains.Admits(site.Owner, site.Assembly, accessibility, declaring))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.InaccessibleMember, at, [],
                    name, accessibility.ToString().ToLowerInvariant(), AccessDomains.Where(accessibility));
            }
            else if(!Declarations.IsAtLeast(accessibility, site.Reach))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.InconsistentAccessibility, at, [],
                    storage.Kind, $"{TypeDisplay.Mist(declaring)}.{name}", site.Kind, site.Name);
            }
        }
    }

    private static (MemberModifiers Modifiers, MistType? Declaring, string Kind)? Storage(LookupResult? member)
    {
        return member switch
        {
            FieldResult found => (found.Field.Modifiers, found.Field.DeclaringType ?? AccessDomains.Definition(found.DeclaringType), "field"),
            PropertyResult found => (found.Property.Modifiers, found.Property.DeclaringType ?? AccessDomains.Definition(found.DeclaringType), "property"),
            _ => null,
        };
    }

    private static void JudgeCounted(AnalysisContext context, object use, SyntaxNode at, IReadOnlyList<Expression> about, IReadOnlyList<object> called)
    {
        foreach(var member in called)
        {
            switch(member)
            {
                case Property property:
                    Judge(context, use, at, about, property.Name, property.Modifiers, property.DeclaringType);
                    break;

                case Indexer indexer:
                    Judge(context, use, at, about, "this[]", indexer.Modifiers, indexer.DeclaringType);
                    break;

                case Method method:
                    Judge(context, use, at, about, method.Name, method.Modifiers, method.DeclaringType);
                    break;
            }
        }
    }

    private static void Judge(
        AnalysisContext context, object use, SyntaxNode at, IReadOnlyList<Expression> about, string name, MemberModifiers modifiers, MistType? declaring)
    {
        if(declaring is not null && AccessDomains.Reaches(context, use, declaring)
            && !AccessDomains.Admits(context, use, modifiers.Accessibility, declaring))
        {
            context.Report(
                ProgramAnalysisDiagnostics.InaccessibleMember, at, about,
                name, modifiers.Accessibility.ToString().ToLowerInvariant(), AccessDomains.Where(modifiers.Accessibility));
        }
    }

    private static void CheckExposure(AnalysisContext context, MistType type)
    {
        var typeAccess = Declarations.Effective(type);
        var typeNode = type is DeclaredType declared ? Declarations.NodeOf(declared) : null;

        switch(type)
        {
            case TypeAlias { AliasedType: { } aliased }:
                Expose(context, type, typeNode, "aliased type", aliased, typeAccess, "type alias", type.Name);
                return;

            case Newtype { SourceType: { } source }:
                Expose(context, type, typeNode, "source type", source, typeAccess, "newtype", type.Name);
                return;

            case MemberType member:
                CheckMemberExposure(context, member, typeAccess, typeNode);
                return;
        }
    }

    private static void CheckMemberExposure(AnalysisContext context, MemberType type, Accessibility typeAccess, SyntaxNode? typeNode)
    {
        if(type is ConcreteType { BaseType: { } baseType })
        {
            Expose(context, type, typeNode, "base type", baseType, typeAccess, "type", type.Name);
        }

        foreach(var method in type.EnumerateDelegates())
        {
            var access = Declarations.Narrowest(method.Modifiers.Accessibility, typeAccess);
            var kind = method is Constructor ? "constructor" : "method";

            if(method is not Constructor)
            {
                Expose(context, type, method.DeclarationNode, "return type", method.ReturnType, access, kind, method.Name);
            }

            foreach(var parameter in method.ParametersList)
            {
                Expose(context, type, method.DeclarationNode, "parameter type", parameter.Type, access, kind, method.Name);
            }
        }

        foreach(var property in type.PropertiesList)
        {
            Expose(context, type, property.DeclarationNode, "property type", property.Type, Declarations.Narrowest(property.Modifiers.Accessibility, typeAccess), "property", property.Name);
        }

        foreach(var indexer in type.IndexersList)
        {
            Expose(context, type, indexer.DeclarationNode, "indexer type", indexer.Type, Declarations.Narrowest(indexer.Modifiers.Accessibility, typeAccess), "indexer", "this");
        }

        foreach(var field in (type as ConcreteType)?.FieldsList ?? [])
        {
            Expose(context, type, field.DeclarationNode, "field type", field.Type, Declarations.Narrowest(field.Modifiers.Accessibility, typeAccess), "field", field.Name);
        }
    }

    private static void Expose(
        AnalysisContext context, MistType declaring, SyntaxNode? at, string exposedKind, IMistType exposed, Accessibility required,
        string memberKind, string memberName)
    {
        if(at is not null && !Declarations.IsAtLeast(Declarations.Effective(exposed), required)
            && AccessDomains.Reaches(declaring, declaring.Assembly, exposed))
        {
            context.Report(
                ProgramAnalysisDiagnostics.InconsistentAccessibility, at, [],
                exposedKind, TypeDisplay.Mist(exposed), memberKind, memberName);
        }
    }
}