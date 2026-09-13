using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The two accessibility questions, from the full-accessibility-model spec: whether a use sits inside
/// the member's domain -- its declaring type for private, that type or a derivation for protected, the
/// same compilation for internal, with a nested type counting as inside its enclosing one -- and whether
/// a declaration exposes a type less accessible than itself. Order 6 of the analysis register.
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
                case MemberAccessExpression { Member: FieldResult found } access:
                    Judge(context, access, access.MemberName, found.Field.Modifiers, found.Field.DeclaringType ?? Definition(found.DeclaringType));
                    break;

                case MemberAccessExpression { Member: PropertyResult found } access:
                    Judge(context, access, access.MemberName, found.Property.Modifiers, found.Property.DeclaringType ?? Definition(found.DeclaringType));
                    break;

                case InvocationExpression { Method: { DeclaringType: { } declaring } method } call:
                    Judge(context, call, method.Name, method.Modifiers, declaring);
                    break;

                case NewExpression { Constructor: { DeclaringType: { } declaring } constructor } creation:
                    Judge(context, creation, constructor.Name, constructor.Modifiers, declaring);
                    break;

                case FunctionReferenceExpression { Method: { DeclaringType: { } declaring } method } reference:
                    Judge(context, reference, method.Name, method.Modifiers, declaring);
                    break;
            }
        }

        foreach(var type in context.Types)
        {
            CheckExposure(context, type);
        }
    }

    private static void Judge(
        AnalysisContext context, Expression use, string name, MemberModifiers modifiers, MistType? declaring)
    {
        if(declaring is null)
        {
            return;
        }

        var owner = context.OwnerOf(use);

        var (admitted, where) = modifiers.Accessibility switch
        {
            Accessibility.Public => (true, ""),
            Accessibility.Private => (Encloses(declaring, owner), "within the declaring type"),
            Accessibility.Protected => (DerivesFrom(owner, declaring), "within the declaring type or a type derived from it"),
            _ => (declaring.Assembly == context.AssemblyOf(use), "within the same compilation"),
        };

        if(!admitted)
        {
            context.Report(
                ProgramAnalysisDiagnostics.InaccessibleMember, use.Node, [use],
                name, modifiers.Accessibility.ToString().ToLowerInvariant(), where);
        }
    }

    private static bool Encloses(MistType declaring, MistType? owner)
    {
        for(var type = owner; type is not null; type = type.DeclaringType)
        {
            if(ReferenceEquals(type, declaring))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFrom(MistType? owner, MistType declaring)
    {
        for(var type = owner; type is not null; type = type.DeclaringType)
        {
            for(IMistType? current = type; current is not null; current = (Definition(current) as ConcreteType)?.BaseType)
            {
                if(ReferenceEquals(Definition(current), declaring))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static MistType? Definition(IMistType? type)
    {
        return type switch
        {
            ConstructedType constructed => constructed.Definition as MistType,
            MistType mist => mist,
            _ => null,
        };
    }

    private static void CheckExposure(AnalysisContext context, MistType type)
    {
        var typeAccess = Declarations.Effective(type);
        var typeNode = type is DeclaredType declared ? Declarations.NodeOf(declared) : null;

        switch(type)
        {
            case TypeAlias { AliasedType: { } aliased }:
                Expose(context, typeNode, "aliased type", aliased, typeAccess, "type alias", type.Name);
                return;

            case Newtype { SourceType: { } source }:
                Expose(context, typeNode, "source type", source, typeAccess, "newtype", type.Name);
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
            Expose(context, typeNode, "base type", baseType, typeAccess, "type", type.Name);
        }

        foreach(var method in type.EnumerateDelegates())
        {
            var access = Narrowest(method.Modifiers.Accessibility, typeAccess);
            var kind = method is Constructor ? "constructor" : "method";

            if(method is not Constructor)
            {
                Expose(context, method.DeclarationNode, "return type", method.ReturnType, access, kind, method.Name);
            }

            foreach(var parameter in method.ParametersList)
            {
                Expose(context, method.DeclarationNode, "parameter type", parameter.Type, access, kind, method.Name);
            }
        }

        foreach(var property in type.PropertiesList)
        {
            Expose(context, property.DeclarationNode, "property type", property.Type, Narrowest(property.Modifiers.Accessibility, typeAccess), "property", property.Name);
        }

        foreach(var indexer in type.IndexersList)
        {
            Expose(context, indexer.DeclarationNode, "indexer type", indexer.Type, Narrowest(indexer.Modifiers.Accessibility, typeAccess), "indexer", "this");
        }

        foreach(var field in (type as ConcreteType)?.FieldsList ?? [])
        {
            Expose(context, field.DeclarationNode, "field type", field.Type, Narrowest(field.Modifiers.Accessibility, typeAccess), "field", field.Name);
        }
    }

    private static void Expose(
        AnalysisContext context, SyntaxNode? at, string exposedKind, IMistType exposed, Accessibility required, string memberKind, string memberName)
    {
        if(at is not null && !Declarations.IsAtLeast(Declarations.Effective(exposed), required))
        {
            context.Report(
                ProgramAnalysisDiagnostics.InconsistentAccessibility, at, [],
                exposedKind, TypeDisplay.Mist(exposed), memberKind, memberName);
        }
    }

    private static Accessibility Narrowest(Accessibility member, Accessibility type)
    {
        return Declarations.IsAtLeast(member, type) ? type : Declarations.IsAtLeast(type, member) ? member : Accessibility.Private;
    }
}