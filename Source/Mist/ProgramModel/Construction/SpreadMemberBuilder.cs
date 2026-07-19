using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Builds spread-generated member families (Requirement 10). A spread of a
/// type-parameter pack in a member-declaring position — <c>public TArgs... Item;</c>
/// (a field family) or <c>public TArgs... Slot { get; }</c> (a property family) — surfaces
/// an aggregate member of the value-pack tuple type <c>(TArgs...)</c> named by the stem,
/// plus the 1-based element projections <c>Stem1 .. StemN</c>.
///
/// <para>
/// This builder resolves the aggregate type and records the <see cref="SpreadMember"/>
/// family on the declaring type; the caller (<see cref="ConcreteTypeBuilder"/> for fields,
/// <see cref="PropertyBuilder"/> for properties) uses the returned aggregate type to build
/// the real aggregate member. Because the pack is symbolic at declaration time, the
/// <c>N</c> element projections are not enumerated here — they are materialized when the
/// pack binds a concrete <c>Type[]</c>, following the value-pack tuple's own positional
/// fields (see <see cref="SpreadMember"/>). This builder also reserves the stem namespace
/// and reports the two program-model spread diagnostics: <c>MIST_VAR_0008</c> for an empty
/// stem and <c>MIST_VAR_0007</c> for a stem that collides with another member or with
/// another spread stem.
/// </para>
/// </summary>
internal sealed class SpreadMemberBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;

    public SpreadMemberBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
    }

    /// <summary>
    /// True when a member's declared type is a lone pack spread <c>TArgs...</c> — the
    /// surface form of a spread-generated member. Mirrors the lone-spread test used for
    /// the value-pack tuple <c>(TArgs...)</c>: a single (non-union) type reference whose
    /// sole constituent is a <see cref="SpreadSingleTypeRefSingleTypeRefNode"/>.
    /// </summary>
    public static bool TryGetMemberSpread(TypeReferenceNode type, out SpreadSingleTypeRefSingleTypeRefNode spread)
    {
        if(type.SingleTypeRefs.Length == 0 && type.First is SpreadSingleTypeRefSingleTypeRefNode loneSpread)
        {
            spread = loneSpread;
            return true;
        }

        spread = null!;
        return false;
    }

    /// <summary>
    /// Resolves the aggregate member type <c>(TArgs...)</c> for a spread-generated member
    /// and records the <see cref="SpreadMember"/> family on <paramref name="host"/>. The
    /// aggregate and its projections take <paramref name="modifiers"/> (accessibility) and
    /// <paramref name="kind"/> (field or property) from the declaration (Requirement 10.1).
    ///
    /// Returns the aggregate tuple type for the caller to use as the aggregate member's
    /// type, or <see cref="ErrorType"/> when the family is malformed: an empty stem
    /// (<c>MIST_VAR_0008</c>, Requirement 10.4/10.5) or a spread operand that is not a pack
    /// (already diagnosed). On an error the caller skips building the aggregate member.
    /// </summary>
    public IMistType ResolveAggregateAndRecord(
        SpreadSingleTypeRefSingleTypeRefNode spread,
        string stem,
        MemberType host,
        SpreadMemberKind kind,
        MemberModifiers modifiers,
        OwnershipKind ownership,
        Storm.Runtime.SyntaxNode declarationNode)
    {
        // Requirement 10.4 / 10.5: a spread-generated member requires a non-empty stem.
        // The grammar mandates an identifier, so this guards the error-recovery case where
        // a missing name yields a synthetic empty lexeme.
        if(string.IsNullOrEmpty(stem))
        {
            context.Diagnostics.ReportAtNode(VariadicsDiagnostics.EmptySpreadStem, declarationNode, null);
            return ErrorType.Instance;
        }

        var pack = typeResolver.ResolveSpreadOperandPack(spread, host.Scope);
        if(pack is null)
        {
            // The operand did not resolve to a pack; the diagnostic (if any) was already
            // reported by the resolver. Do not record a family for a malformed spread.
            return ErrorType.Instance;
        }

        // The aggregate is the whole value-pack tuple `(TArgs...)`. While the pack is
        // symbolic this carries a single pack element; binding re-interns it into an
        // N-element tuple, from which the element projections Stem1..StemN follow.
        var aggregateType = typeResolver.BuildValuePackTuple(pack);
        host.SpreadMembersList.Add(new SpreadMember(stem, pack, aggregateType, kind, modifiers, ownership));
        return aggregateType;
    }

    /// <summary>
    /// Reserves the stem namespace across a declaring type's members and reports
    /// <c>MIST_VAR_0007</c> on a collision (Requirement 10.2 / 10.3). The spread owns the
    /// aggregate name <c>S</c> and every projection name <c>S{i}</c>, so a collision is:
    /// a member outside the spread family whose name is the stem <c>S</c> or a projection
    /// name <c>S{i}</c>; or two spread stems that prefix one another (their reserved
    /// namespaces would overlap). Runs after all members are resolved so the full member
    /// set is visible; walks the declaration nodes to attribute each diagnostic to the
    /// offending declaration.
    /// </summary>
    public void ValidateStems(MemberType host, IReadOnlyList<MemberDeclarationNode> members)
    {
        if(host.SpreadMembersList.Count == 0)
        {
            return;
        }

        // Partition the type's named members into spread stems (with their declaration
        // nodes for diagnostics) and the names of every other named member. A spread
        // member's stem is its own name and belongs to its family, so it is not counted
        // as a colliding "other member".
        var stems = new List<(string Stem, Storm.Runtime.SyntaxNode Node)>();
        var otherNames = new List<string>();

        foreach(var member in members)
        {
            switch(member)
            {
                case FieldMemberMemberDeclarationNode fieldMember:
                    Categorize(fieldMember.Field.Type, fieldMember.Field.Name.Lexeme, fieldMember.Field, stems, otherNames);
                    break;

                case PropertyMemberMemberDeclarationNode propertyMember:
                    var (propType, propName) = GetPropertyTypeAndName(propertyMember.Property);
                    Categorize(propType, propName, propertyMember.Property, stems, otherNames);
                    break;

                case MethodMemberMemberDeclarationNode methodMember:
                    otherNames.Add(methodMember.Method.Name.Lexeme);
                    break;

                default:
                    // Constructors, operators, cast operators, indexers, default blocks,
                    // and nested types have no simple member name that can occupy the
                    // reserved stem namespace.
                    break;
            }
        }

        for(var j = 0; j < stems.Count; j++)
        {
            var (stem, node) = stems[j];

            // A member outside the spread family that uses the stem or an `S{i}` name.
            if(otherNames.Exists(name => NameIsInStemNamespace(name, stem)))
            {
                context.Diagnostics.ReportAtNode(VariadicsDiagnostics.SpreadStemCollision, node, null, stem);
                continue;
            }

            // Two spread stems that prefix one another. Report on the later declaration so
            // the conflict is flagged exactly once, mirroring the duplicate-`params` rule.
            var prefixesEarlierStem = false;
            for(var i = 0; i < j; i++)
            {
                if(StemsPrefixOneAnother(stems[i].Stem, stem))
                {
                    prefixesEarlierStem = true;
                    break;
                }
            }

            if(prefixesEarlierStem)
            {
                context.Diagnostics.ReportAtNode(VariadicsDiagnostics.SpreadStemCollision, node, null, stem);
            }
        }
    }

    /// <summary>
    /// Classifies one member declaration: a spread-typed member contributes its stem (when
    /// non-empty; an empty stem was already rejected with <c>MIST_VAR_0008</c> and owns no
    /// namespace), while any other member contributes its name to the pool that stems are
    /// checked against.
    /// </summary>
    private static void Categorize(
        TypeReferenceNode type,
        string name,
        Storm.Runtime.SyntaxNode node,
        List<(string Stem, Storm.Runtime.SyntaxNode Node)> stems,
        List<string> otherNames)
    {
        if(TryGetMemberSpread(type, out _))
        {
            if(!string.IsNullOrEmpty(name))
            {
                stems.Add((name, node));
            }
        }
        else
        {
            otherNames.Add(name);
        }
    }

    /// <summary>
    /// Extracts the declared type and name from either property surface form (a block
    /// property or an expression-bodied property).
    /// </summary>
    private static (TypeReferenceNode Type, string Name) GetPropertyTypeAndName(PropertyDeclarationNode property)
    {
        return property switch
        {
            BlockPropertyPropertyDeclarationNode block => (block.Type, block.Name.Lexeme),
            ExpressionBodiedPropertyPropertyDeclarationNode expr => (expr.Type, expr.Name.Lexeme),
            _ => throw new InvalidOperationException($"Unrecognized property declaration node: {property.GetType().Name}"),
        };
    }

    /// <summary>
    /// True when <paramref name="name"/> falls inside the stem's reserved namespace: it is
    /// the stem itself, or the stem followed by one or more digits (a projection name
    /// <c>Stem{i}</c>).
    /// </summary>
    private static bool NameIsInStemNamespace(string name, string stem)
    {
        if(name == stem)
        {
            return true;
        }

        if(name.Length <= stem.Length || !name.StartsWith(stem, StringComparison.Ordinal))
        {
            return false;
        }

        for(var i = stem.Length; i < name.Length; i++)
        {
            if(!char.IsAsciiDigit(name[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when one stem is a textual prefix of the other (including equality). The design
    /// forbids two spread stems from prefixing one another because their <c>S{i}</c>
    /// projection namespaces would overlap.
    /// </summary>
    private static bool StemsPrefixOneAnother(string a, string b)
    {
        return a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal);
    }
}