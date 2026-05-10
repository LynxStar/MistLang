using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// Builds an immutable <see cref="SemanticModel"/> from a set of ASTs.
/// Walks ASTs to build symbols, scopes, and types, then freezes all mutable
/// builders before returning the immutable model.
/// </summary>
public sealed class SemanticModelBuilder : ISemanticModelBuilder
{
    // ── Overloading diagnostic IDs ───────────────────────────────────────
    internal static readonly DiagnosticId DuplicateMethodSignature = new("MIST_SM_0020");
    internal static readonly DiagnosticId DuplicateConstructorSignature = new("MIST_SM_0021");
    internal static readonly DiagnosticId MissingInterfaceOverload = new("MIST_SM_0022");

    // ── Extension member lookup context ──────────────────────────────────
    // Set before Pass 2 type resolution and cleared after. Allows
    // LookupMemberType to perform extension member fallback without
    // threading the registry through the entire deep call chain.
    [ThreadStatic]
    private static ExtensionRegistry? _extensionRegistryContext;

    // ── Target type context for empty array literal inference ─────────────
    // Set before resolving a typed variable declaration's initializer so that
    // EmptyArrayLiteralExpressionPrimaryNode can infer its element type from
    // the declared array type. Cleared immediately after resolution.
    [ThreadStatic]
    private static IType? _emptyArrayTargetType;

    // ── Intrinsic-marker diagnostic IDs (Task 9, Requirements 5.3, 5.6, 5.13, 5.15) ──
    /// <summary>
    /// An intrinsic member was declared with a body. Per Requirement 5.3,
    /// intrinsic members must terminate with <c>;</c> because their body is
    /// supplied by the backend's <c>IntrinsicEmissionTable</c>.
    /// </summary>
    internal static readonly DiagnosticId IntrinsicMemberHasBody = new("MIST_SB_0030");

    /// <summary>
    /// The <c>intrinsic</c> modifier (at either type or member level) was
    /// used in a non-stdlib compilation unit. Per Requirements 5.6 and 5.13,
    /// the modifier is reserved for the Standard Library.
    /// </summary>
    internal static readonly DiagnosticId IntrinsicReservedForStdlib = new("MIST_SB_0031");

    /// <summary>
    /// A member-level <c>intrinsic</c> appeared inside a type that does not
    /// itself carry the type-level <c>intrinsic</c> marker. Per Requirement
    /// 5.15, intrinsic members are always hosted on Backend_Backed_Types.
    /// </summary>
    internal static readonly DiagnosticId MemberIntrinsicRequiresTypeIntrinsic = new("MIST_SB_0032");

    // ── Operator-shape diagnostic IDs (Task 12, Requirements 4.2, 4.7, 4.8, 4.9) ──
    /// <summary>
    /// An <c>operator</c> declaration appeared outside a class or struct body
    /// (Requirement 4.7). NOTE: the grammar already forbids operator
    /// declarations outside a class/struct body (see <c>MemberDeclaration</c>
    /// rule in <c>Mist.storm</c>), so this diagnostic is a defensive
    /// safety net that currently has no triggerable path from parsed source.
    /// It is registered here so the diagnostic ID is allocated and the
    /// shape-validation logic would cover a future grammar change that
    /// admitted top-level operator declarations.
    /// </summary>
    internal static readonly DiagnosticId OperatorOutsideTypeBody = new("MIST_SB_0033");

    /// <summary>
    /// An <c>operator</c> declaration's parameter count does not match the
    /// arity required by its operator symbol (Requirements 4.8 &amp; 4.9).
    /// Binary operators require exactly 2 parameters; unary operators require
    /// exactly 1. The <c>-</c> symbol is overloaded binary/unary; its arity
    /// is disambiguated by declared parameter count (2 = binary, 1 = unary;
    /// anything else selects the closest-fitting error message).
    /// </summary>
    internal static readonly DiagnosticId OperatorArityMismatch = new("MIST_SB_0034");

    /// <summary>
    /// An <c>operator</c> declaration is missing the required
    /// <c>public static</c> modifier combination (Requirement 4.2).
    /// Every operator must be declared <c>public static</c> in v1; any
    /// deviation (missing either modifier, or adding <c>private</c>) fires
    /// this diagnostic.
    /// </summary>
    internal static readonly DiagnosticId OperatorMustBePublicStatic = new("MIST_SB_0035");

    // ── Cast-operator-shape diagnostic IDs (Task 3.2, Requirements 6.1, 6.3, 6.4, 6.5) ──

    /// <summary>
    /// A cast operator declaration has a parameter count other than 1
    /// (Requirement 6.3, 6.4). Cast operators must have exactly one parameter.
    /// </summary>
    internal static readonly DiagnosticId CastOperatorArityMismatch = new("MIST_SB_0040");

    /// <summary>
    /// A cast operator declaration is missing the required
    /// <c>public static</c> modifier combination (Requirement 6.1).
    /// </summary>
    internal static readonly DiagnosticId CastOperatorMustBePublicStatic = new("MIST_SB_0041");

    /// <summary>
    /// A cast operator declaration appeared outside a class or struct body
    /// (Requirement 6.1). Like <see cref="OperatorOutsideTypeBody"/>, this
    /// is currently unreachable from parsed source because the grammar
    /// restricts cast operator declarations to <c>MemberDeclaration</c>.
    /// Registered defensively.
    /// </summary>
    internal static readonly DiagnosticId CastOperatorOutsideTypeBody = new("MIST_SB_0042");

    /// <summary>
    /// Two cast operators in the same type have the same kind (both implicit
    /// or both explicit) and the same target type name (Requirement 6.5).
    /// This is a name-based duplicate check; full type-based duplicate
    /// detection is deferred to the type checker.
    /// </summary>
    internal static readonly DiagnosticId DuplicateCastOperator = new("MIST_SB_0043");

    // ── Storage-ban diagnostic IDs (unknown-type Task 11, Requirements 5.1–5.3, 7.5–7.6, 16.1) ──

    /// <summary>
    /// A field declaration has a declared type that is storage-banned under
    /// the <c>unknown-type</c> rules (Requirement 5.1). Fires when the
    /// declared type is <see cref="UnknownType"/> directly, or transitively
    /// contains it through <see cref="ArrayType"/> / <see cref="ConstructedType"/>
    /// arguments (Requirements 7.5, 7.6). Detection is performed via
    /// <see cref="Types.TypeContainment.IsStorageBanned"/> in
    /// <see cref="ResolveFieldDeclaration"/>, immediately after the field
    /// type is resolved in Pass 2. The diagnostic message names the
    /// declaring type and the field (Requirement 16.1).
    /// </summary>
    internal static readonly DiagnosticId UnknownAsFieldType = new("MIST_SB_0050");

    /// <summary>
    /// A property declaration has a declared type that is storage-banned
    /// under the <c>unknown-type</c> rules (Requirement 5.2). Like
    /// <see cref="UnknownAsFieldType"/>, fires on bare
    /// <see cref="UnknownType"/> and on transitive containment through
    /// arrays and constructed generics (Requirements 7.5, 7.6). Detection
    /// runs immediately after the property type is resolved in Pass 2
    /// (for both block and expression-bodied properties). The diagnostic
    /// message names the declaring type and the property (Requirement 16.1).
    /// </summary>
    internal static readonly DiagnosticId UnknownAsPropertyType = new("MIST_SB_0051");

    /// <summary>
    /// An indexer declaration has an element type (the indexer's declared
    /// return type) that is storage-banned under the <c>unknown-type</c>
    /// rules (Requirement 5.3). Like the field and property variants, fires
    /// on bare <see cref="UnknownType"/> and transitive containment
    /// (Requirements 7.5, 7.6). Detection runs immediately after the
    /// indexer element type is resolved in Pass 2. The diagnostic message
    /// names the declaring type and the indexer (Requirement 16.1).
    /// </summary>
    internal static readonly DiagnosticId UnknownAsIndexerElementType = new("MIST_SB_0052");

    /// <summary>
    /// A <c>new T(...)</c> object-creation expression constructs a generic
    /// type whose substituted type contains <see cref="UnknownType"/> at one
    /// or more positions — e.g., <c>new List&lt;unknown&gt;()</c> or
    /// <c>new Map&lt;unknown, int&gt;()</c> (Requirement 5.7). The
    /// constructed type satisfies <see cref="Types.TypeContainment.IsStorageBanned"/>
    /// because each constructor stores its parameters into T-typed slots,
    /// which would require persisting an <c>unknown</c>-typed value. The
    /// diagnostic message names the generic type and the position(s) where
    /// <c>unknown</c> appears (Requirement 16.2). Detection is performed by
    /// <see cref="Builtin.TypeCheckerAnalyzer"/> during
    /// <c>CheckObjectCreation</c>; the ID is declared here alongside the
    /// other storage-ban IDs so the whole cascade lives in one place.
    /// </summary>
    internal static readonly DiagnosticId UnknownInGenericConstruction = new("MIST_SB_0053");

    /// <summary>
    /// A class, struct, interface, or trait declaration names
    /// <see cref="UnknownType"/> in its base clause or interface list
    /// (Requirements 6.1, 6.2, 6.3, 6.4). <c>UnknownType</c> is the
    /// user-facing top type and is not an inheritable or implementable
    /// shape: it declares no members (Requirement 3.4) and does not
    /// participate in inheritance relationships (Requirement 3.5).
    /// </summary>
    /// <remarks>
    /// In practice the grammar rejects this form before Pass 1b runs —
    /// <c>BaseTypeRef = name:IDENTIFIER typeArgs:TypeArgumentList?</c>
    /// requires an <c>IDENTIFIER</c> token, and <c>unknown</c> is a
    /// reserved keyword that lexes to <c>UNKNOWN</c>, not
    /// <c>IDENTIFIER</c>. Traits have no base-clause production at all
    /// (<c>TraitDeclaration = TRAIT IDENTIFIER LEFT_BRACE ... RIGHT_BRACE</c>),
    /// so <c>trait T : unknown { }</c> also fails at parse time. The
    /// check is retained as a defensive guard for programmatically
    /// constructed ASTs and for documentation alignment with
    /// Requirements 6.1–6.4 and 16.6. When triggered, the offending
    /// base/interface reference is dropped from the declaring type's
    /// base/interface list so subsequent passes see a consistent model.
    /// </remarks>
    internal static readonly DiagnosticId UnknownAsBaseType = new("MIST_SB_0054");

    /// <summary>
    /// A <c>new T[n]</c> array-creation expression uses
    /// <see cref="UnknownType"/> as the array element type — e.g.,
    /// <c>new unknown[5]</c> (Requirement 5.5). The storage ban applies
    /// because every array slot would hold an <c>unknown</c>-typed value,
    /// and array elements are writable storage. Detection is performed by
    /// <see cref="Builtin.TypeCheckerAnalyzer"/> during
    /// <c>CheckArrayCreation</c>; the ID is declared here alongside the
    /// other storage-ban IDs.
    /// </summary>
    internal static readonly DiagnosticId UnknownAsArrayElement = new("MIST_SB_0055");

    /// <summary>
    /// A <c>new T(...)</c> object-creation expression attempts to
    /// instantiate <see cref="UnknownType"/> directly — <c>new unknown(...)</c>
    /// (Requirement 5.4). <c>UnknownType</c> has no declared members
    /// (Requirement 3.4) and no constructor, so the operation is
    /// categorically forbidden. Detection is performed by
    /// <see cref="Builtin.TypeCheckerAnalyzer"/> during
    /// <c>CheckObjectCreation</c>.
    /// </summary>
    /// <remarks>
    /// In practice the grammar rejects this form before the type checker
    /// runs — <c>ObjectCreationExpression</c> requires a
    /// <c>QualifiedIdentifier</c>, and <c>unknown</c> is a reserved keyword
    /// that lexes to <c>UNKNOWN</c>, not <c>IDENTIFIER</c>. The check is
    /// retained as a defensive guard for programmatically-constructed ASTs
    /// and for documentation alignment with Requirement 5.4.
    /// </remarks>
    internal static readonly DiagnosticId UnknownCannotBeInstantiated = new("MIST_SB_0056");

    /// <summary>
    /// A <c>default(T)</c> expression or a bare <c>default</c> whose
    /// inferred context type is <see cref="UnknownType"/> — e.g.,
    /// <c>default(unknown)</c> or <c>unknown x = default;</c>
    /// (Requirement 5.6). <c>UnknownType</c> has no default value because
    /// it has no declared members and no associated runtime representation
    /// beyond C# <c>object</c>; producing a default would materialize a
    /// specific value (<c>null</c>) whose static type is <c>unknown</c>,
    /// violating the storage ban on freshly-materialized <c>unknown</c>
    /// values. Detection is performed by
    /// <see cref="Builtin.TypeCheckerAnalyzer"/> inside
    /// <c>ValidateDefaultableType</c>, before the interface/trait/type-parameter
    /// checks, so both explicit <c>default(unknown)</c> and bare
    /// <c>default</c> inferred to <c>unknown</c> fire.
    /// </summary>
    internal static readonly DiagnosticId UnknownHasNoDefault = new("MIST_SB_0057");

    // ── Type alias diagnostic IDs (Task 5.3, Requirements 4.1–4.4, 5.4) ──

    /// <summary>
    /// A type alias forms a cycle (e.g., <c>type A = B; type B = A;</c>).
    /// Detected during Pass 2 resolution using a visited-set. The alias is
    /// assigned <see cref="Types.ErrorType"/> to prevent infinite loops.
    /// </summary>
    internal static readonly DiagnosticId CyclicTypeAlias = new("MIST_SB_0060");

    /// <summary>
    /// A type alias name is already defined in the same scope (Requirement 4.3).
    /// </summary>
    internal static readonly DiagnosticId DuplicateTypeAlias = new("MIST_SB_0061");

    /// <summary>
    /// The target type in a type alias declaration could not be resolved
    /// (Requirement 4.4).
    /// </summary>
    internal static readonly DiagnosticId UnresolvedTypeAliasTarget = new("MIST_SB_0062");

    /// <summary>
    /// A refinement predicate is incompatible with the base type (e.g., relational
    /// patterns like <c>&gt;= 18</c> applied to a non-comparable type like <c>string</c>).
    /// </summary>
    internal static readonly DiagnosticId IncompatibleRefinementPredicate = new("MIST_TC_0072");

    /// <summary>
    /// A newtype name is already defined in the same scope (Requirement 3.3).
    /// </summary>
    internal static readonly DiagnosticId DuplicateNewtype = new("MIST_SB_0063");

    /// <summary>
    /// The source type in a newtype declaration could not be resolved (Requirement 3.4).
    /// </summary>
    internal static readonly DiagnosticId UnresolvedNewtypeSource = new("MIST_SB_0064");

    // ── Trait with-block diagnostic IDs (Task 4.1, Requirements 6.1–6.9) ──

    /// <summary>
    /// The type name in a <c>with</c> block could not be resolved to a
    /// declared class or struct (Requirement 6.2).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUndefinedType = new("MIST_TRAIT_WITH_UNDEFINED_TYPE");

    /// <summary>
    /// The trait name in a <c>with</c> block could not be resolved to a
    /// declared trait (Requirement 6.3).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUndefinedTrait = new("MIST_TRAIT_WITH_UNDEFINED_TRAIT");

    /// <summary>
    /// A duplicate <c>with</c> block was found for the same type-trait pair
    /// (Requirement 6.9).
    /// </summary>
    internal static readonly DiagnosticId TraitDuplicateWith = new("MIST_TRAIT_DUPLICATE_WITH");

    /// <summary>
    /// A member mapping in a <c>with</c> block references a source member
    /// that does not exist as a public member on the type (Requirement 6.4).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUndefinedSource = new("MIST_TRAIT_WITH_UNDEFINED_SOURCE");

    /// <summary>
    /// A member mapping in a <c>with</c> block references a target member
    /// that does not exist in the trait (Requirement 6.5).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUndefinedTarget = new("MIST_TRAIT_WITH_UNDEFINED_TARGET");

    /// <summary>
    /// A member mapping in a <c>with</c> block maps a source member to a
    /// target trait member with an incompatible type signature (Requirement 6.6).
    /// </summary>
    internal static readonly DiagnosticId TraitWithTypeMismatch = new("MIST_TRAIT_WITH_TYPE_MISMATCH");

    // ── Trait satisfaction diagnostic IDs (Task 5.1, Requirements 5.1–5.7, 6.7–6.8) ──

    /// <summary>
    /// A private member on a type matches a trait member by name, but trait
    /// matching requires public visibility (Requirement 5.7).
    /// </summary>
    internal static readonly DiagnosticId TraitMemberVisibility = new("MIST_TRAIT_MEMBER_VISIBILITY");

    /// <summary>
    /// A <c>with</c> block exists for a type-trait pair but does not cover all
    /// trait members that are not structurally matched (Requirement 6.8).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUnsatisfied = new("MIST_TRAIT_WITH_UNSATISFIED");

    // ── Extension block diagnostic IDs (Task 5.3, Requirements 8.1–8.8, 9.1, 9.4, 10.1–10.3, 20.1–20.6) ──

    /// <summary>
    /// The receiver type reference in an extension block could not be resolved
    /// to a declared type (Requirement 8.4).
    /// </summary>
    internal static readonly DiagnosticId ExtensionReceiverUnresolved = new("MIST_SB_0070");

    /// <summary>
    /// An extension property declares a <c>set</c> or <c>init</c> accessor.
    /// Extension properties are read-only (Requirement 20.3).
    /// </summary>
    internal static readonly DiagnosticId ExtensionPropertyHasSetAccessor = new("MIST_SB_0071");

    /// <summary>
    /// An extension block contains an invalid member declaration. Only methods
    /// and properties are allowed inside extension blocks (Requirement 20.2).
    /// </summary>
    internal static readonly DiagnosticId ExtensionInvalidMember = new("MIST_SB_0072");

    /// <summary>
    /// Two extension blocks for the same receiver type declare members with identical
    /// signatures (Requirement 8.9, 20.4).
    /// </summary>
    internal static readonly DiagnosticId ExtensionDuplicateMember = new("MIST_SB_0073");

    /// <summary>
    /// The <c>void</c> type cannot be used as an extension receiver type
    /// (Requirement 20.6).
    /// </summary>
    internal static readonly DiagnosticId ExtensionVoidReceiver = new("MIST_SB_0074");

    /// <summary>
    /// A static extension member body references the receiver parameter.
    /// Static members cannot access the receiver instance (Requirements 3.3, 13.4).
    /// </summary>
    internal static readonly DiagnosticId ExtensionStaticReceiverAccess = new("MIST_TC_0080");

    /// <summary>
    /// A method parameter name conflicts with the receiver parameter name,
    /// creating ambiguity in the method body (Requirement 20.5).
    /// </summary>
    internal static readonly DiagnosticId ExtensionParameterConflictsWithReceiver = new("MIST_TC_0081");

    /// <summary>
    /// A member declaration inside an extension block has the same name as the
    /// receiver parameter, which shadows it and may cause confusion
    /// (Requirements 10.4, 20.5).
    /// </summary>
    internal static readonly DiagnosticId ExtensionMemberShadowsReceiver = new("MIST_TC_0083");

    // ── Constraint diagnostic IDs (generic-constraints spec, Requirements 3.1–3.9) ──

    /// <summary>
    /// A <c>where</c> clause references a type parameter name that is not
    /// defined on the declaring type or method (Requirement 3.2).
    /// </summary>
    internal static readonly DiagnosticId ConstraintUndefinedTypeParam = new("MIST_CONSTRAINT_0001");

    /// <summary>
    /// Multiple <c>where</c> clauses target the same type parameter on a
    /// single declaration (Requirement 3.9).
    /// </summary>
    internal static readonly DiagnosticId ConstraintDuplicateWhereClause = new("MIST_CONSTRAINT_0007");

    /// <summary>
    /// The 'class' and 'struct' constraints are mutually exclusive on the same
    /// type parameter (Requirement 4.1).
    /// </summary>
    internal static readonly DiagnosticId ConstraintClassStructMutualExclusion = new("MIST_CONSTRAINT_0002");

    /// <summary>
    /// The 'new()' constraint is redundant when 'struct' is present because all
    /// structs have a parameterless constructor (Requirement 4.2).
    /// </summary>
    internal static readonly DiagnosticId ConstraintStructNewRedundant = new("MIST_CONSTRAINT_0003");

    /// <summary>
    /// At most one base class constraint is allowed on a type parameter
    /// (Requirement 4.3).
    /// </summary>
    internal static readonly DiagnosticId ConstraintMultipleBaseClasses = new("MIST_CONSTRAINT_0004");

    /// <summary>
    /// A struct type cannot be used as a base class constraint
    /// (Requirement 4.4).
    /// </summary>
    internal static readonly DiagnosticId ConstraintStructAsBaseClass = new("MIST_CONSTRAINT_0005");

    /// <summary>
    /// A base class constraint is incompatible with the 'struct' constraint
    /// (Requirement 4.5).
    /// </summary>
    internal static readonly DiagnosticId ConstraintBaseClassStructIncompatible = new("MIST_CONSTRAINT_0006");

    /// <summary>
    /// A constraint type reference could not be resolved to a known type
    /// (Requirement 3.4).
    /// </summary>
    internal static readonly DiagnosticId ConstraintUnresolvableType = new("MIST_CONSTRAINT_0008");

    // ── Enum diagnostic IDs (Task 3.6, Requirements 3.5, 3.6, 3.7, 5.2) ──

    /// <summary>
    /// An enum member's value expression is not assignable to the explicitly
    /// annotated underlying type (Requirement 3.5). Fires during Pass 2 when
    /// the member value type does not match the declared type annotation.
    /// </summary>
    internal static readonly DiagnosticId EnumMemberValueNotAssignableToExplicitType = new("MIST_SB_0080");

    /// <summary>
    /// An enum member's value expression is not assignable to the inferred
    /// underlying type (Requirement 3.7). Fires during Pass 2 when the
    /// member value type does not conform to the type inferred from other
    /// members.
    /// </summary>
    internal static readonly DiagnosticId EnumMemberValueNotAssignableToInferredType = new("MIST_SB_0081");

    /// <summary>
    /// An expression accesses a member name that does not exist on the enum
    /// (Requirement 5.2). Fires when <c>EnumName.InvalidMember</c> is used
    /// and <c>InvalidMember</c> is not a declared member of the enum.
    /// </summary>
    internal static readonly DiagnosticId UndefinedEnumMemberAccess = new("MIST_SB_0082");

    /// <summary>
    /// A value of the underlying type is assigned to a variable of the enum
    /// type (Requirement 7.3). Reverse conversion from underlying type to
    /// enum type is not supported; only enum-to-underlying is implicit.
    /// </summary>
    internal static readonly DiagnosticId CannotAssignUnderlyingToEnum = new("MIST_SB_0083");

    /// <summary>
    /// An enum declaration uses a name that is already defined in the same
    /// scope (Requirement 5.2 — duplicate enum name). Fires during Pass 1
    /// when a second enum with the same name is registered.
    /// </summary>
    internal static readonly DiagnosticId DuplicateEnumName = new("MIST_SB_0084");

    // ── Dependent type parameter diagnostic IDs (Requirements 4.1–4.6, 6.1, 6.4) ──

    /// <summary>
    /// A type annotation used as a dependent type parameter is not in the
    /// allowed set {int, uint, long, ulong, short, ushort, byte, sbyte, bool, char}.
    /// The parameter is still registered with <see cref="Types.ErrorType"/> as its
    /// value type to allow downstream analysis to continue without cascading errors.
    /// </summary>
    internal static readonly DiagnosticId DependentTypeDisallowedAnnotation = new("MIST_DT_0001");

    /// <summary>
    /// A dependent type parameter name conflicts with a member declared in the
    /// same type. The dependent parameter takes precedence in scope resolution
    /// (consistent with how type parameters shadow members in C#), but the
    /// diagnostic warns the user to rename one or the other.
    /// </summary>
    internal static readonly DiagnosticId DependentTypeParameterNameCollision = new("MIST_DT_0005");

    /// <summary>
    /// A type with dependent parameters is used without providing the required
    /// dependent type arguments. Also used for duplicate dependent parameter names
    /// within the same type parameter list.
    /// </summary>
    internal static readonly DiagnosticId DependentTypeMissingArguments = new("MIST_DT_0006");

    // ── Adhoc refinement constraint diagnostic IDs (Requirements 4.4, 4.5, 4.6) ──

    /// <summary>
    /// An adhoc refinement constraint references a name that is not a dependent
    /// type parameter or value parameter on the declaration (Requirement 4.4).
    /// </summary>
    internal static readonly DiagnosticId RefinementConstraintUnknownName = new("MIST_REFINEMENT_0001");

    /// <summary>
    /// An adhoc refinement constraint targets a regular type parameter instead of
    /// a dependent type parameter or value parameter (Requirement 4.5).
    /// </summary>
    internal static readonly DiagnosticId RefinementConstraintOnRegularTypeParam = new("MIST_REFINEMENT_0002");

    /// <summary>
    /// Duplicate adhoc refinement constraint on the same name within a single
    /// declaration (Requirement 4.6).
    /// </summary>
    internal static readonly DiagnosticId RefinementConstraintDuplicate = new("MIST_REFINEMENT_0003");

    /// <summary>
    /// The set of primitive type keywords allowed as dependent type parameter annotations.
    /// </summary>
    private static readonly HashSet<string> AllowedDependentTypeAnnotations = new(StringComparer.Ordinal)
    {
        "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "bool", "char",
    };

    public ISemanticModel Build(
        MistCompilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        IAmbientTypeResolver? ambientTypeResolver,
        string namespaceRoot,
        string projectFolder,
        DiagnosticBag diagnostics,
        ImmutableHashSet<string>? directReferencePaths = null)
    {
        var globalScope = new Scope(parent: null);
        var transitiveScope = new Scope(parent: null);
        var symbolTable = new SymbolTable();

        // (Task 49: PrimitiveTypeRegistry and PrimitiveType have been
        // deleted. No migration-window registry reset is needed.)

        // Pass 1: Register user-defined types.
        // Walk each AST to discover namespaces, types, and members.
        // Units from transitive-only references are registered in a separate
        // scope that is not searched during name resolution for the user
        // project, enforcing transitive visibility rules (Requirements 6.1,
        // 6.2, 6.4).
        var withDeclarations = new List<WithDeclarationNode>();
        var extensionBlockScopes = new Dictionary<ExtensionBlockDeclarationNode, (Scope Scope, string? Namespace, SourceText? SourceText)>();
        for (var i = 0; i < asts.Length; i++)
        {
            var ast = asts[i];
            var sourceText = i < compilation.Units.Length ? compilation.Units[i].Source : null;
            var unit = i < compilation.Units.Length ? compilation.Units[i] : null;

            // Each unit can carry its own namespace root and project folder
            // (e.g., stdlib units use "Standard" and the Standard/ directory).
            // Fall back to the driver-level values for user units.
            var effectiveNamespaceRoot = unit?.NamespaceRoot ?? namespaceRoot;
            var effectiveProjectFolder = unit?.ProjectFolder ?? projectFolder;

            // Determine whether this unit is directly visible to the user
            // project. A unit is directly visible if:
            //   - OriginProjectPath is null (stdlib or user's own code), OR
            //   - OriginProjectPath is in the set of direct reference paths.
            // Units from transitive-only references are registered in a
            // separate scope so their symbols are not found during name
            // resolution for the user project.
            var isTransitiveOnly = directReferencePaths is not null
                && unit?.OriginProjectPath is not null
                && !directReferencePaths.Contains(unit.OriginProjectPath);

            var targetScope = isTransitiveOnly ? transitiveScope : globalScope;

            try
            {
                Pass1_RegisterTypes(ast, targetScope, symbolTable, sourceText, effectiveNamespaceRoot, effectiveProjectFolder, diagnostics, withDeclarations, extensionBlockScopes);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_SEMANTIC_ERROR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Error during semantic model construction: {ex.Message}",
                    Location: default,
                    CompilationUnitPath: "<unknown>",
                    AnalyzerName: null));
            }
        }

        // Pass 1e: Validate intrinsic markers (Task 9).
        // Walks every registered type and member to emit diagnostics for:
        //   * MIST_SB_0030 — intrinsic member with a body (Requirement 5.3).
        //   * MIST_SB_0031 — 'intrinsic' modifier used in a non-stdlib unit
        //     (Requirements 5.6, 5.13). Until Task 26 lands
        //     CompilationUnit.IsStdlib, every unit is treated as non-stdlib,
        //     so any 'intrinsic' in the semantic model currently fires this.
        //   * MIST_SB_0032 — member-level 'intrinsic' inside a non-intrinsic
        //     containing type (Requirement 5.15).
        Pass1e_ValidateIntrinsicMarkers(asts, symbolTable, compilation, diagnostics);

        // Pass 1e (cont.): Validate operator-declaration shape (Task 12).
        // Walks every operator symbol registered on class/struct member scopes
        // to emit diagnostics for:
        //   * MIST_SB_0033 — operator declared outside a class/struct body
        //     (Requirement 4.7). Unreachable from parsed source because the
        //     grammar already restricts OperatorDeclaration to MemberDeclaration;
        //     registered defensively.
        //   * MIST_SB_0034 — operator parameter count mismatches its required
        //     arity (Requirements 4.8, 4.9).
        //   * MIST_SB_0035 — operator not declared `public static`
        //     (Requirement 4.2).
        Pass1e_ValidateOperatorShape(asts, symbolTable, compilation, diagnostics);

        // Pass 1e (cont.): Validate cast-operator-declaration shape.
        // Walks every cast operator symbol registered on class/struct member
        // scopes to emit diagnostics for:
        //   * MIST_SB_0040 — cast operator parameter count is not 1
        //     (Requirements 6.3, 6.4).
        //   * MIST_SB_0041 — cast operator not declared `public static`
        //     (Requirement 6.1).
        //   * MIST_SB_0042 — cast operator declared outside a class/struct
        //     body (Requirement 6.1). Defensive; unreachable from parsed source.
        //   * MIST_SB_0043 — duplicate cast operator (same kind + same target
        //     type name) in the same type (Requirement 6.5).
        Pass1e_ValidateCastOperatorShape(asts, symbolTable, compilation, diagnostics);

        // (Task 49: PrimitiveTypeRegistry has been deleted. Stdlib types
        // are resolved directly via ResolvePrimitiveKeyword in Pass 2.)

        // Pass 1b: Resolve base types for class inheritance.
        // This runs after all types are registered so that forward references
        // to base classes are supported.
        Pass1_ResolveBaseTypes(asts, globalScope, symbolTable, compilation, diagnostics);

        // Pass 1b (cont.): Resolve interface lists on classes, structs, and interfaces.
        // After base types are resolved, resolve interface names on all types.
        // For classes, the first name in the base clause may be a base class
        // (already resolved above) or an interface; this pass handles the
        // interface case and any additional interface names.
        Pass1_ResolveInterfaceLists(symbolTable, asts, compilation, diagnostics);

        // Pass 1b (cont.): Resolve constraint clauses on type/method declarations.
        // After base types and interface lists are resolved, resolve constraint
        // type references and store them on TypeParameterType.Constraints.
        // Emits MIST_CONSTRAINT_0001 for undefined type parameter references,
        // MIST_CONSTRAINT_0007 for duplicate where clauses, and
        // MIST_CONSTRAINT_0008 for unresolvable constraint types.
        var parameterRefinements = new Dictionary<ISymbol, RefinementPredicate>();
        Pass1_ResolveConstraints(symbolTable, asts, compilation, diagnostics);

        // Pass 1b (cont.): Resolve trait parent references.
        // After all types are registered and interface lists are resolved,
        // resolve the inheritance clause on each trait declaration. This
        // validates that parents are traits, checks arity, constructs
        // ConstructedTraitType for generic parents, and detects duplicates.
        Pass1_ResolveTraitParents(symbolTable, asts, compilation, diagnostics);

        // Pass 1b (cont.): Detect trait inheritance cycles.
        // After parent trait references are resolved, perform DFS on the
        // trait inheritance graph to detect cycles. Traits involved in
        // cycles are marked as cyclic and excluded from satisfaction
        // computation.
        Pass1_DetectTraitCycles(symbolTable, asts, compilation, diagnostics);

        // Pass 1g: Resolve with-block declarations (Task 4.1).
        // After all types are registered and interface lists are resolved,
        // validate with-block type/trait references and member mappings.
        // Stores validated mappings in a WithBlockRegistry for use by the
        // trait satisfaction pass.
        var withBlockRegistry = new WithBlockRegistry();
        Pass1_ResolveWithBlocks(withDeclarations, globalScope, symbolTable, withBlockRegistry, asts, compilation, diagnostics);

        // Pass 1g (cont.): Resolve trait satisfaction (Task 5.1).
        // After with blocks are resolved, compute structural matches and
        // combine with with-block mappings to determine which types satisfy
        // which traits. Results are stored in a TraitSatisfactionMap.
        var traitSatisfactionMap = new TraitSatisfactionMap();
        Pass1_ResolveTraitSatisfaction(symbolTable, globalScope, withBlockRegistry, traitSatisfactionMap, asts, compilation, diagnostics);

        // Pass 1c: Detect circular interface inheritance chains.
        // After interface lists are resolved, walk each interface's Interfaces
        // chain to detect cycles (including self-inheritance). Cycles are
        // broken by removing the back-edge from the Interfaces list.
        Pass1_DetectInterfaceCycles(symbolTable, asts, compilation, diagnostics);

        // Pass 1c (cont.): Detect circular class inheritance chains.
        // After all base types are resolved, walk each class's BaseType chain
        // to detect cycles (including self-inheritance). Cycles are broken by
        // setting BaseType = null on the class that closes the cycle.
        Pass1_DetectCycles(symbolTable, asts, compilation, diagnostics);

        // Pass 1d: Validate interface satisfaction.
        // After cycles are broken, check that each class and struct
        // implementing interfaces provides all required methods.
        Pass1_ValidateInterfaceSatisfaction(symbolTable, asts, compilation, diagnostics);

        // Pass 1f: Check that base class constructor requirements are satisfied.
        // If a base class has explicit constructors that require parameters,
        // the derived class must provide a constructor with `: base(args)`.
        Pass1_CheckBaseConstructorSatisfied(symbolTable, asts, compilation, diagnostics);

        // Upgrade the ambient type resolver's stdlib provider so that
        // lazy type factories (e.g., Math.Abs returning Int32) reference
        // the real stdlib IType instances registered in Pass 1 instead of
        // the FallbackStdlibTypeProvider singletons (which have empty
        // member scopes and therefore no operator members).
        if (ambientTypeResolver is Backends.CSharpTranspiler.CSharpAmbientTypeResolver csharpResolver)
        {
            csharpResolver.UpdateStdlibProvider(
                new ScopeBasedStdlibTypeProvider(globalScope, symbolTable));
        }

        // Pass 2e (Phase A): Resolve extension block receiver types and register
        // member signatures in the ExtensionRegistry. This runs BEFORE Pass 2 so
        // that extension members are available during member lookup fallback.
        // Only receiver types and member signatures are resolved here; method
        // bodies are resolved in Phase B (after Pass 2) because they may
        // reference expressions that require full type resolution.
        var extensionRegistry = new ExtensionRegistry();
        Pass2_RegisterExtensionMembers(extensionBlockScopes, globalScope, symbolTable, ambientTypeResolver, extensionRegistry, asts, compilation, diagnostics);

        // Pass 2: Resolve types and build expression types.
        // Set the extension registry context so LookupMemberType can perform
        // extension member fallback without threading the registry through
        // the entire deep call chain.
        _extensionRegistryContext = extensionRegistry;
        var expressionTypes = new Dictionary<Storm.Runtime.SyntaxNode, IType>();
        for (var i = 0; i < asts.Length; i++)
        {
            var ast = asts[i];
            var sourceText = i < compilation.Units.Length ? compilation.Units[i].Source : null;

            try
            {
                Pass2_ResolveTypes(ast, globalScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_SEMANTIC_ERROR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Error during type resolution: {ex.Message}",
                    Location: default,
                    CompilationUnitPath: "<unknown>",
                    AnalyzerName: null));
            }
        }
        _extensionRegistryContext = null;

        // Pass 2e (Phase B): Resolve extension block method/property bodies.
        // This runs after Pass 2 so that expression types are available for
        // body resolution.
        Pass2_ResolveExtensionBodies(extensionBlockScopes, globalScope, symbolTable, expressionTypes, ambientTypeResolver, asts, compilation, diagnostics);

        // Post-Pass 2: Resolve adhoc refinement constraints.
        // This runs after Pass 2 so that parameter symbols are registered and
        // can be looked up by name. Attaches predicates to dependent type
        // parameters and stores parameter refinements in the lookup map.
        Pass2_ResolveAdhocRefinementConstraints(symbolTable, asts, compilation, diagnostics, parameterRefinements);

        // Post-Pass 2: Check for duplicate extension member signatures across
        // all extension blocks for the same receiver type (Requirement 8.9, 20.4, 20.7).
        Pass2_CheckDuplicateExtensionSignatures(extensionRegistry, symbolTable, asts, compilation, diagnostics);

        // Post-Pass 2: Compute defaultability for all class and struct types.
        // This runs after type resolution because field types must be resolved
        // to determine whether they are defaultable. The pass builds a
        // dependency graph and processes types in topological order.
        ComputeDefaultability(symbolTable, diagnostics);

        // Post-Pass 2: Check for member redeclarations in derived classes.
        // This runs after type resolution so that parameter types are resolved
        // and method signatures can be compared accurately for overloading.
        Pass1_CheckMemberRedeclarations(symbolTable, asts, compilation, diagnostics);

        // Post-Pass 2: Check for duplicate method/constructor signatures.
        // This runs after type resolution so that parameter types are resolved
        // and signatures can be compared accurately.
        Pass1_CheckDuplicateSignatures(symbolTable, asts, compilation, diagnostics);

        return new SemanticModel(
            compilation,
            compilation.Units.Select(u => u).ToList(),
            asts,
            globalScope,
            symbolTable,
            expressionTypes,
            traitSatisfactionMap,
            extensionRegistry,
            parameterRefinements);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Inferred Namespace Computation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes the inferred namespace for a source file based on its location
    /// relative to the project folder.
    /// </summary>
    /// <param name="namespaceRoot">The root namespace segment (from mist.toml or project name).</param>
    /// <param name="projectFolder">The absolute path to the project folder.</param>
    /// <param name="filePath">The absolute path to the source file.</param>
    /// <returns>The inferred namespace string.</returns>
    internal static string ComputeInferredNamespace(
        string namespaceRoot, string projectFolder, string filePath)
    {
        var fileDir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(fileDir))
            return namespaceRoot;

        var relativePath = Path.GetRelativePath(projectFolder, fileDir);
        if (relativePath == "." || string.IsNullOrEmpty(relativePath))
            return namespaceRoot;

        var segments = relativePath
            .Replace(Path.DirectorySeparatorChar, '.')
            .Replace(Path.AltDirectorySeparatorChar, '.');
        return $"{namespaceRoot}.{segments}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1 — Register user-defined types
    // ═══════════════════════════════════════════════════════════════════════

    private static void Pass1_RegisterTypes(
        Storm.Runtime.SyntaxNode ast,
        Scope globalScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        string namespaceRoot,
        string projectFolder,
        DiagnosticBag diagnostics,
        List<WithDeclarationNode>? withDeclarations = null,
        Dictionary<ExtensionBlockDeclarationNode, (Scope Scope, string? Namespace, SourceText? SourceText)>? extensionBlockScopes = null)
    {
        if (ast is not CompilationUnitNode compilationUnit)
        {
            return;
        }

        // Compute the inferred namespace for this file based on its path.
        var filePath = sourceText?.Path ?? "";
        var inferredNamespace = string.IsNullOrEmpty(filePath)
            ? namespaceRoot
            : ComputeInferredNamespace(namespaceRoot, projectFolder, filePath);

        // Track the "current namespace" — initially the inferred namespace.
        var currentNamespace = inferredNamespace;
        var currentScope = GetOrCreateNamespaceScope(globalScope, symbolTable, currentNamespace, ast);

        // Iterate over TopLevelItem nodes.
        foreach (var item in compilationUnit.Items)
        {
            switch (item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                {
                    var nsDecl = nsItem.NamespaceDeclaration;
                    var declaredName = ExtractNamespaceName(nsDecl, sourceText);

                    if (nsDecl is FileScopedNamespaceNamespaceDeclarationNode fileScopedNs)
                    {
                        // File-scoped: update current namespace for subsequent types.
                        currentNamespace = declaredName;
                        currentScope = GetOrCreateNamespaceScope(globalScope, symbolTable, currentNamespace, nsDecl);

                        // Register types in the file-scoped namespace body.
                        var bodyTypes = GetNamespaceBodyTypes(nsDecl);
                        foreach (var typeDecl in bodyTypes)
                        {
                            RegisterTypeDeclaration(typeDecl, currentScope, symbolTable, currentNamespace, sourceText, diagnostics);
                        }

                        // Register type aliases in the file-scoped namespace body.
                        var bodyTypeAliases = GetNamespaceBodyTypeAliases(nsDecl);
                        foreach (var aliasDecl in bodyTypeAliases)
                        {
                            RegisterTypeAliasDeclaration(aliasDecl, currentScope, symbolTable, sourceText, diagnostics);
                        }

                        // Register newtypes in the file-scoped namespace body.
                        var bodyNewtypes = GetNamespaceBodyNewtypes(nsDecl);
                        foreach (var newtypeDecl in bodyNewtypes)
                        {
                            RegisterNewtypeDeclaration(newtypeDecl, currentScope, symbolTable, sourceText, diagnostics);
                        }

                        // Register extension blocks in the file-scoped namespace body.
                        var bodyExtensions = GetNamespaceBodyExtensions(nsDecl);
                        foreach (var extDecl in bodyExtensions)
                        {
                            RegisterExtensionBlock(extDecl, currentScope, currentNamespace, sourceText, diagnostics, extensionBlockScopes);
                        }
                    }
                    else if (nsDecl is BlockNamespaceNamespaceDeclarationNode)
                    {
                        // Block: register only enclosed types; current namespace unchanged after block.
                        var blockScope = GetOrCreateNamespaceScope(globalScope, symbolTable, declaredName, nsDecl);
                        var bodyTypes = GetNamespaceBodyTypes(nsDecl);
                        foreach (var typeDecl in bodyTypes)
                        {
                            RegisterTypeDeclaration(typeDecl, blockScope, symbolTable, declaredName, sourceText, diagnostics);
                        }

                        // Register type aliases in the block namespace body.
                        var blockTypeAliases = GetNamespaceBodyTypeAliases(nsDecl);
                        foreach (var aliasDecl in blockTypeAliases)
                        {
                            RegisterTypeAliasDeclaration(aliasDecl, blockScope, symbolTable, sourceText, diagnostics);
                        }

                        // Register newtypes in the block namespace body.
                        var blockNewtypes = GetNamespaceBodyNewtypes(nsDecl);
                        foreach (var newtypeDecl in blockNewtypes)
                        {
                            RegisterNewtypeDeclaration(newtypeDecl, blockScope, symbolTable, sourceText, diagnostics);
                        }

                        // Register extension blocks in the block namespace body.
                        var bodyExtensions = GetNamespaceBodyExtensions(nsDecl);
                        foreach (var extDecl in bodyExtensions)
                        {
                            RegisterExtensionBlock(extDecl, blockScope, declaredName, sourceText, diagnostics, extensionBlockScopes);
                        }
                    }
                    break;
                }
                case TypeItemTopLevelItemNode typeItem:
                {
                    // Register type under the current namespace.
                    RegisterTypeDeclaration(typeItem.TypeDeclaration, currentScope, symbolTable, currentNamespace, sourceText, diagnostics);
                    break;
                }
                case TypeAliasItemTopLevelItemNode typeAliasItem:
                {
                    // Register type alias under the current namespace.
                    RegisterTypeAliasDeclaration(typeAliasItem.TypeAliasDeclaration, currentScope, symbolTable, sourceText, diagnostics);
                    break;
                }
                case NewtypeItemTopLevelItemNode newtypeItem:
                {
                    // Register newtype under the current namespace.
                    RegisterNewtypeDeclaration(newtypeItem.NewtypeDeclaration, currentScope, symbolTable, sourceText, diagnostics);
                    break;
                }
                case WithItemTopLevelItemNode withItem:
                {
                    // Store the WithDeclaration AST node for later resolution.
                    // No symbols are registered at this point — with blocks don't declare types.
                    withDeclarations?.Add(withItem.WithDeclaration);
                    break;
                }
                case ExtensionItemTopLevelItemNode extensionItem:
                {
                    // Create a placeholder scope for the extension block and register
                    // type parameters (if generic). The scope is stored for Pass 2
                    // to resolve the receiver type and register member symbols.
                    RegisterExtensionBlock(extensionItem.ExtensionDeclaration, currentScope, currentNamespace, sourceText, diagnostics, extensionBlockScopes);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Gets or creates a namespace scope for the given namespace name.
    /// Reuses an existing scope if the namespace was already registered by a previous file.
    /// </summary>
    private static Scope GetOrCreateNamespaceScope(
        Scope globalScope,
        SymbolTable symbolTable,
        string namespaceName,
        Storm.Runtime.SyntaxNode declaringNode)
    {
        // Try to find an existing namespace scope.
        Scope? namespaceScope = FindExistingNamespaceScope(globalScope, symbolTable, namespaceName);

        if (namespaceScope is not null)
            return namespaceScope;

        // Also check if there's already a namespace symbol registered (even if no types yet).
        foreach (var member in globalScope.Members)
        {
            if (member.Kind == SymbolKind.Namespace && member.Name == namespaceName)
            {
                // The namespace symbol exists but FindExistingNamespaceScope didn't find a scope
                // (no types registered yet). We need to find or create the scope.
                // Look for the scope associated with this namespace symbol.
                // Since we can't directly get the scope from the symbol, create a new one
                // and it will be found next time via types registered in it.
                namespaceScope = new Scope(parent: globalScope);
                return namespaceScope;
            }
        }

        // Create a new namespace scope and register the namespace symbol.
        namespaceScope = new Scope(parent: globalScope);

        var namespaceSpan = ToMistSourceSpan(declaringNode.Span);
        var namespaceSymbol = new Symbol(
            namespaceName,
            SymbolKind.Namespace,
            SymbolOrigin.UserCode,
            globalScope,
            declaredType: null,
            namespaceSpan);

        globalScope.AddMember(namespaceSymbol);
        symbolTable.Register(namespaceSymbol, declaringNode);

        return namespaceScope;
    }

    private static void RegisterTypeDeclaration(
        TypeDeclarationNode typeDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (typeDecl)
        {
            case ClassTypeTypeDeclarationNode classTypeNode:
                RegisterClassDeclaration(classTypeNode.ClassDeclaration, parentScope, symbolTable, namespaceName, sourceText, diagnostics);
                break;
            case StructTypeTypeDeclarationNode structTypeNode:
                RegisterStructDeclaration(structTypeNode.StructDeclaration, parentScope, symbolTable, namespaceName, sourceText, diagnostics);
                break;
            case InterfaceTypeTypeDeclarationNode interfaceTypeNode:
                RegisterInterfaceDeclaration(
                    interfaceTypeNode.InterfaceDeclaration,
                    parentScope, symbolTable, namespaceName, sourceText, diagnostics);
                break;
            case TraitTypeTypeDeclarationNode traitTypeNode:
                RegisterTraitDeclaration(
                    traitTypeNode.TraitDeclaration,
                    parentScope, symbolTable, namespaceName, sourceText, diagnostics);
                break;
            case EnumTypeTypeDeclarationNode enumTypeNode:
                RegisterEnumDeclaration(
                    enumTypeNode.EnumDeclaration,
                    parentScope, symbolTable, namespaceName, sourceText, diagnostics);
                break;
        }
    }

    private static void RegisterClassDeclaration(
        ClassDeclarationNode classDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var className = classDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var isBackendBacked = HasIntrinsicModifier(classDecl.Modifiers);
        var classType = new ClassType(className, typeScope, namespaceName, isBackendBacked);

        var isReadonly = HasReadonlyModifier(classDecl.Modifiers);
        classType.IsReadonly = isReadonly;

        var classSpan = ToMistSourceSpan(classDecl.Name.Span);
        var classSymbol = new Symbol(className, SymbolKind.Class, SymbolOrigin.UserCode, parentScope, classType, classSpan);

        parentScope.AddMember(classSymbol);
        symbolTable.Register(classSymbol, classDecl);

        // Register type parameters if present.
        RegisterTypeParameters(classDecl.TypeParams, className, typeScope, classType, sourceText, diagnostics);

        foreach (var memberDecl in classDecl.Members)
        {
            RegisterMemberDeclaration(memberDecl, typeScope, symbolTable, sourceText, diagnostics);
        }

        // If the type is readonly, set all members to ImplicitReadonly.
        if (isReadonly)
        {
            SetMembersImplicitReadonly(typeScope);
        }
    }

    private static void RegisterStructDeclaration(
        StructDeclarationNode structDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var structName = structDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var isBackendBacked = HasIntrinsicModifier(structDecl.Modifiers);
        var structType = new StructType(structName, typeScope, namespaceName, isBackendBacked);

        var isReadonly = HasReadonlyModifier(structDecl.Modifiers);
        structType.IsReadonly = isReadonly;

        var structSpan = ToMistSourceSpan(structDecl.Name.Span);
        var structSymbol = new Symbol(structName, SymbolKind.Struct, SymbolOrigin.UserCode, parentScope, structType, structSpan);

        parentScope.AddMember(structSymbol);
        symbolTable.Register(structSymbol, structDecl);

        // Register type parameters if present.
        RegisterTypeParameters(structDecl.TypeParams, structName, typeScope, structType, sourceText, diagnostics);

        foreach (var memberDecl in structDecl.Members)
        {
            RegisterMemberDeclaration(memberDecl, typeScope, symbolTable, sourceText, diagnostics);
        }

        // If the type is readonly, set all members to ImplicitReadonly.
        if (isReadonly)
        {
            SetMembersImplicitReadonly(typeScope);
        }
    }

    private static void RegisterInterfaceDeclaration(
        InterfaceDeclarationNode interfaceDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var interfaceName = interfaceDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var interfaceType = new InterfaceType(interfaceName, typeScope, namespaceName);

        var isReadonly = HasReadonlyModifier(interfaceDecl.Modifiers);
        interfaceType.IsReadonly = isReadonly;

        var interfaceSpan = ToMistSourceSpan(interfaceDecl.Name.Span);
        var interfaceSymbol = new Symbol(interfaceName, SymbolKind.Interface, SymbolOrigin.UserCode, parentScope, interfaceType, interfaceSpan);

        parentScope.AddMember(interfaceSymbol);
        symbolTable.Register(interfaceSymbol, interfaceDecl);

        // Register type parameters if present.
        RegisterTypeParameters(interfaceDecl.TypeParams, interfaceName, typeScope, interfaceType, sourceText, diagnostics);

        foreach (var member in interfaceDecl.Members)
        {
            switch (member)
            {
                case InterfaceMethodMemberInterfaceMemberDeclarationNode methodMember:
                    RegisterInterfaceMethodDeclaration(methodMember.Method, typeScope, symbolTable, sourceText, diagnostics);
                    break;
                case InterfacePropertyMemberInterfaceMemberDeclarationNode propertyMember:
                    RegisterInterfacePropertyDeclaration(propertyMember.Property, typeScope, symbolTable);
                    break;
            }
        }

        // If the interface is readonly, set all members to ImplicitReadonly.
        if (isReadonly)
        {
            SetMembersImplicitReadonly(typeScope);
        }
    }

    private static void RegisterTraitDeclaration(
        TraitDeclarationNode traitDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var traitName = traitDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var traitType = new TraitType(traitName, typeScope, namespaceName);

        var isReadonly = HasReadonlyModifier(traitDecl.Modifiers);
        traitType.IsReadonly = isReadonly;

        var traitSpan = ToMistSourceSpan(traitDecl.Name.Span);
        var traitSymbol = new Symbol(traitName, SymbolKind.Trait, SymbolOrigin.UserCode, parentScope, traitType, traitSpan);

        parentScope.AddMember(traitSymbol);
        symbolTable.Register(traitSymbol, traitDecl);

        // Register type parameters if present.
        RegisterTypeParameters(traitDecl.TypeParams, traitName, typeScope, traitType, sourceText, diagnostics);

        foreach (var member in traitDecl.Members)
        {
            switch (member)
            {
                case TraitFieldMemberTraitMemberDeclarationNode fieldMember:
                {
                    var fieldDecl = fieldMember.Field;
                    var fieldName = fieldDecl.Name.Lexeme;
                    var fieldSpan = ToMistSourceSpan(fieldDecl.Name.Span);
                    var fieldSymbol = new Symbol(fieldName, SymbolKind.Field, SymbolOrigin.UserCode, typeScope, declaredType: null, fieldSpan);
                    typeScope.AddMember(fieldSymbol);
                    symbolTable.Register(fieldSymbol, fieldDecl);
                    break;
                }
                case TraitMethodMemberTraitMemberDeclarationNode methodMember:
                {
                    var methodDecl = methodMember.Method;
                    var methodName = methodDecl.Name.Lexeme;
                    var methodSpan = ToMistSourceSpan(methodDecl.Name.Span);
                    var methodSymbol = new Symbol(methodName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope, declaredType: null, methodSpan);
                    typeScope.AddMember(methodSymbol);
                    symbolTable.Register(methodSymbol, methodDecl);
                    break;
                }
                case TraitPropertyMemberTraitMemberDeclarationNode propertyMember:
                {
                    var propertyDecl = propertyMember.Property;
                    var propName = propertyDecl.Name.Lexeme;
                    var propSpan = ToMistSourceSpan(propertyDecl.Name.Span);
                    var propSymbol = new Symbol(propName, SymbolKind.Property, SymbolOrigin.UserCode, typeScope, declaredType: null, propSpan);
                    typeScope.AddMember(propSymbol);
                    symbolTable.Register(propSymbol, propertyDecl);
                    break;
                }
            }
        }

        // If the trait is readonly, set all members to ImplicitReadonly.
        if (isReadonly)
        {
            SetMembersImplicitReadonly(typeScope);
        }
    }

    private static void RegisterEnumDeclaration(
        EnumDeclarationNode enumDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var enumName = enumDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var enumType = new EnumType(enumName, typeScope, namespaceName);

        var enumSpan = ToMistSourceSpan(enumDecl.Name.Span);
        var enumSymbol = new Symbol(enumName, SymbolKind.Enum, SymbolOrigin.UserCode, parentScope, enumType, enumSpan);

        parentScope.AddMember(enumSymbol);
        symbolTable.Register(enumSymbol, enumDecl);

        // Register each enum member as a field symbol in the enum's MemberScope.
        if (enumDecl.Members is not null)
        {
            var members = new List<EnumMemberNode> { enumDecl.Members.EnumMember };
            foreach (var member in enumDecl.Members.EnumMembers)
            {
                members.Add(member);
            }

            foreach (var member in members)
            {
                var memberName = member.Name.Lexeme;
                var memberSpan = ToMistSourceSpan(member.Name.Span);
                var memberSymbol = new Symbol(memberName, SymbolKind.Field, SymbolOrigin.UserCode, typeScope, declaredType: enumType, memberSpan);
                typeScope.AddMember(memberSymbol);
                symbolTable.Register(memberSymbol, member);
            }
        }
    }

    /// <summary>
    /// Registers a type alias declaration as a symbol in the enclosing scope.
    /// Reports <see cref="DuplicateTypeAlias"/> if the name already exists in scope.
    /// </summary>
    private static void RegisterTypeAliasDeclaration(
        TypeAliasDeclarationNode aliasDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var aliasName = aliasDecl.Name.Lexeme;
        var aliasSpan = ToMistSourceSpan(aliasDecl.Name.Span);

        // Check for duplicate alias name in the same scope.
        foreach (var member in parentScope.Members)
        {
            if (member.Name == aliasName)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    DuplicateTypeAlias.Value,
                    DiagnosticSeverity.Error,
                    $"Type alias '{aliasName}' is already defined in this scope.",
                    aliasDecl,
                    sourceText));
                return;
            }
        }

        // Extract type parameters (if generic alias).
        var typeParams = ExtractTypeParameters(aliasDecl.TypeParams, aliasName, sourceText, diagnostics);

        // Create the symbol with a null DeclaredType — it will be set in Pass 2
        // after the target type reference is resolved.
        var aliasSymbol = new Symbol(
            aliasName,
            SymbolKind.TypeAlias,
            SymbolOrigin.UserCode,
            parentScope,
            declaredType: null,
            aliasSpan);

        // Store type parameters on the symbol for later resolution.
        aliasSymbol.TypeParameters = typeParams;

        parentScope.AddMember(aliasSymbol);
        symbolTable.Register(aliasSymbol, aliasDecl);
    }

    /// <summary>
    /// Registers a newtype declaration as a symbol in the enclosing scope.
    /// Reports <see cref="DuplicateNewtype"/> if the name already exists in scope.
    /// The <c>DeclaredType</c> is left null — it will be set in Pass 2 after
    /// the source type reference is resolved and members are cloned.
    /// </summary>
    private static void RegisterNewtypeDeclaration(
        NewtypeDeclarationNode newtypeDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var name = newtypeDecl.Name.Lexeme;
        var span = ToMistSourceSpan(newtypeDecl.Name.Span);

        // Check for duplicate name in the same scope.
        foreach (var member in parentScope.Members)
        {
            if (member.Name == name)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    DuplicateNewtype.Value,
                    DiagnosticSeverity.Error,
                    $"Type '{name}' is already defined in this scope.",
                    newtypeDecl,
                    sourceText));
                return;
            }
        }

        // Create the symbol with a null DeclaredType — it will be set in Pass 2
        // after the source type reference is resolved and members are cloned.
        var newtypeSymbol = new Symbol(
            name,
            SymbolKind.Newtype,
            SymbolOrigin.UserCode,
            parentScope,
            declaredType: null,
            span);

        parentScope.AddMember(newtypeSymbol);
        symbolTable.Register(newtypeSymbol, newtypeDecl);
    }

    /// <summary>
    /// Creates a placeholder scope for an extension block and registers its type
    /// parameters (if generic). The scope is stored in the dictionary so Pass 2
    /// can resolve the receiver type and register member symbols.
    /// </summary>
    private static void RegisterExtensionBlock(
        ExtensionBlockDeclarationNode extDecl,
        Scope parentScope,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics,
        Dictionary<ExtensionBlockDeclarationNode, (Scope Scope, string? Namespace, SourceText? SourceText)>? extensionBlockScopes)
    {
        // Create a child scope for the extension block.
        var extensionScope = new Scope(parent: parentScope);

        // Register type parameters (if generic) in the extension block's scope.
        if (extDecl.TypeParams is not null)
        {
            var typeParams = ExtractTypeParameters(extDecl.TypeParams, enclosingTypeName: null, sourceText, diagnostics);
            foreach (var tp in typeParams)
            {
                var (tpName, tpOrdinal) = GetTypeParameterNameAndOrdinal(tp);
                var tpSpan = GetTypeParameterSpan(extDecl.TypeParams, tpOrdinal);
                var tpSymbol = new Symbol(tpName, SymbolKind.Class, SymbolOrigin.UserCode, extensionScope, tp, tpSpan);
                extensionScope.AddMember(tpSymbol);
            }
        }

        // Store the scope so Pass 2 can find it later.
        extensionBlockScopes?.TryAdd(extDecl, (extensionScope, namespaceName, sourceText));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 2e (Phase A) — Register extension member signatures
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves extension block receiver types, registers receiver parameters,
    /// registers member symbols with their declared types, and populates the
    /// <see cref="ExtensionRegistry"/>. Does NOT resolve method/property bodies
    /// (that happens in Phase B after Pass 2).
    /// </summary>
    private static void Pass2_RegisterExtensionMembers(
        Dictionary<ExtensionBlockDeclarationNode, (Scope Scope, string? Namespace, SourceText? SourceText)> extensionBlockScopes,
        Scope globalScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        ExtensionRegistry extensionRegistry,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var (extDecl, (extensionScope, namespaceName, sourceText)) in extensionBlockScopes)
        {

            // Create a file scope for type resolution that includes the
            // extension block's parent scope (which has namespace context).
            var fileScope = extensionScope.Parent is Scope parentScope
                ? parentScope
                : globalScope;

            // Resolve the receiver type reference.
            var receiverType = ResolveTypeReference(
                extDecl.ReceiverType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

            // If resolution fails (ErrorType), emit MIST_SB_0070 and skip.
            if (receiverType is ErrorType)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    ExtensionReceiverUnresolved.Value,
                    DiagnosticSeverity.Error,
                    "Extension block receiver type cannot be resolved.",
                    extDecl.ReceiverType,
                    sourceText));
                continue;
            }

            // If receiver type is void, emit MIST_SB_0074 and skip.
            if (receiverType.IsVoid())
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    ExtensionVoidReceiver.Value,
                    DiagnosticSeverity.Error,
                    "'void' cannot be used as an extension receiver type.",
                    extDecl.ReceiverType,
                    sourceText));
                continue;
            }

            // Register a parameter symbol for the receiver in the extension block's scope.
            var receiverName = extDecl.ReceiverName.Lexeme;
            var receiverSpan = ToMistSourceSpan(extDecl.ReceiverName.Span);
            var receiverSymbol = new Symbol(
                receiverName, SymbolKind.Parameter, SymbolOrigin.UserCode,
                extensionScope, receiverType, receiverSpan);
            extensionScope.AddMember(receiverSymbol);
            symbolTable.Register(receiverSymbol, extDecl);

            // Create a member scope for the extension block's members.
            var memberScope = new Scope(parent: extensionScope);

            // Walk member declarations — register symbols and resolve types (but not bodies).
            foreach (var member in extDecl.Members)
            {
                switch (member)
                {
                    case ExtensionMethodMemberExtensionMemberDeclarationNode methodMember:
                    {
                        var methodDecl = methodMember.Method;
                        var methodName = methodDecl.Name.Lexeme;
                        var methodSpan = ToMistSourceSpan(methodDecl.Name.Span);
                        var hasBody = methodDecl.Body is not MethodNoBodyMethodBodyNode;
                        var methodSymbol = new Symbol(
                            methodName, SymbolKind.Method, SymbolOrigin.UserCode,
                            memberScope, declaredType: null, methodSpan, hasBody: hasBody);
                        memberScope.AddMember(methodSymbol);
                        symbolTable.Register(methodSymbol, methodDecl);

                        // Warn if the method name shadows the receiver parameter name.
                        if (string.Equals(methodName, receiverName, StringComparison.Ordinal))
                        {
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                ExtensionMemberShadowsReceiver.Value,
                                DiagnosticSeverity.Warning,
                                $"Extension method '{methodName}' shadows the receiver parameter name '{receiverName}'.",
                                methodDecl,
                                sourceText));
                        }

                        // Emit error if any method parameter name conflicts with the receiver parameter name.
                        if (methodDecl.Parameters is not null)
                        {
                            foreach (var child in methodDecl.Parameters.Children())
                            {
                                if (child is ParameterNode param &&
                                    string.Equals(param.Name.Lexeme, receiverName, StringComparison.Ordinal))
                                {
                                    diagnostics.Add(MakeDiagnosticWithLocation(
                                        ExtensionParameterConflictsWithReceiver.Value,
                                        DiagnosticSeverity.Error,
                                        $"Method parameter '{param.Name.Lexeme}' conflicts with the receiver parameter name '{receiverName}'.",
                                        param,
                                        sourceText));
                                }
                            }
                        }

                        // Register method type parameters if present.
                        RegisterMethodTypeParameters(methodDecl.TypeParams, methodSymbol, memberScope, sourceText, diagnostics);

                        // Resolve the return type.
                        var returnType = ResolveTypeReference(
                            methodDecl.ReturnType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                        methodSymbol.DeclaredType = returnType;

                        // Register parameters.
                        if (methodDecl.Parameters is not null)
                        {
                            var methodScope = new Scope(parent: memberScope);
                            RegisterParameters(methodDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                        }
                        break;
                    }
                    case ExtensionPropertyMemberExtensionMemberDeclarationNode propertyMember:
                    {
                        var propertyDecl = propertyMember.Property;

                        // Check for set/init accessors — reject with MIST_SB_0071.
                        if (HasSetOrInitAccessor(propertyDecl))
                        {
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                ExtensionPropertyHasSetAccessor.Value,
                                DiagnosticSeverity.Error,
                                "Extension properties cannot declare a 'set' or 'init' accessor; extension properties are read-only.",
                                propertyDecl switch
                                {
                                    BlockPropertyPropertyDeclarationNode bp => (Storm.Runtime.SyntaxNode)bp,
                                    ExpressionBodiedPropertyPropertyDeclarationNode ep => ep,
                                    _ => member
                                },
                                sourceText));
                        }

                        // Register the property symbol.
                        var (propName, propSpan) = propertyDecl switch
                        {
                            BlockPropertyPropertyDeclarationNode bp => (bp.Name.Lexeme, ToMistSourceSpan(bp.Name.Span)),
                            ExpressionBodiedPropertyPropertyDeclarationNode ep => (ep.Name.Lexeme, ToMistSourceSpan(ep.Name.Span)),
                            _ => ("?", (Common.Text.SourceSpan?)null)
                        };
                        var propSymbol = new Symbol(
                            propName, SymbolKind.Property, SymbolOrigin.UserCode,
                            memberScope, declaredType: null, propSpan);
                        memberScope.AddMember(propSymbol);
                        symbolTable.Register(propSymbol, propertyDecl switch
                        {
                            BlockPropertyPropertyDeclarationNode bp => (Storm.Runtime.SyntaxNode)bp,
                            ExpressionBodiedPropertyPropertyDeclarationNode ep => ep,
                            _ => member
                        });

                        // Warn if the property name shadows the receiver parameter name.
                        if (string.Equals(propName, receiverName, StringComparison.Ordinal))
                        {
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                ExtensionMemberShadowsReceiver.Value,
                                DiagnosticSeverity.Warning,
                                $"Extension property '{propName}' shadows the receiver parameter name '{receiverName}'.",
                                propertyDecl switch
                                {
                                    BlockPropertyPropertyDeclarationNode bp => (Storm.Runtime.SyntaxNode)bp,
                                    ExpressionBodiedPropertyPropertyDeclarationNode ep => ep,
                                    _ => member
                                },
                                sourceText));
                        }

                        // Resolve the property type.
                        var propType = propertyDecl switch
                        {
                            BlockPropertyPropertyDeclarationNode bp =>
                                ResolveTypeReference(bp.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics),
                            ExpressionBodiedPropertyPropertyDeclarationNode ep =>
                                ResolveTypeReference(ep.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics),
                            _ => ErrorType.Instance
                        };
                        propSymbol.DeclaredType = propType;
                        break;
                    }
                    default:
                    {
                        // Invalid member (field or other) — emit MIST_SB_0072.
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            ExtensionInvalidMember.Value,
                            DiagnosticSeverity.Error,
                            "Extension blocks can only contain methods and properties.",
                            member,
                            sourceText));
                        break;
                    }
                }
            }

            // Collect type parameters from the extension block's scope.
            var typeParameters = new List<TypeParameterType>();
            foreach (var sym in extensionScope.Members)
            {
                if (sym.DeclaredType is TypeParameterType tp)
                {
                    typeParameters.Add(tp);
                }
            }

            // Create and register the ExtensionBlockInfo.
            var extensionBlockInfo = new ExtensionBlockInfo
            {
                ReceiverType = receiverType,
                ReceiverParameterName = receiverName,
                MemberScope = memberScope,
                TypeParameters = typeParameters,
                ContainingNamespace = namespaceName,
                DeclaringNode = extDecl,
            };
            extensionRegistry.Register(extensionBlockInfo);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 2e (Phase B) — Resolve extension block method/property bodies
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves extension block method and property bodies. Runs after Pass 2
    /// so that expression types are available for body resolution.
    /// </summary>
    private static void Pass2_ResolveExtensionBodies(
        Dictionary<ExtensionBlockDeclarationNode, (Scope Scope, string? Namespace, SourceText? SourceText)> extensionBlockScopes,
        Scope globalScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var (extDecl, (extensionScope, namespaceName, sourceText)) in extensionBlockScopes)
        {

            // Create a file scope for type resolution that includes the
            // extension block's parent scope (which has namespace context).
            var fileScope = extensionScope.Parent is Scope parentScope
                ? parentScope
                : globalScope;

            // Find the receiver type (already resolved in Phase A).
            var receiverType = ResolveTypeReference(
                extDecl.ReceiverType, fileScope, ambientTypeResolver, symbolTable, sourceText, new DiagnosticBag());

            // Skip blocks that failed receiver resolution in Phase A.
            if (receiverType is ErrorType || receiverType.IsVoid())
                continue;

            // Get the receiver parameter name for static receiver access validation.
            var receiverName = extDecl.ReceiverName.Lexeme;

            // Walk member declarations — resolve bodies only.
            foreach (var member in extDecl.Members)
            {
                switch (member)
                {
                    case ExtensionMethodMemberExtensionMemberDeclarationNode methodMember:
                    {
                        var methodDecl = methodMember.Method;
                        var isStatic = methodDecl.Modifiers.Any(m => m is StaticModifierModifierNode);

                        // Resolve the body if present.
                        if (methodDecl.Body.GetBlock() is BlockNode methodBlock)
                        {
                            var bodyScope = new Scope(parent: extensionScope);

                            // Register method parameters in the body scope so they're
                            // accessible during body resolution.
                            if (methodDecl.Parameters is not null)
                            {
                                RegisterParameters(methodDecl.Parameters, bodyScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                            }

                            ResolveBlock(methodBlock, bodyScope, fileScope, receiverType, /* isStatic */ false, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                            // Task 7.5: If this is a static extension member, check for
                            // receiver parameter references in the body → emit MIST_TC_0080.
                            if (isStatic)
                            {
                                CheckStaticExtensionReceiverAccess(methodBlock, receiverName, sourceText, diagnostics);
                            }
                        }
                        break;
                    }
                    // Properties don't have bodies that need expression resolution
                    // (expression-bodied properties are handled by the emitter).
                    case ExtensionPropertyMemberExtensionMemberDeclarationNode propertyMember:
                    {
                        var propertyDecl = propertyMember.Property;
                        var isStatic = propertyDecl switch
                        {
                            BlockPropertyPropertyDeclarationNode bp => bp.Modifiers.Any(m => m is StaticModifierModifierNode),
                            ExpressionBodiedPropertyPropertyDeclarationNode ep => ep.Modifiers.Any(m => m is StaticModifierModifierNode),
                            _ => false,
                        };

                        // Task 7.5: If this is a static extension property, check for
                        // receiver parameter references in the property body → emit MIST_TC_0080.
                        if (isStatic)
                        {
                            switch (propertyDecl)
                            {
                                case BlockPropertyPropertyDeclarationNode bp:
                                    foreach (var accessor in bp.Accessors)
                                    {
                                        CheckStaticExtensionReceiverAccess(accessor, receiverName, sourceText, diagnostics);
                                    }
                                    break;
                                case ExpressionBodiedPropertyPropertyDeclarationNode ep:
                                    CheckStaticExtensionReceiverAccess(ep.Body, receiverName, sourceText, diagnostics);
                                    break;
                            }
                        }
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Recursively walks an AST node tree looking for <see cref="IdentifierExpressionPrimaryNode"/>
    /// references that match the receiver parameter name. Emits <c>MIST_TC_0080</c> for each
    /// occurrence found. Used to validate that static extension members do not access the receiver.
    /// </summary>
    private static void CheckStaticExtensionReceiverAccess(
        Storm.Runtime.SyntaxNode node,
        string receiverName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (node is IdentifierExpressionPrimaryNode identifier &&
            string.Equals(identifier.Name.Lexeme, receiverName, StringComparison.Ordinal))
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                ExtensionStaticReceiverAccess.Value,
                DiagnosticSeverity.Error,
                $"Static extension member cannot access the receiver parameter '{receiverName}'.",
                node,
                sourceText));
            return; // Don't recurse into children of this identifier node.
        }

        foreach (var child in node.Children())
        {
            if (child is not null)
            {
                CheckStaticExtensionReceiverAccess(child, receiverName, sourceText, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks for duplicate member signatures across all extension blocks registered
    /// for the same receiver type. Emits <c>MIST_SB_0073</c> on the second occurrence.
    /// </summary>
    private static void Pass2_CheckDuplicateExtensionSignatures(
        ExtensionRegistry extensionRegistry,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var (receiverType, blocks) in extensionRegistry.AllGroups)
        {
            if (blocks.Count < 1)
                continue;

            // Collect all member symbols across all extension blocks for this receiver type.
            // Track which block each member came from for diagnostic reporting.
            var allMembers = new List<(ISymbol Symbol, ExtensionBlockInfo Block)>();
            foreach (var block in blocks)
            {
                foreach (var member in block.MemberScope.Members)
                {
                    if (member.Kind is SymbolKind.Method or SymbolKind.Property)
                    {
                        allMembers.Add((member, block));
                    }
                }
            }

            // Group by name, then check for duplicate signatures within each name group.
            var nameGroups = allMembers.GroupBy(m => m.Symbol.Name);
            foreach (var group in nameGroups)
            {
                var groupList = group.ToList();
                if (groupList.Count <= 1)
                    continue;

                // Track which members have already been reported as duplicates.
                var reported = new HashSet<int>();

                for (var i = 0; i < groupList.Count; i++)
                {
                    for (var j = i + 1; j < groupList.Count; j++)
                    {
                        if (reported.Contains(j))
                            continue;

                        var (symbolA, _) = groupList[i];
                        var (symbolB, blockB) = groupList[j];

                        // For properties, same name is sufficient for a duplicate.
                        if (symbolA.Kind == SymbolKind.Property && symbolB.Kind == SymbolKind.Property)
                        {
                            reported.Add(j);
                            EmitDuplicateExtensionMemberDiagnostic(
                                symbolB, blockB, receiverType, symbolTable, asts, compilation, diagnostics);
                            continue;
                        }

                        // For methods, compare parameter signatures.
                        if (symbolA.Kind == SymbolKind.Method && symbolB.Kind == SymbolKind.Method)
                        {
                            var sigA = ParameterSignature.Extract(symbolA, symbolTable);
                            var sigB = ParameterSignature.Extract(symbolB, symbolTable);

                            if (ParameterSignature.AreEqual(sigA, sigB))
                            {
                                reported.Add(j);
                                EmitDuplicateExtensionMemberDiagnostic(
                                    symbolB, blockB, receiverType, symbolTable, asts, compilation, diagnostics);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Emits the <c>MIST_SB_0073</c> diagnostic for a duplicate extension member.
    /// </summary>
    private static void EmitDuplicateExtensionMemberDiagnostic(
        ISymbol duplicateSymbol,
        ExtensionBlockInfo containingBlock,
        IType receiverType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var memberKind = duplicateSymbol.Kind == SymbolKind.Method ? "method" : "property";
        var message = $"Duplicate extension {memberKind} '{duplicateSymbol.Name}' for receiver type '{receiverType.DisplayName}'. " +
                      $"Another extension block for the same receiver type already declares a member with this signature.";

        var declaringNode = symbolTable.GetDeclaringNode(duplicateSymbol);
        if (declaringNode is not null)
        {
            var sourceText = FindSourceTextForNode(containingBlock.DeclaringNode, asts, compilation);
            diagnostics.Add(MakeDiagnosticWithLocation(
                ExtensionDuplicateMember.Value,
                DiagnosticSeverity.Error,
                message,
                declaringNode,
                sourceText));
        }
        else
        {
            diagnostics.Add(new Diagnostic(
                ExtensionDuplicateMember,
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                message,
                Location: default,
                CompilationUnitPath: "<unknown>",
                AnalyzerName: null));
        }
    }

    /// <summary>
    /// Checks whether a property declaration has a set or init accessor.
    /// </summary>
    private static bool HasSetOrInitAccessor(PropertyDeclarationNode propertyDecl)
    {
        switch (propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                foreach (var accessor in blockProp.Accessors)
                {
                    if (accessor is AutoSetAccessorAccessorDeclarationNode
                        or BlockSetAccessorAccessorDeclarationNode
                        or ExpressionSetAccessorAccessorDeclarationNode
                        or AutoInitAccessorAccessorDeclarationNode
                        or BlockInitAccessorAccessorDeclarationNode
                        or ExpressionInitAccessorAccessorDeclarationNode)
                    {
                        return true;
                    }
                }
                return false;
            case ExpressionBodiedPropertyPropertyDeclarationNode:
                // Expression-bodied properties are always get-only.
                return false;
            default:
                return false;
        }
    }

    private static void RegisterInterfaceMethodDeclaration(
        InterfaceMethodDeclarationNode methodDecl,
        Scope typeScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var methodName = methodDecl.Name.Lexeme;
        var methodSpan = ToMistSourceSpan(methodDecl.Name.Span);
        var methodSymbol = new Symbol(methodName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope, declaredType: null, methodSpan);
        typeScope.AddMember(methodSymbol);
        symbolTable.Register(methodSymbol, methodDecl);

        // Register method type parameters if present.
        RegisterMethodTypeParameters(methodDecl.TypeParams, methodSymbol, typeScope, sourceText, diagnostics);
    }

    /// <summary>
    /// Registers an interface property declaration as a <see cref="SymbolKind.Property"/> symbol
    /// on the interface type scope.
    /// </summary>
    private static void RegisterInterfacePropertyDeclaration(
        InterfacePropertyDeclarationNode propertyDecl,
        Scope typeScope,
        SymbolTable symbolTable)
    {
        var propName = propertyDecl.Name.Lexeme;
        var propSpan = ToMistSourceSpan(propertyDecl.Name.Span);

        var hasGet = false;
        var hasSet = false;
        AccessorInfo? getAccessor = null;
        AccessorInfo? setAccessor = null;

        foreach (var accessor in propertyDecl.Accessors)
        {
            switch (accessor)
            {
                case InterfaceGetAccessorInterfaceAccessorDeclarationNode:
                    hasGet = true;
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Auto, Modifier = null };
                    break;
                case InterfaceSetAccessorInterfaceAccessorDeclarationNode:
                    hasSet = true;
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Auto, Modifier = null };
                    break;
            }
        }

        var propertyInfo = new PropertyInfo
        {
            HasGet = hasGet,
            HasSet = hasSet,
            HasInit = false,
            IsComputed = false,
            IsStatic = false,
            IsExpressionBodied = false,
            GetAccessor = getAccessor,
            SetAccessor = setAccessor,
            InitAccessor = null,
            BackingField = null,
        };

        var propSymbol = new Symbol(propName, SymbolKind.Property, SymbolOrigin.UserCode, typeScope, declaredType: null, propSpan);
        propSymbol.PropertyMetadata = propertyInfo;
        typeScope.AddMember(propSymbol);
        symbolTable.Register(propSymbol, propertyDecl);
    }

    private static void RegisterMemberDeclaration(
        MemberDeclarationNode memberDecl,
        Scope typeScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (memberDecl)
        {
            case FieldMemberMemberDeclarationNode fieldMember:
                RegisterFieldDeclaration(fieldMember.Field, typeScope, symbolTable);
                break;
            case ConstructorMemberMemberDeclarationNode ctorMember:
                RegisterConstructorDeclaration(ctorMember.Constructor, typeScope, symbolTable);
                break;
            case MethodMemberMemberDeclarationNode methodMember:
                RegisterMethodDeclaration(methodMember.Method, typeScope, symbolTable, sourceText, diagnostics);
                break;
            case OperatorMemberMemberDeclarationNode operatorMember:
                RegisterOperatorDeclaration(operatorMember.Operator, typeScope, symbolTable);
                break;
            case PropertyMemberMemberDeclarationNode propertyMember:
                RegisterPropertyDeclaration(propertyMember.Property, typeScope, symbolTable);
                break;
            case IndexerMemberMemberDeclarationNode indexerMember:
                RegisterIndexerDeclaration(indexerMember.Indexer, typeScope, symbolTable);
                break;
            case CastOperatorMemberMemberDeclarationNode castOpMember:
                RegisterCastOperatorDeclaration(castOpMember.CastOperator, typeScope, symbolTable, sourceText);
                break;
            case DefaultBlockMemberMemberDeclarationNode defaultBlockMember:
                RegisterDefaultBlockDeclaration(defaultBlockMember, typeScope, symbolTable, sourceText, diagnostics);
                break;
        }
    }

    private static void RegisterDefaultBlockDeclaration(
        DefaultBlockMemberMemberDeclarationNode defaultBlockMember,
        Scope typeScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Find the containing ClassType or StructType whose MemberScope matches typeScope.
        IType? containingType = null;
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var memberScope = symbol.DeclaredType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope is not null && ReferenceEquals(memberScope, typeScope))
            {
                containingType = symbol.DeclaredType;
                break;
            }
        }

        if (containingType is null)
            return;

        var defaultBlockNode = defaultBlockMember.DefaultBlock;

        switch (containingType)
        {
            case ClassType classType:
                if (classType.DefaultBlockNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.DefaultDuplicateBlock.Value,
                        DiagnosticSeverity.Error,
                        $"Type '{classType.DisplayName}' declares more than one default block; only one is permitted per type.",
                        defaultBlockNode,
                        sourceText));
                    return;
                }
                classType.DefaultBlockNode = defaultBlockNode;
                classType.Defaultability = DefaultabilityStatus.Explicit;
                break;

            case StructType structType:
                if (structType.DefaultBlockNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.DefaultDuplicateBlock.Value,
                        DiagnosticSeverity.Error,
                        $"Type '{structType.DisplayName}' declares more than one default block; only one is permitted per type.",
                        defaultBlockNode,
                        sourceText));
                    return;
                }
                structType.DefaultBlockNode = defaultBlockNode;
                structType.Defaultability = DefaultabilityStatus.Explicit;
                break;
        }
    }

    private static void RegisterFieldDeclaration(FieldDeclarationNode fieldDecl, Scope typeScope, SymbolTable symbolTable)
    {
        var fieldName = fieldDecl.Name.Lexeme;
        var fieldSpan = ToMistSourceSpan(fieldDecl.Name.Span);
        var fieldSymbol = new Symbol(fieldName, SymbolKind.Field, SymbolOrigin.UserCode, typeScope, declaredType: null, fieldSpan);

        if (HasReadonlyModifier(fieldDecl.Modifiers))
        {
            fieldSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
        }

        typeScope.AddMember(fieldSymbol);
        symbolTable.Register(fieldSymbol, fieldDecl);
    }

    private static void RegisterConstructorDeclaration(ConstructorDeclarationNode ctorDecl, Scope typeScope, SymbolTable symbolTable)
    {
        var ctorName = ctorDecl.Name.Lexeme;
        var ctorSpan = ToMistSourceSpan(ctorDecl.Name.Span);
        var isIntrinsic = HasIntrinsicModifier(ctorDecl.Modifiers);
        var hasBody = ctorDecl.Body is not MethodNoBodyMethodBodyNode;
        var ctorSymbol = new Symbol(
            ctorName, SymbolKind.Constructor, SymbolOrigin.UserCode, typeScope,
            declaredType: null, ctorSpan,
            isIntrinsic: isIntrinsic, hasBody: hasBody);

        if (HasReadonlyModifier(ctorDecl.Modifiers))
        {
            ctorSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
        }

        typeScope.AddMember(ctorSymbol);
        symbolTable.Register(ctorSymbol, ctorDecl);
    }

    private static void RegisterMethodDeclaration(MethodDeclarationNode methodDecl, Scope typeScope, SymbolTable symbolTable, SourceText? sourceText, DiagnosticBag diagnostics)
    {
        var methodName = methodDecl.Name.Lexeme;
        var methodSpan = ToMistSourceSpan(methodDecl.Name.Span);
        var isIntrinsic = HasIntrinsicModifier(methodDecl.Modifiers);
        var hasBody = methodDecl.Body is not MethodNoBodyMethodBodyNode;
        var methodSymbol = new Symbol(
            methodName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope,
            declaredType: null, methodSpan,
            isIntrinsic: isIntrinsic, hasBody: hasBody);

        if (HasReadonlyModifier(methodDecl.Modifiers))
        {
            methodSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
        }

        typeScope.AddMember(methodSymbol);
        symbolTable.Register(methodSymbol, methodDecl);

        // Register method type parameters if present.
        RegisterMethodTypeParameters(methodDecl.TypeParams, methodSymbol, typeScope, sourceText, diagnostics);
    }

    /// <summary>
    /// Registers an operator declaration as a static method symbol on the
    /// containing type's scope. The operator's name is mangled as
    /// <c>operator&lt;symbol&gt;</c> (e.g., <c>operator+</c>) so that overload
    /// resolution in Task 14 can look it up by constructing the same key from
    /// an operator expression.
    ///
    /// Pass 1 establishes only the symbol's identity (name, kind, modifiers,
    /// body-presence). Parameter signature extraction and the operator's
    /// return type are wired up in Pass 2's <see cref="ResolveOperatorDeclaration"/>,
    /// which mirrors <see cref="ResolveMethodDeclaration"/> — operators are
    /// treated as static methods for semantic-model purposes (Requirement 4.6),
    /// and <see cref="ParameterSignature.Extract"/> reads the parameter list
    /// from the <see cref="OperatorDeclarationNode"/> just like for methods.
    /// </summary>
    private static void RegisterOperatorDeclaration(OperatorDeclarationNode opDecl, Scope typeScope, SymbolTable symbolTable)
    {
        var opLexeme = GetOperatorSymbolLexeme(opDecl.Symbol);
        var opName = "operator" + opLexeme;
        var opSpan = ToMistSourceSpan(opDecl.Symbol.Span);
        var isIntrinsic = HasIntrinsicModifier(opDecl.Modifiers);
        var hasBody = opDecl.Body is not OperatorNoBodyOperatorBodyNode;
        var opSymbol = new Symbol(
            opName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope,
            declaredType: null, opSpan,
            isIntrinsic: isIntrinsic, hasBody: hasBody);
        typeScope.AddMember(opSymbol);
        symbolTable.Register(opSymbol, opDecl);
    }

    /// <summary>
    /// Registers a cast operator declaration as a static method symbol on the
    /// containing type's scope. The operator's name is encoded as
    /// <c>operator_{kind}_{targetType}</c> (e.g., <c>operator_implicit_Weight</c>,
    /// <c>operator_explicit_string</c>) so that the type checker can look it up
    /// by constructing the same key from a cast expression or implicit conversion
    /// site.
    ///
    /// Pass 1 establishes only the symbol's identity (name, kind, modifiers,
    /// body-presence). The target type and parameter are resolved in Pass 2's
    /// <see cref="ResolveCastOperatorDeclaration"/>.
    /// </summary>
    private static void RegisterCastOperatorDeclaration(
        CastOperatorDeclarationNode castOpDecl, Scope typeScope, SymbolTable symbolTable, SourceText? sourceText = null)
    {
        // Extract the cast kind prefix.
        var kindPrefix = castOpDecl.CastKind switch
        {
            ImplicitCastCastOperatorKindNode => "implicit",
            ExplicitCastCastOperatorKindNode => "explicit",
            _ => "unknown",
        };

        // Extract the target type name from the type reference for the encoded name.
        // The full type resolution happens in Pass 2; here we use the raw lexeme.
        var targetTypeName = ExtractTypeReferenceName(castOpDecl.TargetType, sourceText);
        var opName = $"operator_{kindPrefix}_{targetTypeName}";

        var opSpan = ToMistSourceSpan(castOpDecl.CastKind.Span);
        var isIntrinsic = HasIntrinsicModifier(castOpDecl.Modifiers);
        var hasBody = castOpDecl.Body is not OperatorNoBodyOperatorBodyNode;

        var opSymbol = new Symbol(
            opName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope,
            declaredType: null, opSpan,
            isIntrinsic: isIntrinsic, hasBody: hasBody);
        typeScope.AddMember(opSymbol);
        symbolTable.Register(opSymbol, castOpDecl);
    }

    /// <summary>
    /// Extracts the raw type name from a <see cref="TypeReferenceNode"/> for use
    /// in encoded symbol names (e.g., cast operator names). For named types, this
    /// returns the IDENTIFIER lexeme; for primitive types, the keyword string
    /// (e.g., <c>"int"</c>, <c>"string"</c>). Array ranks are not included.
    /// <para>
    /// The generated parser may not populate <c>IDENTIFIER.Lexeme</c> on
    /// <see cref="QualifiedIdentifierNode"/>. When the lexeme is empty, this
    /// method falls back to extracting the name from the node's source span
    /// via the <paramref name="sourceText"/> parameter (if available).
    /// </para>
    /// </summary>
    private static string ExtractTypeReferenceName(TypeReferenceNode typeRef, SourceText? sourceText = null)
    {
        switch (typeRef)
        {
            case NamedTypeRefTypeReferenceNode namedRef:
                var lexeme = namedRef.Name.IDENTIFIER.Lexeme;
                if (!string.IsNullOrEmpty(lexeme))
                    return lexeme;
                // Fallback: extract from the QualifiedIdentifier's source span.
                if (sourceText is not null)
                {
                    var span = namedRef.Name.Span;
                    if (span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
                        return sourceText.Contents.Substring(span.Start, span.Length);
                }
                // Last resort: extract from the TypeReference's own span (covers
                // cases where the QualifiedIdentifier span is zero but the
                // TypeReference span is valid).
                {
                    var span = namedRef.Span;
                    if (sourceText is not null && span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
                    {
                        var raw = sourceText.Contents.Substring(span.Start, span.Length);
                        // Strip array rank suffix if present.
                        var bracketIdx = raw.IndexOf('[');
                        return bracketIdx >= 0 ? raw.Substring(0, bracketIdx).Trim() : raw.Trim();
                    }
                }
                return "";
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
                return FormatPrimitiveType(primitiveRef.PrimitiveType);
            case VoidTypeRefTypeReferenceNode:
                return "void";
            case TupleTypeRefTypeReferenceNode tupleRef:
                return FormatTupleTypeReference(tupleRef);
            default:
                return "unknown";
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the modifier list contains an
    /// <see cref="IntrinsicModifierModifierNode"/>. Used at type level to set
    /// <c>IsBackendBacked</c> on <see cref="ClassType"/>/<see cref="StructType"/>
    /// and at member level to set <c>IsIntrinsic</c> on <see cref="Symbol"/>.
    /// </summary>
    private static bool HasIntrinsicModifier(ImmutableArray<ModifierNode> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            if (modifier is IntrinsicModifierModifierNode)
                return true;
        }
        return false;
    }

    private static bool HasReadonlyModifier(ImmutableArray<ModifierNode> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            if (modifier is ReadonlyModifierModifierNode)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Sets all member symbols in the given type scope to <see cref="ReadonlyStatus.ImplicitReadonly"/>,
    /// unless they are already explicitly marked readonly. Called when a type is declared as readonly.
    /// </summary>
    private static void SetMembersImplicitReadonly(Scope typeScope)
    {
        foreach (var member in typeScope.Members)
        {
            if (member is Symbol symbol &&
                symbol.Kind is SymbolKind.Field or SymbolKind.Property or SymbolKind.Indexer or SymbolKind.Method or SymbolKind.Constructor)
            {
                if (symbol.ReadonlyStatus == ReadonlyStatus.Unknown)
                {
                    symbol.ReadonlyStatus = ReadonlyStatus.ImplicitReadonly;
                }

                // Also update PropertyInfo.ReadonlyStatus for properties and indexers.
                if (symbol.PropertyMetadata is { ReadonlyStatus: ReadonlyStatus.Unknown } propInfo)
                {
                    symbol.PropertyMetadata = new PropertyInfo
                    {
                        HasGet = propInfo.HasGet,
                        HasSet = propInfo.HasSet,
                        HasInit = propInfo.HasInit,
                        IsComputed = propInfo.IsComputed,
                        IsStatic = propInfo.IsStatic,
                        IsExpressionBodied = propInfo.IsExpressionBodied,
                        GetAccessor = propInfo.GetAccessor,
                        SetAccessor = propInfo.SetAccessor,
                        InitAccessor = propInfo.InitAccessor,
                        BackingField = propInfo.BackingField,
                        ReadonlyStatus = ReadonlyStatus.ImplicitReadonly,
                    };
                }
            }
        }
    }

    /// <summary>
    /// Extracts the textual lexeme of an operator symbol node
    /// (e.g., <c>"+"</c> for <see cref="PlusOpOperatorSymbolNode"/>). The
    /// lexeme becomes the suffix of the operator's symbol name
    /// (<c>"operator+"</c>) so the type checker can construct the same key
    /// from an operator expression.
    /// </summary>
    private static string GetOperatorSymbolLexeme(OperatorSymbolNode symbol) => symbol switch
    {
        PlusOpOperatorSymbolNode => "+",
        MinusOpOperatorSymbolNode => "-",
        StarOpOperatorSymbolNode => "*",
        SlashOpOperatorSymbolNode => "/",
        ModuloOpOperatorSymbolNode => "%",
        EqualEqualOpOperatorSymbolNode => "==",
        NotEqualOpOperatorSymbolNode => "!=",
        LessThanOpOperatorSymbolNode => "<",
        GreaterThanOpOperatorSymbolNode => ">",
        LessThanOrEqualOpOperatorSymbolNode => "<=",
        GreaterThanOrEqualOpOperatorSymbolNode => ">=",
        LogicalAndOpOperatorSymbolNode => "&&",
        LogicalOrOpOperatorSymbolNode => "||",
        ExclamationOpOperatorSymbolNode => "!",
        _ => "",
    };

    /// <summary>
    /// Extracts type parameter names from a <see cref="TypeParameterListNode"/> and returns
    /// them as a list of <see cref="IType"/> instances (either <see cref="TypeParameterType"/>
    /// or <see cref="DependentTypeParameterType"/>) with correct ordinals.
    /// Also checks for duplicate names and shadowing of the enclosing type name.
    /// </summary>
    private static IReadOnlyList<IType> ExtractTypeParameters(
        TypeParameterListNode? typeParamList,
        string? enclosingTypeName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (typeParamList is null)
            return Array.Empty<IType>();

        var typeParams = new List<IType>();
        var seenNames = new HashSet<string>();
        var ordinal = 0;

        // Process the first type parameter.
        ProcessTypeParameter(typeParamList.TypeParameter, ordinal++, enclosingTypeName, seenNames, typeParams, sourceText, diagnostics);

        // Process the rest of the type parameters.
        foreach (var tp in typeParamList.TypeParameters)
        {
            ProcessTypeParameter(tp, ordinal++, enclosingTypeName, seenNames, typeParams, sourceText, diagnostics);
        }

        return typeParams;
    }

    /// <summary>
    /// Processes a single type parameter: checks for duplicates and shadowing,
    /// then creates either a <see cref="TypeParameterType"/> or a
    /// <see cref="DependentTypeParameterType"/> and adds it to the list.
    /// </summary>
    private static void ProcessTypeParameter(
        TypeParameterNode node,
        int ordinal,
        string? enclosingTypeName,
        HashSet<string> seenNames,
        List<IType> typeParams,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var name = node.GetName().Lexeme;

        // Check for duplicate type parameter names.
        if (!seenNames.Add(name))
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                DiagnosticId.DuplicateTypeParameter.Value,
                DiagnosticSeverity.Error,
                $"Duplicate type parameter name '{name}'.",
                node,
                sourceText));
        }

        // Check if the type parameter shadows the enclosing type name.
        if (enclosingTypeName is not null && name == enclosingTypeName)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                DiagnosticId.TypeParameterShadowsType.Value,
                DiagnosticSeverity.Warning,
                $"Type parameter '{name}' shadows the enclosing type name.",
                node,
                sourceText));
        }

        // Determine whether this is a dependent type parameter or a regular one.
        if (node is DependentTypeParameterTypeParameterNode depNode)
        {
            var valueTypeKeyword = FormatPrimitiveType(depNode.ValueType);

            if (!AllowedDependentTypeAnnotations.Contains(valueTypeKeyword))
            {
                // Emit MIST_DT_0001 and use ErrorType as the value type.
                diagnostics.Add(MakeDiagnosticWithLocation(
                    DependentTypeDisallowedAnnotation.Value,
                    DiagnosticSeverity.Error,
                    $"Type '{valueTypeKeyword}' cannot be used as a dependent type parameter. Only primitive integral types, bool, and char are allowed.",
                    depNode,
                    sourceText));

                typeParams.Add(new DependentTypeParameterType(name, ordinal, ErrorType.Instance));
            }
            else
            {
                // Resolve the value type using the legacy primitive type mapping.
                // During Pass 1, stdlib types may not be fully registered yet,
                // so we use the LegacyPrimitiveTypes singletons directly.
                var valueType = ResolvePrimitiveType(depNode.ValueType);
                typeParams.Add(new DependentTypeParameterType(name, ordinal, valueType));
            }
        }
        else
        {
            typeParams.Add(new TypeParameterType(name, ordinal));
        }
    }

    /// <summary>
    /// Registers type parameters for a class, struct, or interface declaration.
    /// Creates <see cref="TypeParameterType"/> or <see cref="DependentTypeParameterType"/>
    /// instances, stores them on the declaring type, and registers each as a symbol
    /// in the type's member scope.
    /// </summary>
    private static void RegisterTypeParameters(
        TypeParameterListNode? typeParamList,
        string typeName,
        Scope typeScope,
        IType declaringType,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var typeParams = ExtractTypeParameters(typeParamList, typeName, sourceText, diagnostics);
        if (typeParams.Count == 0)
            return;

        // Store type parameters on the declaring type.
        switch (declaringType)
        {
            case ClassType classType:
                classType.TypeParameters = typeParams;
                break;
            case StructType structType:
                structType.TypeParameters = typeParams;
                break;
            case InterfaceType interfaceType:
                interfaceType.TypeParameters = typeParams;
                break;
            case TraitType traitType:
                traitType.TypeParameters = typeParams;
                break;
        }

        // Register each type parameter as a symbol in the type's member scope
        // so it can be resolved as a type within the type body.
        foreach (var tp in typeParams)
        {
            var (tpName, tpOrdinal) = GetTypeParameterNameAndOrdinal(tp);
            var tpSpan = GetTypeParameterSpan(typeParamList!, tpOrdinal);
            var tpSymbol = new Symbol(tpName, SymbolKind.Class, SymbolOrigin.UserCode, typeScope, tp, tpSpan);
            typeScope.AddMember(tpSymbol);
        }
    }

    /// <summary>
    /// Registers type parameters for a method declaration (both regular and interface methods).
    /// Creates <see cref="TypeParameterType"/> or <see cref="DependentTypeParameterType"/>
    /// instances, stores them on the method symbol, and registers each in the method's
    /// enclosing type scope so they can be resolved.
    /// </summary>
    private static void RegisterMethodTypeParameters(
        TypeParameterListNode? typeParamList,
        Symbol methodSymbol,
        Scope typeScope,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (typeParamList is null)
            return;

        // For method type parameters, we don't check shadowing against the enclosing type name
        // (that's only for type-level type parameters). We only check for duplicates.
        var typeParams = ExtractTypeParameters(typeParamList, enclosingTypeName: null, sourceText, diagnostics);
        if (typeParams.Count == 0)
            return;

        // Store type parameters on the method symbol.
        methodSymbol.TypeParameters = typeParams;

        // Register each type parameter as a symbol in the type scope so they
        // take precedence over enclosing type parameters during resolution.
        foreach (var tp in typeParams)
        {
            var (tpName, tpOrdinal) = GetTypeParameterNameAndOrdinal(tp);
            var tpSpan = GetTypeParameterSpan(typeParamList, tpOrdinal);
            var tpSymbol = new Symbol(tpName, SymbolKind.Class, SymbolOrigin.UserCode, typeScope, tp, tpSpan);
            typeScope.AddMember(tpSymbol);
        }
    }

    /// <summary>
    /// Extracts the name and ordinal from a type parameter (either regular or dependent).
    /// </summary>
    private static (string Name, int Ordinal) GetTypeParameterNameAndOrdinal(IType typeParam)
    {
        return typeParam switch
        {
            TypeParameterType tpt => (tpt.Name, tpt.Ordinal),
            DependentTypeParameterType dpt => (dpt.Name, dpt.Ordinal),
            _ => (typeParam.DisplayName, 0),
        };
    }

    /// <summary>
    /// Gets the source span for a type parameter at the given ordinal in a <see cref="TypeParameterListNode"/>.
    /// </summary>
    private static Common.Text.SourceSpan? GetTypeParameterSpan(TypeParameterListNode typeParamList, int ordinal)
    {
        if (ordinal == 0)
            return ToMistSourceSpan(typeParamList.TypeParameter.GetName().Span);

        var index = ordinal - 1;
        if (index < typeParamList.TypeParameters.Length)
            return ToMistSourceSpan(typeParamList.TypeParameters[index].GetName().Span);

        return null;
    }

    /// <summary>
    /// Generates implicit public static get-only property symbols for each
    /// <see cref="DependentTypeParameterType"/> on a type. Called during Pass 2
    /// (member resolution) after the type symbol is retrieved.
    /// <para>
    /// For each dependent parameter, this method:
    /// <list type="number">
    ///   <item>Checks for name collisions with existing members → emits <c>MIST_DT_0005</c>.</item>
    ///   <item>Checks for duplicate dependent parameter names → emits <c>MIST_DT_0006</c>.</item>
    ///   <item>Creates a public static get-only property symbol with the parameter's value type.</item>
    ///   <item>Registers the property in the type's member scope.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static void GenerateDependentTypeProperties(
        IReadOnlyList<IType> typeParameters,
        Scope typeScope,
        IType declaringType,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (typeParameters.Count == 0)
            return;

        var seenDependentNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tp in typeParameters)
        {
            if (tp is not DependentTypeParameterType depParam)
                continue;

            var paramName = depParam.Name;

            // Check for duplicate dependent parameter names.
            if (!seenDependentNames.Add(paramName))
            {
                diagnostics.Add(new Diagnostic(
                    DependentTypeMissingArguments,
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Duplicate dependent type parameter name '{paramName}' in type '{declaringType.DisplayName}'.",
                    Location: default,
                    CompilationUnitPath: sourceText?.Path ?? "<unknown>",
                    AnalyzerName: null));
                continue;
            }

            // Check for name collisions with existing members (fields, methods, properties).
            foreach (var member in typeScope.Members)
            {
                if (member.Name == paramName &&
                    member.Kind is SymbolKind.Field or SymbolKind.Method or SymbolKind.Property or SymbolKind.Indexer)
                {
                    diagnostics.Add(new Diagnostic(
                        DependentTypeParameterNameCollision,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"Dependent type parameter '{paramName}' conflicts with member '{paramName}' declared in type '{declaringType.DisplayName}'. Rename either the parameter or the member.",
                        Location: default,
                        CompilationUnitPath: sourceText?.Path ?? "<unknown>",
                        AnalyzerName: null));
                    break;
                }
            }

            // Generate the implicit property even if there's a collision
            // (the dependent parameter takes precedence per design doc).
            var propertyInfo = new PropertyInfo
            {
                HasGet = true,
                HasSet = false,
                HasInit = false,
                IsComputed = true,
                IsStatic = true,
                IsExpressionBodied = true,
            };

            var propSymbol = new Symbol(
                paramName,
                SymbolKind.Property,
                SymbolOrigin.UserCode,
                typeScope,
                declaredType: depParam.ValueType,
                declarationSpan: null);
            propSymbol.PropertyMetadata = propertyInfo;
            typeScope.AddMember(propSymbol);
        }
    }

    /// <summary>
    /// Registers dependent type parameter names as compile-time constants in a
    /// method's lexical scope. Called during Pass 2 for method-level dependent
    /// parameters.
    /// </summary>
    private static void RegisterMethodDependentParameters(
        IReadOnlyList<IType> typeParameters,
        Scope methodScope,
        IType enclosingType,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (typeParameters.Count == 0)
            return;

        foreach (var tp in typeParameters)
        {
            if (tp is not DependentTypeParameterType depParam)
                continue;

            // Register the dependent parameter name as a compile-time constant
            // in the method's lexical scope so it can be resolved within the method body.
            var constSymbol = new Symbol(
                depParam.Name,
                SymbolKind.Property,
                SymbolOrigin.UserCode,
                methodScope,
                declaredType: depParam.ValueType,
                declarationSpan: null);
            constSymbol.PropertyMetadata = new PropertyInfo
            {
                HasGet = true,
                HasSet = false,
                HasInit = false,
                IsComputed = true,
                IsStatic = true,
                IsExpressionBodied = true,
            };
            methodScope.AddMember(constSymbol);
        }
    }

    /// <summary>
    /// Registers a property declaration as a <see cref="SymbolKind.Property"/> symbol.
    /// Walks the accessor list to build <see cref="PropertyInfo"/>, classifies
    /// computed properties, and generates a backing field when needed.
    /// </summary>
    private static void RegisterPropertyDeclaration(PropertyDeclarationNode propertyDecl, Scope typeScope, SymbolTable symbolTable)
    {
        switch (propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                RegisterBlockProperty(blockProp, typeScope, symbolTable);
                break;
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                RegisterExpressionBodiedProperty(exprProp, typeScope, symbolTable);
                break;
        }
    }

    private static void RegisterBlockProperty(BlockPropertyPropertyDeclarationNode blockProp, Scope typeScope, SymbolTable symbolTable)
    {
        var propName = blockProp.Name.Lexeme;
        var propSpan = ToMistSourceSpan(blockProp.Name.Span);
        var isStatic = blockProp.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Walk accessor list to build PropertyInfo.
        AccessorInfo? getAccessor = null;
        AccessorInfo? setAccessor = null;
        AccessorInfo? initAccessor = null;
        var hasAutoAccessor = false;
        var usesFieldKeyword = false;

        foreach (var accessor in blockProp.Accessors)
        {
            switch (accessor)
            {
                case AutoGetAccessorAccessorDeclarationNode autoGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Auto, Modifier = autoGet.AccessorModifier };
                    hasAutoAccessor = true;
                    break;
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Block, Modifier = blockGet.AccessorModifier };
                    if (ContainsFieldKeyword(blockGet.Body)) usesFieldKeyword = true;
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Expression, Modifier = exprGet.AccessorModifier };
                    if (ContainsFieldKeywordInExpression(exprGet.Expression)) usesFieldKeyword = true;
                    break;
                case AutoSetAccessorAccessorDeclarationNode autoSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Auto, Modifier = autoSet.AccessorModifier };
                    hasAutoAccessor = true;
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Block, Modifier = blockSet.AccessorModifier };
                    if (ContainsFieldKeyword(blockSet.Body)) usesFieldKeyword = true;
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Expression, Modifier = exprSet.AccessorModifier };
                    if (ContainsFieldKeywordInExpression(exprSet.Expression)) usesFieldKeyword = true;
                    break;
                case AutoInitAccessorAccessorDeclarationNode autoInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Auto, Modifier = autoInit.AccessorModifier };
                    hasAutoAccessor = true;
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Block, Modifier = blockInit.AccessorModifier };
                    if (ContainsFieldKeyword(blockInit.Body)) usesFieldKeyword = true;
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Expression, Modifier = exprInit.AccessorModifier };
                    if (ContainsFieldKeywordInExpression(exprInit.Expression)) usesFieldKeyword = true;
                    break;
            }
        }

        var hasGet = getAccessor is not null;
        var hasSet = setAccessor is not null;
        var hasInit = initAccessor is not null;

        // Computed property classification:
        // IsComputed = HasGet && !HasSet && !HasInit && GetAccessor.BodyKind != Auto
        var isComputed = hasGet && !hasSet && !hasInit && getAccessor!.BodyKind != AccessorBodyKind.Auto;

        // Backing field generation:
        // NeedsBackingField = !IsComputed && (HasAutoAccessor || UsesFieldKeyword)
        Symbol? backingFieldSymbol = null;
        if (!isComputed && (hasAutoAccessor || usesFieldKeyword))
        {
            var backingFieldName = $"_{propName}_k__BackingField";
            var backingFieldSpan = propSpan; // Use property span for the backing field.
            backingFieldSymbol = new Symbol(backingFieldName, SymbolKind.Field, SymbolOrigin.UserCode, typeScope, declaredType: null, backingFieldSpan);
            typeScope.AddMember(backingFieldSymbol);
            symbolTable.Register(backingFieldSymbol, blockProp);
        }

        var propertyInfo = new PropertyInfo
        {
            HasGet = hasGet,
            HasSet = hasSet,
            HasInit = hasInit,
            IsComputed = isComputed,
            IsStatic = isStatic,
            IsExpressionBodied = false,
            GetAccessor = getAccessor,
            SetAccessor = setAccessor,
            InitAccessor = initAccessor,
            BackingField = backingFieldSymbol,
            ReadonlyStatus = HasReadonlyModifier(blockProp.Modifiers) ? ReadonlyStatus.ExplicitReadonly : ReadonlyStatus.Unknown,
        };

        var propSymbol = new Symbol(propName, SymbolKind.Property, SymbolOrigin.UserCode, typeScope, declaredType: null, propSpan);
        propSymbol.PropertyMetadata = propertyInfo;

        if (HasReadonlyModifier(blockProp.Modifiers))
        {
            propSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
        }

        typeScope.AddMember(propSymbol);
        symbolTable.Register(propSymbol, blockProp);
    }

    private static void RegisterExpressionBodiedProperty(ExpressionBodiedPropertyPropertyDeclarationNode exprProp, Scope typeScope, SymbolTable symbolTable)
    {
        var propName = exprProp.Name.Lexeme;
        var propSpan = ToMistSourceSpan(exprProp.Name.Span);
        var isStatic = exprProp.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Expression-bodied properties are always computed (get-only with expression body).
        var getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Expression, Modifier = null };

        var propertyInfo = new PropertyInfo
        {
            HasGet = true,
            HasSet = false,
            HasInit = false,
            IsComputed = true,
            IsStatic = isStatic,
            IsExpressionBodied = true,
            GetAccessor = getAccessor,
            SetAccessor = null,
            InitAccessor = null,
            BackingField = null,
            ReadonlyStatus = HasReadonlyModifier(exprProp.Modifiers) ? ReadonlyStatus.ExplicitReadonly : ReadonlyStatus.Unknown,
        };

        var propSymbol = new Symbol(propName, SymbolKind.Property, SymbolOrigin.UserCode, typeScope, declaredType: null, propSpan);
        propSymbol.PropertyMetadata = propertyInfo;

        if (HasReadonlyModifier(exprProp.Modifiers))
        {
            propSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
        }

        typeScope.AddMember(propSymbol);
        symbolTable.Register(propSymbol, exprProp);
    }

    /// <summary>
    /// Registers an indexer declaration as a <see cref="SymbolKind.Indexer"/> symbol.
    /// Stores parameter types and accessor info via <see cref="PropertyInfo"/>.
    /// </summary>
    private static void RegisterIndexerDeclaration(IndexerDeclarationNode indexerDecl, Scope typeScope, SymbolTable symbolTable)
    {
        if (indexerDecl is not BlockIndexerIndexerDeclarationNode blockIndexer)
            return;

        var indexerName = "this[]";
        var indexerSpan = ToMistSourceSpan(blockIndexer.THIS.Span);
        var isStatic = blockIndexer.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Walk accessor list to build PropertyInfo (same as block property).
        AccessorInfo? getAccessor = null;
        AccessorInfo? setAccessor = null;
        AccessorInfo? initAccessor = null;

        foreach (var accessor in blockIndexer.Accessors)
        {
            switch (accessor)
            {
                case AutoGetAccessorAccessorDeclarationNode autoGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Auto, Modifier = autoGet.AccessorModifier };
                    break;
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Block, Modifier = blockGet.AccessorModifier };
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Expression, Modifier = exprGet.AccessorModifier };
                    break;
                case AutoSetAccessorAccessorDeclarationNode autoSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Auto, Modifier = autoSet.AccessorModifier };
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Block, Modifier = blockSet.AccessorModifier };
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Expression, Modifier = exprSet.AccessorModifier };
                    break;
                case AutoInitAccessorAccessorDeclarationNode autoInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Auto, Modifier = autoInit.AccessorModifier };
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Block, Modifier = blockInit.AccessorModifier };
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Expression, Modifier = exprInit.AccessorModifier };
                    break;
            }
        }

        var propertyInfo = new PropertyInfo
        {
            HasGet = getAccessor is not null,
            HasSet = setAccessor is not null,
            HasInit = initAccessor is not null,
            IsComputed = false,
            IsStatic = isStatic,
            IsExpressionBodied = false,
            GetAccessor = getAccessor,
            SetAccessor = setAccessor,
            InitAccessor = initAccessor,
            BackingField = null,
            ReadonlyStatus = HasReadonlyModifier(blockIndexer.Modifiers) ? ReadonlyStatus.ExplicitReadonly : ReadonlyStatus.Unknown,
        };

        var indexerSymbol = new Symbol(indexerName, SymbolKind.Indexer, SymbolOrigin.UserCode, typeScope, declaredType: null, indexerSpan);
        indexerSymbol.PropertyMetadata = propertyInfo;

        if (HasReadonlyModifier(blockIndexer.Modifiers))
        {
            indexerSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
        }

        typeScope.AddMember(indexerSymbol);
        symbolTable.Register(indexerSymbol, blockIndexer);
    }

    /// <summary>
    /// Checks whether a block body contains a <c>field</c> keyword expression.
    /// </summary>
    private static bool ContainsFieldKeyword(BlockNode block)
    {
        foreach (var child in block.Children())
        {
            if (ContainsFieldKeywordInNode(child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether an expression contains a <c>field</c> keyword expression.
    /// </summary>
    private static bool ContainsFieldKeywordInExpression(ExpressionNode expr)
    {
        return ContainsFieldKeywordInNode(expr);
    }

    /// <summary>
    /// Recursively checks whether a syntax node tree contains a <c>field</c> keyword expression.
    /// </summary>
    private static bool ContainsFieldKeywordInNode(Storm.Runtime.SyntaxNode node)
    {
        if (node is FieldKeywordExpressionPrimaryNode)
            return true;

        foreach (var child in node.Children())
        {
            if (child is not null && ContainsFieldKeywordInNode(child))
                return true;
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1e — Validate intrinsic markers (Task 9)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates the <c>intrinsic</c> markers propagated by Pass 1 onto types
    /// and members. Emits three diagnostics:
    /// <list type="bullet">
    /// <item>
    /// <description><c>MIST_SB_0030</c> — an intrinsic member has a body
    /// (Requirement 5.3). Intrinsic members must terminate with <c>;</c>
    /// because their bodies are supplied by the backend.</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0031</c> — the <c>intrinsic</c> modifier
    /// appears in a non-stdlib compilation unit (Requirements 5.6 &amp; 5.13).
    /// Fires on both type-level and member-level misuse. Until Task 26
    /// introduces <c>CompilationUnit.IsStdlib</c>, every unit is treated as
    /// non-stdlib, so any occurrence of <c>intrinsic</c> currently triggers
    /// this diagnostic.</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0032</c> — a member-level <c>intrinsic</c> sits
    /// inside a type that is not itself declared <c>intrinsic</c>
    /// (Requirement 5.15). Intrinsic members are always hosted on
    /// Backend_Backed_Types.</description>
    /// </item>
    /// </list>
    /// </summary>
    private static void Pass1e_ValidateIntrinsicMarkers(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        SymbolTable symbolTable,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Iterate class and struct type symbols. For each, check the
        // type-level intrinsic marker (MIST_SB_0031), then walk its member
        // scope to check each member-level intrinsic marker.
        foreach (var typeSymbol in symbolTable.AllSymbols)
        {
            if (typeSymbol.Kind != SymbolKind.Class && typeSymbol.Kind != SymbolKind.Struct)
                continue;

            var containingType = typeSymbol.DeclaredType;
            if (containingType is null)
                continue;

            var isBackendBacked = containingType switch
            {
                ClassType ct => ct.IsBackendBacked,
                StructType st => st.IsBackendBacked,
                _ => false,
            };

            // MIST_SB_0031 (type-level): the 'intrinsic' modifier is reserved
            // for the Standard Library. Units loaded via StdlibLoader carry
            // IsStdlib=true; user-code units do not, so this fires only on
            // user code.
            if (isBackendBacked && !IsStdlibSymbol(typeSymbol, symbolTable, asts, compilation))
            {
                var declaringNode = symbolTable.GetDeclaringNode(typeSymbol);
                if (declaringNode is not null)
                {
                    var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        IntrinsicReservedForStdlib.Value,
                        DiagnosticSeverity.Error,
                        "The 'intrinsic' modifier is reserved for the Standard Library and cannot be used in user code.",
                        declaringNode,
                        sourceText));
                }
            }

            // Walk the type's members to check member-level intrinsic markers.
            var memberScope = containingType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };
            if (memberScope is null)
                continue;

            foreach (var member in memberScope.Members)
            {
                if (member is not Symbol memberSymbol || !memberSymbol.IsIntrinsic)
                    continue;

                // Only validate members that can carry 'intrinsic': methods,
                // constructors, and operators (operators are registered as
                // SymbolKind.Method with a mangled "operator<sym>" name).
                if (memberSymbol.Kind != SymbolKind.Method
                    && memberSymbol.Kind != SymbolKind.Constructor)
                {
                    continue;
                }

                var memberDeclaringNode = symbolTable.GetDeclaringNode(memberSymbol);
                var memberSourceText = memberDeclaringNode is null
                    ? null
                    : FindSourceTextForNode(memberDeclaringNode, asts, compilation);

                // MIST_SB_0030 — intrinsic member with a body.
                if (memberSymbol.HasBody && memberDeclaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        IntrinsicMemberHasBody.Value,
                        DiagnosticSeverity.Error,
                        $"Intrinsic member '{typeSymbol.Name}.{memberSymbol.Name}' must not have a body; use ';' to mark it as backend-provided.",
                        memberDeclaringNode,
                        memberSourceText));
                }

                // MIST_SB_0031 — member-level 'intrinsic' in user code.
                if (!IsStdlibSymbol(typeSymbol, symbolTable, asts, compilation) && memberDeclaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        IntrinsicReservedForStdlib.Value,
                        DiagnosticSeverity.Error,
                        "The 'intrinsic' modifier is reserved for the Standard Library and cannot be used in user code.",
                        memberDeclaringNode,
                        memberSourceText));
                }

                // MIST_SB_0032 — member-level 'intrinsic' inside a
                // non-intrinsic containing type (Requirement 5.15).
                if (!isBackendBacked && memberDeclaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        MemberIntrinsicRequiresTypeIntrinsic.Value,
                        DiagnosticSeverity.Error,
                        $"Member-level 'intrinsic' is only permitted inside a type that is itself declared 'intrinsic'. Type '{typeSymbol.Name}' is not intrinsic.",
                        memberDeclaringNode,
                        memberSourceText));
                }
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the type declared by <paramref name="typeSymbol"/>
    /// resides in a Standard Library compilation unit — i.e., the
    /// <see cref="CompilationUnit"/> whose AST contains the symbol's declaring
    /// node has <see cref="CompilationUnit.IsStdlib"/> set to <c>true</c>.
    /// Units loaded via <see cref="StdlibLoader"/> carry this flag; user-code
    /// units do not. Gates <c>MIST_SB_0031</c> (Requirements 5.6, 5.13) so the
    /// diagnostic fires only on user code.
    /// </summary>
    private static bool IsStdlibSymbol(
        ISymbol typeSymbol,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        // Locate the declaring AST node for this type symbol, then walk the
        // ASTs using reference-equality containment (the same mechanism that
        // powers FindCompilationUnitPathForSymbol) to find the matching
        // CompilationUnit and read its IsStdlib flag. Reference equality is
        // reliable across files; span-based matching is not (every file's
        // span starts at 0).
        var declaringNode = symbolTable.GetDeclaringNode(typeSymbol);
        if (declaringNode is null)
            return false;

        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsTypeDeclarationNode(cu, declaringNode))
            {
                return i < compilation.Units.Length && compilation.Units[i].IsStdlib;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1e — Validate operator-declaration shape (Task 12)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Binary operator symbols that require exactly 2 parameters
    /// (Requirement 4.3, 4.5). The <c>-</c> symbol is omitted here because it
    /// is overloaded binary/unary and disambiguated by parameter count below.
    /// </summary>
    private static readonly HashSet<string> BinaryOperatorSymbols = new()
    {
        "+", "*", "/", "%", "==", "!=", "<", ">", "<=", ">=", "&&", "||",
    };

    /// <summary>
    /// Unary operator symbols that require exactly 1 parameter
    /// (Requirement 4.4). The <c>-</c> symbol is omitted here because it is
    /// overloaded binary/unary.
    /// </summary>
    private static readonly HashSet<string> UnaryOperatorSymbols = new()
    {
        "!",
    };

    /// <summary>
    /// Validates the shape of every registered operator declaration and
    /// emits diagnostics for:
    /// <list type="bullet">
    /// <item>
    /// <description><c>MIST_SB_0033</c> — operator declared outside a class
    /// or struct body (Requirement 4.7). This is currently unreachable from
    /// parsed source because the grammar already restricts
    /// <c>OperatorDeclaration</c> to appear only within
    /// <c>MemberDeclaration</c>. The diagnostic ID is registered defensively
    /// so a future grammar change that admits top-level operators inherits
    /// the validation automatically.</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0034</c> — binary operator without exactly 2
    /// parameters, or unary operator without exactly 1 parameter
    /// (Requirements 4.8, 4.9). Arity rules: <c>+ * / % == != &lt; &gt; &lt;= &gt;= &amp;&amp; ||</c>
    /// are binary; <c>!</c> is unary; <c>-</c> is overloaded (2 params → binary,
    /// 1 param → unary, anything else treated as binary when count &gt; 1 or
    /// unary when count ≤ 1).</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0035</c> — operator not declared
    /// <c>public static</c> (Requirement 4.2). Both modifiers must be present
    /// in the declaration's modifier list.</description>
    /// </item>
    /// </list>
    /// </summary>
    private static void Pass1e_ValidateOperatorShape(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        SymbolTable symbolTable,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Walk every registered symbol whose declaring node is an
        // OperatorDeclarationNode. Operator symbols are registered with
        // SymbolKind.Method and a mangled "operator<sym>" name, so filter by
        // declaring-node shape rather than by name to avoid collisions with
        // user methods that happen to start with "operator".
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbolTable.GetDeclaringNode(symbol) is not OperatorDeclarationNode opDecl)
                continue;

            var sourceText = FindSourceTextForNode(opDecl, asts, compilation);
            var opLexeme = GetOperatorSymbolLexeme(opDecl.Symbol);

            // Parameter count: the ParameterList rule is
            // `Parameter (COMMA Parameter)*`, so count = 1 + Parameters.Length
            // when non-null (one for the `Parameter` head, plus each COMMA-
            // separated `Parameter` in the tail), or 0 when the list is absent.
            var paramCount = opDecl.Parameters is null
                ? 0
                : 1 + opDecl.Parameters.Parameters.Length;

            // Determine the operator's expected arity. For `-` (overloaded
            // binary/unary), disambiguate by the declared parameter count:
            // a count > 1 is diagnosed as a binary-arity mismatch (because
            // the author was likely declaring subtraction), and a count ≤ 1
            // is diagnosed as a unary-arity mismatch.
            bool isBinary;
            if (opLexeme == "-")
            {
                isBinary = paramCount > 1;
            }
            else if (BinaryOperatorSymbols.Contains(opLexeme))
            {
                isBinary = true;
            }
            else if (UnaryOperatorSymbols.Contains(opLexeme))
            {
                isBinary = false;
            }
            else
            {
                // Unknown operator symbol (grammar forbids this); skip shape
                // validation so we don't produce a misleading diagnostic.
                continue;
            }

            var expectedArity = isBinary ? 2 : 1;

            // MIST_SB_0034 — arity mismatch (Requirements 4.8 & 4.9).
            if (paramCount != expectedArity)
            {
                var message = isBinary
                    ? $"Binary operator '{opLexeme}' must declare exactly 2 parameters; found {paramCount}."
                    : $"Unary operator '{opLexeme}' must declare exactly 1 parameter; found {paramCount}.";

                diagnostics.Add(MakeDiagnosticWithLocation(
                    OperatorArityMismatch.Value,
                    DiagnosticSeverity.Error,
                    message,
                    opDecl,
                    sourceText));
            }

            // MIST_SB_0035 — operator must be declared `public static`
            // (Requirement 4.2). Both modifiers must be present.
            var hasPublic = false;
            var hasStatic = false;
            foreach (var modifier in opDecl.Modifiers)
            {
                if (modifier is PublicModifierModifierNode) hasPublic = true;
                if (modifier is StaticModifierModifierNode) hasStatic = true;
            }

            if (!hasPublic || !hasStatic)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    OperatorMustBePublicStatic.Value,
                    DiagnosticSeverity.Error,
                    $"Operator '{opLexeme}' must be declared 'public static'.",
                    opDecl,
                    sourceText));
            }

            // MIST_SB_0033 — operator declared outside a class/struct body
            // (Requirement 4.7). The grammar already restricts
            // OperatorDeclaration to MemberDeclaration (see Mist.storm), so
            // every operator reaches this pass inside a type's member scope
            // and this check is unreachable from parsed source today. It is
            // wired defensively so a future grammar change admitting top-
            // level operators would inherit validation automatically. We
            // detect a top-level operator by checking whether the symbol's
            // containing scope is the member scope of a registered class or
            // struct type. If no registered type claims this scope, the
            // operator is not inside any type body.
            if (!IsScopeInsideRegisteredType(symbol.ContainingScope, symbolTable))
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    OperatorOutsideTypeBody.Value,
                    DiagnosticSeverity.Error,
                    "Operators must be declared as 'public static' members of a class or struct.",
                    opDecl,
                    sourceText));
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="scope"/> (or any of its
    /// ancestor scopes) is the member scope of a class or struct type
    /// registered in <paramref name="symbolTable"/>. Used by
    /// <see cref="Pass1e_ValidateOperatorShape"/> as the trigger for
    /// <c>MIST_SB_0033</c>. The grammar forbids operator declarations
    /// outside a type body today, so every reachable operator symbol
    /// satisfies this condition; the helper exists to guarantee the
    /// diagnostic would still fire on a future grammar relaxation.
    /// </summary>
    private static bool IsScopeInsideRegisteredType(
        IScope? scope,
        SymbolTable symbolTable)
    {
        for (var s = scope; s is not null; s = s.Parent)
        {
            foreach (var typeSymbol in symbolTable.AllSymbols)
            {
                if (typeSymbol.Kind != SymbolKind.Class && typeSymbol.Kind != SymbolKind.Struct)
                    continue;

                var memberScope = typeSymbol.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };

                if (memberScope is not null && ReferenceEquals(memberScope, s))
                    return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1e — Validate cast-operator-declaration shape (Task 3.2)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates the shape of every registered cast operator declaration and
    /// emits diagnostics for:
    /// <list type="bullet">
    /// <item>
    /// <description><c>MIST_SB_0040</c> — cast operator parameter count is
    /// not exactly 1 (Requirements 6.3, 6.4).</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0041</c> — cast operator not declared
    /// <c>public static</c> (Requirement 6.1).</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0042</c> — cast operator declared outside a
    /// class or struct body (Requirement 6.1). Unreachable from parsed source
    /// because the grammar restricts <c>CastOperatorDeclaration</c> to
    /// <c>MemberDeclaration</c>; registered defensively.</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0043</c> — duplicate cast operator (same kind
    /// and same target type name) in the same type (Requirement 6.5).
    /// Name-based only; full type-based duplicate detection is deferred to
    /// the type checker.</description>
    /// </item>
    /// </list>
    /// </summary>
    private static void Pass1e_ValidateCastOperatorShape(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        SymbolTable symbolTable,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Track seen cast operator encoded names per containing scope for
        // duplicate detection. Key = containing scope reference, Value = set
        // of encoded names already seen (e.g., "operator_implicit_Weight").
        var seenPerScope = new Dictionary<IScope, HashSet<string>>(ReferenceEqualityComparer.Instance);

        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbolTable.GetDeclaringNode(symbol) is not CastOperatorDeclarationNode castOpDecl)
                continue;

            var sourceText = FindSourceTextForNode(castOpDecl, asts, compilation);

            // ── MIST_SB_0040: Parameter count must be exactly 1 ──────────
            // The ParameterList rule is `Parameter (COMMA Parameter)*`, so
            // count = 1 + Parameters.Length when non-null, or 0 when absent.
            var paramCount = castOpDecl.Parameters is null
                ? 0
                : 1 + castOpDecl.Parameters.Parameters.Length;

            if (paramCount != 1)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    CastOperatorArityMismatch.Value,
                    DiagnosticSeverity.Error,
                    $"Cast operator must have exactly one parameter; found {paramCount}.",
                    castOpDecl,
                    sourceText));
            }

            // ── MIST_SB_0041: Must be declared `public static` ──────────
            var hasPublic = false;
            var hasStatic = false;
            foreach (var modifier in castOpDecl.Modifiers)
            {
                if (modifier is PublicModifierModifierNode) hasPublic = true;
                if (modifier is StaticModifierModifierNode) hasStatic = true;
            }

            if (!hasPublic || !hasStatic)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    CastOperatorMustBePublicStatic.Value,
                    DiagnosticSeverity.Error,
                    "Cast operator must be declared 'public static'.",
                    castOpDecl,
                    sourceText));
            }

            // ── MIST_SB_0042: Must be inside a class or struct ──────────
            if (!IsScopeInsideRegisteredType(symbol.ContainingScope, symbolTable))
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    CastOperatorOutsideTypeBody.Value,
                    DiagnosticSeverity.Error,
                    "Cast operators must be declared inside a class or struct.",
                    castOpDecl,
                    sourceText));
            }

            // ── MIST_SB_0043: Duplicate cast operator detection ─────────
            // Two cast operators in the same type with the same encoded name
            // (same kind + same target type name) are duplicates.
            if (symbol.ContainingScope is not null)
            {
                if (!seenPerScope.TryGetValue(symbol.ContainingScope, out var seenNames))
                {
                    seenNames = new HashSet<string>();
                    seenPerScope[symbol.ContainingScope] = seenNames;
                }

                if (!seenNames.Add(symbol.Name))
                {
                    // Extract kind and type names for the diagnostic message.
                    var kindPrefix = castOpDecl.CastKind switch
                    {
                        ImplicitCastCastOperatorKindNode => "implicit",
                        ExplicitCastCastOperatorKindNode => "explicit",
                        _ => "unknown",
                    };
                    var targetTypeName = ExtractTypeReferenceName(castOpDecl.TargetType, sourceText);

                    // Extract source type name from the parameter (if present).
                    var sourceTypeName = castOpDecl.Parameters is not null
                        ? ExtractTypeReferenceName(castOpDecl.Parameters.Parameter.Type, sourceText)
                        : "?";

                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DuplicateCastOperator.Value,
                        DiagnosticSeverity.Error,
                        $"Duplicate {kindPrefix} cast operator from '{sourceTypeName}' to '{targetTypeName}'.",
                        castOpDecl,
                        sourceText));
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1b — Resolve base types for class inheritance
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After all types are registered in Pass 1, resolve base type references
    /// for classes with inheritance clauses. This runs as a separate step so
    /// that forward references to base classes are supported.
    /// </summary>
    private static void Pass1_ResolveBaseTypes(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        Scope globalScope,
        SymbolTable symbolTable,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Walk all registered symbols to find class types and their AST nodes.
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Class || symbol.DeclaredType is not ClassType classType)
            {
                continue;
            }

            var declaringNode = symbolTable.GetDeclaringNode(symbol);
            if (declaringNode is not ClassDeclarationNode classDecl)
            {
                continue;
            }

            // Check if this class has an inheritance clause.
            if (classDecl.BaseList is null)
            {
                continue;
            }

            var baseClassName = classDecl.BaseList.First.Name.Lexeme;
            var parentScope = symbol.ContainingScope;

            // Look up the base class name in the parent scope.
            var resolvedSymbol = parentScope.Lookup(baseClassName);

            if (resolvedSymbol is null)
            {
                // Determine the source text for the diagnostic.
                var sourceText = FindSourceTextForClassDecl(classDecl, asts, compilation);

                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_UNDEFINED_BASE_TYPE",
                    DiagnosticSeverity.Error,
                    $"The base type '{baseClassName}' could not be found.",
                    classDecl,
                    sourceText));
            }
            else if (resolvedSymbol.DeclaredType is StructType)
            {
                var sourceText = FindSourceTextForClassDecl(classDecl, asts, compilation);

                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_CANNOT_INHERIT_STRUCT",
                    DiagnosticSeverity.Error,
                    $"Class '{classType.DisplayName}' cannot inherit from struct '{baseClassName}'.",
                    classDecl,
                    sourceText));
            }
            else if (resolvedSymbol.DeclaredType is UnknownType)
            {
                // Defensive guard (Requirements 6.1, 16.6). The grammar
                // prevents `unknown` from appearing as the name in a
                // `BaseTypeRef` because `unknown` lexes to the reserved
                // `UNKNOWN` token, not `IDENTIFIER`. This branch exists
                // to keep the rule robust for programmatically-constructed
                // ASTs and for documentation alignment with Req 6.1. The
                // reference is dropped from the base list by simply not
                // assigning `classType.BaseType`.
                var sourceText = FindSourceTextForClassDecl(classDecl, asts, compilation);

                diagnostics.Add(MakeDiagnosticWithLocation(
                    UnknownAsBaseType.Value,
                    DiagnosticSeverity.Error,
                    $"Class '{classType.DisplayName}' cannot inherit from 'unknown'. " +
                    "'unknown' is the user-facing top type and does not participate in inheritance.",
                    classDecl,
                    sourceText));
            }
            else if (resolvedSymbol.DeclaredType is ClassType resolvedClassType)
            {
                var typeArgs = classDecl.BaseList.First.TypeArgs;
                classType.BaseType = ResolveGenericTypeReference(
                    resolvedClassType, typeArgs, baseClassName,
                    classType, classDecl, asts, compilation, diagnostics);
            }
        }
    }

    /// <summary>
    /// Resolves a type reference in an inheritance context (base clause or interface list),
    /// handling type arguments to produce a <see cref="ConstructedType"/> when appropriate.
    /// Emits diagnostics for arity mismatches, non-generic type arguments, and missing type arguments.
    /// </summary>
    private static IType ResolveGenericTypeReference(
        IType resolvedType,
        TypeArgumentListNode? typeArgs,
        string typeName,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Get the type parameters from the resolved type.
        var typeParams = GetTypeParameters(resolvedType);
        var hasTypeArgs = typeArgs is not null;
        var typeParamCount = typeParams.Count;

        if (hasTypeArgs)
        {
            // Type arguments were provided.
            if (typeParamCount == 0)
            {
                // The referenced type is not generic — emit MIST_NOT_GENERIC.
                var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                diagnostics.Add(MakeDiagnosticWithLocation(
                    DiagnosticId.NotGeneric.Value,
                    DiagnosticSeverity.Error,
                    $"Type '{typeName}' is not generic and does not accept type arguments.",
                    declaringNode,
                    sourceText));
                return resolvedType;
            }

            // Check if the resolved type has any dependent type parameters.
            // If so, use the dependent-aware resolution path that handles
            // literal type arguments and forwarded dependent parameters.
            var hasDependentParams = typeParams.Any(p => p is DependentTypeParameterType);

            if (hasDependentParams)
            {
                // Use dependent-aware resolution (handles literals, const refs, forwarding).
                var (resolvedTypeArgs, dependentArgs) = ResolvePass1TypeArgumentListWithDependents(
                    typeArgs!, typeParams, declaringType, declaringNode, asts, compilation, diagnostics);
                var typeArgCount = resolvedTypeArgs.Count;

                if (typeArgCount != typeParamCount)
                {
                    // Arity mismatch — emit MIST_ARITY_MISMATCH.
                    var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.ArityMismatch.Value,
                        DiagnosticSeverity.Error,
                        $"Type '{typeName}' expects {typeParamCount} type argument(s) but received {typeArgCount}.",
                        declaringNode,
                        sourceText));
                    return resolvedType;
                }

                // Construct the ConstructedType with dependent arguments.
                return new ConstructedType(resolvedType, resolvedTypeArgs, dependentArgs);
            }
            else
            {
                // No dependent parameters — use the standard resolution path.
                var resolvedTypeArgs = ResolveTypeArgumentList(typeArgs!, declaringType, declaringNode, asts, compilation, diagnostics);
                var typeArgCount = resolvedTypeArgs.Count;

                if (typeArgCount != typeParamCount)
                {
                    // Arity mismatch — emit MIST_ARITY_MISMATCH.
                    var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.ArityMismatch.Value,
                        DiagnosticSeverity.Error,
                        $"Type '{typeName}' expects {typeParamCount} type argument(s) but received {typeArgCount}.",
                        declaringNode,
                        sourceText));
                    return resolvedType;
                }

                // Construct the ConstructedType.
                return new ConstructedType(resolvedType, resolvedTypeArgs);
            }
        }
        else
        {
            // No type arguments provided.
            if (typeParamCount > 0)
            {
                // The referenced type is generic but no type arguments were provided.
                // Check if the declaring type has matching type parameters that could
                // be forwarded (e.g., class Foo<T> : Bar<T> is handled when type args
                // are explicitly provided). If the declaring type is non-generic,
                // emit MIST_MISSING_TYPE_ARGUMENTS.
                var declaringTypeParams = GetTypeParameters(declaringType);
                if (declaringTypeParams.Count == 0)
                {
                    var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.MissingTypeArguments.Value,
                        DiagnosticSeverity.Error,
                        $"Generic type '{typeName}' requires type arguments.",
                        declaringNode,
                        sourceText));
                }
            }

            return resolvedType;
        }
    }

    /// <summary>
    /// Resolves a <see cref="TypeArgumentListNode"/> to a list of <see cref="IType"/> instances.
    /// Each type argument name is looked up in the declaring type's member scope (for type parameters)
    /// and in the parent scope (for concrete types).
    /// </summary>
    private static IReadOnlyList<IType> ResolveTypeArgumentList(
        TypeArgumentListNode typeArgList,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var resolvedArgs = new List<IType>();

        // Resolve the first type argument.
        var firstType = ResolveTypeArgFromTypeReference(typeArgList.GetTypeReference(), declaringType, declaringNode, asts, compilation, diagnostics);
        resolvedArgs.Add(firstType);

        // Resolve the rest of the type arguments.
        foreach (var typeRef in typeArgList.GetTypeReferences())
        {
            var argType = ResolveTypeArgFromTypeReference(typeRef, declaringType, declaringNode, asts, compilation, diagnostics);
            resolvedArgs.Add(argType);
        }

        return resolvedArgs;
    }

    /// <summary>
    /// Resolves a <see cref="TypeArgumentListNode"/> to a list of <see cref="IType"/> instances
    /// and a parallel list of <see cref="DependentValueArgument"/> entries in the Pass 1
    /// inheritance context. This handles literal type arguments (e.g., <c>16</c> in
    /// <c>IFixedCollection&lt;16&gt;</c>) and forwarded dependent parameters (e.g., <c>N</c>
    /// in <c>class Buffer&lt;N:int&gt; : IFixedCollection&lt;N&gt;</c>).
    /// </summary>
    private static (IReadOnlyList<IType> TypeArgs, IReadOnlyList<DependentValueArgument?>? DependentArgs) ResolvePass1TypeArgumentListWithDependents(
        TypeArgumentListNode typeArgList,
        IReadOnlyList<IType> typeParams,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Collect all TypeArgumentNode entries in order.
        var argNodes = new List<TypeArgumentNode> { typeArgList.TypeArgument };
        foreach (var extra in typeArgList.TypeArguments)
            argNodes.Add(extra);

        var resolvedTypeArgs = new List<IType>(argNodes.Count);
        List<DependentValueArgument?>? dependentArgs = null;

        for (var i = 0; i < argNodes.Count; i++)
        {
            var argNode = argNodes[i];
            var correspondingParam = i < typeParams.Count ? typeParams[i] : null;
            var isDependentPosition = correspondingParam is DependentTypeParameterType;

            if (isDependentPosition)
            {
                var depParam = (DependentTypeParameterType)correspondingParam!;
                var (depArg, forwardedType) = TryResolvePass1DependentValueArgument(
                    argNode, depParam, declaringType, declaringNode, asts, compilation, diagnostics);
                if (depArg is not null)
                {
                    // Successfully resolved as a dependent value argument.
                    // If a forwarded DependentTypeParameterType was detected, use it as the
                    // type argument so ValidateDependentForwardingForConstructedBase can detect it.
                    // Otherwise, use the dependent parameter's value type as a placeholder.
                    resolvedTypeArgs.Add(forwardedType ?? depParam.ValueType);
                    dependentArgs ??= new List<DependentValueArgument?>(new DependentValueArgument?[argNodes.Count]);
                    dependentArgs[i] = depArg;
                    continue;
                }
                // If resolution returned null, fall through to normal type resolution.
            }

            // Normal type argument resolution (for regular type parameters or when
            // a dependent position has a type reference that isn't a const).
            if (argNode is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg)
            {
                var resolved = ResolveTypeArgFromTypeReference(
                    typeRefArg.Type, declaringType, declaringNode, asts, compilation, diagnostics);
                resolvedTypeArgs.Add(resolved);
            }
            else
            {
                // A literal type argument in a non-dependent position — this is an error
                // that will be caught by the type checker later. For now, use ErrorType.
                resolvedTypeArgs.Add(ErrorType.Instance);
            }
        }

        return (resolvedTypeArgs, dependentArgs);
    }

    /// <summary>
    /// Attempts to resolve a <see cref="TypeArgumentNode"/> as a <see cref="DependentValueArgument"/>
    /// in the Pass 1 inheritance context. Returns <c>(null, null)</c> if the argument is a type reference
    /// (not a const or literal), indicating normal type argument resolution should proceed.
    /// When a forwarded dependent parameter is detected, the second tuple element contains the
    /// <see cref="DependentTypeParameterType"/> so it can be placed in the TypeArguments list
    /// for forwarding validation.
    /// </summary>
    private static (DependentValueArgument? DepArg, DependentTypeParameterType? ForwardedType) TryResolvePass1DependentValueArgument(
        TypeArgumentNode argNode,
        DependentTypeParameterType depParam,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        switch (argNode)
        {
            case IntegerLiteralTypeArgumentTypeArgumentNode intLit:
            {
                var valueText = intLit.Literal.Lexeme;
                if (long.TryParse(valueText, out var value))
                    return (new DependentValueArgument(value, depParam.ValueType, valueText), null);
                return (new DependentValueArgument(0L, depParam.ValueType, valueText), null);
            }

            case NegatedIntegerTypeArgumentTypeArgumentNode negLit:
            {
                var valueText = negLit.Literal.Lexeme;
                if (long.TryParse(valueText, out var value))
                    return (new DependentValueArgument(-value, depParam.ValueType, $"-{valueText}"), null);
                return (new DependentValueArgument(0L, depParam.ValueType, $"-{valueText}"), null);
            }

            case HexLiteralTypeArgumentTypeArgumentNode hexLit:
            {
                var valueText = hexLit.Literal.Lexeme;
                var hexStr = valueText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? valueText[2..] : valueText;
                if (long.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out var value))
                    return (new DependentValueArgument(value, depParam.ValueType, valueText), null);
                return (new DependentValueArgument(0L, depParam.ValueType, valueText), null);
            }

            case BinaryLiteralTypeArgumentTypeArgumentNode binLit:
            {
                var valueText = binLit.Literal.Lexeme;
                var binStr = valueText.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
                    ? valueText[2..] : valueText;
                try
                {
                    var value = Convert.ToInt64(binStr, 2);
                    return (new DependentValueArgument(value, depParam.ValueType, valueText), null);
                }
                catch
                {
                    return (new DependentValueArgument(0L, depParam.ValueType, valueText), null);
                }
            }

            case TrueLiteralTypeArgumentTypeArgumentNode:
                return (new DependentValueArgument(true, depParam.ValueType, "true"), null);

            case FalseLiteralTypeArgumentTypeArgumentNode:
                return (new DependentValueArgument(false, depParam.ValueType, "false"), null);

            case CharLiteralTypeArgumentTypeArgumentNode charLit:
            {
                var valueText = charLit.Literal.Lexeme;
                // Strip surrounding quotes to get the char value.
                var charValue = valueText.Length >= 3 ? valueText[1] : '\0';
                return (new DependentValueArgument(charValue, depParam.ValueType, valueText), null);
            }

            case TypeReferenceTypeArgumentTypeArgumentNode typeRefArg:
            {
                // Disambiguation: check if this is a bare identifier that resolves to a
                // forwarded dependent parameter from the declaring type.
                if (typeRefArg.Type is NamedTypeRefTypeReferenceNode namedRef
                    && namedRef.TypeArgs is null
                    && !namedRef.ArrayRank.HasValue)
                {
                    var argName = namedRef.Name.IDENTIFIER.Lexeme;
                    if (string.IsNullOrEmpty(argName))
                    {
                        var span = namedRef.Name.Span;
                        if (span.Length > 0)
                        {
                            var srcTextForArg = FindSourceTextByContainment(declaringNode, asts, compilation);
                            if (srcTextForArg is not null && span.Start + span.Length <= srcTextForArg.Contents.Length)
                                argName = srcTextForArg.Contents.Substring(span.Start, span.Length);
                        }
                    }

                    // Look up in the declaring type's member scope for dependent parameters.
                    var memberScope = GetMemberScope(declaringType);
                    var resolvedSymbol = memberScope?.Lookup(argName);

                    if (resolvedSymbol is not null && resolvedSymbol.DeclaredType is DependentTypeParameterType forwardedParam)
                    {
                        // This is a forwarded dependent parameter (e.g., class Buffer<N:int> : IFixedCollection<N>).
                        // Return a DependentValueArgument with the parameter name as the value,
                        // and the forwarded DependentTypeParameterType for the TypeArguments list.
                        return (new DependentValueArgument(argName, forwardedParam.ValueType, argName), forwardedParam);
                    }

                    // If it resolves to a type, fall through to normal resolution.
                    if (resolvedSymbol is not null
                        && resolvedSymbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                            or SymbolKind.Trait or SymbolKind.TypeAlias or SymbolKind.Enum)
                    {
                        return (null, null);
                    }

                    // If it resolves to a field (potential const), treat as dependent value.
                    if (resolvedSymbol is not null
                        && resolvedSymbol.Kind is SymbolKind.Field or SymbolKind.LocalVariable)
                    {
                        return (new DependentValueArgument(argName, resolvedSymbol.DeclaredType ?? depParam.ValueType, argName), null);
                    }
                }

                // Not a dependent value — fall through to normal type resolution.
                return (null, null);
            }

            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Resolves a single type argument from a <see cref="TypeReferenceNode"/> in an inheritance context.
    /// Handles primitive types, type parameter references, and named types.
    /// </summary>
    private static IType ResolveTypeArgFromTypeReference(
        TypeReferenceNode typeRef,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
                var baseType = ResolvePrimitiveType(primitiveRef.PrimitiveType);
                if (primitiveRef.ArrayRank.HasValue)
                    return new ArrayType(baseType);
                return baseType;

            case VoidTypeRefTypeReferenceNode:
                return LegacyPrimitiveTypes.Void;

            case NamedTypeRefTypeReferenceNode namedRef:
            {
                // The generated parser may not populate IDENTIFIER.Lexeme on
                // QualifiedIdentifierNode. Fall back to extracting the name
                // from the source text span when the lexeme is empty.
                var argName = namedRef.Name.IDENTIFIER.Lexeme;
                if (string.IsNullOrEmpty(argName))
                {
                    var span = namedRef.Name.Span;
                    if (span.Length > 0)
                    {
                        // Find the correct source text by matching the declaring
                        // node to its compilation unit via containment check
                        // (span-based matching is unreliable across files).
                        var srcTextForArg = FindSourceTextByContainment(declaringNode, asts, compilation);
                        if (srcTextForArg is not null && span.Start + span.Length <= srcTextForArg.Contents.Length)
                        {
                            argName = srcTextForArg.Contents.Substring(span.Start, span.Length);
                        }
                    }
                }

                // First, look up in the declaring type's member scope (for type parameters).
                var memberScope = GetMemberScope(declaringType);
                ISymbol? resolvedSymbol = memberScope?.Lookup(argName);

                if (resolvedSymbol is not null && resolvedSymbol.DeclaredType is TypeParameterType tpt)
                {
                    return tpt;
                }

                // If the symbol found is not a type (e.g., a constructor with the same
                // name as the class), skip it and look in the parent scope instead.
                if (resolvedSymbol is not null
                    && resolvedSymbol.Kind is not (SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Trait or SymbolKind.TypeAlias or SymbolKind.Enum))
                {
                    resolvedSymbol = null;
                }

                // Look up in the parent scope chain (for concrete types).
                if (resolvedSymbol is null && memberScope?.Parent is not null)
                {
                    resolvedSymbol = memberScope.Parent.Lookup(argName);
                }

                // If the parent scope lookup also found a non-type symbol, try
                // walking further up the scope chain to find a type symbol.
                if (resolvedSymbol is not null
                    && resolvedSymbol.DeclaredType is null
                    && resolvedSymbol.Kind is not (SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Trait or SymbolKind.TypeAlias or SymbolKind.Enum))
                {
                    // Walk up the scope chain manually to find a type symbol.
                    IScope? searchScope = memberScope?.Parent?.Parent;
                    resolvedSymbol = null;
                    while (searchScope is not null && resolvedSymbol is null)
                    {
                        if (searchScope is Scope s)
                        {
                            foreach (var m in s.Members)
                            {
                                if (m.Name == argName && m.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Trait or SymbolKind.TypeAlias or SymbolKind.Enum)
                                {
                                    resolvedSymbol = m;
                                    break;
                                }
                            }
                        }
                        searchScope = searchScope.Parent;
                    }
                }

                if (resolvedSymbol is not null && resolvedSymbol.DeclaredType is not null)
                {
                    var resolvedType = resolvedSymbol.DeclaredType;

                    // If this type argument itself has type arguments (nested generics),
                    // resolve them recursively.
                    if (namedRef.TypeArgs is not null)
                    {
                        var nestedTypeParams = GetTypeParameters(resolvedType);
                        if (nestedTypeParams.Count == 0)
                        {
                            var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                DiagnosticId.NotGeneric.Value,
                                DiagnosticSeverity.Error,
                                $"Type '{argName}' is not generic and does not accept type arguments.",
                                declaringNode,
                                sourceText));
                            return resolvedType;
                        }

                        var nestedArgs = ResolveTypeArgumentList(namedRef.TypeArgs, declaringType, declaringNode, asts, compilation, diagnostics);
                        if (nestedArgs.Count != nestedTypeParams.Count)
                        {
                            var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                DiagnosticId.ArityMismatch.Value,
                                DiagnosticSeverity.Error,
                                $"Type '{argName}' expects {nestedTypeParams.Count} type argument(s) but received {nestedArgs.Count}.",
                                declaringNode,
                                sourceText));
                            return resolvedType;
                        }

                        return new ConstructedType(resolvedType, nestedArgs);
                    }

                    if (namedRef.ArrayRank.HasValue)
                        return new ArrayType(resolvedType);

                    return resolvedType;
                }

                // Type argument could not be resolved.
                var srcText = FindSourceTextForNode(declaringNode, asts, compilation);
                diagnostics.Add(MakeDiagnosticWithLocation(
                    DiagnosticId.UndefinedTypeArgument.Value,
                    DiagnosticSeverity.Error,
                    $"Type argument '{argName}' could not be resolved.",
                    declaringNode,
                    srcText));
                return ErrorType.Instance;
            }

            case TupleTypeRefTypeReferenceNode tupleRef:
            {
                // Collect all elements: First + Rest + TupleTypeElements (additional).
                var elementCount = 2 + tupleRef.TupleTypeElements.Length;
                var elementTypes = new List<IType>(elementCount);
                var elementNames = new List<string?>(elementCount);
                var hasAnyName = false;

                // Resolve First element.
                elementTypes.Add(ResolveTypeArgFromTypeReference(tupleRef.First.Type, declaringType, declaringNode, asts, compilation, diagnostics));
                var firstName = tupleRef.First.Name?.Lexeme;
                elementNames.Add(firstName);
                if (firstName is not null) hasAnyName = true;

                // Resolve Rest element.
                elementTypes.Add(ResolveTypeArgFromTypeReference(tupleRef.Rest.Type, declaringType, declaringNode, asts, compilation, diagnostics));
                var restName = tupleRef.Rest.Name?.Lexeme;
                elementNames.Add(restName);
                if (restName is not null) hasAnyName = true;

                // Resolve additional elements.
                foreach (var additional in tupleRef.TupleTypeElements)
                {
                    elementTypes.Add(ResolveTypeArgFromTypeReference(additional.Type, declaringType, declaringNode, asts, compilation, diagnostics));
                    var additionalName = additional.Name?.Lexeme;
                    elementNames.Add(additionalName);
                    if (additionalName is not null) hasAnyName = true;
                }

                return new TupleType(elementTypes, hasAnyName ? elementNames : null);
            }

            default:
                return ErrorType.Instance;
        }
    }

    /// <summary>
    /// Gets the type parameters from a type (ClassType, StructType, or InterfaceType).
    /// Returns an empty list for types that don't have type parameters.
    /// </summary>
    private static IReadOnlyList<IType> GetTypeParameters(IType type)
    {
        return type switch
        {
            ClassType ct => ct.TypeParameters,
            StructType st => st.TypeParameters,
            InterfaceType it => it.TypeParameters,
            TraitType tt => tt.TypeParameters,
            TypeAliasType at => at.TypeParameters,
            _ => Array.Empty<IType>(),
        };
    }

    /// <summary>
    /// Gets the member scope from a type (ClassType, StructType, or InterfaceType).
    /// Returns null for types that don't have a member scope.
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            TraitType tt => tt.MemberScope,
            NewtypeType nt => nt.MemberScope,
            _ => null,
        };
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for any syntax node by walking the ASTs.
    /// </summary>
    private static SourceText? FindSourceTextForNode(
        Storm.Runtime.SyntaxNode node,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        // Use the node's span to find which compilation unit it belongs to.
        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu)
            {
                // Check if the node's span falls within this compilation unit's span.
                if (node.Span.Start >= cu.Span.Start && node.Span.Start < cu.Span.Start + cu.Span.Length)
                {
                    return i < compilation.Units.Length ? compilation.Units[i].Source : null;
                }
            }
        }

        // Fallback: try to find by checking if the node is contained in any type declaration.
        for (var i = 0; i < asts.Length; i++)
        {
            if (i < compilation.Units.Length)
            {
                return compilation.Units[i].Source;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for a type declaration node by checking
    /// which compilation unit contains it via reference-equality containment check.
    /// This is more reliable than span-based matching when multiple files have
    /// overlapping spans.
    /// </summary>
    private static SourceText? FindSourceTextByContainment(
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsTypeDeclarationNode(cu, declaringNode))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source : null;
            }
        }

        // Fallback to span-based matching.
        return FindSourceTextForNode(declaringNode, asts, compilation);
    }

    /// <summary>
    /// Finds the compilation unit path for a class declaration node by walking
    /// the ASTs to find which compilation unit contains the node.
    /// </summary>
    private static string? FindCompilationUnitPath(
        ClassDeclarationNode classDecl,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsClassDeclaration(cu, classDecl))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source?.Path : null;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for a class declaration node by walking
    /// the ASTs to find which compilation unit contains the node.
    /// </summary>
    private static SourceText? FindSourceTextForClassDecl(
        ClassDeclarationNode classDecl,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsClassDeclaration(cu, classDecl))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source : null;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks whether a compilation unit contains the given class declaration.
    /// </summary>
    private static bool ContainsClassDeclaration(CompilationUnitNode cu, ClassDeclarationNode classDecl)
    {
        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    if (typeItem.TypeDeclaration is ClassTypeTypeDeclarationNode ct && ReferenceEquals(ct.ClassDeclaration, classDecl))
                        return true;
                    break;
                case NamespaceItemTopLevelItemNode nsItem:
                    var namespaceTypes = GetNamespaceBodyTypes(nsItem.NamespaceDeclaration);
                    foreach (var typeDecl in namespaceTypes)
                    {
                        if (typeDecl is ClassTypeTypeDeclarationNode nsCt && ReferenceEquals(nsCt.ClassDeclaration, classDecl))
                            return true;
                    }
                    break;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1b (cont.) — Resolve interface lists on all types
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After all types are registered and base types are resolved, resolve
    /// interface lists on classes, structs, and interfaces.
    ///
    /// For classes: the first name in the base clause (<c>BaseClassName</c>)
    /// may be a base class (already resolved by <see cref="Pass1_ResolveBaseTypes"/>)
    /// or an interface. If it resolves to an <see cref="InterfaceType"/>, it is
    /// treated as an interface (no base class). The class's <c>Interfaces</c>
    /// property is set accordingly.
    ///
    /// For structs: the first name in the interface list (<c>InterfaceName</c>)
    /// is resolved to an <see cref="InterfaceType"/> and stored on
    /// <c>StructType.Interfaces</c>.
    ///
    /// For interfaces: the first name in the interface list (<c>InterfaceName</c>)
    /// is resolved to an <see cref="InterfaceType"/> and stored on
    /// <c>InterfaceType.Interfaces</c>.
    ///
    /// Emits <c>MIST_UNDEFINED_INTERFACE</c> when an interface name is not found,
    /// and <c>MIST_NOT_AN_INTERFACE</c> when a name resolves to a class or struct.
    /// </summary>
    private static void Pass1_ResolveInterfaceLists(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Class when symbol.DeclaredType is ClassType classType:
                    ResolveClassInterfaceList(symbol, classType, symbolTable, asts, compilation, diagnostics);
                    break;

                case SymbolKind.Struct when symbol.DeclaredType is StructType structType:
                    ResolveStructInterfaceList(symbol, structType, symbolTable, asts, compilation, diagnostics);
                    break;

                case SymbolKind.Interface when symbol.DeclaredType is InterfaceType interfaceType:
                    ResolveInterfaceInterfaceList(symbol, interfaceType, symbolTable, asts, compilation, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves the interface list for a class declaration.
    /// The first name in the base clause (<c>BaseClassName</c>) may be a base class
    /// or an interface. If Pass1_ResolveBaseTypes already set <c>BaseType</c>, the
    /// first name is a base class and is skipped here. If <c>BaseType</c> is null
    /// and the base clause is present, the first name is checked as an interface.
    /// </summary>
    private static void ResolveClassInterfaceList(
        ISymbol classSymbol,
        ClassType classType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var declaringNode = symbolTable.GetDeclaringNode(classSymbol);
        if (declaringNode is not ClassDeclarationNode classDecl)
            return;

        // No base clause means no base class and no interfaces.
        if (classDecl.BaseList is null)
            return;

        // Collect all BaseTypeRefNode entries from the base list.
        var allEntries = new List<BaseTypeRefNode>();
        allEntries.Add(classDecl.BaseList.First);
        foreach (var rest in classDecl.BaseList.BaseTypeRefs)
        {
            allEntries.Add(rest);
        }

        // If Pass1_ResolveBaseTypes already resolved a base class, the first
        // entry was consumed as the base class. Start from the second entry.
        var startIndex = classType.BaseType is not null ? 1 : 0;

        if (startIndex == 0)
        {
            // The first name was NOT resolved as a base class. Check if it's an interface.
            var firstName = allEntries[0].Name.Lexeme;
            if (string.IsNullOrEmpty(firstName))
                return;

            var parentScope = classSymbol.ContainingScope;
            var resolvedSymbol = parentScope.Lookup(firstName);

            if (resolvedSymbol is null)
            {
                // Already reported as MIST_UNDEFINED_BASE_TYPE by Pass1_ResolveBaseTypes. Skip.
                startIndex = 1;
            }
            else if (resolvedSymbol.DeclaredType is InterfaceType resolvedInterface)
            {
                var typeArgs = allEntries[0].TypeArgs;
                var resolvedType = ResolveGenericTypeReference(
                    resolvedInterface, typeArgs, firstName,
                    classType, classDecl, asts, compilation, diagnostics);
                classType.Interfaces = new List<IType> { resolvedType };
                startIndex = 1;
            }
            else if (resolvedSymbol.DeclaredType is StructType)
            {
                // Already reported as MIST_CANNOT_INHERIT_STRUCT by Pass1_ResolveBaseTypes.
                startIndex = 1;
            }
            else if (resolvedSymbol.DeclaredType is UnknownType)
            {
                // Already reported by Pass1_ResolveBaseTypes' defensive
                // UnknownType guard (Requirements 6.1, 16.6). Skip the
                // first entry so it is not additionally reported here.
                startIndex = 1;
            }
            else
            {
                // Resolved to a ClassType but BaseType is null (shouldn't happen normally).
                startIndex = 1;
            }
        }

        // Resolve remaining entries as interfaces.
        if (startIndex < allEntries.Count)
        {
            var parentScope = classSymbol.ContainingScope;
            var additionalInterfaces = ResolveInterfaceEntries(
                allEntries.Skip(startIndex),
                parentScope,
                classType,
                classDecl,
                symbolTable, asts, compilation, diagnostics,
                findPathForSymbol: classSymbol);

            if (additionalInterfaces.Count > 0)
            {
                var existing = classType.Interfaces.ToList();
                existing.AddRange(additionalInterfaces);
                classType.Interfaces = existing;
            }
        }
    }

    /// <summary>
    /// Resolves the interface list for a struct declaration.
    /// </summary>
    private static void ResolveStructInterfaceList(
        ISymbol structSymbol,
        StructType structType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var declaringNode = symbolTable.GetDeclaringNode(structSymbol);
        if (declaringNode is not StructDeclarationNode structDecl)
            return;

        if (structDecl.Interfaces is null)
            return;

        var entries = ExtractBaseListEntries(structDecl.Interfaces);
        if (entries.Count == 0)
            return;

        var parentScope = structSymbol.ContainingScope;
        var resolvedInterfaces = ResolveInterfaceEntries(
            entries,
            parentScope,
            structType,
            structDecl,
            symbolTable, asts, compilation, diagnostics,
            findPathForSymbol: structSymbol);

        if (resolvedInterfaces.Count > 0)
        {
            structType.Interfaces = resolvedInterfaces;
        }
    }

    /// <summary>
    /// Resolves the interface list for an interface declaration.
    /// </summary>
    private static void ResolveInterfaceInterfaceList(
        ISymbol interfaceSymbol,
        InterfaceType interfaceType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var declaringNode = symbolTable.GetDeclaringNode(interfaceSymbol);
        if (declaringNode is not InterfaceDeclarationNode interfaceDecl)
            return;

        if (interfaceDecl.Interfaces is null)
            return;

        var entries = ExtractBaseListEntries(interfaceDecl.Interfaces);
        if (entries.Count == 0)
            return;

        var parentScope = interfaceSymbol.ContainingScope;
        var resolvedInterfaces = ResolveInterfaceEntries(
            entries,
            parentScope,
            interfaceType,
            interfaceDecl,
            symbolTable, asts, compilation, diagnostics,
            findPathForSymbol: interfaceSymbol);

        if (resolvedInterfaces.Count > 0)
        {
            interfaceType.Interfaces = resolvedInterfaces;
        }
    }

    /// <summary>
    /// Resolves a set of interface names to <see cref="InterfaceType"/> instances.
    /// Emits diagnostics for undefined names and non-interface types.
    /// </summary>
    private static List<InterfaceType> ResolveInterfaceNames(
        IEnumerable<string> interfaceNames,
        IScope parentScope,
        string declaringTypeName,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        ISymbol findPathForSymbol)
    {
        var resolved = new List<InterfaceType>();

        foreach (var name in interfaceNames)
        {
            var resolvedSymbol = parentScope.Lookup(name);

            if (resolvedSymbol is null)
            {
                var sourceText = FindSourceTextForSymbol(
                    findPathForSymbol, symbolTable, asts, compilation);
                var declaringNode = symbolTable.GetDeclaringNode(findPathForSymbol);

                if (declaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_UNDEFINED_INTERFACE",
                        DiagnosticSeverity.Error,
                        $"The interface type '{name}' could not be found.",
                        declaringNode,
                        sourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForSymbol(
                        findPathForSymbol, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_UNDEFINED_INTERFACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"The interface type '{name}' could not be found.",
                        Location: default,
                        CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                        AnalyzerName: null));
                }
                continue;
            }

            if (resolvedSymbol.DeclaredType is InterfaceType resolvedInterface)
            {
                resolved.Add(resolvedInterface);
            }
            else
            {
                var sourceText = FindSourceTextForSymbol(
                    findPathForSymbol, symbolTable, asts, compilation);
                var declaringNode = symbolTable.GetDeclaringNode(findPathForSymbol);

                if (declaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_NOT_AN_INTERFACE",
                        DiagnosticSeverity.Error,
                        $"'{name}' is not an interface; only interfaces can appear in an interface list.",
                        declaringNode,
                        sourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForSymbol(
                        findPathForSymbol, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_NOT_AN_INTERFACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"'{name}' is not an interface; only interfaces can appear in an interface list.",
                        Location: default,
                        CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                        AnalyzerName: null));
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// Resolves a set of <see cref="BaseTypeRefNode"/> entries to interface types,
    /// handling type arguments to produce <see cref="ConstructedType"/> when appropriate.
    /// Emits diagnostics for undefined names and non-interface types.
    /// </summary>
    private static List<IType> ResolveInterfaceEntries(
        IEnumerable<BaseTypeRefNode> entries,
        IScope parentScope,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        ISymbol findPathForSymbol)
    {
        var resolved = new List<IType>();

        foreach (var entry in entries)
        {
            var name = entry.Name.Lexeme;
            if (string.IsNullOrEmpty(name))
                continue;

            var resolvedSymbol = parentScope.Lookup(name);

            if (resolvedSymbol is null)
            {
                var sourceText = FindSourceTextForSymbol(
                    findPathForSymbol, symbolTable, asts, compilation);
                var declNode = symbolTable.GetDeclaringNode(findPathForSymbol);

                if (declNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_UNDEFINED_INTERFACE",
                        DiagnosticSeverity.Error,
                        $"The interface type '{name}' could not be found.",
                        declNode,
                        sourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForSymbol(
                        findPathForSymbol, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_UNDEFINED_INTERFACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"The interface type '{name}' could not be found.",
                        Location: default,
                        CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                        AnalyzerName: null));
                }
                continue;
            }

            if (resolvedSymbol.DeclaredType is InterfaceType resolvedInterface)
            {
                var resolvedType = ResolveGenericTypeReference(
                    resolvedInterface, entry.TypeArgs, name,
                    declaringType, declaringNode, asts, compilation, diagnostics);
                resolved.Add(resolvedType);
            }
            else if (resolvedSymbol.DeclaredType is UnknownType)
            {
                // Defensive guard (Requirements 6.2, 6.3, 16.6). The
                // grammar prevents `unknown` from appearing as the name
                // of a `BaseTypeRef` because `unknown` lexes to the
                // reserved `UNKNOWN` token rather than `IDENTIFIER`, so
                // no parsed struct interface list or interface base
                // list can land here. The branch is retained so the
                // rule is complete for programmatically-constructed
                // ASTs and for documentation alignment with Req 6.2–6.3.
                // The reference is dropped by simply not appending it to
                // `resolved`.
                var sourceText = FindSourceTextForSymbol(
                    findPathForSymbol, symbolTable, asts, compilation);
                var declNode = symbolTable.GetDeclaringNode(findPathForSymbol);

                if (declNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        UnknownAsBaseType.Value,
                        DiagnosticSeverity.Error,
                        $"'{declaringType.DisplayName}' cannot reference 'unknown' as a base or implemented type. " +
                        "'unknown' is the user-facing top type and does not participate in inheritance.",
                        declNode,
                        sourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForSymbol(
                        findPathForSymbol, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        UnknownAsBaseType,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"'{declaringType.DisplayName}' cannot reference 'unknown' as a base or implemented type. " +
                        "'unknown' is the user-facing top type and does not participate in inheritance.",
                        Location: default,
                        CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                        AnalyzerName: null));
                }
            }
            else
            {
                var sourceText = FindSourceTextForSymbol(
                    findPathForSymbol, symbolTable, asts, compilation);
                var declNode = symbolTable.GetDeclaringNode(findPathForSymbol);

                if (declNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_NOT_AN_INTERFACE",
                        DiagnosticSeverity.Error,
                        $"'{name}' is not an interface; only interfaces can appear in an interface list.",
                        declNode,
                        sourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForSymbol(
                        findPathForSymbol, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_NOT_AN_INTERFACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"'{name}' is not an interface; only interfaces can appear in an interface list.",
                        Location: default,
                        CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                        AnalyzerName: null));
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// Extracts all <see cref="BaseTypeRefNode"/> entries from a <see cref="BaseListNode"/>.
    /// </summary>
    private static List<BaseTypeRefNode> ExtractBaseListEntries(BaseListNode baseList)
    {
        var entries = new List<BaseTypeRefNode>();
        entries.Add(baseList.First);
        foreach (var rest in baseList.BaseTypeRefs)
        {
            entries.Add(rest);
        }
        return entries;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Pass 1b (cont.): Constraint Resolution
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves constraint clauses on all type and method declarations.
    /// For each declaration with constraint clauses:
    ///   1. Validates the <c>where</c> clause references a valid type parameter name.
    ///   2. Checks for duplicate <c>where</c> clauses on the same type parameter.
    ///   3. Resolves each constraint (class, struct, new(...), type reference).
    ///   4. Stores the resulting <see cref="TypeParameterConstraints"/> on the
    ///      <see cref="TypeParameterType.Constraints"/> property.
    /// Adhoc refinement constraints are skipped here and processed in
    /// <see cref="Pass2_ResolveAdhocRefinementConstraints"/> after parameter
    /// symbols are registered.
    /// </summary>
    private static void Pass1_ResolveConstraints(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            var declaringNode = symbolTable.GetDeclaringNode(symbol);
            if (declaringNode is null)
                continue;

            // Get the constraint clauses and type parameters for this declaration.
            var (constraintClauses, typeParameters) = GetConstraintClausesAndTypeParams(symbol, declaringNode);
            if (constraintClauses.IsDefaultOrEmpty || typeParameters.Count == 0)
                continue;

            var sourceText = FindSourceTextForSymbol(symbol, symbolTable, asts, compilation)
                ?? FindSourceTextForNode(declaringNode, asts, compilation);
            var declaringName = symbol.Name;

            // Track which type parameters have already been constrained to detect duplicates.
            var constrainedParams = new Dictionary<string, bool>();

            foreach (var clause in constraintClauses)
            {
                // Only process generic constraint clauses in this pass.
                // AdhocRefinementConstraintWhereClauseNode is processed in
                // Pass2_ResolveAdhocRefinementConstraints after parameter symbols exist.
                if (clause is not GenericConstraintClauseWhereClauseNode genericClause)
                    continue;

                var typeParamName = genericClause.Name.Lexeme;

                // Validate the type parameter name exists on this declaration.
                var typeParam = FindTypeParameter(typeParameters, typeParamName);
                if (typeParam is null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        ConstraintUndefinedTypeParam.Value,
                        DiagnosticSeverity.Error,
                        $"Type parameter '{typeParamName}' is not defined on '{declaringName}'.",
                        genericClause,
                        sourceText));
                    continue;
                }

                // Check for duplicate where clauses on the same type parameter.
                if (constrainedParams.ContainsKey(typeParamName))
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        ConstraintDuplicateWhereClause.Value,
                        DiagnosticSeverity.Error,
                        $"Type parameter '{typeParamName}' cannot have multiple 'where' clauses. Combine constraints in a single clause.",
                        genericClause,
                        sourceText));
                    continue;
                }
                constrainedParams[typeParamName] = true;

                // Resolve the constraints in this clause.
                var constraints = ResolveConstraintClause(
                    genericClause, typeParam, symbol, declaringNode, symbolTable, asts, compilation, diagnostics, sourceText);

                // Store the resolved constraints on the type parameter.
                typeParam.Constraints = constraints;
            }
        }
    }

    /// <summary>
    /// Resolves adhoc refinement constraint clauses on all declarations after Pass 2
    /// has registered parameter symbols. For each <see cref="AdhocRefinementConstraintWhereClauseNode"/>:
    ///   - Looks up the name in dependent type parameters → attaches predicate to
    ///     <see cref="DependentTypeParameterType.AdhocConstraint"/>.
    ///   - Looks up the name in value parameters → stores in parameter refinement map.
    ///   - Emits MIST_REFINEMENT_0001 if name not found.
    ///   - Emits MIST_REFINEMENT_0002 if name is a regular type parameter.
    ///   - Emits MIST_REFINEMENT_0003 if duplicate.
    /// </summary>
    private static void Pass2_ResolveAdhocRefinementConstraints(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        Dictionary<ISymbol, RefinementPredicate> parameterRefinements)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            var declaringNode = symbolTable.GetDeclaringNode(symbol);
            if (declaringNode is null)
                continue;

            // Get the constraint clauses and type parameters for this declaration.
            var (constraintClauses, typeParameters) = GetConstraintClausesAndTypeParams(symbol, declaringNode);
            if (constraintClauses.IsDefaultOrEmpty)
                continue;

            // Check if there are any adhoc refinement constraints to process.
            var hasAdhocClauses = false;
            foreach (var clause in constraintClauses)
            {
                if (clause is AdhocRefinementConstraintWhereClauseNode)
                {
                    hasAdhocClauses = true;
                    break;
                }
            }
            if (!hasAdhocClauses)
                continue;

            var sourceText = FindSourceTextForSymbol(symbol, symbolTable, asts, compilation)
                ?? FindSourceTextForNode(declaringNode, asts, compilation);

            // Track which names have already been refined to detect duplicate adhoc constraints.
            var refinedNames = new Dictionary<string, bool>();

            foreach (var clause in constraintClauses)
            {
                if (clause is not AdhocRefinementConstraintWhereClauseNode refinementClause)
                    continue;

                ResolveAdhocRefinementConstraint(
                    refinementClause, symbol, declaringNode, typeParameters,
                    refinedNames, parameterRefinements, symbolTable, sourceText, diagnostics);
            }
        }
    }

    /// <summary>
    /// Extracts constraint clauses and type parameters from a symbol's declaring node.
    /// Returns empty/default values for declarations that don't support constraints.
    /// </summary>
    private static (ImmutableArray<WhereClauseNode> Clauses, IReadOnlyList<IType> TypeParams) GetConstraintClausesAndTypeParams(
        ISymbol symbol,
        Storm.Runtime.SyntaxNode declaringNode)
    {
        switch (declaringNode)
        {
            case ClassDeclarationNode classDecl when symbol.DeclaredType is ClassType classType:
                return (classDecl.Constraints, classType.TypeParameters);

            case StructDeclarationNode structDecl when symbol.DeclaredType is StructType structType:
                return (structDecl.Constraints, structType.TypeParameters);

            case InterfaceDeclarationNode interfaceDecl when symbol.DeclaredType is InterfaceType interfaceType:
                return (interfaceDecl.Constraints, interfaceType.TypeParameters);

            case TraitDeclarationNode traitDecl when symbol.DeclaredType is TraitType traitType:
                return (traitDecl.Constraints, traitType.TypeParameters);

            case MethodDeclarationNode methodDecl when symbol is Symbol methodSymbol:
                return (methodDecl.Constraints, methodSymbol.TypeParameters);

            case InterfaceMethodDeclarationNode interfaceMethodDecl when symbol is Symbol ifaceMethodSymbol:
                return (interfaceMethodDecl.Constraints, ifaceMethodSymbol.TypeParameters);

            case TypeAliasDeclarationNode aliasDecl when symbol is Symbol aliasSymbol:
                return (aliasDecl.Constraints, aliasSymbol.TypeParameters);

            case ConstructorDeclarationNode ctorDecl:
                return (ctorDecl.Constraints, Array.Empty<IType>());

            case BlockIndexerIndexerDeclarationNode blockIndexerDecl:
                return (blockIndexerDecl.Constraints, Array.Empty<IType>());

            default:
                return (ImmutableArray<WhereClauseNode>.Empty, Array.Empty<IType>());
        }
    }

    /// <summary>
    /// Finds a type parameter by name in the given list.
    /// </summary>
    private static TypeParameterType? FindTypeParameter(IReadOnlyList<IType> typeParameters, string name)
    {
        foreach (var tp in typeParameters)
        {
            if (tp is TypeParameterType tpt && tpt.Name == name)
                return tpt;
        }
        return null;
    }

    /// <summary>
    /// Resolves a single adhoc refinement constraint clause. Looks up the constrained
    /// name in dependent type parameters and value parameters, attaching the predicate
    /// or emitting diagnostics as appropriate.
    /// </summary>
    private static void ResolveAdhocRefinementConstraint(
        AdhocRefinementConstraintWhereClauseNode refinementClause,
        ISymbol symbol,
        Storm.Runtime.SyntaxNode declaringNode,
        IReadOnlyList<IType> typeParameters,
        Dictionary<string, bool> refinedNames,
        Dictionary<ISymbol, RefinementPredicate> parameterRefinements,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var name = refinementClause.Name.Lexeme;

        // Check for duplicate refinement constraint on the same name.
        if (refinedNames.ContainsKey(name))
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                RefinementConstraintDuplicate.Value,
                DiagnosticSeverity.Error,
                $"Duplicate refinement constraint on '{name}'. Combine predicates using 'and' in a single where clause.",
                refinementClause,
                sourceText));
            return;
        }

        // Check if the name is a regular type parameter (not dependent).
        var regularTypeParam = FindTypeParameter(typeParameters, name);
        if (regularTypeParam is not null)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                RefinementConstraintOnRegularTypeParam.Value,
                DiagnosticSeverity.Error,
                $"Refinement constraints cannot be applied to type parameter '{name}'. Refinement constraints apply only to dependent type parameters and value parameters.",
                refinementClause,
                sourceText));
            return;
        }

        // Check if the name is a dependent type parameter.
        var dependentParam = FindDependentTypeParameter(typeParameters, name);
        if (dependentParam is not null)
        {
            refinedNames[name] = true;
            dependentParam.AdhocConstraint = new RefinementPredicate(refinementClause.Predicate);
            return;
        }

        // Check if the name is a value parameter on the declaration.
        var valueParam = FindValueParameter(symbol, declaringNode, name, symbolTable);
        if (valueParam is not null)
        {
            refinedNames[name] = true;
            parameterRefinements[valueParam] = new RefinementPredicate(refinementClause.Predicate);
            return;
        }

        // Name not found — emit diagnostic.
        diagnostics.Add(MakeDiagnosticWithLocation(
            RefinementConstraintUnknownName.Value,
            DiagnosticSeverity.Error,
            $"Refinement constraint references '{name}', which is not a dependent type parameter or parameter on this declaration.",
            refinementClause,
            sourceText));
    }

    /// <summary>
    /// Finds a dependent type parameter by name in the given type parameter list.
    /// </summary>
    private static DependentTypeParameterType? FindDependentTypeParameter(IReadOnlyList<IType> typeParameters, string name)
    {
        foreach (var tp in typeParameters)
        {
            if (tp is DependentTypeParameterType dpt && dpt.Name == name)
                return dpt;
        }
        return null;
    }

    /// <summary>
    /// Finds a value parameter symbol by name on the given declaration.
    /// Searches method, constructor, and indexer parameter lists.
    /// </summary>
    private static ISymbol? FindValueParameter(
        ISymbol symbol,
        Storm.Runtime.SyntaxNode declaringNode,
        string paramName,
        SymbolTable symbolTable)
    {
        // Get the parameter list node from the declaration.
        ParameterListNode? paramList = declaringNode switch
        {
            MethodDeclarationNode methodDecl => methodDecl.Parameters,
            ConstructorDeclarationNode ctorDecl => ctorDecl.Parameters,
            BlockIndexerIndexerDeclarationNode indexerDecl => indexerDecl.Parameters,
            InterfaceMethodDeclarationNode ifaceMethodDecl => ifaceMethodDecl.Parameters,
            _ => null,
        };

        if (paramList is null)
            return null;

        // Walk the parameter list children to find the parameter by name.
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param && param.Name.Lexeme == paramName)
            {
                // Look up the symbol registered for this parameter node.
                return symbolTable.GetDeclaredSymbol(param);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves all constraints in a single <c>where</c> clause and builds a
    /// <see cref="TypeParameterConstraints"/> instance.
    /// </summary>
    private static TypeParameterConstraints ResolveConstraintClause(
        GenericConstraintClauseWhereClauseNode clause,
        TypeParameterType typeParam,
        ISymbol declaringSymbol,
        Storm.Runtime.SyntaxNode declaringNode,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        SourceText? sourceText)
    {
        var hasClassConstraint = false;
        var hasStructConstraint = false;
        var hasReadonlyConstraint = false;
        var hasMutableConstraint = false;
        IType? baseClassConstraint = null;
        var interfaceConstraints = new List<IType>();
        var traitConstraints = new List<IType>();
        var constructorConstraints = new List<IReadOnlyList<IType>>();

        // Track all base class constraints for validation (MIST_CONSTRAINT_0004).
        var baseClassConstraints = new List<IType>();
        // Track struct types used as type constraints (MIST_CONSTRAINT_0005).
        var structTypeConstraints = new List<IType>();

        // Collect all constraints from the clause (first + rest).
        var allConstraints = new List<ConstraintNode> { clause.Constraints.Constraint };
        allConstraints.AddRange(clause.Constraints.Constraints);

        foreach (var constraint in allConstraints)
        {
            switch (constraint)
            {
                case ClassConstraintConstraintNode:
                    hasClassConstraint = true;
                    break;

                case StructConstraintConstraintNode:
                    hasStructConstraint = true;
                    break;

                case ReadonlyConstraintConstraintNode:
                    hasReadonlyConstraint = true;
                    break;

                case MutableConstraintConstraintNode:
                    hasMutableConstraint = true;
                    break;

                case ConstructorConstraintConstraintNode ctorConstraint:
                {
                    var paramTypes = ResolveConstructorConstraintParams(
                        ctorConstraint, declaringSymbol, declaringNode, symbolTable, asts, compilation, diagnostics, sourceText);
                    constructorConstraints.Add(paramTypes);
                    break;
                }

                case TypeConstraintConstraintNode typeConstraint:
                {
                    var resolvedType = ResolveConstraintTypeReference(
                        typeConstraint, declaringSymbol, declaringNode, asts, compilation, diagnostics, sourceText);

                    if (resolvedType is null || resolvedType is ErrorType)
                    {
                        // Already emitted MIST_CONSTRAINT_0008 in ResolveConstraintTypeReference.
                        break;
                    }

                    // Classify the resolved type, tracking base class and struct constraints.
                    ClassifyConstraintType(
                        resolvedType, ref baseClassConstraint,
                        interfaceConstraints, traitConstraints,
                        baseClassConstraints, structTypeConstraints);
                    break;
                }
            }
        }

        // Validate constraint combinations after all constraints are resolved.
        ValidateConstraintCombinations(
            hasClassConstraint, hasStructConstraint,
            baseClassConstraints, structTypeConstraints,
            constructorConstraints,
            typeParam, clause, sourceText, diagnostics);

        return new TypeParameterConstraints
        {
            HasClassConstraint = hasClassConstraint,
            HasStructConstraint = hasStructConstraint,
            BaseClassConstraint = baseClassConstraint,
            InterfaceConstraints = interfaceConstraints,
            TraitConstraints = traitConstraints,
            ConstructorConstraints = constructorConstraints,
            HasReadonlyConstraint = hasReadonlyConstraint,
            HasMutableConstraint = hasMutableConstraint,
        };
    }

    /// <summary>
    /// Resolves the parameter types for a constructor constraint (<c>new(...)</c>).
    /// Returns an empty list for parameterless <c>new()</c>.
    /// </summary>
    private static IReadOnlyList<IType> ResolveConstructorConstraintParams(
        ConstructorConstraintConstraintNode ctorConstraint,
        ISymbol declaringSymbol,
        Storm.Runtime.SyntaxNode declaringNode,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        SourceText? sourceText)
    {
        if (ctorConstraint.ParamTypes is null)
            return Array.Empty<IType>();

        var paramTypes = new List<IType>();
        var scope = declaringSymbol.ContainingScope;

        // Resolve the first parameter type.
        var firstType = ResolveConstraintParamType(
            ctorConstraint.ParamTypes.TypeReference, scope, symbolTable, declaringNode, asts, compilation, diagnostics, sourceText);
        paramTypes.Add(firstType);

        // Resolve additional parameter types.
        foreach (var typeRef in ctorConstraint.ParamTypes.TypeReferences)
        {
            var resolvedType = ResolveConstraintParamType(
                typeRef, scope, symbolTable, declaringNode, asts, compilation, diagnostics, sourceText);
            paramTypes.Add(resolvedType);
        }

        return paramTypes;
    }

    /// <summary>
    /// Resolves a single type reference within a constructor constraint parameter list.
    /// Uses scope-based lookup (same as Pass 1b base type resolution).
    /// </summary>
    private static IType ResolveConstraintParamType(
        TypeReferenceNode typeRef,
        IScope scope,
        SymbolTable symbolTable,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        SourceText? sourceText)
    {
        // For primitive type references, resolve through the stdlib path so
        // that the resolved type is the same instance used during Pass 2
        // (e.g., Standard.Int32 rather than the legacy "int" singleton).
        // Falls back to legacy primitives when stdlib is not loaded (unit tests).
        if (typeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef)
        {
            try
            {
                var globalScope = FindGlobalScopeFromIScope(scope);
                if (globalScope is not null)
                    return ResolvePrimitiveKeyword(globalScope, symbolTable, primitiveRef.PrimitiveType);
            }
            catch (StdlibMissingException)
            {
                // Fall through to legacy path.
            }
            return ResolveLegacyPrimitive(primitiveRef.PrimitiveType);
        }

        // For named type references, look up in scope.
        if (typeRef is NamedTypeRefTypeReferenceNode namedRef)
        {
            var typeName = ExtractQualifiedName(namedRef.Name, sourceText);

            var resolvedSymbol = scope.Lookup(typeName);
            if (resolvedSymbol?.DeclaredType is not null)
                return resolvedSymbol.DeclaredType;

            diagnostics.Add(MakeDiagnosticWithLocation(
                ConstraintUnresolvableType.Value,
                DiagnosticSeverity.Error,
                $"Constraint type '{typeName}' could not be resolved.",
                declaringNode,
                sourceText));
            return ErrorType.Instance;
        }

        return ErrorType.Instance;
    }

    /// <summary>
    /// Resolves the type reference in a type constraint (<c>where T : SomeType</c>).
    /// Returns null if the type cannot be resolved (diagnostic already emitted).
    /// </summary>
    private static IType? ResolveConstraintTypeReference(
        TypeConstraintConstraintNode typeConstraint,
        ISymbol declaringSymbol,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        SourceText? sourceText)
    {
        var typeRef = typeConstraint.Type;
        var scope = declaringSymbol.ContainingScope;

        // For named type references, look up in scope.
        if (typeRef is NamedTypeRefTypeReferenceNode namedRef)
        {
            var typeName = ExtractQualifiedName(namedRef.Name, sourceText);

            var resolvedSymbol = scope.Lookup(typeName);
            if (resolvedSymbol?.DeclaredType is not null)
            {
                var resolvedType = resolvedSymbol.DeclaredType;

                // Handle generic type arguments if present.
                if (namedRef.TypeArgs is not null && resolvedType is ClassType or InterfaceType or StructType)
                {
                    resolvedType = ResolveGenericTypeReference(
                        resolvedType, namedRef.TypeArgs, typeName,
                        declaringSymbol.DeclaredType ?? ErrorType.Instance,
                        declaringNode, asts, compilation, diagnostics);
                }

                return resolvedType;
            }

            diagnostics.Add(MakeDiagnosticWithLocation(
                ConstraintUnresolvableType.Value,
                DiagnosticSeverity.Error,
                $"Constraint type '{typeName}' could not be resolved.",
                declaringNode,
                sourceText));
            return null;
        }

        // For other type reference forms (primitives, etc.), they're not valid as constraints
        // but we still try to resolve them gracefully.
        diagnostics.Add(MakeDiagnosticWithLocation(
            ConstraintUnresolvableType.Value,
            DiagnosticSeverity.Error,
            "Constraint type could not be resolved.",
            declaringNode,
            sourceText));
        return null;
    }

    /// <summary>
    /// Classifies a resolved constraint type as a base class, interface, or trait constraint
    /// and adds it to the appropriate collection. Also tracks base class and struct types
    /// for validation purposes.
    /// </summary>
    private static void ClassifyConstraintType(
        IType resolvedType,
        ref IType? baseClassConstraint,
        List<IType> interfaceConstraints,
        List<IType> traitConstraints,
        List<IType> baseClassConstraints,
        List<IType> structTypeConstraints)
    {
        // Unwrap ConstructedType to check the underlying definition.
        var definitionType = resolvedType is ConstructedType constructed
            ? constructed.GenericDefinition
            : resolvedType;

        switch (definitionType)
        {
            case InterfaceType:
                interfaceConstraints.Add(resolvedType);
                break;

            case TraitType:
                traitConstraints.Add(resolvedType);
                break;

            case ClassType:
                // Track all base class constraints for MIST_CONSTRAINT_0004 validation.
                baseClassConstraints.Add(resolvedType);
                // Store the first one as the effective base class constraint.
                baseClassConstraint ??= resolvedType;
                break;

            case StructType:
                // Track struct types used as constraints for MIST_CONSTRAINT_0005 validation.
                structTypeConstraints.Add(resolvedType);
                break;

            case TypeParameterType:
                // A type parameter used as a constraint (e.g., where U : T).
                // Treat as an interface-like constraint for now.
                interfaceConstraints.Add(resolvedType);
                break;

            default:
                // Other types used as constraints — store as interface constraint.
                interfaceConstraints.Add(resolvedType);
                break;
        }
    }

    /// <summary>
    /// Validates constraint combinations after all constraints in a clause are resolved.
    /// Emits diagnostics for invalid combinations (Requirements 4.1–4.5).
    /// </summary>
    private static void ValidateConstraintCombinations(
        bool hasClassConstraint,
        bool hasStructConstraint,
        List<IType> baseClassConstraints,
        List<IType> structTypeConstraints,
        List<IReadOnlyList<IType>> constructorConstraints,
        TypeParameterType typeParam,
        GenericConstraintClauseWhereClauseNode clause,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // MIST_CONSTRAINT_0002: class + struct mutual exclusion.
        if (hasClassConstraint && hasStructConstraint)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                ConstraintClassStructMutualExclusion.Value,
                DiagnosticSeverity.Error,
                $"The 'class' and 'struct' constraints are mutually exclusive on type parameter '{typeParam.Name}'.",
                clause,
                sourceText));
        }

        // MIST_CONSTRAINT_0003: struct + new() redundancy (warning).
        if (hasStructConstraint)
        {
            foreach (var ctorParams in constructorConstraints)
            {
                if (ctorParams.Count == 0)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        ConstraintStructNewRedundant.Value,
                        DiagnosticSeverity.Warning,
                        $"The 'new()' constraint is redundant on type parameter '{typeParam.Name}' because all structs have a parameterless constructor.",
                        clause,
                        sourceText));
                    break;
                }
            }
        }

        // MIST_CONSTRAINT_0004: Multiple base class constraints.
        if (baseClassConstraints.Count > 1)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                ConstraintMultipleBaseClasses.Value,
                DiagnosticSeverity.Error,
                $"At most one base class constraint is allowed on type parameter '{typeParam.Name}'.",
                clause,
                sourceText));
        }

        // MIST_CONSTRAINT_0005: Struct used as base class constraint.
        foreach (var structType in structTypeConstraints)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                ConstraintStructAsBaseClass.Value,
                DiagnosticSeverity.Error,
                $"Struct '{structType.DisplayName}' cannot be used as a base class constraint.",
                clause,
                sourceText));
        }

        // MIST_CONSTRAINT_0006: Base class + struct incompatibility.
        if (baseClassConstraints.Count > 0 && hasStructConstraint)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                ConstraintBaseClassStructIncompatible.Value,
                DiagnosticSeverity.Error,
                $"A base class constraint is incompatible with the 'struct' constraint on type parameter '{typeParam.Name}'.",
                clause,
                sourceText));
        }
    }

    /// <summary>
    /// Finds the compilation unit path for any symbol by looking up its declaring
    /// node in the symbol table and walking the ASTs.
    /// </summary>
    private static string? FindCompilationUnitPathForSymbol(
        ISymbol symbol,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        var declaringNode = symbolTable.GetDeclaringNode(symbol);
        if (declaringNode is null)
            return null;

        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsTypeDeclarationNode(cu, declaringNode))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source?.Path : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for any symbol by looking up its declaring
    /// node in the symbol table and walking the ASTs.
    /// </summary>
    private static SourceText? FindSourceTextForSymbol(
        ISymbol symbol,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        var declaringNode = symbolTable.GetDeclaringNode(symbol);
        if (declaringNode is null)
            return null;

        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsTypeDeclarationNode(cu, declaringNode))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether a compilation unit contains the given declaring node
    /// (class, struct, or interface declaration).
    /// </summary>
    private static bool ContainsTypeDeclarationNode(CompilationUnitNode cu, Storm.Runtime.SyntaxNode declaringNode)
    {
        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    if (MatchesTypeDeclaration(typeItem.TypeDeclaration, declaringNode))
                        return true;
                    break;
                case NamespaceItemTopLevelItemNode nsItem:
                    var namespaceTypes = GetNamespaceBodyTypes(nsItem.NamespaceDeclaration);
                    foreach (var typeDecl in namespaceTypes)
                    {
                        if (MatchesTypeDeclaration(typeDecl, declaringNode))
                            return true;
                    }
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a type declaration node wraps the given declaring node.
    /// </summary>
    private static bool MatchesTypeDeclaration(TypeDeclarationNode typeDecl, Storm.Runtime.SyntaxNode declaringNode)
    {
        return typeDecl switch
        {
            ClassTypeTypeDeclarationNode ct => ReferenceEquals(ct.ClassDeclaration, declaringNode),
            StructTypeTypeDeclarationNode st => ReferenceEquals(st.StructDeclaration, declaringNode),
            InterfaceTypeTypeDeclarationNode it => ReferenceEquals(it.InterfaceDeclaration, declaringNode),
            TraitTypeTypeDeclarationNode tt => ReferenceEquals(tt.TraitDeclaration, declaringNode),
            _ => false,
        };
    }

    /// <summary>
    /// Checks whether a compilation unit contains a given node by reference equality.
    /// Walks all top-level items including type declarations and namespace members.
    /// </summary>
    private static bool ContainsNodeByReference(CompilationUnitNode cu, Storm.Runtime.SyntaxNode targetNode)
    {
        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    if (MatchesTypeDeclaration(typeItem.TypeDeclaration, targetNode))
                        return true;
                    break;
                case NamespaceItemTopLevelItemNode nsItem:
                    var namespaceTypes = GetNamespaceBodyTypes(nsItem.NamespaceDeclaration);
                    foreach (var typeDecl in namespaceTypes)
                    {
                        if (MatchesTypeDeclaration(typeDecl, targetNode))
                            return true;
                    }
                    break;
            }
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1g — Resolve trait parent references
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the inheritance clause on each trait declaration.
    /// For each <see cref="TraitDeclarationNode"/> with a <c>Parents</c> base list:
    /// <list type="number">
    ///   <item>Resolve each parent name to a <see cref="TraitType"/>.</item>
    ///   <item>If the parent is generic, validate type arguments and construct a <see cref="ConstructedTraitType"/>.</item>
    ///   <item>Check for duplicate parents (same trait with same type arguments).</item>
    ///   <item>Store the resolved list on <see cref="TraitType.ParentTraits"/>.</item>
    /// </list>
    /// </summary>
    private static void Pass1_ResolveTraitParents(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Trait || symbol.DeclaredType is not TraitType traitType)
                continue;

            var declaringNode = symbolTable.GetDeclaringNode(symbol);
            if (declaringNode is not TraitDeclarationNode traitDecl)
                continue;

            if (traitDecl.Parents is null)
                continue;

            var entries = ExtractBaseListEntries(traitDecl.Parents);
            if (entries.Count == 0)
                continue;

            var parentScope = symbol.ContainingScope;
            var resolvedParents = new List<IType>();

            foreach (var entry in entries)
            {
                var name = entry.Name.Lexeme;
                if (string.IsNullOrEmpty(name))
                    continue;

                var resolvedSymbol = parentScope.Lookup(name);

                if (resolvedSymbol is null)
                {
                    var sourceText = FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.TraitParentNotTrait.Value,
                        DiagnosticSeverity.Error,
                        $"'{name}' in the trait inheritance clause could not be found.",
                        traitDecl,
                        sourceText));
                    continue;
                }

                if (resolvedSymbol.DeclaredType is not TraitType parentTraitType)
                {
                    var sourceText = FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.TraitParentNotTrait.Value,
                        DiagnosticSeverity.Error,
                        $"'{name}' in the trait inheritance clause is not a trait. Only traits can be extended by other traits.",
                        traitDecl,
                        sourceText));
                    continue;
                }

                // Resolve the parent trait reference (handling generics).
                var parentTypeParams = parentTraitType.TypeParameters;
                var hasTypeArgs = entry.TypeArgs is not null;

                if (hasTypeArgs)
                {
                    if (parentTypeParams.Count == 0)
                    {
                        // Parent is not generic but type arguments were provided.
                        var sourceText = FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.NotGeneric.Value,
                            DiagnosticSeverity.Error,
                            $"Trait '{name}' is not generic and does not accept type arguments.",
                            traitDecl,
                            sourceText));
                        resolvedParents.Add(parentTraitType);
                        continue;
                    }

                    // Resolve type arguments.
                    var resolvedTypeArgs = ResolveTypeArgumentList(
                        entry.TypeArgs!, traitType, traitDecl, asts, compilation, diagnostics);

                    if (resolvedTypeArgs.Count != parentTypeParams.Count)
                    {
                        // Arity mismatch.
                        var sourceText = FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.ArityMismatch.Value,
                            DiagnosticSeverity.Error,
                            $"Trait '{name}' expects {parentTypeParams.Count} type argument(s) but received {resolvedTypeArgs.Count}.",
                            traitDecl,
                            sourceText));
                        resolvedParents.Add(parentTraitType);
                        continue;
                    }

                    // Construct a ConstructedTraitType.
                    var constructedParent = new ConstructedTraitType(parentTraitType, resolvedTypeArgs);
                    resolvedParents.Add(constructedParent);
                }
                else
                {
                    // No type arguments provided.
                    if (parentTypeParams.Count > 0)
                    {
                        // Parent is generic but no type arguments were provided.
                        var sourceText = FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.MissingTypeArguments.Value,
                            DiagnosticSeverity.Error,
                            $"Generic trait '{name}' requires type arguments.",
                            traitDecl,
                            sourceText));
                        resolvedParents.Add(parentTraitType);
                        continue;
                    }

                    // Non-generic parent — use the TraitType directly.
                    resolvedParents.Add(parentTraitType);
                }
            }

            // Check for duplicate parents.
            var seen = new List<IType>();
            var deduplicatedParents = new List<IType>();

            foreach (var parent in resolvedParents)
            {
                if (IsDuplicateParent(parent, seen))
                {
                    var parentName = parent.DisplayName;
                    var sourceText = FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.TraitDuplicateParent.Value,
                        DiagnosticSeverity.Error,
                        $"Trait '{traitType.DisplayName}' lists parent '{parentName}' more than once.",
                        traitDecl,
                        sourceText));
                }
                else
                {
                    seen.Add(parent);
                    deduplicatedParents.Add(parent);
                }
            }

            traitType.ParentTraits = deduplicatedParents;
        }
    }

    /// <summary>
    /// Determines whether a parent trait is a duplicate of one already seen.
    /// Two parents are considered duplicates if they reference the same trait definition
    /// with the same type arguments.
    /// </summary>
    private static bool IsDuplicateParent(IType candidate, List<IType> seen)
    {
        foreach (var existing in seen)
        {
            if (AreSameTraitReference(candidate, existing))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Compares two trait references for equality (same trait definition, same type arguments).
    /// </summary>
    private static bool AreSameTraitReference(IType a, IType b)
    {
        if (a is TraitType traitA && b is TraitType traitB)
            return ReferenceEquals(traitA, traitB);

        if (a is ConstructedTraitType ctA && b is ConstructedTraitType ctB)
        {
            if (!ReferenceEquals(ctA.GenericTraitDefinition, ctB.GenericTraitDefinition))
                return false;

            if (ctA.TypeArguments.Count != ctB.TypeArguments.Count)
                return false;

            for (var i = 0; i < ctA.TypeArguments.Count; i++)
            {
                if (!AreSameTypeArgument(ctA.TypeArguments[i], ctB.TypeArguments[i]))
                    return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Compares two type arguments for structural equality.
    /// </summary>
    private static bool AreSameTypeArgument(IType a, IType b)
    {
        // Same reference (covers TypeParameterType, TraitType, ClassType, etc.)
        if (ReferenceEquals(a, b))
            return true;

        // Both are constructed types with the same definition and arguments.
        if (a is ConstructedType ctA && b is ConstructedType ctB)
        {
            if (!ReferenceEquals(ctA.GenericDefinition, ctB.GenericDefinition))
                return false;
            if (ctA.TypeArguments.Count != ctB.TypeArguments.Count)
                return false;
            for (var i = 0; i < ctA.TypeArguments.Count; i++)
            {
                if (!AreSameTypeArgument(ctA.TypeArguments[i], ctB.TypeArguments[i]))
                    return false;
            }

            // Compare dependent value arguments (Requirements 8.1, 8.2).
            if (!DependentArgumentsEqual(ctA.DependentArguments, ctB.DependentArguments))
                return false;

            return true;
        }

        // Both are constructed trait types.
        if (a is ConstructedTraitType cttA && b is ConstructedTraitType cttB)
        {
            return AreSameTraitReference(cttA, cttB);
        }

        return false;
    }

    /// <summary>
    /// Compares two dependent argument lists for value equality.
    /// Two lists are equal when they have the same length and each pair of
    /// non-null entries has equal values (using <see cref="object.Equals(object?, object?)"/>).
    /// </summary>
    private static bool DependentArgumentsEqual(
        IReadOnlyList<DependentValueArgument?>? a,
        IReadOnlyList<DependentValueArgument?>? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            var argA = a[i];
            var argB = b[i];
            if (argA is null && argB is null)
                continue;
            if (argA is null || argB is null)
                return false;
            if (!Equals(argA.Value, argB.Value))
                return false;
        }
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1b (cont.) — Detect trait inheritance cycles
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Performs DFS on the trait inheritance graph to detect cycles.
    /// When a cycle is detected, emits <c>MIST_TRAIT_INHERITANCE_CYCLE</c>
    /// for each trait involved and marks them as cyclic so they are excluded
    /// from satisfaction computation.
    /// </summary>
    private static void Pass1_DetectTraitCycles(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Build adjacency list: each TraitType -> list of parent TraitTypes
        // (for ConstructedTraitType parents, we use the GenericTraitDefinition).
        var allTraits = new List<TraitType>();
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind == SymbolKind.Trait && symbol.DeclaredType is TraitType tt)
                allTraits.Add(tt);
        }

        // DFS coloring: White = unvisited, Gray = on current path, Black = fully processed.
        // 0 = White, 1 = Gray, 2 = Black
        var color = new Dictionary<TraitType, int>();
        foreach (var trait in allTraits)
            color[trait] = 0; // White

        foreach (var trait in allTraits)
        {
            if (color[trait] == 0)
            {
                DetectTraitCyclesDfsVisit(
                    trait, color, new List<TraitType>(),
                    symbolTable, asts, compilation, diagnostics);
            }
        }
    }

    /// <summary>
    /// Recursive DFS visitor for trait cycle detection. Uses white/gray/black
    /// coloring. When a gray node is revisited, a cycle is detected.
    /// </summary>
    private static void DetectTraitCyclesDfsVisit(
        TraitType current,
        Dictionary<TraitType, int> color,
        List<TraitType> path,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        color[current] = 1; // Gray — on current path
        path.Add(current);

        foreach (var parentType in current.ParentTraits)
        {
            // Extract the underlying TraitType from the parent reference.
            var parentTrait = parentType switch
            {
                TraitType tt => tt,
                ConstructedTraitType ct => ct.GenericTraitDefinition,
                _ => null,
            };

            if (parentTrait is null)
                continue;

            // If the parent trait is not in our color map, it's from a
            // different compilation unit or is otherwise unresolvable — skip.
            if (!color.ContainsKey(parentTrait))
                continue;

            if (color[parentTrait] == 1)
            {
                // Cycle detected — parentTrait is on the current path.
                var cycleStart = path.IndexOf(parentTrait);
                var cycleMembers = path.Skip(cycleStart).ToList();

                // Mark all traits in the cycle as cyclic.
                foreach (var cyclicTrait in cycleMembers)
                    cyclicTrait.IsCyclic = true;

                // Build the cycle description string.
                var cycleNames = cycleMembers.Select(t => t.DisplayName).ToList();
                cycleNames.Add(parentTrait.DisplayName);
                var chainStr = string.Join(" -> ", cycleNames);

                // Emit a diagnostic for each trait in the cycle.
                foreach (var cyclicTrait in cycleMembers)
                {
                    Storm.Runtime.SyntaxNode? declaringNode = null;
                    ISymbol? traitSymbol = null;
                    foreach (var sym in symbolTable.AllSymbols)
                    {
                        if (ReferenceEquals(sym.DeclaredType, cyclicTrait))
                        {
                            traitSymbol = sym;
                            declaringNode = symbolTable.GetDeclaringNode(sym);
                            break;
                        }
                    }

                    if (declaringNode is not null)
                    {
                        var sourceText = FindSourceTextForSymbol(
                            traitSymbol!, symbolTable, asts, compilation);
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.TraitInheritanceCycle.Value,
                            DiagnosticSeverity.Error,
                            $"Trait inheritance cycle detected: {chainStr}.",
                            declaringNode,
                            sourceText));
                    }
                    else
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticId.TraitInheritanceCycle,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"Trait inheritance cycle detected: {chainStr}.",
                            Location: default,
                            CompilationUnitPath: "<unknown>",
                            AnalyzerName: null));
                    }
                }

                // Don't continue DFS into the cyclic parent — it's already gray.
                continue;
            }

            if (color[parentTrait] == 0)
            {
                // White — not yet visited, recurse.
                DetectTraitCyclesDfsVisit(
                    parentTrait, color, path,
                    symbolTable, asts, compilation, diagnostics);
            }
            // If Black, already fully processed — no cycle through this path.
        }

        path.RemoveAt(path.Count - 1);
        color[current] = 2; // Black — fully processed
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1g — Resolve with-block declarations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves <c>with</c> block declarations collected during Pass 1.
    /// For each <see cref="WithDeclarationNode"/>:
    /// <list type="number">
    ///   <item>Resolve the type name to a <see cref="ClassType"/> or <see cref="StructType"/>.</item>
    ///   <item>Resolve the trait name to a <see cref="TraitType"/> (or <see cref="ConstructedTraitType"/> for generic traits).</item>
    ///   <item>Check for duplicate <c>with</c> blocks for the same type-trait pair.</item>
    ///   <item>Validate each member mapping (source exists and is public, target exists in trait, types compatible after substitution).</item>
    ///   <item>Store validated mappings in the <see cref="WithBlockRegistry"/>.</item>
    /// </list>
    /// </summary>
    private static void Pass1_ResolveWithBlocks(
        List<WithDeclarationNode> withDeclarations,
        Scope globalScope,
        SymbolTable symbolTable,
        WithBlockRegistry registry,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Track seen type-trait pairs for duplicate detection.
        var seenPairs = new HashSet<(IType, IType)>();

        foreach (var withDecl in withDeclarations)
        {
            var typeName = withDecl.TypeName.Lexeme;
            var traitName = withDecl.TraitName.Lexeme;

            // 1. Resolve type name to a ClassType or StructType.
            var resolvedType = ResolveWithBlockTypeName(typeName, globalScope, symbolTable);
            if (resolvedType is null)
            {
                diagnostics.Add(new Diagnostic(
                    TraitWithUndefinedType,
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"The type '{typeName}' in the `with` block could not be found.",
                    Location: default,
                    CompilationUnitPath: "<unknown>",
                    AnalyzerName: null));
                continue; // Skip this with block.
            }

            // 2. Resolve trait name to a TraitType.
            var resolvedTraitType = ResolveWithBlockTraitName(traitName, globalScope, symbolTable);
            if (resolvedTraitType is null)
            {
                diagnostics.Add(new Diagnostic(
                    TraitWithUndefinedTrait,
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"The trait '{traitName}' in the `with` block could not be found.",
                    Location: default,
                    CompilationUnitPath: "<unknown>",
                    AnalyzerName: null));
                continue; // Skip this with block.
            }

            // 2b. Handle generic trait type arguments (Task 7.1).
            // Resolve traitName + traitTypeArgs to either a TraitType (non-generic)
            // or ConstructedTraitType (generic).
            IType resolvedTrait;
            IReadOnlyList<IType>? typeParams = null;
            IReadOnlyList<IType>? typeArgs = null;

            var traitTypeParams = resolvedTraitType.TypeParameters;
            var hasTypeArgs = withDecl.TraitTypeArgs is not null;

            if (hasTypeArgs)
            {
                if (traitTypeParams.Count == 0)
                {
                    // Trait is not generic but type arguments were provided.
                    // Use the trait directly (non-fatal, ignore extra args).
                    resolvedTrait = resolvedTraitType;
                }
                else
                {
                    // Resolve type arguments.
                    var resolvedTypeArgs = ResolveTypeArgumentList(
                        withDecl.TraitTypeArgs!, resolvedType, withDecl, asts, compilation, diagnostics);

                    if (resolvedTypeArgs.Count != traitTypeParams.Count)
                    {
                        // Arity mismatch.
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId(DiagnosticId.ArityMismatch.Value),
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"Trait '{traitName}' expects {traitTypeParams.Count} type argument(s) but received {resolvedTypeArgs.Count}.",
                            Location: default,
                            CompilationUnitPath: "<unknown>",
                            AnalyzerName: null));
                        continue; // Skip this with block.
                    }

                    // Construct a ConstructedTraitType.
                    resolvedTrait = new ConstructedTraitType(resolvedTraitType, resolvedTypeArgs);
                    typeParams = traitTypeParams;
                    typeArgs = resolvedTypeArgs;
                }
            }
            else
            {
                // No type arguments provided.
                if (traitTypeParams.Count > 0)
                {
                    // Trait is generic but no type arguments were provided.
                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId(DiagnosticId.MissingTypeArguments.Value),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"Generic trait '{traitName}' requires type arguments.",
                        Location: default,
                        CompilationUnitPath: "<unknown>",
                        AnalyzerName: null));
                    continue; // Skip this with block.
                }

                // Non-generic trait — use the TraitType directly.
                resolvedTrait = resolvedTraitType;
            }

            // 3. Check for duplicate with blocks for the same type-trait pair.
            if (!seenPairs.Add((resolvedType, resolvedTrait)))
            {
                diagnostics.Add(new Diagnostic(
                    TraitDuplicateWith,
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Duplicate `with` block for type '{typeName}' and trait '{resolvedTrait.DisplayName}'.",
                    Location: default,
                    CompilationUnitPath: "<unknown>",
                    AnalyzerName: null));
                continue; // Skip duplicate.
            }

            // 4. Validate each member mapping.
            foreach (var mapping in withDecl.Mappings)
            {
                var sourceName = mapping.SourceName.Lexeme;
                var targetName = mapping.TargetName.Lexeme;

                // 4a. Resolve source member on the type (must be public).
                var sourceMember = FindPublicMember(resolvedType, sourceName, symbolTable);
                if (sourceMember is null)
                {
                    diagnostics.Add(new Diagnostic(
                        TraitWithUndefinedSource,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"The member '{sourceName}' does not exist as a public member on type '{typeName}'.",
                        Location: default,
                        CompilationUnitPath: "<unknown>",
                        AnalyzerName: null));
                    continue; // Skip this mapping.
                }

                // 4b. Resolve target member in the trait.
                var targetMember = FindTraitMember(resolvedTraitType, targetName);
                if (targetMember is null)
                {
                    diagnostics.Add(new Diagnostic(
                        TraitWithUndefinedTarget,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"The member '{targetName}' does not exist in trait '{traitName}'.",
                        Location: default,
                        CompilationUnitPath: "<unknown>",
                        AnalyzerName: null));
                    continue; // Skip this mapping.
                }

                // 4c. Check type compatibility between source and target.
                // At this stage (Pass 1), member DeclaredType may not yet be resolved.
                // Compare the type references from the AST nodes structurally.
                // For generic traits, substitute type parameters with type arguments
                // before comparing (e.g., trait member `T Value` with T=int should
                // match source member `int Value`).
                var sourceTypeRef = GetMemberTypeReference(sourceMember, symbolTable);
                var targetTypeRef = GetTraitMemberTypeReference(targetMember, symbolTable);

                if (sourceTypeRef is not null && targetTypeRef is not null)
                {
                    // Find the correct source text for the trait member's type reference.
                    // Use reference-equality containment: walk each AST's compilation unit
                    // and check if it contains the trait declaration node by reference.
                    SourceText? traitMemberSourceText = null;
                    var traitDeclSymbol = symbolTable.AllSymbols.FirstOrDefault(s =>
                        s.Kind == SymbolKind.Trait &&
                        ReferenceEquals(s.DeclaredType, resolvedTraitType));
                    var traitDeclNode = traitDeclSymbol is not null
                        ? symbolTable.GetDeclaringNode(traitDeclSymbol)
                        : null;

                    if (traitDeclNode is not null)
                    {
                        for (var cuIdx = 0; cuIdx < asts.Length; cuIdx++)
                        {
                            if (asts[cuIdx] is CompilationUnitNode cu &&
                                ContainsNodeByReference(cu, traitDeclNode))
                            {
                                traitMemberSourceText = cuIdx < compilation.Units.Length
                                    ? compilation.Units[cuIdx].Source
                                    : null;
                                break;
                            }
                        }
                    }

                    var typesMatch = typeParams is not null && typeArgs is not null
                        ? TypeReferencesMatchWithSubstitution(sourceTypeRef, targetTypeRef, typeParams, typeArgs, traitMemberSourceText, asts, compilation)
                        : TypeReferencesMatch(sourceTypeRef, targetTypeRef);

                    if (!typesMatch)
                    {
                        var expectedTypeStr = typeParams is not null && typeArgs is not null
                            ? FormatTypeReferenceWithSubstitution(targetTypeRef, typeParams, typeArgs, traitMemberSourceText, asts, compilation)
                            : FormatTypeReference(targetTypeRef);
                        var actualTypeStr = FormatTypeReference(sourceTypeRef);
                        diagnostics.Add(new Diagnostic(
                            TraitWithTypeMismatch,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"Cannot map '{typeName}.{sourceName}' to '{resolvedTrait.DisplayName}.{targetName}': expected type '{expectedTypeStr}', found '{actualTypeStr}'.",
                            Location: default,
                            CompilationUnitPath: "<unknown>",
                            AnalyzerName: null));
                        continue; // Skip this mapping.
                    }
                }

                // 5. Store validated mapping using the resolved trait key
                //    (ConstructedTraitType for generic traits, TraitType for non-generic).
                registry.AddMapping(resolvedType, resolvedTrait, new ValidatedMemberMapping
                {
                    SourceMember = sourceMember,
                    TargetMember = targetMember,
                    SourceName = sourceName,
                    TargetName = targetName,
                });
            }
        }
    }

    /// <summary>
    /// Resolves a type name from a <c>with</c> block to a <see cref="ClassType"/>
    /// or <see cref="StructType"/> by searching all namespace scopes.
    /// </summary>
    private static IType? ResolveWithBlockTypeName(
        string typeName, Scope globalScope, SymbolTable symbolTable)
    {
        // Search all namespace scopes for the type.
        foreach (var nsMember in globalScope.Members)
        {
            if (nsMember.Kind == SymbolKind.Namespace)
            {
                var nsScope = FindNamespaceScopeForSymbol(nsMember, symbolTable);
                if (nsScope is not null)
                {
                    var found = nsScope.Lookup(typeName);
                    if (found?.DeclaredType is ClassType or StructType)
                        return found.DeclaredType;
                }
            }
        }

        // Also search the global scope directly (types without a namespace).
        var globalFound = globalScope.Lookup(typeName);
        if (globalFound?.DeclaredType is ClassType or StructType)
            return globalFound.DeclaredType;

        return null;
    }

    /// <summary>
    /// Resolves a trait name from a <c>with</c> block to a <see cref="TraitType"/>
    /// by searching all namespace scopes.
    /// </summary>
    private static TraitType? ResolveWithBlockTraitName(
        string traitName, Scope globalScope, SymbolTable symbolTable)
    {
        // Search all namespace scopes for the trait.
        foreach (var nsMember in globalScope.Members)
        {
            if (nsMember.Kind == SymbolKind.Namespace)
            {
                var nsScope = FindNamespaceScopeForSymbol(nsMember, symbolTable);
                if (nsScope is not null)
                {
                    var found = nsScope.Lookup(traitName);
                    if (found?.DeclaredType is TraitType traitType)
                        return traitType;
                }
            }
        }

        // Also search the global scope directly.
        var globalFound = globalScope.Lookup(traitName);
        if (globalFound?.DeclaredType is TraitType globalTrait)
            return globalTrait;

        return null;
    }

    /// <summary>
    /// Finds the scope associated with a namespace symbol by looking at
    /// the types registered in that namespace.
    /// </summary>
    private static IScope? FindNamespaceScopeForSymbol(
        ISymbol namespaceSymbol, SymbolTable symbolTable)
    {
        // Find any type symbol whose ContainingScope is the namespace scope.
        foreach (var sym in symbolTable.AllSymbols)
        {
            if (sym.ContainingScope != namespaceSymbol.ContainingScope
                && sym.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Trait)
            {
                // Check if this symbol's containing scope is a child of the global scope
                // and the namespace name matches.
                var declaringNode = symbolTable.GetDeclaringNode(sym);
                if (declaringNode is not null)
                {
                    // The symbol's containing scope is the namespace scope if
                    // the namespace symbol's name matches the type's containing namespace.
                    var typeNamespace = GetTypeContainingNamespace(sym.DeclaredType);
                    if (typeNamespace == namespaceSymbol.Name)
                        return sym.ContainingScope;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the containing namespace from a type.
    /// </summary>
    private static string? GetTypeContainingNamespace(IType? type)
    {
        return type switch
        {
            ClassType ct => ct.ContainingNamespace,
            StructType st => st.ContainingNamespace,
            InterfaceType it => it.ContainingNamespace,
            TraitType tt => tt.ContainingNamespace,
            _ => null,
        };
    }

    /// <summary>
    /// Finds a public member on a type by name. Returns <c>null</c> if the member
    /// does not exist or is not public.
    /// </summary>
    private static ISymbol? FindPublicMember(
        IType type, string memberName, SymbolTable symbolTable)
    {
        var memberScope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return null;

        // Search for a member with the given name.
        foreach (var member in memberScope.Members)
        {
            if (member.Name == memberName
                && member.Kind is SymbolKind.Field or SymbolKind.Method or SymbolKind.Property)
            {
                // Check if the member is public by examining its AST node modifiers.
                if (IsMemberPublic(member, symbolTable))
                    return member;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a member symbol is public by checking its declaring
    /// AST node's modifiers. A member is considered public if it has an explicit
    /// <c>public</c> modifier.
    /// </summary>
    private static bool IsMemberPublic(ISymbol member, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(member);
        if (declaringNode is null)
            return false;

        ImmutableArray<ModifierNode>? modifiers = declaringNode switch
        {
            FieldDeclarationNode f => f.Modifiers,
            MethodDeclarationNode m => m.Modifiers,
            BlockPropertyPropertyDeclarationNode bp => bp.Modifiers,
            ExpressionBodiedPropertyPropertyDeclarationNode ep => ep.Modifiers,
            ConstructorDeclarationNode c => c.Modifiers,
            OperatorDeclarationNode o => o.Modifiers,
            _ => null,
        };

        if (modifiers is null)
            return false;

        return modifiers.Value.Any(m => m is PublicModifierModifierNode);
    }

    /// <summary>
    /// Finds a member in a trait's member scope by name.
    /// </summary>
    private static ISymbol? FindTraitMember(TraitType trait, string memberName)
    {
        foreach (var member in trait.MemberScope.Members)
        {
            if (member.Name == memberName)
                return member;
        }
        return null;
    }

    /// <summary>
    /// Extracts the type reference AST node from a type member's declaring node.
    /// Used for type compatibility checking in <c>with</c> block validation.
    /// </summary>
    private static TypeReferenceNode? GetMemberTypeReference(
        ISymbol member, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(member);
        return declaringNode switch
        {
            FieldDeclarationNode f => f.Type,
            MethodDeclarationNode m => m.ReturnType,
            BlockPropertyPropertyDeclarationNode bp => bp.Type,
            ExpressionBodiedPropertyPropertyDeclarationNode ep => ep.Type,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the type reference AST node from a trait member's declaring node.
    /// Used for type compatibility checking in <c>with</c> block validation.
    /// </summary>
    private static TypeReferenceNode? GetTraitMemberTypeReference(
        ISymbol traitMember, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(traitMember);
        return declaringNode switch
        {
            TraitFieldDeclarationNode f => f.Type,
            TraitMethodDeclarationNode m => m.ReturnType,
            TraitPropertyDeclarationNode p => p.Type,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Effective members computation for trait inheritance
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a trait member in the effective member set, carrying the
    /// original symbol and any type parameter substitution context needed
    /// for structural matching against implementing types.
    /// </summary>
    /// <param name="Member">The trait member symbol (field, method, or property).</param>
    /// <param name="TypeParams">
    /// Type parameters to substitute when matching (from the trait's definition).
    /// Null for non-generic traits or when no substitution is needed.
    /// </param>
    /// <param name="TypeArgs">
    /// Type arguments to substitute for the type parameters.
    /// Null for non-generic traits or when no substitution is needed.
    /// </param>
    /// <param name="SourceTrait">
    /// The trait (or constructed trait) that originally declared this member.
    /// Used for diagnostic messages.
    /// </param>
    internal readonly record struct EffectiveMember(
        ISymbol Member,
        IReadOnlyList<IType>? TypeParams,
        IReadOnlyList<IType>? TypeArgs,
        IType SourceTrait);

    /// <summary>
    /// Computes the effective members for a trait — the complete set of member
    /// signatures that a type must satisfy. This includes:
    /// <list type="number">
    ///   <item>The trait's own declared members (with type argument substitution
    ///         for <see cref="ConstructedTraitType"/>).</item>
    ///   <item>All members inherited from parent traits (transitively), with
    ///         appropriate type argument substitution composed through the chain.</item>
    /// </list>
    /// Members are deduplicated by name: if a member with the same name appears
    /// from multiple inheritance paths, it is counted once (diamond inheritance).
    /// </summary>
    /// <param name="trait">
    /// The trait to compute effective members for. May be a <see cref="TraitType"/>
    /// (non-generic or unsubstituted) or a <see cref="ConstructedTraitType"/>
    /// (generic with type arguments applied).
    /// </param>
    /// <returns>
    /// A list of <see cref="EffectiveMember"/> entries representing all members
    /// a type must satisfy, with substitution context attached.
    /// </returns>
    internal static List<EffectiveMember> GetEffectiveMembers(IType trait)
    {
        var result = new List<EffectiveMember>();
        var seenNames = new HashSet<string>();
        var visited = new HashSet<TraitType>();

        CollectEffectiveMembers(trait, outerTypeParams: null, outerTypeArgs: null, result, seenNames, visited);

        return result;
    }

    /// <summary>
    /// Recursively collects effective members from a trait and its parent chain.
    /// </summary>
    /// <param name="trait">The trait (or constructed trait) to collect from.</param>
    /// <param name="outerTypeParams">
    /// Type parameters from an outer constructed context that need to be composed
    /// with this trait's own substitution. Null if no outer substitution.
    /// </param>
    /// <param name="outerTypeArgs">
    /// Type arguments from an outer constructed context. Null if no outer substitution.
    /// </param>
    /// <param name="result">Accumulator for effective members.</param>
    /// <param name="seenNames">Set of member names already added (for deduplication).</param>
    /// <param name="visited">Set of trait definitions already visited (prevents infinite loops on cycles).</param>
    private static void CollectEffectiveMembers(
        IType trait,
        IReadOnlyList<IType>? outerTypeParams,
        IReadOnlyList<IType>? outerTypeArgs,
        List<EffectiveMember> result,
        HashSet<string> seenNames,
        HashSet<TraitType> visited)
    {
        // Decompose the trait into its definition and any type arguments.
        TraitType traitDef;
        IReadOnlyList<IType>? effectiveTypeParams;
        IReadOnlyList<IType>? effectiveTypeArgs;

        if (trait is ConstructedTraitType ctt)
        {
            traitDef = ctt.GenericTraitDefinition;
            // Compose: if there's an outer substitution, apply it to this trait's type arguments.
            if (outerTypeParams is not null && outerTypeArgs is not null)
            {
                var composedArgs = new IType[ctt.TypeArguments.Count];
                for (var i = 0; i < ctt.TypeArguments.Count; i++)
                {
                    composedArgs[i] = SubstituteTypeInMember(ctt.TypeArguments[i], outerTypeParams, outerTypeArgs);
                }
                effectiveTypeParams = traitDef.TypeParameters;
                effectiveTypeArgs = composedArgs;
            }
            else
            {
                effectiveTypeParams = traitDef.TypeParameters;
                effectiveTypeArgs = ctt.TypeArguments;
            }
        }
        else if (trait is TraitType tt)
        {
            traitDef = tt;
            // For a plain TraitType, pass through any outer substitution context.
            // This handles the case where a non-generic trait is a parent of a
            // generic trait — its members don't need substitution themselves, but
            // we still track the context for consistency.
            effectiveTypeParams = outerTypeParams;
            effectiveTypeArgs = outerTypeArgs;
        }
        else
        {
            return; // Unknown trait type — skip.
        }

        // Guard against cycles (should already be detected, but be defensive).
        if (!visited.Add(traitDef))
            return;

        // Collect this trait's own declared members.
        foreach (var member in traitDef.MemberScope.Members)
        {
            // Skip type parameter symbols (registered as SymbolKind.Class with TypeParameterType).
            if (member.DeclaredType is TypeParameterType)
                continue;

            // Skip dependent type parameter symbols (e.g., Max:int in trait Bounded<Max:int>).
            if (member.DeclaredType is DependentTypeParameterType)
                continue;

            // Skip non-trait-member kinds (only fields, methods, properties are trait members).
            if (member.Kind is not (SymbolKind.Field or SymbolKind.Method or SymbolKind.Property))
                continue;

            // Deduplicate by name: first occurrence wins (diamond inheritance).
            if (!seenNames.Add(member.Name))
                continue;

            result.Add(new EffectiveMember(
                Member: member,
                TypeParams: effectiveTypeParams,
                TypeArgs: effectiveTypeArgs,
                SourceTrait: trait));
        }

        // Recursively collect from parent traits.
        foreach (var parent in traitDef.ParentTraits)
        {
            CollectEffectiveMembers(parent, effectiveTypeParams, effectiveTypeArgs, result, seenNames, visited);
        }

        // Remove from visited to allow the same trait to appear in different
        // branches of a diamond (its members are already deduplicated by name).
        visited.Remove(traitDef);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1g (cont.) — Resolve trait satisfaction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For each trait in the program and each class/struct, computes structural
    /// matches and combines them with <c>with</c> block mappings to determine
    /// which types satisfy which traits. Results are stored in the
    /// <see cref="TraitSatisfactionMap"/>.
    /// <list type="bullet">
    ///   <item>Emits <c>MIST_TRAIT_MEMBER_VISIBILITY</c> when a private member
    ///         matches a trait member name.</item>
    ///   <item>Emits <c>MIST_TRAIT_WITH_UNSATISFIED</c> when a <c>with</c> block
    ///         exists but doesn't cover all unmapped members.</item>
    /// </list>
    /// </summary>
    private static void Pass1_ResolveTraitSatisfaction(
        SymbolTable symbolTable,
        Scope globalScope,
        WithBlockRegistry withBlockRegistry,
        TraitSatisfactionMap satisfactionMap,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Collect all traits.
        var traits = new List<(ISymbol Symbol, TraitType Trait)>();
        foreach (var sym in symbolTable.AllSymbols)
        {
            if (sym.Kind == SymbolKind.Trait && sym.DeclaredType is TraitType tt)
                traits.Add((sym, tt));
        }

        // Collect all classes and structs.
        var types = new List<(ISymbol Symbol, IType Type)>();
        foreach (var sym in symbolTable.AllSymbols)
        {
            if (sym.Kind == SymbolKind.Class && sym.DeclaredType is ClassType ct)
                types.Add((sym, ct));
            else if (sym.Kind == SymbolKind.Struct && sym.DeclaredType is StructType st)
                types.Add((sym, st));
        }

        // For each non-generic trait × type pair that has a with-block, attempt satisfaction.
        // Lazy model: only with-blocks trigger satisfaction during Pass 1.
        // Call-site structural matching is deferred to the type checker.
        foreach (var (_, trait) in traits)
        {
            // Skip cyclic traits — they cannot be meaningfully satisfied.
            if (trait.IsCyclic)
                continue;

            // Skip generic traits in this loop — they are handled below
            // as ConstructedTraitType instances with specific type arguments.
            if (trait.TypeParameters.Count > 0)
                continue;

            foreach (var (typeSym, type) in types)
            {
                // Only process types that have a with-block for this trait
                // OR have the trait in their base list (explicit declaration).
                var hasWithBlock = withBlockRegistry.HasMappings(type, trait);
                var hasTraitInBaseList = HasTraitInBaseList(typeSym, trait, symbolTable);
                if (!hasWithBlock && !hasTraitInBaseList)
                    continue;

                var typeName = typeSym.Name;
                var memberScope = type switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };

                if (memberScope is null)
                    continue;

                // 1. Structural matching: for each trait member, search the
                //    type's public members for a match.
                var structuralMatches = new Dictionary<string, ISymbol>();
                var privateMatches = new Dictionary<string, ISymbol>();

                foreach (var traitMember in trait.MemberScope.Members)
                {
                    // Skip dependent type parameter symbols — they are not
                    // members that need structural satisfaction.
                    if (traitMember.DeclaredType is DependentTypeParameterType)
                        continue;

                    var matched = TryStructuralMatch(
                        traitMember, memberScope, typeName, globalScope, symbolTable,
                        asts, compilation, diagnostics, privateMatches);

                    if (matched is not null)
                    {
                        structuralMatches[traitMember.Name] = matched;
                    }
                }

                // 2. With-block augmentation: apply with-block mappings to
                //    cover any trait members not structurally matched.
                var withMappings = new Dictionary<string, ISymbol>();

                foreach (var mapping in withBlockRegistry.GetMappings(type, trait))
                {
                    // Only apply the mapping if the trait member is not
                    // already structurally matched.
                    if (!structuralMatches.ContainsKey(mapping.TargetName))
                    {
                        withMappings[mapping.TargetName] = mapping.SourceMember;
                    }
                }

                // 3. Satisfaction check: are all trait members covered?
                var unsatisfiedMembers = new List<string>();
                foreach (var traitMember in trait.MemberScope.Members)
                {
                    // Skip dependent type parameter symbols.
                    if (traitMember.DeclaredType is DependentTypeParameterType)
                        continue;

                    if (!structuralMatches.ContainsKey(traitMember.Name)
                        && !withMappings.ContainsKey(traitMember.Name))
                    {
                        unsatisfiedMembers.Add(traitMember.Name);
                    }
                }

                if (unsatisfiedMembers.Count == 0)
                {
                    // All trait members are covered — register satisfaction.
                    satisfactionMap.Register(type, trait, new TraitSatisfaction
                    {
                        StructuralMatches = structuralMatches,
                        WithMappings = withMappings,
                    });

                    // Add to the type's SatisfiedTraits list.
                    if (type is ClassType classType)
                        classType.SatisfiedTraits.Add(trait);
                    else if (type is StructType structType)
                        structType.SatisfiedTraits.Add(trait);

                    // Transitive parent satisfaction: register the type as
                    // satisfying each parent trait (with appropriate type
                    // argument substitution).
                    RegisterTransitiveParentSatisfaction(
                        type, trait, typeParams: null, typeArgs: null, satisfactionMap);
                }
                else
                {
                    // Emit MIST_TRAIT_WITH_UNSATISFIED: a with block exists
                    // but doesn't cover all unmapped members.
                    var memberList = string.Join(", ", unsatisfiedMembers);
                    diagnostics.Add(new Diagnostic(
                        TraitWithUnsatisfied,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"`with` block for '{typeName} : {trait.DisplayName}' does not cover all trait members. Unsatisfied: {memberList}.",
                        Location: default,
                        CompilationUnitPath: "<unknown>",
                        AnalyzerName: null));
                }

                // 4. Emit MIST_TRAIT_MEMBER_VISIBILITY when private members
                //    match trait member names (developer likely intended to
                //    satisfy the trait but forgot to make some members public).
                if (privateMatches.Count > 0)
                {
                    foreach (var (memberName, privateSym) in privateMatches)
                    {
                        var memberPath = FindCompilationUnitPathForType(typeName, symbolTable, asts, compilation);
                        LinePositionSpan location = default;
                        var memberNode = symbolTable.GetDeclaringNode(privateSym);
                        if (memberNode is not null)
                        {
                            var sourceText = FindSourceTextForType(typeName, symbolTable, asts, compilation);
                            if (sourceText is not null && memberNode.Span.Length > 0)
                            {
                                var mistSpan = new SourceSpan(memberNode.Span.Start, memberNode.Span.Length);
                                location = sourceText.ToLinePositionSpan(mistSpan);
                            }
                        }
                        diagnostics.Add(new Diagnostic(
                            TraitMemberVisibility,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"Member '{typeName}.{memberName}' exists but is not public; trait matching requires public visibility.",
                            Location: location,
                            CompilationUnitPath: memberPath ?? "<unknown>",
                            AnalyzerName: null));
                    }
                }
            }
        }

        // ─── Generic trait satisfaction (ConstructedTraitType) ───────────
        // Collect all distinct ConstructedTraitType instances that are "in play":
        // from with-block registrations, parent trait references, and base list references.
        var constructedTraits = new List<ConstructedTraitType>();
        var constructedTraitSet = new HashSet<string>(); // Deduplicate by DisplayName.

        // From with-block registry: any with block that targets a ConstructedTraitType.
        foreach (var (_, traitKey, _) in withBlockRegistry.All())
        {
            if (traitKey is ConstructedTraitType ctt && constructedTraitSet.Add(ctt.DisplayName))
                constructedTraits.Add(ctt);
        }

        // From parent trait references on all traits: if a trait has a
        // ConstructedTraitType parent, that constructed trait is also a
        // candidate for satisfaction checking.
        foreach (var (_, trait) in traits)
        {
            CollectConstructedTraitsFromParents(trait, constructedTraits, constructedTraitSet);
        }

        // From base list references on types: if a class/struct has a
        // ConstructedTraitType in its base list, that's an explicit declaration.
        foreach (var (typeSym, _) in types)
        {
            CollectConstructedTraitsFromBaseList(
                typeSym, symbolTable, globalScope, constructedTraits, constructedTraitSet);
        }

        // For each constructed trait × type pair that has a with-block, attempt satisfaction.
        // Lazy model: only with-blocks trigger satisfaction during Pass 1.
        foreach (var constructedTrait in constructedTraits)
        {
            // Skip if the underlying generic definition is cyclic.
            if (constructedTrait.GenericTraitDefinition.IsCyclic)
                continue;

            // Compute effective members (own + inherited, with substitution).
            var effectiveMembers = GetEffectiveMembers(constructedTrait);

            foreach (var (typeSym, type) in types)
            {
                var hasWithBlock = withBlockRegistry.HasMappings(type, constructedTrait);

                var typeName = typeSym.Name;
                var memberScope = type switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };

                if (memberScope is null)
                    continue;

                // 1. Structural matching with type argument substitution.
                var structuralMatches = new Dictionary<string, ISymbol>();
                var privateMatches = new Dictionary<string, ISymbol>();

                foreach (var effectiveMember in effectiveMembers)
                {
                    var matched = TryStructuralMatchWithSubstitution(
                        effectiveMember, memberScope, typeName, globalScope,
                        symbolTable, asts, compilation, diagnostics, privateMatches);

                    if (matched is not null)
                    {
                        structuralMatches[effectiveMember.Member.Name] = matched;
                    }
                }

                // 2. With-block augmentation.
                var withMappings = new Dictionary<string, ISymbol>();

                foreach (var mapping in withBlockRegistry.GetMappings(type, constructedTrait))
                {
                    if (!structuralMatches.ContainsKey(mapping.TargetName))
                    {
                        withMappings[mapping.TargetName] = mapping.SourceMember;
                    }
                }

                // 3. Satisfaction check: are all effective members covered?
                var unsatisfiedMembers = new List<string>();
                foreach (var effectiveMember in effectiveMembers)
                {
                    var memberName = effectiveMember.Member.Name;
                    if (!structuralMatches.ContainsKey(memberName)
                        && !withMappings.ContainsKey(memberName))
                    {
                        unsatisfiedMembers.Add(memberName);
                    }
                }

                if (unsatisfiedMembers.Count == 0)
                {
                    // All effective members are covered — register satisfaction.
                    satisfactionMap.Register(type, constructedTrait, new TraitSatisfaction
                    {
                        StructuralMatches = structuralMatches,
                        WithMappings = withMappings,
                    });

                    // Add to the type's SatisfiedTraits list.
                    if (type is ClassType classType)
                        classType.SatisfiedTraits.Add(constructedTrait);
                    else if (type is StructType structType)
                        structType.SatisfiedTraits.Add(constructedTrait);

                    // Transitive parent satisfaction: register the type as
                    // satisfying each parent trait (with appropriate type
                    // argument substitution through the chain).
                    RegisterTransitiveParentSatisfaction(
                        type, constructedTrait,
                        constructedTrait.GenericTraitDefinition.TypeParameters,
                        constructedTrait.TypeArguments,
                        satisfactionMap);
                }
                else if (hasWithBlock)
                {
                    var memberList = string.Join(", ", unsatisfiedMembers);
                    diagnostics.Add(new Diagnostic(
                        TraitWithUnsatisfied,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"`with` block for '{typeName} : {constructedTrait.DisplayName}' does not cover all trait members. Unsatisfied: {memberList}.",
                        Location: default,
                        CompilationUnitPath: "<unknown>",
                        AnalyzerName: null));
                }

                // 4. Emit MIST_TRAIT_MEMBER_VISIBILITY when private members
                //    match trait member names (only when there's a with-block).
                if (privateMatches.Count > 0 && hasWithBlock)
                {
                    foreach (var (memberName, privateSym) in privateMatches)
                    {
                        var memberPath = FindCompilationUnitPathForType(typeName, symbolTable, asts, compilation);
                        LinePositionSpan location = default;
                        var memberNode = symbolTable.GetDeclaringNode(privateSym);
                        if (memberNode is not null)
                        {
                            var sourceText = FindSourceTextForType(typeName, symbolTable, asts, compilation);
                            if (sourceText is not null && memberNode.Span.Length > 0)
                            {
                                var mistSpan = new SourceSpan(memberNode.Span.Start, memberNode.Span.Length);
                                location = sourceText.ToLinePositionSpan(mistSpan);
                            }
                        }
                        diagnostics.Add(new Diagnostic(
                            TraitMemberVisibility,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"Member '{typeName}.{memberName}' exists but is not public; trait matching requires public visibility.",
                            Location: location,
                            CompilationUnitPath: memberPath ?? "<unknown>",
                            AnalyzerName: null));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks whether a type has a non-generic trait referenced in its base list
    /// (explicit satisfaction declaration like <c>class Foo : MyTrait</c>).
    /// </summary>
    private static bool HasTraitInBaseList(ISymbol typeSym, TraitType trait, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(typeSym);
        var baseList = declaringNode switch
        {
            ClassDeclarationNode classDecl => classDecl.BaseList,
            _ => null,
        };

        if (baseList is null)
            return false;

        var entries = ExtractBaseListEntries(baseList);
        foreach (var entry in entries)
        {
            var name = entry.Name.Lexeme;
            if (name == trait.DisplayName && entry.TypeArgs is null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether a type has a constructed trait referenced in its base list
    /// (explicit satisfaction declaration like <c>class Foo : MyTrait&lt;int&gt;</c>).
    /// </summary>
    private static bool HasConstructedTraitInBaseList(
        ISymbol typeSym, ConstructedTraitType constructedTrait, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(typeSym);
        var baseList = declaringNode switch
        {
            ClassDeclarationNode classDecl => classDecl.BaseList,
            _ => null,
        };

        if (baseList is null)
            return false;

        var entries = ExtractBaseListEntries(baseList);
        foreach (var entry in entries)
        {
            var name = entry.Name.Lexeme;
            if (name == constructedTrait.GenericTraitDefinition.DisplayName && entry.TypeArgs is not null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Collects ConstructedTraitType instances from a type's base list references.
    /// When a class declares <c>class Foo : MyTrait&lt;int&gt;</c>, the constructed
    /// trait is collected for satisfaction checking.
    /// </summary>
    private static void CollectConstructedTraitsFromBaseList(
        ISymbol typeSym,
        SymbolTable symbolTable,
        Scope globalScope,
        List<ConstructedTraitType> constructedTraits,
        HashSet<string> constructedTraitSet)
    {
        var declaringNode = symbolTable.GetDeclaringNode(typeSym);
        var baseList = declaringNode switch
        {
            ClassDeclarationNode classDecl => classDecl.BaseList,
            _ => null,
        };

        if (baseList is null)
            return;

        var entries = ExtractBaseListEntries(baseList);
        foreach (var entry in entries)
        {
            if (entry.TypeArgs is null)
                continue;

            var name = entry.Name.Lexeme;
            if (string.IsNullOrEmpty(name))
                continue;

            // Look up the name in the global scope to find the trait.
            var resolvedSymbol = globalScope.Lookup(name)
                ?? typeSym.ContainingScope.Lookup(name);
            if (resolvedSymbol?.DeclaredType is not TraitType traitType)
                continue;
            if (traitType.TypeParameters.Count == 0)
                continue;

            // Resolve type arguments from the base list entry.
            var typeArgs = new List<IType>();
            var typeArgNodes = new List<TypeReferenceNode> { entry.TypeArgs.GetTypeReference() };
            foreach (var extra in entry.TypeArgs.GetTypeReferences())
                typeArgNodes.Add(extra);

            foreach (var typeArgNode in typeArgNodes)
            {
                // Simple resolution: look up named type refs in the scope.
                var resolvedArg = ResolveTypeArgFromBaseList(typeArgNode, globalScope, typeSym.ContainingScope);
                if (resolvedArg is not null)
                    typeArgs.Add(resolvedArg);
            }

            if (typeArgs.Count == traitType.TypeParameters.Count)
            {
                var ctt = new ConstructedTraitType(traitType, typeArgs);
                if (constructedTraitSet.Add(ctt.DisplayName))
                    constructedTraits.Add(ctt);
            }
        }
    }

    /// <summary>
    /// Simple type argument resolution for base list entries.
    /// Handles primitive types and named type references.
    /// </summary>
    private static IType? ResolveTypeArgFromBaseList(
        TypeReferenceNode typeRef, Scope globalScope, IScope containingScope)
    {
        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
                return ResolveLegacyPrimitive(primitiveRef.PrimitiveType);
            case NamedTypeRefTypeReferenceNode namedRef:
                var name = namedRef.Name.IDENTIFIER.Lexeme;
                var sym = containingScope.Lookup(name) ?? globalScope.Lookup(name);
                return sym?.DeclaredType;
            default:
                return null;
        }
    }

    /// <summary>
    /// Registers transitive parent trait satisfaction for a type that has been
    /// confirmed to satisfy a child trait. Walks the child trait's parent chain
    /// and registers the type as satisfying each parent trait with appropriate
    /// type argument substitution.
    /// <para>
    /// Handles diamond inheritance: if the same parent trait is reached from
    /// multiple paths, it is only registered once (checked via
    /// <see cref="TraitSatisfactionMap.Satisfies"/>).
    /// </para>
    /// </summary>
    /// <param name="type">The implementing type (class or struct).</param>
    /// <param name="childTrait">
    /// The child trait that was satisfied. Used to walk the parent chain.
    /// May be a <see cref="TraitType"/> or <see cref="ConstructedTraitType"/>.
    /// </param>
    /// <param name="typeParams">
    /// Type parameters from the child trait's generic definition (null for non-generic traits).
    /// </param>
    /// <param name="typeArgs">
    /// Type arguments applied to the child trait (null for non-generic traits).
    /// </param>
    /// <param name="satisfactionMap">The satisfaction map to register into.</param>
    private static void RegisterTransitiveParentSatisfaction(
        IType type,
        IType childTrait,
        IReadOnlyList<IType>? typeParams,
        IReadOnlyList<IType>? typeArgs,
        TraitSatisfactionMap satisfactionMap)
    {
        // Get the trait definition and its parent traits.
        TraitType traitDef;
        if (childTrait is ConstructedTraitType ctt)
            traitDef = ctt.GenericTraitDefinition;
        else if (childTrait is TraitType tt)
            traitDef = tt;
        else
            return;

        if (traitDef.ParentTraits.Count == 0)
            return;

        // Use a visited set to prevent infinite recursion on cyclic hierarchies
        // (should already be detected, but be defensive).
        var visited = new HashSet<string>();
        RegisterTransitiveParentSatisfactionCore(type, traitDef, typeParams, typeArgs, satisfactionMap, visited);
    }

    /// <summary>
    /// Core recursive implementation for transitive parent satisfaction registration.
    /// </summary>
    private static void RegisterTransitiveParentSatisfactionCore(
        IType type,
        TraitType traitDef,
        IReadOnlyList<IType>? typeParams,
        IReadOnlyList<IType>? typeArgs,
        TraitSatisfactionMap satisfactionMap,
        HashSet<string> visited)
    {
        foreach (var parent in traitDef.ParentTraits)
        {
            // Resolve the parent trait with type argument substitution.
            IType resolvedParent;
            TraitType parentDef;
            IReadOnlyList<IType>? parentTypeParams;
            IReadOnlyList<IType>? parentTypeArgs;

            if (parent is ConstructedTraitType parentCtt)
            {
                parentDef = parentCtt.GenericTraitDefinition;

                // Substitute the child's type arguments into the parent's type arguments.
                // E.g., trait Sortable<T> : Comparable<T> — when satisfying Sortable<int>,
                // the parent becomes Comparable<int> (T is substituted with int).
                if (typeParams is not null && typeArgs is not null && typeParams.Count > 0)
                {
                    var substitutedArgs = new IType[parentCtt.TypeArguments.Count];
                    for (var i = 0; i < parentCtt.TypeArguments.Count; i++)
                    {
                        substitutedArgs[i] = SubstituteTypeInMember(parentCtt.TypeArguments[i], typeParams, typeArgs);
                    }
                    resolvedParent = new ConstructedTraitType(parentDef, substitutedArgs);
                    parentTypeParams = parentDef.TypeParameters;
                    parentTypeArgs = substitutedArgs;
                }
                else
                {
                    resolvedParent = parentCtt;
                    parentTypeParams = parentDef.TypeParameters;
                    parentTypeArgs = parentCtt.TypeArguments;
                }
            }
            else if (parent is TraitType parentTt)
            {
                parentDef = parentTt;
                resolvedParent = parentTt;
                parentTypeParams = null;
                parentTypeArgs = null;
            }
            else
            {
                continue;
            }

            // Diamond inheritance guard: skip if already registered.
            var parentKey = resolvedParent.DisplayName;
            if (!visited.Add(parentKey))
                continue;

            if (satisfactionMap.Satisfies(type, resolvedParent))
                continue;

            // Verify that the type actually satisfies the parent trait's
            // effective members before registering transitive satisfaction.
            // This prevents incorrect propagation when a child trait has no
            // members of its own (e.g., a bridge trait like `_Bridge : Comparable<int>`)
            // — satisfying the empty child does not imply satisfying the parent.
            var parentEffectiveMembers = GetEffectiveMembers(resolvedParent);
            if (parentEffectiveMembers.Count > 0)
            {
                // The child trait's effective members should include all parent
                // members. If the child was satisfied, the parent should be too.
                // However, when the child has no own members and typeParams is null
                // (non-generic child), the child's satisfaction was trivial (0 members)
                // and does not guarantee parent satisfaction. Only propagate if the
                // child trait itself declares or inherits the parent's members in its
                // effective member set (i.e., the child is generic and was checked
                // with full effective members, or the child has own members that
                // subsume the parent's).
                //
                // Safe propagation condition: the caller already verified all
                // effective members of the child trait (which include inherited
                // parent members) against the type. This is true when:
                // - The child is a ConstructedTraitType (generic trait satisfaction
                //   loop uses GetEffectiveMembers which includes inherited members)
                // - The child is a non-generic trait WITH own members that were
                //   checked (the non-generic loop checks trait.MemberScope.Members)
                //
                // Unsafe propagation: the child is a non-generic trait with NO own
                // members (bridge pattern). In this case, typeParams is null and the
                // child's MemberScope is empty, so satisfaction was trivial.
                // We skip propagation here and let the constructed trait satisfaction
                // loop handle the parent independently.
                if (typeParams is null && typeArgs is null)
                {
                    // Non-generic child trait — only propagate to non-generic parents
                    // (which have their own satisfaction check in the non-generic loop).
                    // Skip constructed trait parents; they will be checked independently
                    // in the constructed trait satisfaction loop.
                    if (resolvedParent is ConstructedTraitType)
                        continue;
                }
            }

            // Register the type as satisfying the parent trait.
            satisfactionMap.Register(type, resolvedParent, new TraitSatisfaction());

            // Recurse into the parent's own parents.
            RegisterTransitiveParentSatisfactionCore(
                type, parentDef, parentTypeParams, parentTypeArgs, satisfactionMap, visited);
        }
    }

    /// <summary>
    /// Recursively collects <see cref="ConstructedTraitType"/> instances from
    /// a trait's parent chain. These are candidates for satisfaction checking.
    /// </summary>
    private static void CollectConstructedTraitsFromParents(
        TraitType trait,
        List<ConstructedTraitType> collected,
        HashSet<string> seen)
    {
        CollectConstructedTraitsFromParentsCore(trait, collected, seen, new HashSet<TraitType>());
    }

    /// <summary>
    /// Core recursive implementation with a visited set to prevent infinite recursion
    /// on cyclic trait hierarchies.
    /// </summary>
    private static void CollectConstructedTraitsFromParentsCore(
        TraitType trait,
        List<ConstructedTraitType> collected,
        HashSet<string> seen,
        HashSet<TraitType> visited)
    {
        if (!visited.Add(trait))
            return; // Already visited — prevent infinite recursion.

        foreach (var parent in trait.ParentTraits)
        {
            if (parent is ConstructedTraitType ctt)
            {
                if (seen.Add(ctt.DisplayName))
                    collected.Add(ctt);
                // Also recurse into the parent's own parents.
                CollectConstructedTraitsFromParentsCore(ctt.GenericTraitDefinition, collected, seen, visited);
            }
            else if (parent is TraitType parentTrait)
            {
                CollectConstructedTraitsFromParentsCore(parentTrait, collected, seen, visited);
            }
        }
    }

    /// <summary>
    /// Attempts to structurally match a single effective member (with type
    /// argument substitution) against the type's public members.
    /// <para>
    /// When the effective member has <see cref="EffectiveMember.TypeParams"/>
    /// and <see cref="EffectiveMember.TypeArgs"/>, type parameters in the
    /// trait member's type references are substituted with the corresponding
    /// type arguments before comparison.
    /// </para>
    /// </summary>
    private static ISymbol? TryStructuralMatchWithSubstitution(
        EffectiveMember effectiveMember,
        IScope typeMemberScope,
        string typeName,
        Scope globalScope,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        Dictionary<string, ISymbol>? privateMatches = null)
    {
        var traitMember = effectiveMember.Member;
        var typeParams = effectiveMember.TypeParams;
        var typeArgs = effectiveMember.TypeArgs;

        // If no substitution is needed, fall back to the standard matching.
        if (typeParams is null || typeArgs is null || typeParams.Count == 0)
        {
            return TryStructuralMatch(
                traitMember, typeMemberScope, typeName, globalScope,
                symbolTable, asts, compilation, diagnostics, privateMatches);
        }

        switch (traitMember.Kind)
        {
            case SymbolKind.Field:
                return TryMatchTraitFieldWithSubstitution(
                    traitMember, typeMemberScope, typeName, typeParams, typeArgs,
                    symbolTable, asts, compilation, diagnostics, privateMatches);

            case SymbolKind.Method:
                return TryMatchTraitMethodWithSubstitution(
                    traitMember, typeMemberScope, typeName, typeParams, typeArgs,
                    globalScope, symbolTable, asts, compilation, diagnostics, privateMatches);

            case SymbolKind.Property:
                return TryMatchTraitPropertyWithSubstitution(
                    traitMember, typeMemberScope, typeName, typeParams, typeArgs,
                    globalScope, symbolTable, asts, compilation, diagnostics, privateMatches);

            default:
                return null;
        }
    }

    /// <summary>
    /// Matches a trait field member against the type's public fields or properties,
    /// substituting type parameters in the trait field's type before comparison.
    /// </summary>
    private static ISymbol? TryMatchTraitFieldWithSubstitution(
        ISymbol traitField,
        IScope typeMemberScope,
        string typeName,
        IReadOnlyList<IType> typeParams,
        IReadOnlyList<IType> typeArgs,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        Dictionary<string, ISymbol>? privateMatches = null)
    {
        var traitFieldName = traitField.Name;
        var traitTypeRef = GetTraitMemberTypeReference(traitField, symbolTable);
        ISymbol? foundPrivateMatch = null;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitFieldName)
                continue;

            if (member.Kind is not (SymbolKind.Field or SymbolKind.Property))
                continue;

            if (!IsMemberPublic(member, symbolTable))
            {
                foundPrivateMatch ??= member;
                continue;
            }

            // Check type compatibility with substitution.
            if (traitTypeRef is not null)
            {
                var memberTypeRef = GetMemberTypeReference(member, symbolTable);
                if (memberTypeRef is not null
                    && !TypeReferencesMatchWithSubstitution(memberTypeRef, traitTypeRef, typeParams, typeArgs))
                    continue;
            }

            return member;
        }

        if (foundPrivateMatch is not null)
        {
            privateMatches?.TryAdd(traitFieldName, foundPrivateMatch);
        }

        return null;
    }

    /// <summary>
    /// Matches a trait method member against the type's public methods,
    /// substituting type parameters in the trait method's return type and
    /// parameter types before comparison.
    /// </summary>
    private static ISymbol? TryMatchTraitMethodWithSubstitution(
        ISymbol traitMethod,
        IScope typeMemberScope,
        string typeName,
        IReadOnlyList<IType> typeParams,
        IReadOnlyList<IType> typeArgs,
        Scope globalScope,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        Dictionary<string, ISymbol>? privateMatches = null)
    {
        var traitMethodName = traitMethod.Name;
        var traitNode = symbolTable.GetDeclaringNode(traitMethod);
        if (traitNode is not TraitMethodDeclarationNode traitMethodDecl)
            return null;

        var traitReturnType = traitMethodDecl.ReturnType;
        var traitParams = ExtractParameterTypes(traitMethodDecl.Parameters);
        var traitSourceText = FindSourceTextForNode(traitMethodDecl, asts, compilation);
        ISymbol? foundPrivateMatch = null;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitMethodName || member.Kind != SymbolKind.Method)
                continue;

            if (!IsMemberPublic(member, symbolTable))
            {
                foundPrivateMatch ??= member;
                continue;
            }

            var memberNode = symbolTable.GetDeclaringNode(member);
            if (memberNode is not MethodDeclarationNode implMethodDecl)
                continue;

            var implSourceText = FindSourceTextForNode(implMethodDecl, asts, compilation);
            var combinedSourceText = traitSourceText ?? implSourceText;

            // Compare return types with substitution.
            if (!TypeReferenceMatchesWithSubstitutionForReturn(
                    implMethodDecl.ReturnType, traitReturnType, typeParams, typeArgs,
                    globalScope, symbolTable, combinedSourceText))
                continue;

            // Compare parameter types with substitution.
            var implParams = ExtractParameterTypes(implMethodDecl.Parameters);
            if (traitParams.Count != implParams.Count)
                continue;

            var paramsMatch = true;
            for (var i = 0; i < traitParams.Count; i++)
            {
                if (!TypeReferencesMatchWithSubstitution(implParams[i], traitParams[i], typeParams, typeArgs, traitSourceText))
                {
                    paramsMatch = false;
                    break;
                }
            }

            if (!paramsMatch)
                continue;

            return member;
        }

        if (foundPrivateMatch is not null)
        {
            privateMatches?.TryAdd(traitMethodName, foundPrivateMatch);
        }

        return null;
    }

    /// <summary>
    /// Matches a trait property member against the type's public properties,
    /// substituting type parameters in the trait property's type before comparison.
    /// </summary>
    private static ISymbol? TryMatchTraitPropertyWithSubstitution(
        ISymbol traitProperty,
        IScope typeMemberScope,
        string typeName,
        IReadOnlyList<IType> typeParams,
        IReadOnlyList<IType> typeArgs,
        Scope globalScope,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        Dictionary<string, ISymbol>? privateMatches = null)
    {
        var traitPropName = traitProperty.Name;
        var traitTypeRef = GetTraitMemberTypeReference(traitProperty, symbolTable);
        var traitNode = symbolTable.GetDeclaringNode(traitProperty);
        var traitAccessors = GetTraitPropertyAccessors(traitNode);
        var traitSourceText = traitNode is null
            ? null
            : FindSourceTextForNode(traitNode, asts, compilation);
        ISymbol? foundPrivateMatch = null;

        var traitHasSet = traitAccessors?.HasSet ?? false;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitPropName || member.Kind != SymbolKind.Property)
                continue;

            if (!IsMemberPublic(member, symbolTable))
            {
                foundPrivateMatch ??= member;
                continue;
            }

            // Check type compatibility with substitution.
            if (traitTypeRef is not null)
            {
                var memberTypeRef = GetMemberTypeReference(member, symbolTable);
                if (memberTypeRef is not null)
                {
                    var typesMatch = traitHasSet
                        ? TypeReferencesMatchWithSubstitution(memberTypeRef, traitTypeRef, typeParams, typeArgs)
                        : TypeReferenceMatchesWithSubstitutionForReturn(
                            memberTypeRef, traitTypeRef, typeParams, typeArgs,
                            globalScope, symbolTable, traitSourceText);
                    if (!typesMatch)
                        continue;
                }
            }

            // Check accessors.
            var implPropInfo = (member as Symbol)?.PropertyMetadata;
            if (implPropInfo is not null && traitAccessors is not null)
            {
                if (traitAccessors.Value.HasGet && !implPropInfo.HasGet)
                    continue;
                if (traitAccessors.Value.HasSet && !implPropInfo.HasSet)
                    continue;
            }

            return member;
        }

        if (foundPrivateMatch is not null)
        {
            privateMatches?.TryAdd(traitPropName, foundPrivateMatch);
        }

        return null;
    }

    /// <summary>
    /// Compares a return type reference from the implementing type against a
    /// trait return type reference with type parameter substitution. Applies
    /// the relaxed unknown-widening rules at the return position.
    /// </summary>
    private static bool TypeReferenceMatchesWithSubstitutionForReturn(
        TypeReferenceNode implReturn,
        TypeReferenceNode traitReturn,
        IReadOnlyList<IType> typeParams,
        IReadOnlyList<IType> typeArgs,
        Scope globalScope,
        SymbolTable symbolTable,
        SourceText? sourceText)
    {
        // If the trait return type is a type parameter, substitute it and
        // compare the impl return against the substituted concrete type.
        if (traitReturn is NamedTypeRefTypeReferenceNode namedTarget)
        {
            var targetName = GetNamedTypeRefName(namedTarget, sourceText);
            for (var i = 0; i < typeParams.Count; i++)
            {
                var (paramName, _) = GetTypeParameterNameAndOrdinal(typeParams[i]);
                if (paramName == targetName)
                {
                    // The trait return type is a type parameter — substitute
                    // and compare against the concrete type.
                    return TypeReferenceMatchesSubstitutedType(implReturn, typeArgs[i]);
                }
            }
        }

        // Not a type parameter — use the standard relaxed return matching.
        return TypeReferencesMatchForTraitReturn(
            traitReturn, implReturn, globalScope, symbolTable, sourceText);
    }

    /// <summary>
    /// Attempts to structurally match a single trait member against the type's
    /// public members. Returns the matching type member symbol, or <c>null</c>
    /// if no structural match was found.
    /// <para>
    /// When a private member matches the trait member by name, it is recorded
    /// in <paramref name="privateMatches"/> for deferred diagnostic emission
    /// by the caller.
    /// </para>
    /// </summary>
    private static ISymbol? TryStructuralMatch(
        ISymbol traitMember,
        IScope typeMemberScope,
        string typeName,
        Scope globalScope,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        Dictionary<string, ISymbol>? privateMatches = null)
    {
        var traitMemberName = traitMember.Name;

        switch (traitMember.Kind)
        {
            case SymbolKind.Field:
                return TryMatchTraitField(traitMember, typeMemberScope, typeName, symbolTable, asts, compilation, diagnostics, privateMatches);

            case SymbolKind.Method:
                return TryMatchTraitMethod(traitMember, typeMemberScope, typeName, globalScope, symbolTable, asts, compilation, diagnostics, privateMatches);

            case SymbolKind.Property:
                return TryMatchTraitProperty(traitMember, typeMemberScope, typeName, globalScope, symbolTable, asts, compilation, diagnostics, privateMatches);

            default:
                return null;
        }
    }

    /// <summary>
    /// Matches a trait field member against the type's public fields or public
    /// properties with compatible type.
    /// </summary>
    private static ISymbol? TryMatchTraitField(
        ISymbol traitField,
        IScope typeMemberScope,
        string typeName,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        Dictionary<string, ISymbol>? privateMatches = null)
    {
        var traitFieldName = traitField.Name;
        var traitTypeRef = GetTraitMemberTypeReference(traitField, symbolTable);
        ISymbol? foundPrivateMatch = null;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitFieldName)
                continue;

            // Accept fields or properties.
            if (member.Kind is not (SymbolKind.Field or SymbolKind.Property))
                continue;

            // Check visibility.
            if (!IsMemberPublic(member, symbolTable))
            {
                foundPrivateMatch ??= member;
                continue;
            }

            // Check type compatibility.
            if (traitTypeRef is not null)
            {
                var memberTypeRef = GetMemberTypeReference(member, symbolTable);
                if (memberTypeRef is not null && !TypeReferencesMatch(traitTypeRef, memberTypeRef))
                    continue; // Type mismatch — not a match.
            }

            return member;
        }

        if (foundPrivateMatch is not null)
        {
            privateMatches?.TryAdd(traitFieldName, foundPrivateMatch);
        }

        return null;
    }

    /// <summary>
    /// Matches a trait method member against the type's public methods with
    /// the same name, return type, and parameter types. Return-position
    /// matching uses the relaxed unknown-widening rules (Req 12.1, 12.2);
    /// parameter-position matching remains exact (Req 12.3).
    /// </summary>
    private static ISymbol? TryMatchTraitMethod(
        ISymbol traitMethod,
        IScope typeMemberScope,
        string typeName,
        Scope globalScope,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        Dictionary<string, ISymbol>? privateMatches = null)
    {
        var traitMethodName = traitMethod.Name;
        var traitNode = symbolTable.GetDeclaringNode(traitMethod);
        if (traitNode is not TraitMethodDeclarationNode traitMethodDecl)
            return null;

        var traitReturnType = traitMethodDecl.ReturnType;
        var traitParams = ExtractParameterTypes(traitMethodDecl.Parameters);
        var traitSourceText = FindSourceTextForNode(traitMethodDecl, asts, compilation);
        ISymbol? foundPrivateMatch = null;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitMethodName || member.Kind != SymbolKind.Method)
                continue;

            // Check visibility.
            if (!IsMemberPublic(member, symbolTable))
            {
                foundPrivateMatch ??= member;
                continue;
            }

            // Check signature: return type + parameter types.
            var memberNode = symbolTable.GetDeclaringNode(member);
            if (memberNode is not MethodDeclarationNode implMethodDecl)
                continue;

            var implSourceText = FindSourceTextForNode(implMethodDecl, asts, compilation);
            // Both source texts should be available; if they disagree, we
            // use the trait's as the primary lookup source (names resolved
            // from the trait side are the ones that need to match the
            // impl's declaration).
            var combinedSourceText = traitSourceText ?? implSourceText;

            // Compare return types under the relaxed trait-return rules
            // (Req 12.1, 12.2 — unknown widening allowed at the return
            // position).
            if (!TypeReferencesMatchForTraitReturn(
                    traitReturnType, implMethodDecl.ReturnType, globalScope, symbolTable, combinedSourceText))
                continue;

            // Compare parameter types.
            var implParams = ExtractParameterTypes(implMethodDecl.Parameters);
            if (traitParams.Count != implParams.Count)
                continue;

            var paramsMatch = true;
            for (var i = 0; i < traitParams.Count; i++)
            {
                if (!TypeReferencesMatch(traitParams[i], implParams[i]))
                {
                    paramsMatch = false;
                    break;
                }
            }

            if (!paramsMatch)
                continue;

            return member;
        }

        if (foundPrivateMatch is not null)
        {
            privateMatches?.TryAdd(traitMethodName, foundPrivateMatch);
        }

        return null;
    }

    /// <summary>
    /// Matches a trait property member against the type's public properties
    /// with the same name, compatible type, and at least the trait's accessors.
    /// Property types apply the relaxed unknown-widening rules (Req 12.1, 12.2)
    /// when the trait declares only a getter (read-only), because the type
    /// appears only at a return position. When the trait declares a setter,
    /// exact matching is required (Req 12.3) because the property type then
    /// appears at a parameter position (the value being assigned).
    /// </summary>
    private static ISymbol? TryMatchTraitProperty(
        ISymbol traitProperty,
        IScope typeMemberScope,
        string typeName,
        Scope globalScope,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        Dictionary<string, ISymbol>? privateMatches = null)
    {
        var traitPropName = traitProperty.Name;
        var traitTypeRef = GetTraitMemberTypeReference(traitProperty, symbolTable);
        var traitNode = symbolTable.GetDeclaringNode(traitProperty);
        var traitAccessors = GetTraitPropertyAccessors(traitNode);
        var traitSourceText = traitNode is null
            ? null
            : FindSourceTextForNode(traitNode, asts, compilation);
        ISymbol? foundPrivateMatch = null;

        // A trait property with a setter exposes the property type at a
        // parameter (write) position, which requires exact matching
        // (Req 12.3). A read-only trait property exposes the type only at
        // a return position and permits widening (Req 12.1, 12.2).
        var traitHasSet = traitAccessors?.HasSet ?? false;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitPropName || member.Kind != SymbolKind.Property)
                continue;

            // Check visibility.
            if (!IsMemberPublic(member, symbolTable))
            {
                foundPrivateMatch ??= member;
                continue;
            }

            // Check type compatibility.
            if (traitTypeRef is not null)
            {
                var memberTypeRef = GetMemberTypeReference(member, symbolTable);
                if (memberTypeRef is not null)
                {
                    var typesMatch = traitHasSet
                        ? TypeReferencesMatch(traitTypeRef, memberTypeRef)
                        : TypeReferencesMatchForTraitReturn(
                            traitTypeRef, memberTypeRef, globalScope, symbolTable, traitSourceText);
                    if (!typesMatch)
                        continue; // Type mismatch.
                }
            }

            // Check that the type's property has at least the accessors
            // declared in the trait.
            var implPropInfo = (member as Symbol)?.PropertyMetadata;
            if (implPropInfo is not null && traitAccessors is not null)
            {
                if (traitAccessors.Value.HasGet && !implPropInfo.HasGet)
                    continue;
                if (traitAccessors.Value.HasSet && !implPropInfo.HasSet)
                    continue;
            }

            return member;
        }

        if (foundPrivateMatch is not null)
        {
            privateMatches?.TryAdd(traitPropName, foundPrivateMatch);
        }

        return null;
    }

    /// <summary>
    /// Extracts the accessor flags (get/set) from a trait property declaration node.
    /// </summary>
    private static (bool HasGet, bool HasSet)? GetTraitPropertyAccessors(
        Storm.Runtime.SyntaxNode? declaringNode)
    {
        if (declaringNode is not TraitPropertyDeclarationNode traitPropDecl)
            return null;

        var hasGet = false;
        var hasSet = false;

        foreach (var accessor in traitPropDecl.Accessors)
        {
            switch (accessor)
            {
                case InterfaceGetAccessorInterfaceAccessorDeclarationNode:
                    hasGet = true;
                    break;
                case InterfaceSetAccessorInterfaceAccessorDeclarationNode:
                    hasSet = true;
                    break;
            }
        }

        return (hasGet, hasSet);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1c — Detect circular interface inheritance chains
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After interface lists are resolved, walk each interface's Interfaces
    /// chain to detect cycles (including self-inheritance). When a cycle is
    /// found, emit a <c>MIST_CIRCULAR_INTERFACE_INHERITANCE</c> diagnostic
    /// and break the cycle by removing the back-edge from the Interfaces list.
    /// </summary>
    private static void Pass1_DetectInterfaceCycles(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Track interfaces that have already been verified as cycle-free so
        // we don't re-walk them.
        var verified = new HashSet<InterfaceType>();

        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Interface || symbol.DeclaredType is not InterfaceType interfaceType)
            {
                continue;
            }

            if (interfaceType.Interfaces.Count == 0 || verified.Contains(interfaceType))
            {
                continue;
            }

            // Walk the Interfaces chain from this interface, tracking visited
            // interfaces using a stack-based DFS.
            DetectInterfaceCyclesDfs(
                interfaceType, verified, symbolTable, asts, compilation, diagnostics);
        }
    }

    /// <summary>
    /// Performs a DFS walk of the interface inheritance graph starting from
    /// <paramref name="root"/>. If a cycle is detected, emits a diagnostic
    /// and removes the back-edge from the parent's Interfaces list.
    /// </summary>
    private static void DetectInterfaceCyclesDfs(
        InterfaceType root,
        HashSet<InterfaceType> verified,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // path tracks the current DFS path for cycle reporting.
        var path = new List<InterfaceType>();
        // pathSet provides O(1) lookup for cycle detection.
        var pathSet = new HashSet<InterfaceType>();

        DetectInterfaceCyclesDfsVisit(
            root, parent: null, path, pathSet, verified,
            symbolTable, asts, compilation, diagnostics);
    }

    /// <summary>
    /// Recursive DFS visitor for interface cycle detection.
    /// </summary>
    private static void DetectInterfaceCyclesDfsVisit(
        InterfaceType current,
        InterfaceType? parent,
        List<InterfaceType> path,
        HashSet<InterfaceType> pathSet,
        HashSet<InterfaceType> verified,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        if (verified.Contains(current))
        {
            return;
        }

        if (!pathSet.Add(current))
        {
            // Cycle detected — current was already on the path.
            // Build the chain string for the diagnostic message.
            var cycleStart = path.IndexOf(current);
            var cycleMembers = path.Skip(cycleStart).Select(i => i.DisplayName).ToList();
            cycleMembers.Add(current.DisplayName);
            var chainStr = string.Join(" -> ", cycleMembers);

            // Find the interface that has the back-edge (the parent that
            // references current in its Interfaces list).
            var closingInterface = path[^1];

            // Find the symbol and source text for the closing interface.
            var sourceText = FindSourceTextForInterfaceType(
                closingInterface, symbolTable, asts, compilation);
            Storm.Runtime.SyntaxNode? closingNode = null;
            foreach (var sym in symbolTable.AllSymbols)
            {
                if (sym.DeclaredType == closingInterface)
                {
                    closingNode = symbolTable.GetDeclaringNode(sym);
                    break;
                }
            }

            if (closingNode is not null)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_CIRCULAR_INTERFACE_INHERITANCE",
                    DiagnosticSeverity.Error,
                    $"Circular interface inheritance detected: '{chainStr}'.",
                    closingNode,
                    sourceText));
            }
            else
            {
                string? compilationUnitPath = FindCompilationUnitPathForInterfaceType(
                    closingInterface, symbolTable, asts, compilation);

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_CIRCULAR_INTERFACE_INHERITANCE"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Circular interface inheritance detected: '{chainStr}'.",
                    Location: default,
                    CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                    AnalyzerName: null));
            }

            // Break the cycle by removing the back-edge from the closing
            // interface's Interfaces list.
            var updatedInterfaces = closingInterface.Interfaces
                .Where(i => !ReferenceEquals(i, current))
                .ToList();
            closingInterface.Interfaces = updatedInterfaces;

            return;
        }

        path.Add(current);

        // Visit each interface in the current interface's Interfaces list.
        // Take a snapshot of the list since it may be modified during cycle breaking.
        var interfaces = current.Interfaces.OfType<InterfaceType>().ToList();
        foreach (var child in interfaces)
        {
            DetectInterfaceCyclesDfsVisit(
                child, current, path, pathSet, verified,
                symbolTable, asts, compilation, diagnostics);
        }

        path.RemoveAt(path.Count - 1);
        pathSet.Remove(current);
        verified.Add(current);
    }

    /// <summary>
    /// Finds the compilation unit path for an interface type by looking up its
    /// declaring node in the symbol table.
    /// </summary>
    private static string? FindCompilationUnitPathForInterfaceType(
        InterfaceType interfaceType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.DeclaredType == interfaceType)
            {
                return FindCompilationUnitPathForSymbol(symbol, symbolTable, asts, compilation);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for an interface type by looking up its
    /// declaring node in the symbol table.
    /// </summary>
    private static SourceText? FindSourceTextForInterfaceType(
        InterfaceType interfaceType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.DeclaredType == interfaceType)
            {
                return FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1c (cont.) — Detect circular class inheritance chains
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After all base types are resolved, walk each class's BaseType chain to
    /// detect cycles (including self-inheritance). When a cycle is found, emit
    /// a <c>MIST_CIRCULAR_INHERITANCE</c> diagnostic and set <c>BaseType = null</c>
    /// on the class that closes the cycle to break it.
    /// </summary>
    private static void Pass1_DetectCycles(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Track classes that have already been verified as cycle-free so we
        // don't re-walk them.
        var verified = new HashSet<ClassType>();

        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Class || symbol.DeclaredType is not ClassType classType)
            {
                continue;
            }

            if (classType.BaseType is null || verified.Contains(classType))
            {
                continue;
            }

            // Walk the BaseType chain from this class, tracking visited classes.
            var visited = new HashSet<ClassType>();
            var chain = new List<ClassType>();
            var current = classType;

            while (current is not null)
            {
                if (!visited.Add(current))
                {
                    // Cycle detected — current was already visited.
                    // Build the chain string for the diagnostic message.
                    var cycleStart = chain.IndexOf(current);
                    var cycleMembers = chain.Skip(cycleStart).Select(c => c.DisplayName).ToList();
                    cycleMembers.Add(current.DisplayName);
                    var chainStr = string.Join(" -> ", cycleMembers);

                    // Find the class declaration node that closes the cycle
                    // (the last class in the chain before we revisited current).
                    var closingClass = chain[^1];
                    var closingSourceText = FindSourceTextForClassType(closingClass, symbolTable, asts, compilation);
                    Storm.Runtime.SyntaxNode? closingNode = null;
                    foreach (var sym in symbolTable.AllSymbols)
                    {
                        if (sym.DeclaredType == closingClass)
                        {
                            closingNode = symbolTable.GetDeclaringNode(sym);
                            break;
                        }
                    }

                    if (closingNode is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            "MIST_CIRCULAR_INHERITANCE",
                            DiagnosticSeverity.Error,
                            $"Circular inheritance detected: '{chainStr}'.",
                            closingNode,
                            closingSourceText));
                    }
                    else
                    {
                        string? compilationUnitPath = FindCompilationUnitPathForClass(closingClass, symbolTable, asts, compilation);

                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_CIRCULAR_INHERITANCE"),
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"Circular inheritance detected: '{chainStr}'.",
                            Location: default,
                            CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                            AnalyzerName: null));
                    }

                    // Break the cycle by nulling the BaseType on the closing class.
                    closingClass.BaseType = null;
                    break;
                }

                chain.Add(current);
                current = current.BaseType as ClassType;
            }

            // All classes in this chain that didn't form a cycle are verified.
            foreach (var c in chain)
            {
                if (c.BaseType is not null || c == classType)
                {
                    verified.Add(c);
                }
            }
        }
    }

    /// <summary>
    /// Finds the compilation unit path for a class type by looking up its
    /// declaring node in the symbol table.
    /// </summary>
    private static string? FindCompilationUnitPathForClass(
        ClassType classType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.DeclaredType == classType)
            {
                var declaringNode = symbolTable.GetDeclaringNode(symbol);
                if (declaringNode is ClassDeclarationNode classDecl)
                {
                    return FindCompilationUnitPath(classDecl, asts, compilation);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for a class type by looking up its
    /// declaring node in the symbol table.
    /// </summary>
    private static SourceText? FindSourceTextForClassType(
        ClassType classType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.DeclaredType == classType)
            {
                var declaringNode = symbolTable.GetDeclaringNode(symbol);
                if (declaringNode is ClassDeclarationNode classDecl)
                {
                    return FindSourceTextForClassDecl(classDecl, asts, compilation);
                }
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1d — Validate interface satisfaction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After cycles are broken, check that each class and struct implementing
    /// interfaces provides all required methods. Walks the interface hierarchy
    /// transitively (using a visited set for diamond inheritance) to collect
    /// required methods, then searches the implementing type's member scope
    /// (and base type chain for classes) for matching methods.
    /// </summary>
    private static void Pass1_ValidateInterfaceSatisfaction(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Class when symbol.DeclaredType is ClassType classType:
                    if (classType.Interfaces.Count > 0)
                    {
                        ValidateInterfaceSatisfaction(
                            classType.DisplayName, classType.Interfaces, classType.MemberScope,
                            classType, symbolTable, asts, compilation, diagnostics);
                    }
                    break;

                case SymbolKind.Struct when symbol.DeclaredType is StructType structType:
                    if (structType.Interfaces.Count > 0)
                    {
                        ValidateInterfaceSatisfaction(
                            structType.DisplayName, structType.Interfaces, structType.MemberScope,
                            null, symbolTable, asts, compilation, diagnostics);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Validates that the implementing type provides all methods required by
    /// its interfaces (including transitively inherited interfaces).
    /// </summary>
    /// <param name="typeName">Display name of the implementing type.</param>
    /// <param name="interfaces">Direct interfaces of the implementing type.</param>
    /// <param name="memberScope">Member scope of the implementing type.</param>
    /// <param name="classType">If the implementing type is a class, its ClassType (for base chain walking). Null for structs.</param>
    /// <param name="symbolTable">The symbol table.</param>
    /// <param name="asts">The ASTs.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="diagnostics">Diagnostic bag.</param>
    private static void ValidateInterfaceSatisfaction(
        string typeName,
        IReadOnlyList<IType> interfaces,
        IScope memberScope,
        ClassType? classType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Collect all required methods transitively from the interface hierarchy.
        // Each entry is (interfaceType, methodSymbol) so we can report which
        // interface requires the method.
        var requiredMethods = new List<(InterfaceType Interface, ISymbol Method)>();
        var requiredProperties = new List<(InterfaceType Interface, ISymbol Property)>();
        var visitedInterfaces = new HashSet<InterfaceType>();
        CollectRequiredMembers(interfaces, requiredMethods, requiredProperties, visitedInterfaces);

        // For each required method, search for a matching implementation.
        foreach (var (requiredInterface, requiredMethod) in requiredMethods)
        {
            var requiredNode = symbolTable.GetDeclaringNode(requiredMethod);
            if (requiredNode is not InterfaceMethodDeclarationNode requiredMethodDecl)
                continue;

            // Search the implementing type's member scope for a method with the same name.
            var matchResult = FindMatchingMethod(
                requiredMethod.Name, requiredMethodDecl,
                memberScope, classType, symbolTable);

            switch (matchResult)
            {
                case MethodMatchResult.MatchedResult:
                    // Method found with matching signature — satisfied.
                    break;

                case MethodMatchResult.MissingResult:
                {
                    // No method with this name found anywhere in the type or its base chain.
                    // Emit MIST_SM_0022 with the specific interface method signature.
                    var paramSig = FormatParameterTypes(requiredMethodDecl.Parameters);
                    var missingSourceText = FindSourceTextForType(
                        typeName, symbolTable, asts, compilation);
                    var missingNode = FindDeclaringNodeForType(typeName, symbolTable);

                    var message = $"'{typeName}' does not implement interface method '{requiredInterface.DisplayName}.{requiredMethod.Name}({paramSig})'.";

                    if (missingNode is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            MissingInterfaceOverload.Value,
                            DiagnosticSeverity.Error,
                            message,
                            missingNode,
                            missingSourceText));
                    }
                    else
                    {
                        string? missingPath = FindCompilationUnitPathForType(
                            typeName, symbolTable, asts, compilation);

                        diagnostics.Add(new Diagnostic(
                            MissingInterfaceOverload,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            message,
                            Location: default,
                            CompilationUnitPath: missingPath ?? "<unknown>",
                            AnalyzerName: null));
                    }
                    break;
                }

                case MethodMatchResult.SignatureMismatch:
                {
                    // Method name matches but no overload has a matching parameter signature.
                    // Emit MIST_SM_0022 with the specific interface method signature.
                    var paramSig = FormatParameterTypes(requiredMethodDecl.Parameters);
                    var mismatchSourceText = FindSourceTextForType(
                        typeName, symbolTable, asts, compilation);
                    var mismatchNode = FindDeclaringNodeForType(typeName, symbolTable);

                    var message = $"'{typeName}' does not implement interface method '{requiredInterface.DisplayName}.{requiredMethod.Name}({paramSig})'.";

                    if (mismatchNode is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            MissingInterfaceOverload.Value,
                            DiagnosticSeverity.Error,
                            message,
                            mismatchNode,
                            mismatchSourceText));
                    }
                    else
                    {
                        string? mismatchPath = FindCompilationUnitPathForType(
                            typeName, symbolTable, asts, compilation);

                        diagnostics.Add(new Diagnostic(
                            MissingInterfaceOverload,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            message,
                            Location: default,
                            CompilationUnitPath: mismatchPath ?? "<unknown>",
                            AnalyzerName: null));
                    }
                    break;
                }
            }
        }

        // For each required property, search for a matching implementation.
        foreach (var (requiredInterface, requiredProperty) in requiredProperties)
        {
            var requiredPropInfo = (requiredProperty as Symbol)?.PropertyMetadata;
            if (requiredPropInfo is null)
                continue;

            // Search the implementing type's member scope (and base chain) for a property with the same name.
            var implProperty = FindMatchingProperty(requiredProperty.Name, memberScope, classType);

            if (implProperty is null)
            {
                string? missingPath = FindCompilationUnitPathForType(
                    typeName, symbolTable, asts, compilation);

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_MISSING_INTERFACE_PROPERTY"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"'{typeName}' does not implement interface property '{requiredInterface.DisplayName}.{requiredProperty.Name}'.",
                    Location: default,
                    CompilationUnitPath: missingPath ?? "<unknown>",
                    AnalyzerName: null));
                continue;
            }

            var implPropInfo = (implProperty as Symbol)?.PropertyMetadata;
            if (implPropInfo is null)
                continue;

            // Check that the implementing property has the required accessors.
            if (requiredPropInfo.HasGet && !implPropInfo.HasGet)
            {
                string? path = FindCompilationUnitPathForType(typeName, symbolTable, asts, compilation);
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_MISSING_INTERFACE_PROPERTY_ACCESSOR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"'{typeName}.{requiredProperty.Name}' does not implement 'get' accessor required by interface '{requiredInterface.DisplayName}'.",
                    Location: default,
                    CompilationUnitPath: path ?? "<unknown>",
                    AnalyzerName: null));
            }

            if (requiredPropInfo.HasSet && !implPropInfo.HasSet && !implPropInfo.HasInit)
            {
                string? path = FindCompilationUnitPathForType(typeName, symbolTable, asts, compilation);
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_MISSING_INTERFACE_PROPERTY_ACCESSOR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"'{typeName}.{requiredProperty.Name}' does not implement 'set' accessor required by interface '{requiredInterface.DisplayName}'.",
                    Location: default,
                    CompilationUnitPath: path ?? "<unknown>",
                    AnalyzerName: null));
            }
        }
    }

    /// <summary>
    /// Recursively collects all required methods and properties from the interface hierarchy,
    /// using a visited set to handle diamond inheritance.
    /// </summary>
    private static void CollectRequiredMembers(
        IReadOnlyList<IType> interfaces,
        List<(InterfaceType Interface, ISymbol Method)> requiredMethods,
        List<(InterfaceType Interface, ISymbol Property)> requiredProperties,
        HashSet<InterfaceType> visited)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (!visited.Add(iface))
                continue;

            // Add all method and property members from this interface.
            foreach (var member in iface.MemberScope.Members)
            {
                if (member.Kind == SymbolKind.Method)
                {
                    requiredMethods.Add((iface, member));
                }
                else if (member.Kind == SymbolKind.Property)
                {
                    requiredProperties.Add((iface, member));
                }
            }

            // Recurse into parent interfaces.
            if (iface.Interfaces.Count > 0)
            {
                CollectRequiredMembers(iface.Interfaces, requiredMethods, requiredProperties, visited);
            }
        }
    }

    /// <summary>
    /// Searches the implementing type's member scope (and base type chain for
    /// classes) for a property with the given name.
    /// </summary>
    private static ISymbol? FindMatchingProperty(
        string propertyName,
        IScope memberScope,
        ClassType? classType)
    {
        // Search the direct member scope first.
        foreach (var member in memberScope.Members)
        {
            if (member.Kind == SymbolKind.Property && member.Name == propertyName)
                return member;
        }

        // For classes, walk the base type chain.
        if (classType is not null)
        {
            var ancestor = classType.BaseType as ClassType;
            while (ancestor is not null)
            {
                foreach (var member in ancestor.MemberScope.Members)
                {
                    if (member.Kind == SymbolKind.Property && member.Name == propertyName)
                        return member;
                }
                ancestor = ancestor.BaseType as ClassType;
            }
        }

        return null;
    }

    /// <summary>
    /// Result of searching for a matching method implementation.
    /// </summary>
    private abstract record MethodMatchResult
    {
        public static readonly MethodMatchResult Matched = new MatchedResult();
        public static readonly MethodMatchResult Missing = new MissingResult();

        public static MethodMatchResult Mismatch(string expectedSig, string actualSig)
            => new SignatureMismatch(expectedSig, actualSig);

        public sealed record MatchedResult : MethodMatchResult;
        public sealed record MissingResult : MethodMatchResult;
        public sealed record SignatureMismatch(string ExpectedSig, string ActualSig) : MethodMatchResult;
    }

    /// <summary>
    /// Searches the implementing type's member scope (and base type chain for
    /// classes) for a method matching the required interface method.
    /// With overloading, we must check all scopes in the chain — a matching
    /// overload might be in a base class even if the derived class has methods
    /// with the same name but different signatures.
    /// </summary>
    private static MethodMatchResult FindMatchingMethod(
        string methodName,
        InterfaceMethodDeclarationNode requiredMethodDecl,
        IScope memberScope,
        ClassType? classType,
        SymbolTable symbolTable)
    {
        // Track whether we found any method with the same name across all scopes.
        var foundAnyWithName = false;
        string? lastMismatchExpected = null;
        string? lastMismatchActual = null;

        // Search the direct member scope first.
        var result = SearchScopeForMethod(methodName, requiredMethodDecl, memberScope, symbolTable);
        if (result is MethodMatchResult.MatchedResult)
            return result;
        if (result is MethodMatchResult.SignatureMismatch directMismatch)
        {
            foundAnyWithName = true;
            lastMismatchExpected = directMismatch.ExpectedSig;
            lastMismatchActual = directMismatch.ActualSig;
        }

        // For classes, walk the base type chain.
        if (classType is not null)
        {
            var ancestor = classType.BaseType as ClassType;
            while (ancestor is not null)
            {
                result = SearchScopeForMethod(methodName, requiredMethodDecl, ancestor.MemberScope, symbolTable);
                if (result is MethodMatchResult.MatchedResult)
                    return result;
                if (result is MethodMatchResult.SignatureMismatch ancestorMismatch)
                {
                    foundAnyWithName = true;
                    lastMismatchExpected = ancestorMismatch.ExpectedSig;
                    lastMismatchActual = ancestorMismatch.ActualSig;
                }

                ancestor = ancestor.BaseType as ClassType;
            }
        }

        if (foundAnyWithName)
            return MethodMatchResult.Mismatch(lastMismatchExpected!, lastMismatchActual!);

        return MethodMatchResult.Missing;
    }

    /// <summary>
    /// Searches a single scope for a method with the given name and checks
    /// whether any overload's signature matches the required interface method.
    /// With overloading, multiple methods may share the same name — we must
    /// check all of them before concluding there is no match.
    /// </summary>
    private static MethodMatchResult SearchScopeForMethod(
        string methodName,
        InterfaceMethodDeclarationNode requiredMethodDecl,
        IScope scope,
        SymbolTable symbolTable)
    {
        var foundAnyWithName = false;
        string? lastActualSig = null;

        foreach (var member in scope.Members)
        {
            if (member.Kind != SymbolKind.Method || member.Name != methodName)
                continue;

            foundAnyWithName = true;

            // Found a method with the same name. Check signature.
            var memberNode = symbolTable.GetDeclaringNode(member);
            if (memberNode is not MethodDeclarationNode implMethodDecl)
                continue;

            if (SignaturesMatch(requiredMethodDecl, implMethodDecl))
                return MethodMatchResult.Matched;

            // Track the last seen actual signature for diagnostic purposes.
            lastActualSig = FormatMethodSignature(implMethodDecl);
        }

        if (foundAnyWithName)
        {
            // Name matches but no overload has a matching signature.
            var expectedSig = FormatInterfaceMethodSignature(requiredMethodDecl);
            return MethodMatchResult.Mismatch(expectedSig, lastActualSig ?? "()");
        }

        return MethodMatchResult.Missing;
    }

    /// <summary>
    /// Checks whether an implementing method's signature matches the required
    /// interface method's signature (return type and parameter types).
    /// </summary>
    private static bool SignaturesMatch(
        InterfaceMethodDeclarationNode required,
        MethodDeclarationNode actual)
    {
        // Compare return types.
        if (!TypeReferencesMatch(required.ReturnType, actual.ReturnType))
            return false;

        // Compare parameter types.
        var requiredParams = ExtractParameterTypes(required.Parameters);
        var actualParams = ExtractParameterTypes(actual.Parameters);

        if (requiredParams.Count != actualParams.Count)
            return false;

        for (var i = 0; i < requiredParams.Count; i++)
        {
            if (!TypeReferencesMatch(requiredParams[i], actualParams[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts the list of parameter type references from a parameter list node.
    /// </summary>
    private static List<TypeReferenceNode> ExtractParameterTypes(ParameterListNode? paramList)
    {
        var types = new List<TypeReferenceNode>();
        if (paramList is null)
            return types;

        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                types.Add(param.Type);
            }
        }

        return types;
    }

    /// <summary>
    /// Compares two TypeReferenceNode instances for structural equality.
    /// Two <c>unknown</c> references match each other (Requirement 12.3 —
    /// trait parameter positions require exact unknown-to-unknown match).
    /// </summary>
    private static bool TypeReferencesMatch(TypeReferenceNode a, TypeReferenceNode b)
    {
        return (a, b) switch
        {
            (VoidTypeRefTypeReferenceNode, VoidTypeRefTypeReferenceNode) => true,

            (PrimitiveTypeRefTypeReferenceNode pa, PrimitiveTypeRefTypeReferenceNode pb) =>
                PrimitiveTypesMatch(pa.PrimitiveType, pb.PrimitiveType)
                && pa.ArrayRank.HasValue == pb.ArrayRank.HasValue,

            (NamedTypeRefTypeReferenceNode na, NamedTypeRefTypeReferenceNode nb) =>
                na.Name.IDENTIFIER.Lexeme == nb.Name.IDENTIFIER.Lexeme
                && na.ArrayRank.HasValue == nb.ArrayRank.HasValue,

            // Two `unknown` references match each other exactly. This is
            // required for both trait return positions (where `unknown`
            // in both sides trivially satisfies Req 12.1) and trait
            // parameter positions (Req 12.3 — `unknown` in a trait
            // parameter requires exactly `unknown` in the implementer).
            (UnknownTypeRefTypeReferenceNode, UnknownTypeRefTypeReferenceNode) => true,

            // Two tuple type references match if they have the same arity
            // and each element type reference matches (names are ignored
            // for structural matching, consistent with name-erasure semantics).
            (TupleTypeRefTypeReferenceNode ta, TupleTypeRefTypeReferenceNode tb) =>
                TupleTypeReferencesMatch(ta, tb),

            // A named type ref could match a primitive type ref if someone
            // uses the type name form (e.g., "string" as named vs primitive).
            // In practice the parser produces consistent node types, so
            // cross-kind comparisons are mismatches.
            _ => false,
        };
    }

    /// <summary>
    /// Compares two <see cref="TupleTypeRefTypeReferenceNode"/> instances for
    /// structural equivalence: same arity and each element type reference matches.
    /// Element names are ignored (name-erasure semantics).
    /// </summary>
    private static bool TupleTypeReferencesMatch(
        TupleTypeRefTypeReferenceNode a, TupleTypeRefTypeReferenceNode b)
    {
        // Check arity: both must have the same number of additional elements.
        if (a.TupleTypeElements.Length != b.TupleTypeElements.Length)
            return false;

        // Compare First elements.
        if (!TypeReferencesMatch(a.First.Type, b.First.Type))
            return false;

        // Compare Rest elements.
        if (!TypeReferencesMatch(a.Rest.Type, b.Rest.Type))
            return false;

        // Compare additional elements.
        for (var i = 0; i < a.TupleTypeElements.Length; i++)
        {
            if (!TypeReferencesMatch(a.TupleTypeElements[i].Type, b.TupleTypeElements[i].Type))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Compares a source type reference against a trait target type reference,
    /// substituting type parameters in the target with the provided type arguments.
    /// For example, if the trait member type is <c>T</c> and <c>T=int</c>, this
    /// checks whether the source type reference matches <c>int</c>.
    /// </summary>
    private static bool TypeReferencesMatchWithSubstitution(
        TypeReferenceNode sourceRef,
        TypeReferenceNode targetRef,
        IReadOnlyList<IType> typeParams,
        IReadOnlyList<IType> typeArgs,
        SourceText? traitSourceText = null,
        ImmutableArray<Storm.Runtime.SyntaxNode>? asts = null,
        MistCompilation? compilation = null)
    {
        // If the target is a named type ref that matches a type parameter name,
        // substitute it with the corresponding type argument and compare.
        if (targetRef is NamedTypeRefTypeReferenceNode namedTarget)
        {
            var targetName = GetNamedTypeRefName(namedTarget, traitSourceText);

            // If primary source text lookup failed, try all compilation units.
            if (string.IsNullOrEmpty(targetName) && asts.HasValue && compilation is not null)
            {
                targetName = GetNamedTypeRefName(namedTarget, asts.Value, compilation);
            }

            for (var i = 0; i < typeParams.Count; i++)
            {
                var (paramName7953, _) = GetTypeParameterNameAndOrdinal(typeParams[i]);
                if (paramName7953 == targetName)
                {
                    // The target type parameter maps to typeArgs[i].
                    // Compare the source type reference against the substituted type.
                    return TypeReferenceMatchesSubstitutedType(sourceRef, typeArgs[i]);
                }
            }

            // Not a type parameter — fall through to standard comparison.
        }

        // For non-type-parameter targets, use the standard comparison.
        return TypeReferencesMatch(sourceRef, targetRef);
    }

    /// <summary>
    /// Checks whether a source <see cref="TypeReferenceNode"/> matches a substituted
    /// <see cref="IType"/>. This bridges AST-level type references with semantic-level
    /// resolved types for with-block validation of generic traits.
    /// </summary>
    private static bool TypeReferenceMatchesSubstitutedType(
        TypeReferenceNode sourceRef, IType substitutedType)
    {
        return (sourceRef, substitutedType) switch
        {
            // Primitive type references match their corresponding semantic types.
            (PrimitiveTypeRefTypeReferenceNode prim, _) =>
                PrimitiveTypeRefMatchesType(prim, substitutedType),

            // Named type references match by display name.
            (NamedTypeRefTypeReferenceNode named, _) =>
                named.Name.IDENTIFIER.Lexeme == substitutedType.DisplayName
                && !named.ArrayRank.HasValue,

            // Void matches void.
            (VoidTypeRefTypeReferenceNode, _) =>
                substitutedType.DisplayName == "Void",

            _ => false,
        };
    }

    /// <summary>
    /// Checks whether a primitive type reference AST node matches a resolved semantic type.
    /// </summary>
    private static bool PrimitiveTypeRefMatchesType(
        PrimitiveTypeRefTypeReferenceNode primRef, IType type)
    {
        var primName = FormatPrimitiveType(primRef.PrimitiveType);
        // The semantic type's DisplayName may be the stdlib name (e.g., "Int32")
        // rather than the keyword (e.g., "int"). Check both the keyword and the
        // stdlib type name from KeywordAliases.
        if (type.DisplayName == primName && !primRef.ArrayRank.HasValue)
            return true;

        // Check against the stdlib type name (e.g., "int" → "Standard.Int32" → "Int32").
        if (KeywordAliases.Map.TryGetValue(primName, out var qualifiedName))
        {
            // qualifiedName is e.g. "Standard.Int32"; extract the simple name.
            var dotIndex = qualifiedName.LastIndexOf('.');
            var simpleName = dotIndex >= 0 ? qualifiedName[(dotIndex + 1)..] : qualifiedName;
            if (type.DisplayName == simpleName && !primRef.ArrayRank.HasValue)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Formats a trait member type reference with type parameter substitution applied.
    /// For example, if the type reference is <c>T</c> and <c>T=int</c>, returns <c>"int"</c>.
    /// </summary>
    private static string FormatTypeReferenceWithSubstitution(
        TypeReferenceNode typeRef,
        IReadOnlyList<IType> typeParams,
        IReadOnlyList<IType> typeArgs,
        SourceText? sourceText = null,
        ImmutableArray<Storm.Runtime.SyntaxNode>? asts = null,
        MistCompilation? compilation = null)
    {
        if (typeRef is NamedTypeRefTypeReferenceNode namedRef)
        {
            var name = GetNamedTypeRefName(namedRef, sourceText);

            // If primary source text lookup failed, try all compilation units.
            if (string.IsNullOrEmpty(name) && asts.HasValue && compilation is not null)
            {
                name = GetNamedTypeRefName(namedRef, asts.Value, compilation);
            }

            for (var i = 0; i < typeParams.Count; i++)
            {
                var (paramName8045, _) = GetTypeParameterNameAndOrdinal(typeParams[i]);
                if (paramName8045 == name)
                {
                    return typeArgs[i].DisplayName;
                }
            }
        }

        // Not a type parameter — use the standard formatting.
        return FormatTypeReference(typeRef);
    }

    /// <summary>
    /// Extracts the textual name from a <see cref="NamedTypeRefTypeReferenceNode"/>,
    /// preferring <c>Name.IDENTIFIER.Lexeme</c> and falling back to slicing
    /// the AST span out of an optional source text. Returns an empty string
    /// when neither source is available.
    /// <para>
    /// Used during trait satisfaction where AST nodes produced by the
    /// parser may carry an empty lexeme on <c>IDENTIFIER</c> (the parser
    /// populates only the span in some cases).
    /// </para>
    /// </summary>
    private static string GetNamedTypeRefName(
        NamedTypeRefTypeReferenceNode named, SourceText? sourceText)
    {
        var lexeme = named.Name.IDENTIFIER.Lexeme;
        if (!string.IsNullOrEmpty(lexeme))
            return lexeme;

        // Fallback 1: try the IDENTIFIER token's own span against the source text.
        if (sourceText is not null)
        {
            var tokenSpan = named.Name.IDENTIFIER.Span;
            if (tokenSpan.Length > 0 && tokenSpan.Start + tokenSpan.Length <= sourceText.Contents.Length)
            {
                var extracted = sourceText.Contents.Substring(tokenSpan.Start, tokenSpan.Length);
                if (!string.IsNullOrEmpty(extracted) && char.IsLetter(extracted[0]))
                    return extracted;
            }

            // Fallback 2: try the QualifiedIdentifier's span.
            var span = named.Name.Span;
            if (span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
            {
                var extracted = sourceText.Contents.Substring(span.Start, span.Length);
                if (!string.IsNullOrEmpty(extracted) && char.IsLetter(extracted[0]))
                    return extracted;
            }
        }

        // Last resort: nothing resolvable.
        return "";
    }

    /// <summary>
    /// Variant of <see cref="GetNamedTypeRefName"/> that tries multiple source texts.
    /// Used when the correct source text is uncertain (e.g., multi-file compilation
    /// where span-based file identification may be unreliable).
    /// </summary>
    private static string GetNamedTypeRefName(
        NamedTypeRefTypeReferenceNode named,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        var lexeme = named.Name.IDENTIFIER.Lexeme;
        if (!string.IsNullOrEmpty(lexeme))
            return lexeme;

        // Try each compilation unit's source text.
        for (var i = 0; i < compilation.Units.Length; i++)
        {
            var src = compilation.Units[i].Source;
            if (src is null) continue;

            var tokenSpan = named.Name.IDENTIFIER.Span;
            if (tokenSpan.Length > 0 && tokenSpan.Start + tokenSpan.Length <= src.Contents.Length)
            {
                var extracted = src.Contents.Substring(tokenSpan.Start, tokenSpan.Length);
                if (!string.IsNullOrEmpty(extracted) && char.IsLetter(extracted[0]))
                    return extracted;
            }

            var span = named.Name.Span;
            if (span.Length > 0 && span.Start + span.Length <= src.Contents.Length)
            {
                var extracted = src.Contents.Substring(span.Start, span.Length);
                if (!string.IsNullOrEmpty(extracted) && char.IsLetter(extracted[0]))
                    return extracted;
            }
        }

        return "";
    }

    /// <summary>
    /// Compares a trait member's return type reference (<paramref name="traitReturn"/>)
    /// to a candidate implementer's return type reference (<paramref name="implReturn"/>)
    /// under the relaxed rules for trait satisfaction (unknown-type feature,
    /// Req 12.1 and Req 12.2). Returns <c>true</c> when the implementer's
    /// return type satisfies the trait's return position.
    /// <para>
    /// Req 12.1: a trait return of <c>unknown</c> matches any non-<c>void</c>
    /// implementer return.
    /// </para>
    /// <para>
    /// Req 12.2: a trait return of <c>G&lt;…, unknown, …&gt;</c> matches an
    /// implementer return of <c>G&lt;…, T_i, …&gt;</c> — the same generic
    /// definition with type arguments that differ only by the trait's
    /// <c>unknown</c> positions — when the generic definition is a class
    /// (Req 8.6 class-only restriction). The widening is position-wise and
    /// recurses into nested constructed types.
    /// </para>
    /// <para>
    /// Non-return positions (trait parameters) MUST use the exact-match
    /// <see cref="TypeReferencesMatch(TypeReferenceNode, TypeReferenceNode)"/>
    /// helper to preserve Req 12.3.
    /// </para>
    /// </summary>
    private static bool TypeReferencesMatchForTraitReturn(
        TypeReferenceNode traitReturn,
        TypeReferenceNode implReturn,
        Scope globalScope,
        SymbolTable symbolTable,
        SourceText? sourceText)
    {
        // Req 12.1 — trait return `unknown` matches any non-void implementer
        // return. The "non-void" guard is enforced by rejecting a void
        // implementer when the trait asks for `unknown`.
        if (traitReturn is UnknownTypeRefTypeReferenceNode)
        {
            return implReturn is not VoidTypeRefTypeReferenceNode;
        }

        // Req 12.2 — widening on generic type arguments. Both sides must be
        // the same generic definition; only the trait's arguments may
        // contain `unknown` at positions where the implementer has a
        // concrete argument. The class-only restriction (Req 8.6) is
        // enforced by a kind check on the generic definition symbol.
        if (traitReturn is NamedTypeRefTypeReferenceNode traitNamed
            && implReturn is NamedTypeRefTypeReferenceNode implNamed)
        {
            var traitName = GetNamedTypeRefName(traitNamed, sourceText);
            var implName = GetNamedTypeRefName(implNamed, sourceText);

            if (traitName == implName
                && traitNamed.ArrayRank.HasValue == implNamed.ArrayRank.HasValue)
            {
                // If neither side carries type arguments, this is just a
                // plain name match.
                if (traitNamed.TypeArgs is null && implNamed.TypeArgs is null)
                    return true;

                // If only one side carries type arguments, arities
                // mismatch — not a valid match.
                if (traitNamed.TypeArgs is null || implNamed.TypeArgs is null)
                    return false;

                // Both sides carry type arguments — apply the widening rule.
                return TypeArgumentsMatchWithUnknownWidening(
                    traitName,
                    traitNamed.TypeArgs,
                    implNamed.TypeArgs,
                    globalScope,
                    symbolTable,
                    sourceText);
            }
        }

        // Otherwise fall back to exact structural match.
        return TypeReferencesMatch(traitReturn, implReturn);
    }

    /// <summary>
    /// Position-wise widening check for the type arguments of a single
    /// generic construction in a trait return position (Req 12.2, Req 8.6).
    /// <para>
    /// For each position <c>i</c>, accepts when:
    /// <list type="bullet">
    ///   <item>The two references are structurally equal; or</item>
    ///   <item>The trait's argument at position <c>i</c> is <c>unknown</c>
    ///         and the generic definition named by <paramref name="genericDefName"/>
    ///         is a class (Req 8.6); or</item>
    ///   <item>Both sides are nested constructed types with the same
    ///         name, and the nested widening recurses successfully.</item>
    /// </list>
    /// </para>
    /// <para>
    /// The implementer's argument at position <c>i</c> being <c>unknown</c>
    /// while the trait's argument is concrete is <b>not</b> accepted — the
    /// widening is one-directional (Req 8.5 symmetry applied to traits).
    /// </para>
    /// </summary>
    private static bool TypeArgumentsMatchWithUnknownWidening(
        string genericDefName,
        TypeArgumentListNode traitArgs,
        TypeArgumentListNode implArgs,
        Scope globalScope,
        SymbolTable symbolTable,
        SourceText? sourceText)
    {
        // Flatten each list into a single ordered sequence of type refs.
        var traitList = FlattenTypeArguments(traitArgs);
        var implList = FlattenTypeArguments(implArgs);

        if (traitList.Count != implList.Count)
            return false;

        // Look up the generic definition's TypeKind to enforce Req 8.6.
        var isClassGeneric = LookupGenericDefinitionIsClass(
            genericDefName, globalScope, symbolTable);

        for (var i = 0; i < traitList.Count; i++)
        {
            var traitArg = traitList[i];
            var implArg = implList[i];

            // Identity at this position — no widening happens here.
            if (TypeReferencesMatch(traitArg, implArg))
                continue;

            // Widening: trait's argument is `unknown`, implementer's is
            // concrete. The class-only rule (Req 8.6) is enforced at the
            // level where the widening actually happens.
            if (traitArg is UnknownTypeRefTypeReferenceNode)
            {
                if (!isClassGeneric)
                    return false;
                // Implementer must be non-void and non-unknown (unknown-to-
                // unknown is already covered by TypeReferencesMatch above).
                if (implArg is VoidTypeRefTypeReferenceNode)
                    return false;
                continue;
            }

            // Nested widening: both sides are named generics with the
            // same name. Recurse into the nested argument list with the
            // nested name as the new defCtx. Nested widening is allowed
            // even when the outer generic is a struct — what is
            // prohibited is widening the struct's own arguments.
            if (traitArg is NamedTypeRefTypeReferenceNode traitNested
                && implArg is NamedTypeRefTypeReferenceNode implNested
                && traitNested.ArrayRank.HasValue == implNested.ArrayRank.HasValue
                && traitNested.TypeArgs is not null
                && implNested.TypeArgs is not null)
            {
                var traitNestedName = GetNamedTypeRefName(traitNested, sourceText);
                var implNestedName = GetNamedTypeRefName(implNested, sourceText);
                if (traitNestedName != implNestedName)
                    return false;

                if (!TypeArgumentsMatchWithUnknownWidening(
                        traitNestedName,
                        traitNested.TypeArgs,
                        implNested.TypeArgs,
                        globalScope,
                        symbolTable,
                        sourceText))
                {
                    return false;
                }
                continue;
            }

            // Anything else (mismatched generic names at nested level,
            // concrete-to-unknown direction, etc.) is not a valid widening.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Flattens a <see cref="TypeArgumentListNode"/> into a plain ordered
    /// list of type references. The AST node splits the first argument
    /// (<c>TypeReference</c>) from the rest (<c>TypeReferences</c>); this
    /// helper merges them so callers can index by position.
    /// </summary>
    private static List<TypeReferenceNode> FlattenTypeArguments(TypeArgumentListNode node)
    {
        var result = new List<TypeReferenceNode>(1 + node.TypeArguments.Length)
        {
            node.GetTypeReference(),
        };
        foreach (var extra in node.GetTypeReferences())
            result.Add(extra);
        return result;
    }

    /// <summary>
    /// Looks up the symbol registered for the generic definition named
    /// <paramref name="genericDefName"/> and returns <c>true</c> if it is
    /// a class (<see cref="SymbolKind.Class"/>). Returns <c>false</c>
    /// when the symbol is a struct, an interface, or cannot be resolved.
    /// Used to enforce Req 8.6 (class-only covariant widening) during
    /// trait return-position matching.
    /// </summary>
    private static bool LookupGenericDefinitionIsClass(
        string genericDefName,
        Scope globalScope,
        SymbolTable symbolTable)
    {
        // First try the global scope, which covers most stdlib and
        // user-defined top-level types.
        var symbol = globalScope.Lookup(genericDefName);
        if (symbol is not null
            && symbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface)
        {
            return symbol.Kind == SymbolKind.Class;
        }

        // Fall back to a symbol-table walk for nested/namespaced types.
        // This handles the common case where user types live inside a
        // namespace scope (not the global scope) and are only reachable
        // via their containing namespace.
        foreach (var candidate in symbolTable.AllSymbols)
        {
            if (candidate.Name == genericDefName
                && (candidate.Kind == SymbolKind.Class
                    || candidate.Kind == SymbolKind.Struct
                    || candidate.Kind == SymbolKind.Interface))
            {
                // Skip type parameters that were registered as
                // SymbolKind.Class by RegisterTypeParameters — their
                // DeclaredType is a TypeParameterType, not a class type.
                if (candidate.DeclaredType is Types.TypeParameterType)
                    continue;
                return candidate.Kind == SymbolKind.Class;
            }
        }

        return false;
    }

    /// <summary>
    /// Compares two PrimitiveTypeNode instances for equality by variant type.
    /// </summary>
    private static bool PrimitiveTypesMatch(PrimitiveTypeNode a, PrimitiveTypeNode b)
    {
        return (a, b) switch
        {
            (IntTypePrimitiveTypeNode, IntTypePrimitiveTypeNode) => true,
            (UintTypePrimitiveTypeNode, UintTypePrimitiveTypeNode) => true,
            (LongTypePrimitiveTypeNode, LongTypePrimitiveTypeNode) => true,
            (UlongTypePrimitiveTypeNode, UlongTypePrimitiveTypeNode) => true,
            (ShortTypePrimitiveTypeNode, ShortTypePrimitiveTypeNode) => true,
            (UshortTypePrimitiveTypeNode, UshortTypePrimitiveTypeNode) => true,
            (ByteTypePrimitiveTypeNode, ByteTypePrimitiveTypeNode) => true,
            (SbyteTypePrimitiveTypeNode, SbyteTypePrimitiveTypeNode) => true,
            (FloatTypePrimitiveTypeNode, FloatTypePrimitiveTypeNode) => true,
            (DoubleTypePrimitiveTypeNode, DoubleTypePrimitiveTypeNode) => true,
            (DecimalTypePrimitiveTypeNode, DecimalTypePrimitiveTypeNode) => true,
            (BoolTypePrimitiveTypeNode, BoolTypePrimitiveTypeNode) => true,
            (CharTypePrimitiveTypeNode, CharTypePrimitiveTypeNode) => true,
            (StringTypePrimitiveTypeNode, StringTypePrimitiveTypeNode) => true,
            _ => false,
        };
    }

    /// <summary>
    /// Formats an interface method signature as a string for diagnostic messages.
    /// Format: "returnType(paramType1, paramType2, ...)"
    /// </summary>
    private static string FormatInterfaceMethodSignature(InterfaceMethodDeclarationNode methodDecl)
    {
        var returnType = FormatTypeReference(methodDecl.ReturnType);
        var paramTypes = FormatParameterTypes(methodDecl.Parameters);
        return $"{returnType}({paramTypes})";
    }

    /// <summary>
    /// Formats a method signature as a string for diagnostic messages.
    /// Format: "returnType(paramType1, paramType2, ...)"
    /// </summary>
    private static string FormatMethodSignature(MethodDeclarationNode methodDecl)
    {
        var returnType = FormatTypeReference(methodDecl.ReturnType);
        var paramTypes = FormatParameterTypes(methodDecl.Parameters);
        return $"{returnType}({paramTypes})";
    }

    /// <summary>
    /// Formats a parameter list as a comma-separated string of type names.
    /// </summary>
    private static string FormatParameterTypes(ParameterListNode? paramList)
    {
        if (paramList is null)
            return "";

        var types = new List<string>();
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                types.Add(FormatTypeReference(param.Type));
            }
        }

        return string.Join(", ", types);
    }

    /// <summary>
    /// Formats a TypeReferenceNode as a human-readable string.
    /// </summary>
    private static string FormatTypeReference(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            VoidTypeRefTypeReferenceNode => "void",
            PrimitiveTypeRefTypeReferenceNode p =>
                FormatPrimitiveType(p.PrimitiveType) + (p.ArrayRank.HasValue ? "[]" : ""),
            NamedTypeRefTypeReferenceNode n =>
                n.Name.IDENTIFIER.Lexeme + (n.ArrayRank.HasValue ? "[]" : ""),
            TupleTypeRefTypeReferenceNode t => FormatTupleTypeReference(t),
            _ => "unknown",
        };
    }

    /// <summary>
    /// Formats a <see cref="TupleTypeRefTypeReferenceNode"/> as a human-readable
    /// string of the form <c>(T1, T2)</c> or <c>(T1 name1, T2 name2)</c>.
    /// </summary>
    private static string FormatTupleTypeReference(TupleTypeRefTypeReferenceNode tupleRef)
    {
        var parts = new List<string>();

        // Format First element.
        parts.Add(FormatTupleElement(tupleRef.First));

        // Format Rest element.
        parts.Add(FormatTupleElement(tupleRef.Rest));

        // Format additional elements.
        foreach (var additional in tupleRef.TupleTypeElements)
        {
            parts.Add(FormatTupleElement(additional));
        }

        return $"({string.Join(", ", parts)})";
    }

    /// <summary>
    /// Formats a single <see cref="TupleTypeElementNode"/> as <c>Type</c> or <c>Type name</c>.
    /// </summary>
    private static string FormatTupleElement(TupleTypeElementNode element)
    {
        var typeStr = FormatTypeReference(element.Type);
        var name = element.Name?.Lexeme;
        return name is not null ? $"{typeStr} {name}" : typeStr;
    }

    /// <summary>
    /// Formats a PrimitiveTypeNode as a keyword string.
    /// </summary>
    private static string FormatPrimitiveType(PrimitiveTypeNode node)
    {
        return node switch
        {
            IntTypePrimitiveTypeNode => "int",
            UintTypePrimitiveTypeNode => "uint",
            LongTypePrimitiveTypeNode => "long",
            UlongTypePrimitiveTypeNode => "ulong",
            ShortTypePrimitiveTypeNode => "short",
            UshortTypePrimitiveTypeNode => "ushort",
            ByteTypePrimitiveTypeNode => "byte",
            SbyteTypePrimitiveTypeNode => "sbyte",
            FloatTypePrimitiveTypeNode => "float",
            DoubleTypePrimitiveTypeNode => "double",
            DecimalTypePrimitiveTypeNode => "decimal",
            BoolTypePrimitiveTypeNode => "bool",
            CharTypePrimitiveTypeNode => "char",
            StringTypePrimitiveTypeNode => "string",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Finds the compilation unit path for a type by its display name.
    /// </summary>
    private static string? FindCompilationUnitPathForType(
        string typeName,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait || symbol.Kind == SymbolKind.Enum)
                && symbol.Name == typeName)
            {
                return FindCompilationUnitPathForSymbol(symbol, symbolTable, asts, compilation);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for a type by its display name.
    /// </summary>
    private static SourceText? FindSourceTextForType(
        string typeName,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait || symbol.Kind == SymbolKind.Enum)
                && symbol.Name == typeName)
            {
                return FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the declaring <see cref="Storm.Runtime.SyntaxNode"/> for a type by its display name.
    /// </summary>
    private static Storm.Runtime.SyntaxNode? FindDeclaringNodeForType(
        string typeName,
        SymbolTable symbolTable)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait || symbol.Kind == SymbolKind.Enum)
                && symbol.Name == typeName)
            {
                return symbolTable.GetDeclaringNode(symbol);
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1e — Check for member redeclarations in derived classes
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After base types are resolved and cycles are broken, walk each class's
    /// ancestor chain to enforce the additive-only constraint: a derived class
    /// cannot redeclare a field or method that already exists in any ancestor.
    /// For methods, same name with a different parameter signature is a valid
    /// overload (not a redeclaration). Only same name AND identical parameter
    /// signature is flagged as a redeclaration error. Constructors are skipped
    /// because they do not inherit across the class hierarchy.
    /// </summary>
    private static void Pass1_CheckMemberRedeclarations(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Class || symbol.DeclaredType is not ClassType classType)
            {
                continue;
            }

            if (classType.BaseType is null)
            {
                continue;
            }

            // Collect the names of members declared directly on this class.
            foreach (var member in classType.MemberScope.Members)
            {
                // Only check fields and methods (not constructors, parameters, etc.)
                // Constructors don't inherit, so they are never redeclarations.
                if (member.Kind != SymbolKind.Field && member.Kind != SymbolKind.Method)
                {
                    continue;
                }

                // For methods, extract the parameter signature so we can distinguish
                // overloads (same name, different signature) from true redeclarations
                // (same name AND same signature).
                IReadOnlyList<IType>? memberSignature = null;
                if (member.Kind == SymbolKind.Method)
                {
                    memberSignature = ParameterSignature.Extract(member, symbolTable);
                }

                // Walk the ancestor chain looking for a member with the same name.
                var ancestor = classType.BaseType as ClassType;
                while (ancestor is not null)
                {
                    foreach (var ancestorMember in ancestor.MemberScope.Members)
                    {
                        if (ancestorMember.Name != member.Name)
                        {
                            continue;
                        }

                        // Fields use name-only matching (unchanged behavior).
                        if (member.Kind == SymbolKind.Field && ancestorMember.Kind == SymbolKind.Field)
                        {
                            EmitRedeclarationDiagnostic(classType, member, ancestor, symbolTable, asts, compilation, diagnostics);
                            goto nextMember;
                        }

                        // Methods: only a redeclaration if the parameter signatures match.
                        // Same name with a different signature is a valid overload.
                        if (member.Kind == SymbolKind.Method && ancestorMember.Kind == SymbolKind.Method)
                        {
                            var ancestorSignature = ParameterSignature.Extract(ancestorMember, symbolTable);
                            if (ParameterSignature.AreEqual(memberSignature!, ancestorSignature))
                            {
                                EmitRedeclarationDiagnostic(classType, member, ancestor, symbolTable, asts, compilation, diagnostics);
                                goto nextMember;
                            }
                            // Different signature — valid overload, keep checking other ancestors.
                            continue;
                        }

                        // Cross-kind conflict (e.g. field in derived vs method in base with same name).
                        if ((ancestorMember.Kind == SymbolKind.Field || ancestorMember.Kind == SymbolKind.Method))
                        {
                            EmitRedeclarationDiagnostic(classType, member, ancestor, symbolTable, asts, compilation, diagnostics);
                            goto nextMember;
                        }
                    }

                    ancestor = ancestor.BaseType as ClassType;
                }

                nextMember:;
            }
        }
    }

    /// <summary>
    /// Emits a <c>MIST_MEMBER_REDECLARATION</c> diagnostic for a member that
    /// conflicts with an ancestor member.
    /// </summary>
    private static void EmitRedeclarationDiagnostic(
        ClassType classType,
        ISymbol member,
        ClassType ancestor,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var memberNode = symbolTable.GetDeclaringNode(member);
        var redeclSourceText = FindSourceTextForClassType(
            classType, symbolTable, asts, compilation);

        if (memberNode is not null)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                "MIST_MEMBER_REDECLARATION",
                DiagnosticSeverity.Error,
                $"'{classType.DisplayName}' cannot redeclare member '{member.Name}' already defined in '{ancestor.DisplayName}'.",
                memberNode,
                redeclSourceText));
        }
        else
        {
            string? compilationUnitPath = FindCompilationUnitPathForClass(
                classType, symbolTable, asts, compilation);

            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_MEMBER_REDECLARATION"),
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"'{classType.DisplayName}' cannot redeclare member '{member.Name}' already defined in '{ancestor.DisplayName}'.",
                Location: default,
                CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                AnalyzerName: null));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1f — Check base constructor requirements are satisfied
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After base types are resolved and cycles are broken, check that each
    /// derived class satisfies its base class's constructor requirements.
    /// If the base class has explicit constructors that all require parameters,
    /// the derived class must have at least one constructor with a
    /// <c>: base(args)</c> chain. If the derived class has no explicit
    /// constructors at all, or has constructors without <c>: base(...)</c>,
    /// and the base class has no parameterless constructor, emit
    /// <c>MIST_BASE_CTOR_UNSATISFIED</c>.
    /// </summary>
    private static void Pass1_CheckBaseConstructorSatisfied(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Class || symbol.DeclaredType is not ClassType classType)
            {
                continue;
            }

            if (classType.BaseType is null)
            {
                continue;
            }

            var baseType = classType.BaseType;

            // Get the member scope from the base type.
            IScope? baseScope = baseType switch
            {
                ClassType ct => ct.MemberScope,
                ConstructedType cst => cst.MemberScope,
                _ => null,
            };
            if (baseScope is null)
                continue;

            // Find explicit constructors on the base class.
            var baseConstructors = baseScope.Members
                .Where(m => m.Kind == SymbolKind.Constructor)
                .ToList();

            // If the base class has no explicit constructors, it has an implicit
            // parameterless constructor — no issue.
            if (baseConstructors.Count == 0)
            {
                continue;
            }

            // Check if the base class has at least one parameterless constructor.
            var baseHasParameterlessCtor = baseConstructors.Any(ctor =>
            {
                var ctorNode = symbolTable.GetDeclaringNode(ctor);
                return ctorNode is ConstructorDeclarationNode ctorDecl && ctorDecl.Parameters is null;
            });

            // If the base class has a parameterless constructor, derived classes
            // can implicitly call it — no issue.
            if (baseHasParameterlessCtor)
            {
                continue;
            }

            // The base class requires constructor arguments. Check if the derived
            // class satisfies this requirement.
            var derivedConstructors = classType.MemberScope.Members
                .Where(m => m.Kind == SymbolKind.Constructor)
                .ToList();

            if (derivedConstructors.Count == 0)
            {
                // Derived class has no explicit constructors and the base class
                // requires arguments — emit diagnostic.
                var classSourceText = FindSourceTextForClassType(
                    classType, symbolTable, asts, compilation);
                Storm.Runtime.SyntaxNode? classNode = null;
                foreach (var sym in symbolTable.AllSymbols)
                {
                    if (sym.DeclaredType == classType)
                    {
                        classNode = symbolTable.GetDeclaringNode(sym);
                        break;
                    }
                }

                if (classNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_BASE_CTOR_UNSATISFIED",
                        DiagnosticSeverity.Error,
                        $"Base class '{baseType.DisplayName}' requires constructor arguments that are not provided.",
                        classNode,
                        classSourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForClass(
                        classType, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_BASE_CTOR_UNSATISFIED"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"Base class '{baseType.DisplayName}' requires constructor arguments that are not provided.",
                        Location: default,
                        CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                        AnalyzerName: null));
                }
                continue;
            }

            // Check each derived constructor: at least one must have a `: base(...)` chain,
            // and any constructor without `: base(...)` is an error since the base class
            // has no parameterless constructor.
            foreach (var derivedCtor in derivedConstructors)
            {
                var ctorNode = symbolTable.GetDeclaringNode(derivedCtor);
                if (ctorNode is not ConstructorDeclarationNode ctorDecl)
                {
                    continue;
                }

                var hasBaseChain = ctorDecl.Chain is BaseChainConstructorChainNode;

                if (!hasBaseChain)
                {
                    // This constructor does not chain to the base class, but the
                    // base class requires arguments — emit diagnostic.
                    var ctorSourceText = FindSourceTextForClassType(
                        classType, symbolTable, asts, compilation);

                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_BASE_CTOR_UNSATISFIED",
                        DiagnosticSeverity.Error,
                        $"Base class '{baseType.DisplayName}' requires constructor arguments that are not provided.",
                        ctorDecl,
                        ctorSourceText));
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Post-Pass 2 — Check duplicate method/constructor signatures
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After Pass 2 (type resolution), check for duplicate method and constructor
    /// signatures within each type scope. Two methods with the same name and
    /// identical parameter types are a duplicate; two constructors with identical
    /// parameter types are a duplicate.
    /// </summary>
    private static void Pass1_CheckDuplicateSignatures(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            IScope memberScope;
            string typeName;

            switch (symbol.Kind)
            {
                case SymbolKind.Class when symbol.DeclaredType is ClassType classType:
                    memberScope = classType.MemberScope;
                    typeName = classType.DisplayName;
                    break;
                case SymbolKind.Struct when symbol.DeclaredType is StructType structType:
                    memberScope = structType.MemberScope;
                    typeName = structType.DisplayName;
                    break;
                case SymbolKind.Interface when symbol.DeclaredType is InterfaceType interfaceType:
                    memberScope = interfaceType.MemberScope;
                    typeName = interfaceType.DisplayName;
                    break;
                default:
                    continue;
            }

            // Check duplicate method signatures: group methods by name.
            var methods = memberScope.Members
                .Where(m => m.Kind == SymbolKind.Method)
                .ToList();

            var methodGroups = methods.GroupBy(m => m.Name);
            foreach (var group in methodGroups)
            {
                var groupList = group.ToList();
                if (groupList.Count <= 1)
                    continue;

                CheckDuplicatePairs(
                    groupList, typeName, isConstructor: false,
                    symbolTable, asts, compilation, diagnostics);
            }

            // Check duplicate constructor signatures.
            var constructors = memberScope.Members
                .Where(m => m.Kind == SymbolKind.Constructor)
                .ToList();

            if (constructors.Count > 1)
            {
                CheckDuplicatePairs(
                    constructors, typeName, isConstructor: true,
                    symbolTable, asts, compilation, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks all pairs of symbols in the list for identical parameter signatures
    /// and emits the appropriate duplicate diagnostic for each match.
    /// </summary>
    private static void CheckDuplicatePairs(
        List<ISymbol> symbols,
        string typeName,
        bool isConstructor,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Pre-extract signatures for all symbols to avoid redundant extraction.
        var signatures = new IReadOnlyList<IType>[symbols.Count];
        for (var i = 0; i < symbols.Count; i++)
        {
            signatures[i] = ParameterSignature.Extract(symbols[i], symbolTable);
        }

        // Track which symbols have already been reported as duplicates
        // to avoid emitting multiple diagnostics for the same pair.
        var reported = new HashSet<int>();

        for (var i = 0; i < symbols.Count; i++)
        {
            for (var j = i + 1; j < symbols.Count; j++)
            {
                if (reported.Contains(j))
                    continue;

                if (!ParameterSignature.AreEqual(signatures[i], signatures[j]))
                    continue;

                // Found a duplicate — emit diagnostic on the second (later) declaration.
                reported.Add(j);

                var diagnosticId = isConstructor
                    ? DuplicateConstructorSignature
                    : DuplicateMethodSignature;

                var paramTypeNames = string.Join(", ", signatures[j].Select(t => t.DisplayName));

                var message = isConstructor
                    ? $"Type '{typeName}' already defines a constructor with parameter types ({paramTypeNames})"
                    : $"Type '{typeName}' already defines method '{symbols[j].Name}' with parameter types ({paramTypeNames})";

                var duplicateNode = symbolTable.GetDeclaringNode(symbols[j]);
                var sourceText = FindSourceTextForType(
                    typeName, symbolTable, asts, compilation);

                if (duplicateNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        diagnosticId.Value,
                        DiagnosticSeverity.Error,
                        message,
                        duplicateNode,
                        sourceText));
                }
                else
                {
                    var compilationUnitPath = FindCompilationUnitPathForType(
                        typeName, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        diagnosticId,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        message,
                        Location: default,
                        CompilationUnitPath: compilationUnitPath ?? "<unknown>",
                        AnalyzerName: null));
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 2 — Resolve types and build expression types
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pass 2: Walk the AST again to resolve type references, register parameters
    /// and local variables, and compute expression types.
    /// </summary>
    private static void Pass2_ResolveTypes(
        Storm.Runtime.SyntaxNode ast,
        Scope globalScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (ast is not CompilationUnitNode compilationUnit)
        {
            return;
        }

        // Compute the inferred namespace for this file (same logic as Pass 1).
        var filePath = sourceText?.Path ?? "";
        // We need namespaceRoot and projectFolder — extract from the global scope context.
        // Since Pass 2 doesn't receive these directly, we infer the primary namespace
        // by looking at what Pass 1 registered for top-level types in this file,
        // or by finding the first namespace declaration.
        var inferredNamespaceScope = FindInferredNamespaceScopeForFile(
            compilationUnit, globalScope, symbolTable, sourceText);

        // Determine the primary namespace scope for this file.
        // The scope chain is: local → file → namespace → global.
        // For files with no explicit namespace, the inferred namespace scope is used.
        // For files with explicit namespaces, each namespace section gets its own scope.
        Scope primaryNamespaceScope = inferredNamespaceScope ?? globalScope;

        // Create a file-level scope to hold using imports. This scope sits between
        // the namespace scope and the type scopes.
        var fileScope = new Scope(parent: primaryNamespaceScope);

        // Add the file's own namespace as implicitly imported so that extensions
        // declared in the same namespace are visible without an explicit `using`.
        var ownNamespaceName = DetermineFileNamespaceName(compilationUnit, sourceText);
        if (ownNamespaceName is not null)
        {
            fileScope.AddImportedNamespace(ownNamespaceName);
        }

        // Process using declarations — verify namespace existence and record imports.
        foreach (var usingDecl in compilationUnit.Usings)
        {
            var usingName = ExtractQualifiedName(usingDecl.Name, sourceText);
            var nsExists = false;

            // Check user-defined namespaces.
            foreach (var member in globalScope.Members)
            {
                if (member.Kind == SymbolKind.Namespace && member.Name == usingName)
                {
                    nsExists = true;
                    break;
                }
            }

            // Check ambient namespaces.
            if (!nsExists && ambientTypeResolver is not null)
            {
                nsExists = ambientTypeResolver.HasNamespace(usingName);
            }

            if (nsExists)
            {
                fileScope.AddImportedNamespace(usingName);
            }
            else
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_UNDEFINED_NAMESPACE",
                    DiagnosticSeverity.Error,
                    $"The namespace '{usingName}' could not be found.",
                    usingDecl,
                    sourceText));
            }
        }

        // Resolve types in items (namespace body types and top-level types).
        // For each namespace section, we use a file scope whose parent is the
        // appropriate namespace scope so that same-namespace types are visible.
        foreach (var item in compilationUnit.Items)
        {
            switch (item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                {
                    var nsDecl = nsItem.NamespaceDeclaration;
                    var declaredName = ExtractNamespaceName(nsDecl, sourceText);

                    // Find the namespace scope for this declaration.
                    var nsScope = FindExistingNamespaceScope(globalScope, symbolTable, declaredName);

                    // Create a file scope for this namespace section if it differs
                    // from the primary namespace scope.
                    Scope sectionFileScope;
                    if (nsScope is not null && nsScope != primaryNamespaceScope)
                    {
                        sectionFileScope = new Scope(parent: nsScope);
                        // Copy using imports to this section scope.
                        foreach (var ns in fileScope.ImportedNamespaces)
                        {
                            sectionFileScope.AddImportedNamespace(ns);
                        }
                    }
                    else
                    {
                        sectionFileScope = fileScope;
                    }

                    var namespaceTypes = GetNamespaceBodyTypes(nsDecl);
                    foreach (var typeDecl in namespaceTypes)
                    {
                        ResolveTypeDeclaration(typeDecl, sectionFileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    }

                    var namespaceTypeAliases = GetNamespaceBodyTypeAliases(nsDecl);
                    foreach (var aliasDecl in namespaceTypeAliases)
                    {
                        ResolveTypeAliasDeclaration(aliasDecl, sectionFileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                    }

                    var namespaceNewtypes = GetNamespaceBodyNewtypes(nsDecl);
                    foreach (var newtypeDecl in namespaceNewtypes)
                    {
                        ResolveNewtypeDeclaration(newtypeDecl, sectionFileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                    }
                    break;
                }
                case TypeItemTopLevelItemNode typeItem:
                    ResolveTypeDeclaration(typeItem.TypeDeclaration, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    break;
                case TypeAliasItemTopLevelItemNode typeAliasItem:
                    ResolveTypeAliasDeclaration(typeAliasItem.TypeAliasDeclaration, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                    break;
                case NewtypeItemTopLevelItemNode newtypeItem:
                    ResolveNewtypeDeclaration(newtypeItem.NewtypeDeclaration, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Finds the inferred namespace scope for a file by looking at the first top-level
    /// type registered in Pass 1 (types outside any explicit namespace declaration).
    /// If the file has no top-level types, falls back to the first namespace declaration's scope.
    /// </summary>
    private static Scope? FindInferredNamespaceScopeForFile(
        CompilationUnitNode compilationUnit,
        Scope globalScope,
        SymbolTable symbolTable,
        SourceText? sourceText)
    {
        // First, try to find a top-level type (outside any namespace) and use its containing scope.
        foreach (var item in compilationUnit.Items)
        {
            if (item is TypeItemTopLevelItemNode typeItem)
            {
                Storm.Runtime.SyntaxNode declNode = typeItem.TypeDeclaration switch
                {
                    ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration,
                    StructTypeTypeDeclarationNode st => st.StructDeclaration,
                    InterfaceTypeTypeDeclarationNode it => it.InterfaceDeclaration,
                    _ => typeItem.TypeDeclaration
                };

                var symbol = symbolTable.GetDeclaredSymbol(declNode);
                if (symbol?.ContainingScope is Scope namespaceScope && namespaceScope.Parent == globalScope)
                {
                    return namespaceScope;
                }
            }
            else if (item is TypeAliasItemTopLevelItemNode typeAliasItem)
            {
                var symbol = symbolTable.GetDeclaredSymbol(typeAliasItem.TypeAliasDeclaration);
                if (symbol?.ContainingScope is Scope namespaceScope && namespaceScope.Parent == globalScope)
                {
                    return namespaceScope;
                }
            }
            else if (item is NewtypeItemTopLevelItemNode newtypeItem)
            {
                var symbol = symbolTable.GetDeclaredSymbol(newtypeItem.NewtypeDeclaration);
                if (symbol?.ContainingScope is Scope namespaceScope && namespaceScope.Parent == globalScope)
                {
                    return namespaceScope;
                }
            }
        }

        // No top-level types found. Try the first namespace declaration.
        foreach (var item in compilationUnit.Items)
        {
            if (item is NamespaceItemTopLevelItemNode nsItem)
            {
                var nsName = ExtractNamespaceName(nsItem.NamespaceDeclaration, sourceText);
                var nsScope = FindExistingNamespaceScope(globalScope, symbolTable, nsName);
                if (nsScope is not null)
                    return nsScope;

                // Fall back to FindNamespaceScope which looks at body types.
                return FindNamespaceScope(globalScope, symbolTable, nsItem.NamespaceDeclaration);
            }
        }

        return null;
    }

    /// <summary>
    /// Determines the namespace name for a file by examining its namespace declarations.
    /// Returns null if the file has no namespace declaration.
    /// Used to implicitly import the file's own namespace for extension visibility.
    /// </summary>
    private static string? DetermineFileNamespaceName(
        CompilationUnitNode compilationUnit,
        SourceText? sourceText)
    {
        foreach (var item in compilationUnit.Items)
        {
            if (item is NamespaceItemTopLevelItemNode nsItem)
            {
                return ExtractNamespaceName(nsItem.NamespaceDeclaration, sourceText);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the namespace scope created in Pass 1 by looking at the type declarations
    /// registered under the namespace. Returns the parent scope of the first type found.
    /// </summary>
    private static Scope FindNamespaceScope(
        Scope globalScope,
        SymbolTable symbolTable,
        NamespaceDeclarationNode namespaceNode)
    {
        var bodyTypes = GetNamespaceBodyTypes(namespaceNode);
        if (bodyTypes.Length > 0)
        {
            // Get the first type declaration and find its symbol's containing scope.
            var firstTypeDecl = bodyTypes[0];
            Storm.Runtime.SyntaxNode declNode = firstTypeDecl switch
            {
                ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration,
                StructTypeTypeDeclarationNode st => st.StructDeclaration,
                InterfaceTypeTypeDeclarationNode it => it.InterfaceDeclaration,
                _ => firstTypeDecl
            };

            var symbol = symbolTable.GetDeclaredSymbol(declNode);
            if (symbol?.ContainingScope is Scope namespaceScope)
            {
                return namespaceScope;
            }
        }

        // Fallback: create a new scope (shouldn't happen in practice).
        return new Scope(parent: globalScope);
    }

    /// <summary>
    /// Finds an existing namespace scope by looking at type symbols already registered
    /// under the given namespace name. Used in Pass 1 to reuse the same scope when
    /// multiple files contribute to the same namespace.
    /// </summary>
    private static Scope? FindExistingNamespaceScope(
        Scope globalScope,
        SymbolTable symbolTable,
        string namespaceName)
    {
        // Look through all symbols in the global scope for types whose
        // ContainingScope is a child of globalScope (i.e. a namespace scope).
        // We identify the right one by checking if any type in that scope
        // belongs to the target namespace.
        foreach (var member in globalScope.Members)
        {
            if (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct || member.Kind == SymbolKind.Interface || member.Kind == SymbolKind.Trait || member.Kind == SymbolKind.Enum)
            {
                // Top-level types (no namespace) — skip.
                continue;
            }
        }

        // Walk all registered symbols to find a type in this namespace.
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait || symbol.Kind == SymbolKind.Enum)
                && symbol.ContainingScope is Scope parentScope
                && parentScope.Parent == globalScope)
            {
                // This type is in a namespace scope. Check if it matches.
                if (symbol.DeclaredType is ClassType ct && ct.ContainingNamespace == namespaceName)
                {
                    return parentScope;
                }
                if (symbol.DeclaredType is StructType st && st.ContainingNamespace == namespaceName)
                {
                    return parentScope;
                }
                if (symbol.DeclaredType is InterfaceType it && it.ContainingNamespace == namespaceName)
                {
                    return parentScope;
                }
                if (symbol.DeclaredType is TraitType tt && tt.ContainingNamespace == namespaceName)
                {
                    return parentScope;
                }
                if (symbol.DeclaredType is EnumType et && et.ContainingNamespace == namespaceName)
                {
                    return parentScope;
                }
            }
        }

        return null;
    }

    private static void ResolveTypeDeclaration(
        TypeDeclarationNode typeDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (typeDecl)
        {
            case ClassTypeTypeDeclarationNode classTypeNode:
                ResolveClassDeclaration(classTypeNode.ClassDeclaration, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case StructTypeTypeDeclarationNode structTypeNode:
                ResolveStructDeclaration(structTypeNode.StructDeclaration, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case InterfaceTypeTypeDeclarationNode interfaceTypeNode:
                ResolveInterfaceDeclaration(interfaceTypeNode.InterfaceDeclaration, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                break;
            case TraitTypeTypeDeclarationNode traitTypeNode:
                ResolveTraitDeclaration(traitTypeNode.TraitDeclaration, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                break;
            case EnumTypeTypeDeclarationNode enumTypeNode:
                ResolveEnumDeclaration(enumTypeNode.EnumDeclaration, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
        }
    }

    // ── Enum type resolution (Pass 2) ──────────────────────────────────

    /// <summary>
    /// Resolves the underlying type and member values for an enum declaration.
    /// <list type="number">
    ///   <item>If an explicit type annotation is present, resolves the <see cref="TypeReferenceNode"/>.</item>
    ///   <item>If members have value expressions, infers the underlying type from member value types
    ///         (homogeneous → that type, heterogeneous → union type).</item>
    ///   <item>If name-only (no values), sets the underlying type to <c>byte</c>.</item>
    /// </list>
    /// Assigns sequential values (0, 1, 2, …) for name-only members, continuing after explicit values.
    /// </summary>
    private static void ResolveEnumDeclaration(
        EnumDeclarationNode enumDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var enumSymbol = symbolTable.GetDeclaredSymbol(enumDecl);
        if (enumSymbol?.DeclaredType is not EnumType enumType) return;

        // Collect all member AST nodes.
        var memberNodes = new List<EnumMemberNode>();
        if (enumDecl.Members is not null)
        {
            memberNodes.Add(enumDecl.Members.EnumMember);
            foreach (var member in enumDecl.Members.EnumMembers)
            {
                memberNodes.Add(member);
            }
        }

        // ── Step 1: Resolve the underlying type ──────────────────────────

        IType underlyingType;

        if (enumDecl.TypeAnnotation is not null)
        {
            // Explicit type annotation — resolve the TypeReference node.
            underlyingType = ResolveTypeReference(
                enumDecl.TypeAnnotation, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

            if (underlyingType is ErrorType)
            {
                // Cannot resolve the annotated type — assign ErrorType and skip member validation.
                enumType.UnderlyingType = ErrorType.Instance;
                enumType.Members = memberNodes.Select(m => new EnumMemberInfo(m.Name.Lexeme)).ToList();
                return;
            }
        }
        else if (memberNodes.Any(m => m.Value is not null))
        {
            // Members have value expressions — infer from member value types.
            // First, resolve all member value expressions to determine their types.
            var valueTypes = new List<IType>();
            foreach (var memberNode in memberNodes)
            {
                if (memberNode.Value is not null)
                {
                    var valueType = ResolveExpression(
                        memberNode.Value, (Scope)enumType.MemberScope, fileScope, enumType,
                        isStatic: true, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    valueTypes.Add(valueType);
                }
            }

            // Determine underlying type: homogeneous → that type, heterogeneous → union type.
            var distinctTypes = valueTypes
                .Where(t => t is not ErrorType)
                .Select(t => t.DisplayName)
                .Distinct()
                .ToList();

            if (distinctTypes.Count == 0)
            {
                // All values resolved to ErrorType — fall back to byte.
                underlyingType = ResolvePrimitiveByKeyword("byte", fileScope, symbolTable);
            }
            else if (distinctTypes.Count == 1)
            {
                // Homogeneous — use that type.
                underlyingType = valueTypes.First(t => t is not ErrorType);
            }
            else
            {
                // Heterogeneous — create a union type from the distinct value types.
                var uniqueTypes = new List<IType>();
                var seenNames = new HashSet<string>();
                foreach (var vt in valueTypes)
                {
                    if (vt is not ErrorType && seenNames.Add(vt.DisplayName))
                    {
                        uniqueTypes.Add(vt);
                    }
                }
                underlyingType = new UnionType(uniqueTypes);
            }
        }
        else
        {
            // Name-only enum — default underlying type is byte.
            underlyingType = ResolvePrimitiveByKeyword("byte", fileScope, symbolTable);
        }

        enumType.UnderlyingType = underlyingType;

        // ── Step 2: Resolve member values ────────────────────────────────

        var memberInfos = new List<EnumMemberInfo>();
        long nextSequentialValue = 0;

        foreach (var memberNode in memberNodes)
        {
            var memberInfo = new EnumMemberInfo(memberNode.Name.Lexeme);

            if (memberNode.Value is not null)
            {
                // Member has an explicit value expression.
                // If we already resolved this expression above (during type inference),
                // retrieve its type from the expression types map. Otherwise resolve it now.
                IType valueType;
                if (expressionTypes.ContainsKey(memberNode.Value))
                {
                    valueType = expressionTypes[memberNode.Value];
                }
                else
                {
                    valueType = ResolveExpression(
                        memberNode.Value, (Scope)enumType.MemberScope, fileScope, enumType,
                        isStatic: true, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }

                memberInfo.ValueType = valueType;

                // Try to extract a compile-time integer value for sequencing.
                var literalValue = TryExtractIntegerLiteral(memberNode.Value);
                if (literalValue.HasValue)
                {
                    memberInfo.ResolvedValue = literalValue.Value;
                    nextSequentialValue = literalValue.Value + 1;
                }
                else
                {
                    // Non-integer or complex expression — store null as resolved value.
                    // The next implicit member still gets nextSequentialValue.
                    memberInfo.ResolvedValue = null;
                    // Don't advance nextSequentialValue for non-integer explicit values.
                }
            }
            else
            {
                // Name-only member — assign sequential value.
                memberInfo.ResolvedValue = nextSequentialValue;
                memberInfo.ValueType = underlyingType;
                nextSequentialValue++;
            }

            memberInfos.Add(memberInfo);
        }

        enumType.Members = memberInfos;
    }

    /// <summary>
    /// Attempts to extract a compile-time integer value from a simple literal expression.
    /// Returns <c>null</c> for complex expressions or non-integer literals.
    /// </summary>
    private static long? TryExtractIntegerLiteral(ExpressionNode expr)
    {
        // Navigate: Expression → Assignment → LogicalOr → LogicalAnd → Equality
        //         → Relational → Additive → Multiplicative → Unary → Postfix → Primary
        var assignment = expr.Assignment;
        if (assignment?.Value is not null) return null; // It's an assignment, not a simple literal.

        var logicalOr = assignment?.Target;
        if (logicalOr?.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left)) return null;

        var logicalAnd = logicalOr?.Left;
        if (logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left)) return null;

        var equality = logicalAnd?.Left;
        if (equality?.Right is not null && !ReferenceEquals(equality.Right, equality.Left)) return null;

        // Relational is a discriminated union.
        if (equality?.Left is not RelationalComparisonRelationalNode relCmp) return null;
        if (relCmp.Right is not null && !ReferenceEquals(relCmp.Right, relCmp.Left)) return null;

        var additive = relCmp.Left;
        if (additive?.Right is not null && !ReferenceEquals(additive.Right, additive.Left)) return null;

        var multiplicative = additive?.Left;
        if (multiplicative?.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left)) return null;

        // Unary is a discriminated union.
        if (multiplicative?.Left is not PostfixExpressionUnaryNode postfixUnary) return null;

        var postfix = postfixUnary.Primary;
        if (postfix.Suffix.Length > 0) return null; // Has postfix suffixes (method calls, member access, etc.)

        if (postfix.Target is not IntegerLiteralExpressionPrimaryNode intLiteral) return null;

        var lexeme = intLiteral.IntegerLiteral.Lexeme;
        // Strip suffixes for parsing.
        var upper = lexeme.ToUpperInvariant();
        var numericPart = lexeme;
        if (upper.EndsWith("UL") || upper.EndsWith("LU"))
            numericPart = lexeme[..^2];
        else if (upper.EndsWith("L") || upper.EndsWith("U"))
            numericPart = lexeme[..^1];

        if (long.TryParse(numericPart, out var value))
            return value;

        return null;
    }

    // ── Type alias visited-set for cycle detection (Pass 2) ──────────────
    [ThreadStatic]
    private static HashSet<string>? _typeAliasResolutionStack;

    /// <summary>
    /// Resolves a type alias declaration's target type reference and constructs
    /// a <see cref="TypeAliasType"/>. Detects cycles using a visited-set and
    /// reports <see cref="CyclicTypeAlias"/> on cycle, assigning <see cref="ErrorType"/>.
    /// Reports <see cref="UnresolvedTypeAliasTarget"/> if the target type cannot be resolved.
    /// </summary>
    private static void ResolveTypeAliasDeclaration(
        TypeAliasDeclarationNode aliasDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var aliasSymbol = symbolTable.GetDeclaredSymbol(aliasDecl) as Symbol;
        if (aliasSymbol is null) return;

        // If already resolved (e.g., resolved as a dependency of another alias), skip.
        if (aliasSymbol.DeclaredType is not null) return;

        var aliasName = aliasDecl.Name.Lexeme;

        // Initialize the resolution stack if needed.
        _typeAliasResolutionStack ??= new HashSet<string>();

        // Check for cycles.
        if (!_typeAliasResolutionStack.Add(aliasName))
        {
            // Cycle detected — report diagnostic and assign ErrorType.
            diagnostics.Add(MakeDiagnosticWithLocation(
                CyclicTypeAlias.Value,
                DiagnosticSeverity.Error,
                $"Type alias '{aliasName}' forms a cycle.",
                aliasDecl,
                sourceText));
            aliasSymbol.DeclaredType = ErrorType.Instance;
            return;
        }

        try
        {
            // Create a resolution scope that includes the alias's type parameters
            // so that type parameter names (e.g., T in type Box<T> = GenericBox<T>)
            // can be resolved when resolving the target type reference.
            var resolutionScope = fileScope;
            if (aliasSymbol.TypeParameters.Count > 0)
            {
                resolutionScope = new Scope(parent: fileScope);
                foreach (var tp in aliasSymbol.TypeParameters)
                {
                    var (tpName9625, _) = GetTypeParameterNameAndOrdinal(tp);
                    var tpSymbol = new Symbol(tpName9625, SymbolKind.Class, SymbolOrigin.UserCode, resolutionScope, tp, null);
                    resolutionScope.AddMember(tpSymbol);
                }
            }

            // Resolve the target type reference.
            // The type alias now uses RefinableTypeExpression which wraps SingleTypeRef(s).
            // When a refinement predicate is present, this constructs a RefinementType.
            var targetType = ResolveRefinableTypeExpression(
                aliasDecl.Type, resolutionScope, ambientTypeResolver, symbolTable, sourceText, diagnostics, aliasName);

            // Check if the resolved type is an error (unresolved).
            if (targetType is ErrorType)
            {
                // Only emit the unresolved-target diagnostic if the error wasn't caused
                // by an incompatible refinement predicate (which already emitted MIST_TC_0072).
                if (!HasRefinementPredicate(aliasDecl.Type))
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        UnresolvedTypeAliasTarget.Value,
                        DiagnosticSeverity.Error,
                        $"Type '{ExtractRefinableTypeExpressionName(aliasDecl.Type, sourceText)}' could not be resolved in type alias '{aliasName}'.",
                        aliasDecl.Type,
                        sourceText));
                }
                aliasSymbol.DeclaredType = ErrorType.Instance;
                return;
            }

            // If the target resolves to another TypeAliasType, ensure it's fully resolved
            // (handles transitive aliases like type A = B; type B = int;).
            if (targetType is TypeAliasType aliasTarget)
            {
                targetType = aliasTarget.UnderlyingType;
            }

            // If the resolved type is a RefinementType, store it directly as the alias's
            // declared type. The RefinementType already carries the alias name.
            if (targetType is RefinementType refinementType)
            {
                aliasSymbol.DeclaredType = refinementType;
            }
            else if (targetType is UnionType unionType && HasRefinementConstituent(unionType))
            {
                // Union with at least one refinement constituent — store directly.
                aliasSymbol.DeclaredType = unionType;
            }
            else
            {
                // Construct the TypeAliasType for non-refined aliases.
                var typeParams = aliasSymbol.TypeParameters;
                var typeAliasType = new TypeAliasType(aliasName, targetType, typeParams);
                aliasSymbol.DeclaredType = typeAliasType;
            }
        }
        finally
        {
            _typeAliasResolutionStack.Remove(aliasName);
        }
    }

    /// <summary>
    /// Resolves a newtype declaration by resolving the source type reference,
    /// creating a <see cref="NewtypeType"/>, and cloning all members from the
    /// source type's scope into the newtype's member scope.
    /// Reports <see cref="UnresolvedNewtypeSource"/> if the source type cannot be resolved.
    /// </summary>
    private static void ResolveNewtypeDeclaration(
        NewtypeDeclarationNode newtypeDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var newtypeSymbol = symbolTable.GetDeclaredSymbol(newtypeDecl) as Symbol;
        if (newtypeSymbol is null) return;

        // If already resolved, skip.
        if (newtypeSymbol.DeclaredType is not null) return;

        var name = newtypeDecl.Name.Lexeme;

        // Resolve the source type reference.
        var sourceType = ResolveTypeReference(
            newtypeDecl.SourceType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        if (sourceType is ErrorType)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                UnresolvedNewtypeSource.Value,
                DiagnosticSeverity.Error,
                $"Type '{ExtractTypeReferenceName(newtypeDecl.SourceType, sourceText)}' could not be resolved in newtype '{name}'.",
                newtypeDecl.SourceType,
                sourceText));
            newtypeSymbol.DeclaredType = ErrorType.Instance;
            return;
        }

        // If the source resolves to a type alias, unwrap to the underlying type.
        if (sourceType is TypeAliasType aliasType)
        {
            sourceType = aliasType.UnderlyingType;
        }

        // Create the NewtypeType and clone all members from the source type's scope.
        var newtypeType = new NewtypeType(name, sourceType);
        CloneAllMembers(sourceType, newtypeType, name, symbolTable);

        // For primitive (backend-backed) source types, add a synthetic 'Value' field
        // so that member access like `myNewtype.Value` resolves in the semantic model.
        // The transpiler emits these as readonly struct wrappers with a Value property.
        var isBackendBacked = sourceType switch
        {
            StructType st => st.IsBackendBacked,
            ClassType ct => ct.IsBackendBacked,
            _ => false,
        };
        if (isBackendBacked)
        {
            var targetScope = (Scope)newtypeType.MemberScope;
            var valueSymbol = new Symbol(
                "Value",
                SymbolKind.Field,
                SymbolOrigin.UserCode,
                targetScope,
                sourceType,
                declarationSpan: null,
                isIntrinsic: false,
                hasBody: false);
            targetScope.AddMember(valueSymbol);
        }

        newtypeSymbol.DeclaredType = newtypeType;
    }

    /// <summary>
    /// Clones all member symbols from the source type's scope into the newtype's member scope.
    /// Handles fields, properties, methods, constructors (with name updated), operators,
    /// cast operators, and indexers.
    /// </summary>
    private static void CloneAllMembers(
        IType sourceType,
        NewtypeType newtypeType,
        string newtypeName,
        SymbolTable symbolTable)
    {
        var sourceScope = GetMemberScope(sourceType);
        if (sourceScope is null) return;

        var targetScope = (Scope)newtypeType.MemberScope;

        foreach (var member in sourceScope.Members)
        {
            var clonedName = member.Kind == SymbolKind.Constructor
                ? newtypeName
                : member.Name;

            var cloned = new Symbol(
                clonedName,
                member.Kind,
                SymbolOrigin.UserCode,
                targetScope,
                member.DeclaredType,
                member.DeclarationSpan,
                isIntrinsic: member is Symbol s && s.IsIntrinsic,
                hasBody: member is Symbol s2 && s2.HasBody);

            // Copy property metadata if present.
            if (member is Symbol sourceSym && sourceSym.PropertyMetadata is not null)
            {
                cloned.PropertyMetadata = sourceSym.PropertyMetadata;
            }

            // Copy type parameters if present (for generic methods).
            if (member is Symbol sourceMethodSym && sourceMethodSym.TypeParameters.Count > 0)
            {
                cloned.TypeParameters = sourceMethodSym.TypeParameters;
            }

            // Copy readonly status.
            if (member is Symbol readonlySym)
            {
                cloned.ReadonlyStatus = readonlySym.ReadonlyStatus;
            }

            targetScope.AddMember(cloned);
        }
    }

    private static void ResolveClassDeclaration(
        ClassDeclarationNode classDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var classSymbol = symbolTable.GetDeclaredSymbol(classDecl);
        if (classSymbol?.DeclaredType is not ClassType classType) return;

        var typeScope = (Scope)classType.MemberScope;

        // Generate implicit properties for dependent type parameters.
        GenerateDependentTypeProperties(classType.TypeParameters, typeScope, classType, sourceText, diagnostics);

        // Create a per-class resolution scope that layers the class's type
        // parameters on top of the file scope. This ensures that type parameter
        // names (e.g., T in GenericBox<T>) resolve to the correct
        // TypeParameterType instance for THIS class, not a same-named parameter
        // from a different generic class in the same file.
        var classFileScope = CreateTypeParameterScope(typeScope, fileScope);

        foreach (var memberDecl in classDecl.Members)
        {
            ResolveMemberDeclaration(memberDecl, typeScope, classFileScope, classType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveStructDeclaration(
        StructDeclarationNode structDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var structSymbol = symbolTable.GetDeclaredSymbol(structDecl);
        if (structSymbol?.DeclaredType is not StructType structType) return;

        var typeScope = (Scope)structType.MemberScope;

        // Generate implicit properties for dependent type parameters.
        GenerateDependentTypeProperties(structType.TypeParameters, typeScope, structType, sourceText, diagnostics);

        // Create a per-struct resolution scope that layers the struct's type
        // parameters on top of the file scope.
        var structFileScope = CreateTypeParameterScope(typeScope, fileScope);

        foreach (var memberDecl in structDecl.Members)
        {
            ResolveMemberDeclaration(memberDecl, typeScope, structFileScope, structType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveInterfaceDeclaration(
        InterfaceDeclarationNode interfaceDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var interfaceSymbol = symbolTable.GetDeclaredSymbol(interfaceDecl);
        if (interfaceSymbol?.DeclaredType is not InterfaceType interfaceType) return;

        // Create a per-interface resolution scope that layers the interface's
        // type parameters on top of the file scope.
        var typeScope = (Scope)interfaceType.MemberScope;

        // Generate implicit properties for dependent type parameters.
        GenerateDependentTypeProperties(interfaceType.TypeParameters, typeScope, interfaceType, sourceText, diagnostics);

        var interfaceFileScope = CreateTypeParameterScope(typeScope, fileScope);

        // Resolve return types and parameter types for each interface member.
        foreach (var member in interfaceDecl.Members)
        {
            switch (member)
            {
                case InterfaceMethodMemberInterfaceMemberDeclarationNode methodMember:
                {
                    var methodDecl = methodMember.Method;
                    var returnType = ResolveTypeReference(methodDecl.ReturnType, interfaceFileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var methodSymbol = symbolTable.GetDeclaredSymbol(methodDecl) as Symbol;
                    if (methodSymbol is not null)
                    {
                        methodSymbol.DeclaredType = returnType;
                    }

                    // Register parameters so ParameterSignature.Extract can resolve
                    // parameter types for overload duplicate-signature detection.
                    if (methodDecl.Parameters is not null)
                    {
                        var methodScope = new Scope(parent: interfaceFileScope);
                        RegisterParameters(methodDecl.Parameters, methodScope, interfaceFileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                    }
                    break;
                }
                case InterfacePropertyMemberInterfaceMemberDeclarationNode propertyMember:
                {
                    var propDecl = propertyMember.Property;
                    var propType = ResolveTypeReference(propDecl.Type, interfaceFileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var propSymbol = symbolTable.GetDeclaredSymbol(propDecl) as Symbol;
                    if (propSymbol is not null)
                    {
                        propSymbol.DeclaredType = propType;
                    }
                    break;
                }
            }
        }
    }

    private static void ResolveTraitDeclaration(
        TraitDeclarationNode traitDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var traitSymbol = symbolTable.GetDeclaredSymbol(traitDecl);
        if (traitSymbol?.DeclaredType is not TraitType traitType) return;

        // Create a resolution scope that includes the trait's type parameters
        // while still having access to the file scope's imported namespaces.
        // We use the trait's type scope directly since its parent is the
        // namespace scope (set up in RegisterTraitDeclaration).
        var typeScope = (Scope)traitType.MemberScope;
        var resolutionScope = typeScope;

        // Generate implicit properties for dependent type parameters.
        GenerateDependentTypeProperties(traitType.TypeParameters, typeScope, traitType, sourceText, diagnostics);

        // Resolve types for each trait member.
        foreach (var member in traitDecl.Members)
        {
            switch (member)
            {
                case TraitFieldMemberTraitMemberDeclarationNode fieldMember:
                {
                    var fieldDecl = fieldMember.Field;
                    var fieldType = ResolveTypeReference(fieldDecl.Type, resolutionScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                    // Storage-ban check for trait fields (unknown-type
                    // feature, Task 27.4, Requirement 12.4). Trait fields
                    // are subject to the same storage ban as class/struct
                    // fields because a type satisfying the trait must
                    // provide writable storage matching the trait's
                    // declared type — and writable storage cannot hold
                    // an `unknown` value (Req 5.1, 7.5, 7.6).
                    if (TypeContainment.IsStorageBanned(fieldType))
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            UnknownAsFieldType.Value,
                            DiagnosticSeverity.Error,
                            $"Field '{traitType.DisplayName}.{fieldDecl.Name.Lexeme}' of type '{fieldType.DisplayName}' cannot have 'unknown' in its declared type: 'unknown' is not permitted as the type of writable storage.",
                            fieldDecl,
                            sourceText));
                    }

                    var fieldSymbol = symbolTable.GetDeclaredSymbol(fieldDecl) as Symbol;
                    if (fieldSymbol is not null)
                    {
                        fieldSymbol.DeclaredType = fieldType;
                    }
                    break;
                }
                case TraitMethodMemberTraitMemberDeclarationNode methodMember:
                {
                    var methodDecl = methodMember.Method;
                    var returnType = ResolveTypeReference(methodDecl.ReturnType, resolutionScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var methodSymbol = symbolTable.GetDeclaredSymbol(methodDecl) as Symbol;
                    if (methodSymbol is not null)
                    {
                        methodSymbol.DeclaredType = returnType;
                    }

                    // Register parameters so parameter types can be resolved.
                    if (methodDecl.Parameters is not null)
                    {
                        var methodScope = new Scope(parent: resolutionScope);
                        RegisterParameters(methodDecl.Parameters, methodScope, resolutionScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                    }
                    break;
                }
                case TraitPropertyMemberTraitMemberDeclarationNode propertyMember:
                {
                    var propDecl = propertyMember.Property;
                    var propType = ResolveTypeReference(propDecl.Type, resolutionScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var propSymbol = symbolTable.GetDeclaredSymbol(propDecl) as Symbol;
                    if (propSymbol is not null)
                    {
                        propSymbol.DeclaredType = propType;
                    }
                    break;
                }
            }
        }
    }

    private static void ResolveMemberDeclaration(
        MemberDeclarationNode memberDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (memberDecl)
        {
            case FieldMemberMemberDeclarationNode fieldMember:
                ResolveFieldDeclaration(fieldMember.Field, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ConstructorMemberMemberDeclarationNode ctorMember:
                ResolveConstructorDeclaration(ctorMember.Constructor, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case MethodMemberMemberDeclarationNode methodMember:
                ResolveMethodDeclaration(methodMember.Method, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case OperatorMemberMemberDeclarationNode operatorMember:
                ResolveOperatorDeclaration(operatorMember.Operator, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case CastOperatorMemberMemberDeclarationNode castOpMember:
                ResolveCastOperatorDeclaration(castOpMember.CastOperator, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case PropertyMemberMemberDeclarationNode propertyMember:
                ResolvePropertyDeclaration(propertyMember.Property, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case IndexerMemberMemberDeclarationNode indexerMember:
                ResolveIndexerDeclaration(indexerMember.Indexer, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
        }
    }

    private static void ResolveFieldDeclaration(
        FieldDeclarationNode fieldDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var fieldType = ResolveTypeReference(fieldDecl.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        // Storage-ban check (unknown-type feature, Task 11.1, Requirements
        // 5.1, 7.5, 7.6, 16.1). Emitted BEFORE the symbol's declared type is
        // updated so downstream passes still see the banned type on the
        // symbol — the design note for Task 11 is explicit that we do not
        // substitute ErrorType here. Only the diagnostic is emitted.
        if (TypeContainment.IsStorageBanned(fieldType))
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                UnknownAsFieldType.Value,
                DiagnosticSeverity.Error,
                $"Field '{enclosingType.DisplayName}.{fieldDecl.Name.Lexeme}' of type '{fieldType.DisplayName}' cannot have 'unknown' in its declared type: 'unknown' is not permitted as the type of writable storage.",
                fieldDecl,
                sourceText));
        }

        // Update the field symbol's declared type.
        var fieldSymbol = symbolTable.GetDeclaredSymbol(fieldDecl) as Symbol;
        if (fieldSymbol is not null)
        {
            fieldSymbol.DeclaredType = fieldType;
        }

        // Resolve the optional initializer expression.
        // The grammar defines: initializer:(EQUAL Expression)?
        // With the bugfix, the Initializer property is now ExpressionNode? directly.
        if (fieldDecl.Initializer is ExpressionNode initExpr)
        {
            var isStatic = fieldDecl.Modifiers.Any(m => m is StaticModifierModifierNode);
            _emptyArrayTargetType = fieldType;
            ResolveExpression(initExpr, typeScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            _emptyArrayTargetType = null;
        }
    }

    private static void ResolveConstructorDeclaration(
        ConstructorDeclarationNode ctorDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Create a method scope for the constructor body.
        var methodScope = new Scope(parent: typeScope);

        // Register parameters.
        if (ctorDecl.Parameters is not null)
        {
            RegisterParameters(ctorDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve constructor chain arguments if present.
        if (ctorDecl.Chain is ThisChainConstructorChainNode thisChain && thisChain.Arguments is not null)
        {
            ResolveArgumentList(thisChain.Arguments, methodScope, fileScope, enclosingType, /* isStatic */ false, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
        else if (ctorDecl.Chain is BaseChainConstructorChainNode baseChain && baseChain.Arguments is not null)
        {
            ResolveArgumentList(baseChain.Arguments, methodScope, fileScope, enclosingType, /* isStatic */ false, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the body. A no-body (;) form is accepted by the grammar but
        // has no statements to resolve; semantic validation of whether ; is
        // permitted on this declaration is done elsewhere.
        if (ctorDecl.Body.GetBlock() is BlockNode ctorBlock)
            ResolveBlock(ctorBlock, methodScope, fileScope, enclosingType, /* isStatic */ false, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    private static void ResolveMethodDeclaration(
        MethodDeclarationNode methodDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the return type and update the method symbol.
        var returnType = ResolveTypeReference(methodDecl.ReturnType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var methodSymbol = symbolTable.GetDeclaredSymbol(methodDecl) as Symbol;
        if (methodSymbol is not null)
        {
            methodSymbol.DeclaredType = returnType;
        }

        // Determine if the method is static.
        var isStatic = methodDecl.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Create a method scope for the body.
        var methodScope = new Scope(parent: typeScope);

        // Register method-level dependent type parameters in the method's lexical scope.
        if (methodSymbol is not null && methodSymbol.TypeParameters.Count > 0)
        {
            RegisterMethodDependentParameters(methodSymbol.TypeParameters, methodScope, enclosingType, sourceText, diagnostics);
        }

        // Register parameters.
        if (methodDecl.Parameters is not null)
        {
            RegisterParameters(methodDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the body. A no-body (;) form is accepted by the grammar but
        // has no statements to resolve.
        if (methodDecl.Body.GetBlock() is BlockNode methodBlock)
            ResolveBlock(methodBlock, methodScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Pass 2 counterpart to <see cref="RegisterOperatorDeclaration"/>. Resolves
    /// the operator's return type (so <see cref="Symbol.DeclaredType"/> is set
    /// to the operator's result type), registers its parameters into a method
    /// scope (so <see cref="ParameterSignature.Extract"/> can retrieve parameter
    /// types for overload resolution in Task 14), and — if the operator has a
    /// body — resolves the body like a static method.
    ///
    /// An operator is treated as a static method for semantic-model purposes
    /// (Requirement 4.2, 4.6): parameter and body resolution use
    /// <c>isStatic = true</c>. Operators don't have type parameters in v1, so
    /// no equivalent of <see cref="RegisterMethodTypeParameters"/> is needed.
    /// </summary>
    private static void ResolveOperatorDeclaration(
        OperatorDeclarationNode opDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the return type and update the operator symbol.
        var returnType = ResolveTypeReference(opDecl.ReturnType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var opSymbol = symbolTable.GetDeclaredSymbol(opDecl) as Symbol;
        if (opSymbol is not null)
        {
            opSymbol.DeclaredType = returnType;
        }

        // Create a method scope for parameters and the optional body.
        var methodScope = new Scope(parent: typeScope);

        // Register parameters so ParameterSignature.Extract can retrieve
        // parameter types during overload resolution (Task 14).
        if (opDecl.Parameters is not null)
        {
            RegisterParameters(opDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the body. A no-body (;) form is accepted by the grammar
        // (Requirement 5.2 for intrinsic operators) and has no statements to
        // resolve; Pass 1e validates whether ; is permitted on this operator
        // based on the intrinsic modifier.
        if (opDecl.Body.GetBlock() is BlockNode opBlock)
            ResolveBlock(opBlock, methodScope, fileScope, enclosingType, /* isStatic */ true, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Pass 2 counterpart to <see cref="RegisterCastOperatorDeclaration"/>. Resolves
    /// the cast operator's target type reference to an <see cref="IType"/> and sets it
    /// as the symbol's <see cref="Symbol.DeclaredType"/> (return type). Registers the
    /// single parameter as a <see cref="SymbolKind.Parameter"/> symbol within the cast
    /// operator's scope, and resolves the body block (if present) using the same logic
    /// as <see cref="ResolveOperatorDeclaration"/>.
    ///
    /// Cast operators are always static methods (Requirement 6.4), so the body is
    /// resolved with <c>isStatic = true</c>.
    /// </summary>
    private static void ResolveCastOperatorDeclaration(
        CastOperatorDeclarationNode castOpDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the target type reference and set it as the symbol's DeclaredType (return type).
        var targetType = ResolveTypeReference(castOpDecl.TargetType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var castOpSymbol = symbolTable.GetDeclaredSymbol(castOpDecl) as Symbol;
        if (castOpSymbol is not null)
        {
            castOpSymbol.DeclaredType = targetType;
        }

        // Create a method scope for parameters and the optional body.
        var methodScope = new Scope(parent: typeScope);

        // Register the single parameter within the cast operator's scope.
        if (castOpDecl.Parameters is not null)
        {
            RegisterParameters(castOpDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the body. A no-body (;) form is accepted by the grammar
        // (for intrinsic cast operators) and has no statements to resolve;
        // Pass 1e validates whether ; is permitted based on the intrinsic modifier.
        if (castOpDecl.Body.GetBlock() is BlockNode castOpBlock)
            ResolveBlock(castOpBlock, methodScope, fileScope, enclosingType, /* isStatic */ true, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Resolves property type references, <c>field</c> keyword, and <c>value</c> implicit parameter.
    /// </summary>
    private static void ResolvePropertyDeclaration(
        PropertyDeclarationNode propertyDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                ResolveBlockPropertyDeclaration(blockProp, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                ResolveExpressionBodiedPropertyDeclaration(exprProp, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
        }
    }

    private static void ResolveBlockPropertyDeclaration(
        BlockPropertyPropertyDeclarationNode blockProp,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the property type and update the property symbol.
        var propType = ResolveTypeReference(blockProp.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        // Storage-ban check (unknown-type feature, Task 11.2, Requirements
        // 5.2, 7.5, 7.6, 16.1). See ResolveFieldDeclaration for the
        // "do not substitute ErrorType" rationale.
        if (TypeContainment.IsStorageBanned(propType))
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                UnknownAsPropertyType.Value,
                DiagnosticSeverity.Error,
                $"Property '{enclosingType.DisplayName}.{blockProp.Name.Lexeme}' of type '{propType.DisplayName}' cannot have 'unknown' in its declared type: 'unknown' is not permitted as the type of writable storage.",
                blockProp,
                sourceText));
        }

        var propSymbol = symbolTable.GetDeclaredSymbol(blockProp) as Symbol;
        if (propSymbol is not null)
        {
            propSymbol.DeclaredType = propType;
        }

        // Also update the backing field's declared type if present.
        var propInfo = propSymbol?.PropertyMetadata;
        if (propInfo?.BackingField is Symbol backingField)
        {
            backingField.DeclaredType = propType;
        }

        var isStatic = blockProp.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Resolve each accessor body.
        foreach (var accessor in blockProp.Accessors)
        {
            ResolveAccessorBody(accessor, propType, propInfo, typeScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the optional initializer expression.
        if (blockProp.Initializer is ExpressionNode initExpr)
        {
            ResolveExpression(initExpr, typeScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveExpressionBodiedPropertyDeclaration(
        ExpressionBodiedPropertyPropertyDeclarationNode exprProp,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the property type and update the property symbol.
        var propType = ResolveTypeReference(exprProp.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        // Storage-ban check (unknown-type feature, Task 11.2, Requirements
        // 5.2, 7.5, 7.6, 16.1). Expression-bodied properties have no
        // backing field per se, but Requirement 5.2 bans `unknown` as the
        // type of any property declaration regardless of how it is
        // implemented — the ban is a declaration-site rule.
        if (TypeContainment.IsStorageBanned(propType))
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                UnknownAsPropertyType.Value,
                DiagnosticSeverity.Error,
                $"Property '{enclosingType.DisplayName}.{exprProp.Name.Lexeme}' of type '{propType.DisplayName}' cannot have 'unknown' in its declared type: 'unknown' is not permitted as the type of writable storage.",
                exprProp,
                sourceText));
        }

        var propSymbol = symbolTable.GetDeclaredSymbol(exprProp) as Symbol;
        if (propSymbol is not null)
        {
            propSymbol.DeclaredType = propType;
        }

        var isStatic = exprProp.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Resolve the expression body.
        ResolveExpression(exprProp.Body, typeScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Resolves an accessor body, injecting implicit <c>value</c> parameter for set/init
    /// and resolving <c>field</c> keyword references.
    /// </summary>
    private static void ResolveAccessorBody(
        AccessorDeclarationNode accessor,
        IType propType,
        PropertyInfo? propInfo,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Create an accessor scope that includes the backing field symbol (if any)
        // so that 'field' keyword resolution finds the correct backing field.
        Scope accessorBaseScope;
        if (propInfo?.BackingField is Symbol backingFieldSym)
        {
            accessorBaseScope = new Scope(parent: typeScope);
            // Register a synthetic property symbol that points to this specific backing field,
            // so ResolveFieldKeyword finds the correct one.
            var syntheticProp = new Symbol("__accessor_property__", SymbolKind.Property, SymbolOrigin.UserCode, accessorBaseScope, propType, declarationSpan: null);
            syntheticProp.PropertyMetadata = propInfo;
            accessorBaseScope.AddMember(syntheticProp);
        }
        else
        {
            accessorBaseScope = typeScope;
        }

        switch (accessor)
        {
            case BlockGetAccessorAccessorDeclarationNode blockGet:
                ResolveBlock(blockGet.Body, accessorBaseScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                ResolveExpression(exprGet.Expression, accessorBaseScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case BlockSetAccessorAccessorDeclarationNode blockSet:
            {
                // Inject implicit 'value' parameter of the property's declared type.
                var setScope = new Scope(parent: accessorBaseScope);
                var valueSymbol = new Symbol("value", SymbolKind.Parameter, SymbolOrigin.UserCode, setScope, propType, declarationSpan: null);
                setScope.AddMember(valueSymbol);
                ResolveBlock(blockSet.Body, setScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }
            case ExpressionSetAccessorAccessorDeclarationNode exprSet:
            {
                var setScope = new Scope(parent: accessorBaseScope);
                var valueSymbol = new Symbol("value", SymbolKind.Parameter, SymbolOrigin.UserCode, setScope, propType, declarationSpan: null);
                setScope.AddMember(valueSymbol);
                ResolveExpression(exprSet.Expression, setScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }
            case BlockInitAccessorAccessorDeclarationNode blockInit:
            {
                var initScope = new Scope(parent: accessorBaseScope);
                var valueSymbol = new Symbol("value", SymbolKind.Parameter, SymbolOrigin.UserCode, initScope, propType, declarationSpan: null);
                initScope.AddMember(valueSymbol);
                ResolveBlock(blockInit.Body, initScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }
            case ExpressionInitAccessorAccessorDeclarationNode exprInit:
            {
                var initScope = new Scope(parent: accessorBaseScope);
                var valueSymbol = new Symbol("value", SymbolKind.Parameter, SymbolOrigin.UserCode, initScope, propType, declarationSpan: null);
                initScope.AddMember(valueSymbol);
                ResolveExpression(exprInit.Expression, initScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }
            // Auto accessors (AutoGet, AutoSet, AutoInit) have no body to resolve.
        }
    }

    /// <summary>
    /// Resolves indexer type references and accessor bodies.
    /// </summary>
    private static void ResolveIndexerDeclaration(
        IndexerDeclarationNode indexerDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (indexerDecl is not BlockIndexerIndexerDeclarationNode blockIndexer)
            return;

        // Resolve the indexer return type and update the indexer symbol.
        var indexerType = ResolveTypeReference(blockIndexer.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        // Storage-ban check (unknown-type feature, Task 11.3, Requirements
        // 5.3, 7.5, 7.6, 16.1). Applies to the indexer's element type — the
        // type yielded by a get and consumed by a set — which is the
        // storage-shaped position per Requirement 5.3.
        if (TypeContainment.IsStorageBanned(indexerType))
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                UnknownAsIndexerElementType.Value,
                DiagnosticSeverity.Error,
                $"Indexer '{enclosingType.DisplayName}.this[]' of element type '{indexerType.DisplayName}' cannot have 'unknown' in its declared type: 'unknown' is not permitted as the element type of writable storage.",
                blockIndexer,
                sourceText));
        }

        var indexerSymbol = symbolTable.GetDeclaredSymbol(blockIndexer) as Symbol;
        if (indexerSymbol is not null)
        {
            indexerSymbol.DeclaredType = indexerType;
        }

        var isStatic = blockIndexer.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Create a scope for the indexer parameters.
        var indexerScope = new Scope(parent: typeScope);

        // Register indexer parameters.
        RegisterParameters(blockIndexer.Parameters, indexerScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);

        // Resolve each accessor body with the indexer scope (which has the parameters).
        var propInfo = indexerSymbol?.PropertyMetadata;
        foreach (var accessor in blockIndexer.Accessors)
        {
            ResolveAccessorBody(accessor, indexerType, propInfo, indexerScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void RegisterParameters(
        ParameterListNode paramList,
        Scope methodScope,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // The grammar rule is: ParameterList = Parameter (COMMA Parameter)*
        // Iterate all ParameterNode children uniformly to register every parameter.
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                RegisterSingleParameter(param, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
            }
        }
    }

    private static void RegisterSingleParameter(
        ParameterNode param,
        Scope methodScope,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var paramType = ResolveTypeReference(param.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var paramName = param.Name.Lexeme;
        var paramSpan = ToMistSourceSpan(param.Name.Span);

        // Check for duplicate parameter names.
        var existing = methodScope.Lookup(paramName);
        if (existing is not null && existing.ContainingScope == methodScope)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                "MIST_DUPLICATE_DECLARATION",
                DiagnosticSeverity.Error,
                $"A parameter named '{paramName}' is already declared in this scope.",
                param,
                sourceText));
            return;
        }

        var paramSymbol = new Symbol(paramName, SymbolKind.Parameter, SymbolOrigin.UserCode, methodScope, paramType, paramSpan);

        // Detect readonly modifier on the parameter.
        if (param.Modifier is not null)
        {
            paramSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
        }

        methodScope.AddMember(paramSymbol);
        symbolTable.Register(paramSymbol, param);
    }

    // ─── Block and Statement Resolution ──────────────────────────────────

    private static void ResolveBlock(
        BlockNode block,
        Scope parentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var blockScope = new Scope(parent: parentScope);

        foreach (var stmt in block.Statements)
        {
            ResolveStatement(stmt, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveStatement(
        StatementNode stmt,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                ResolveVariableDeclaration(varDeclStmt.VariableDeclaration, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ReturnStatementStatementNode returnStmt:
                if (returnStmt.ReturnStatement.Value is not null)
                {
                    ResolveExpression(returnStmt.ReturnStatement.Value, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }
                break;
            case IfStatementStatementNode ifStmt:
                ResolveIfStatement(ifStmt.IfStatement, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case WhileStatementStatementNode whileStmt:
                ResolveExpression(whileStmt.WhileStatement.Condition, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                ResolveBlock(whileStmt.WhileStatement.Body, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ForStatementStatementNode forStmt:
                ResolveForStatement(forStmt.ForStatement, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ForeachStatementStatementNode foreachStmt:
                ResolveForeachStatement(foreachStmt.ForeachStatement, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ExpressionStatementStatementNode exprStmt:
                ResolveExpression(exprStmt.ExpressionStatement.Expression, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case BreakStatementStatementNode:
                // No type resolution needed.
                break;
        }
    }

    private static void ResolveVariableDeclaration(
        VariableDeclarationStatementNode varDecl,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
            {
                var declaredType = ResolveTypeReference(typed.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                // Set target type context so EmptyArrayLiteralExpressionPrimaryNode
                // can infer its element type from the declared array type.
                _emptyArrayTargetType = declaredType;
                var initType = ResolveExpression(typed.Initializer, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                _emptyArrayTargetType = null;

                var varName = typed.Name.Lexeme;
                var varSpan = ToMistSourceSpan(typed.Name.Span);

                CheckDuplicateLocal(varName, blockScope, sourceText, diagnostics);

                var varSymbol = new Symbol(varName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, blockScope, declaredType, varSpan);
                blockScope.AddMember(varSymbol);
                symbolTable.Register(varSymbol, varDecl);
                break;
            }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
            {
                // var — infer type from initializer.
                var initType = ResolveExpression(inferred.Initializer, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                var varName = inferred.Name.Lexeme;
                var varSpan = ToMistSourceSpan(inferred.Name.Span);

                CheckDuplicateLocal(varName, blockScope, sourceText, diagnostics);

                var varSymbol = new Symbol(varName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, blockScope, initType, varSpan);
                blockScope.AddMember(varSymbol);
                symbolTable.Register(varSymbol, varDecl);
                break;
            }
            case VarTupleDeconstructionVariableDeclarationStatementNode varTuple:
            {
                // var (a, b, ...) = expr; — resolve initializer, defer element type inference to type checker.
                var initType = ResolveExpression(varTuple.Initializer, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                // Register first name.
                var firstName = varTuple.FirstName.Lexeme;
                CheckDuplicateLocal(firstName, blockScope, sourceText, diagnostics);
                var firstSymbol = new Symbol(firstName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, blockScope, ErrorType.Instance, ToMistSourceSpan(varTuple.FirstName.Span));
                blockScope.AddMember(firstSymbol);
                symbolTable.Register(firstSymbol, varDecl);

                // Register rest names.
                foreach (var restName in varTuple.RestNames)
                {
                    var name = restName.Lexeme;
                    CheckDuplicateLocal(name, blockScope, sourceText, diagnostics);
                    var restSymbol = new Symbol(name, SymbolKind.LocalVariable, SymbolOrigin.UserCode, blockScope, ErrorType.Instance, ToMistSourceSpan(restName.Span));
                    blockScope.AddMember(restSymbol);
                    symbolTable.Register(restSymbol, varDecl);
                }
                break;
            }
            case TypedTupleDeconstructionVariableDeclarationStatementNode typedTuple:
            {
                // (Type1 a, Type2 b, ...) = expr; — resolve each element's declared type.
                var initType = ResolveExpression(typedTuple.Initializer, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                void RegisterDeconstructionElement(TupleDeconstructionElementNode element)
                {
                    var declaredType = ResolveTypeReference(element.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var name = element.Name.Lexeme;
                    CheckDuplicateLocal(name, blockScope, sourceText, diagnostics);
                    var sym = new Symbol(name, SymbolKind.LocalVariable, SymbolOrigin.UserCode, blockScope, declaredType, ToMistSourceSpan(element.Name.Span));
                    blockScope.AddMember(sym);
                    symbolTable.Register(sym, varDecl);
                }

                RegisterDeconstructionElement(typedTuple.First);
                RegisterDeconstructionElement(typedTuple.Rest);
                foreach (var additional in typedTuple.TupleDeconstructionElements)
                {
                    RegisterDeconstructionElement(additional);
                }
                break;
            }
        }
    }

    private static void ResolveIfStatement(
        IfStatementNode ifStmt,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Step 1: Resolve the condition expression (which may contain `is` expressions).
        ResolveExpression(ifStmt.Condition, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Step 2: Create a child scope for the then-branch and populate it
        // with pattern variables introduced by `is` expressions in the condition.
        var thenScope = new Scope(parent: blockScope);
        CollectPatternVariables(ifStmt.Condition, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);

        // Step 3: Resolve the then-branch in the child scope that contains
        // pattern variables. ResolveBlock creates its own nested scope from
        // the parent we pass, so pattern variables are visible inside.
        ResolveBlock(ifStmt.ThenBranch, thenScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Step 4: The else branch does NOT get pattern variables.
        if (ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
        {
            ResolveIfStatement(elseIf.ElseIf, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
        else if (ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
        {
            ResolveBlock(elseBlock.ElseBlock, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    // ─── Pattern Variable Collection ────────────────────────────────────
    // Walks the condition expression tree looking for IsExpressionRelationalNode
    // nodes and extracts variable bindings from their patterns into the
    // then-branch scope. This enables pattern variables to be used in the
    // then-branch of an `if` statement (e.g., `if (x is string s) { ... s ... }`).

    /// <summary>
    /// Walks the condition expression tree to find <c>is</c> expressions and
    /// registers pattern variables in the given scope.
    /// </summary>
    private static void CollectPatternVariables(
        ExpressionNode condition,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Expression → Assignment → LogicalOr → LogicalAnd → Equality → Relational
        // Walk through the chain to find IsExpressionRelationalNode nodes.
        CollectFromAssignment(condition.Assignment, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
    }

    private static void CollectFromAssignment(
        AssignmentNode node,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        CollectFromLogicalOr(node.Target, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
    }

    private static void CollectFromLogicalOr(
        LogicalOrNode node,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        CollectFromLogicalAnd(node.Left, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            CollectFromLogicalAnd(node.Right, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void CollectFromLogicalAnd(
        LogicalAndNode node,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        CollectFromEquality(node.Left, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            CollectFromEquality(node.Right, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void CollectFromEquality(
        EqualityNode node,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        CollectFromRelational(node.Left, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            CollectFromRelational(node.Right, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void CollectFromRelational(
        RelationalNode node,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (node is IsExpressionRelationalNode isExpr)
        {
            // Get the scrutinee type from the expression types map.
            // The scrutinee (isExpr.Left) was already resolved by ResolveRelational.
            expressionTypes.TryGetValue(isExpr.Left, out var scrutineeType);
            scrutineeType ??= ErrorType.Instance;

            // Extract variable bindings from the pattern.
            CollectFromPattern(isExpr.Pattern, scrutineeType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
        }
        // For RelationalComparisonRelationalNode, no pattern variables to collect.
    }

    private static void CollectFromPattern(
        PatternNode pattern,
        IType scrutineeType,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        CollectFromOrPattern(pattern.OrPattern, scrutineeType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
    }

    private static void CollectFromOrPattern(
        OrPatternNode orPattern,
        IType scrutineeType,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        CollectFromAndPattern(orPattern.Left, scrutineeType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);

        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            CollectFromAndPattern(orPattern.Right, scrutineeType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void CollectFromAndPattern(
        AndPatternNode andPattern,
        IType scrutineeType,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        CollectFromUnaryPattern(andPattern.Left, scrutineeType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            CollectFromUnaryPattern(andPattern.Right, scrutineeType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void CollectFromUnaryPattern(
        UnaryPatternNode unaryPattern,
        IType scrutineeType,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
                // Not patterns may contain subpatterns, but variable bindings
                // inside `not` are semantically invalid (the type checker will
                // flag them). We still collect them here so the semantic model
                // is complete — the type checker handles the diagnostic.
                CollectFromPrimaryPattern(notPattern.Operand, scrutineeType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
                break;
            case DirectPatternUnaryPatternNode directPattern:
                CollectFromPrimaryPattern(directPattern.Primary, scrutineeType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
                break;
        }
    }

    private static void CollectFromPrimaryPattern(
        PrimaryPatternNode primaryPattern,
        IType scrutineeType,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                // Type pattern: `Type name` — register `name` as a local variable
                // with the pattern's resolved type.
                var name = typePattern.Name.Lexeme;

                // Discard `_` does not introduce a variable.
                if (name == "_")
                    break;

                var patternType = ResolveTypeReference(typePattern.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                // C# semantics for `is (int a, int b) pair`: the `pair`
                // binding has the UNNAMED tuple type, not the named form.
                // Element names in the pattern syntax introduce separate
                // local bindings (`a`, `b`) but do not attach to `pair`.
                // We strip the names here so Mist lookups match C#:
                // `pair.a` fails (there's no `a` member on the unnamed
                // tuple), `pair.Item1` succeeds.
                var bindingType = patternType;
                if (patternType is TupleType tupleTypeForBinding
                    && tupleTypeForBinding.ElementNames is not null)
                {
                    bindingType = new TupleType(tupleTypeForBinding.ElementTypes, elementNames: null);
                }

                var varSpan = ToMistSourceSpan(typePattern.Name.Span);
                var varSymbol = new Symbol(name, SymbolKind.LocalVariable, SymbolOrigin.UserCode, thenScope, bindingType, varSpan);
                thenScope.AddMember(varSymbol);
                symbolTable.Register(varSymbol, typePattern);

                // Tuple type pattern: if the pattern type is a named
                // tuple, each named element introduces a local binding
                // in the then-scope, matching C# semantics. In C#,
                // `is (int a, int b) pair` against a tuple scrutinee
                // deconstructs into separate `a`, `b`, and `pair`
                // bindings — `a` and `b` are the deconstructed element
                // values, `pair` is the whole tuple (without named-element
                // metadata on the binding).
                //
                // When the scrutinee is not a concrete tuple (e.g., a
                // bare `unknown`, which lowers to C# `object`), the
                // emitter uses the chained form
                // `u is ValueTuple<int, int> pair && pair is (int a, int b)`
                // to introduce the element names as locals — C# accepts
                // this compound pattern against any scrutinee. The
                // end result is identical from Mist's perspective: `a`
                // and `b` are bound in the then-scope with the
                // deconstructed element values. We therefore register
                // the element-name bindings whenever the pattern type
                // carries names, without restricting on the scrutinee
                // type.
                if (typePattern.Type is TupleTypeRefTypeReferenceNode tupleRef
                    && patternType is TupleType tupleType
                    && tupleType.ElementNames is not null)
                {
                    // Walk the AST elements in order to get token spans
                    // for each named binding; parallel to the element
                    // types on the resolved TupleType.
                    var astElements = new List<TupleTypeElementNode> { tupleRef.First, tupleRef.Rest };
                    astElements.AddRange(tupleRef.TupleTypeElements);

                    for (var i = 0; i < tupleType.ElementNames.Count; i++)
                    {
                        var elementName = tupleType.ElementNames[i];
                        if (elementName is null || elementName == "_") continue;
                        if (i >= astElements.Count) continue;

                        var elementAst = astElements[i];
                        if (!elementAst.Name.HasValue) continue;

                        var elementSpan = ToMistSourceSpan(elementAst.Name.Value.Span);
                        var elementSymbol = new Symbol(
                            elementName,
                            SymbolKind.LocalVariable,
                            SymbolOrigin.UserCode,
                            thenScope,
                            tupleType.ElementTypes[i],
                            elementSpan);
                        thenScope.AddMember(elementSymbol);
                        symbolTable.Register(elementSymbol, elementAst);
                    }
                }

                break;
            }

            case BareTypePatternPrimaryPatternNode:
            {
                // Bare type pattern: `Type` (no binding name) — pure type test.
                // No variable to register.
                break;
            }

            case VarPatternPrimaryPatternNode varPattern:
            {
                // Var pattern: `var name` — register `name` as a local variable
                // with the scrutinee's static type.
                var name = varPattern.Name.Lexeme;

                // Discard `_` does not introduce a variable.
                if (name == "_")
                    break;

                var varSpan = ToMistSourceSpan(varPattern.Name.Span);
                var varSymbol = new Symbol(name, SymbolKind.LocalVariable, SymbolOrigin.UserCode, thenScope, scrutineeType, varSpan);
                thenScope.AddMember(varSymbol);
                symbolTable.Register(varSymbol, varPattern);
                break;
            }

            case PropertyPatternPrimaryPatternNode propertyPattern:
            {
                // Determine the pattern type for subpattern recursion.
                var patternType = scrutineeType;
                if (propertyPattern.TypeName is not null)
                {
                    var typeName = ExtractQualifiedName(propertyPattern.TypeName, sourceText);
                    patternType = ResolveNamedType(typeName, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                }

                // Recursively collect from subpatterns.
                if (propertyPattern.Subpatterns is not null)
                {
                    CollectFromPropertySubpatternList(propertyPattern.Subpatterns, patternType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
                }

                // Property pattern with trailing identifier: register the identifier.
                if (propertyPattern.Name.HasValue)
                {
                    var name = propertyPattern.Name.Value.Lexeme;
                    if (name != "_")
                    {
                        var varSpan = ToMistSourceSpan(propertyPattern.Name.Value.Span);
                        var varSymbol = new Symbol(name, SymbolKind.LocalVariable, SymbolOrigin.UserCode, thenScope, patternType, varSpan);
                        thenScope.AddMember(varSymbol);
                        symbolTable.Register(varSymbol, propertyPattern);
                    }
                }
                break;
            }

            case ListPatternPrimaryPatternNode listPattern:
            {
                // Determine the element type for subpattern recursion.
                var elementType = scrutineeType is ArrayType arrayType
                    ? arrayType.ElementType
                    : ErrorType.Instance;

                if (listPattern.Elements is not null)
                {
                    CollectFromListPatternElementList(listPattern.Elements, elementType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
                }
                break;
            }

            case ParenthesizedPatternPrimaryPatternNode parenPattern:
            {
                CollectFromPattern(parenPattern.Inner, scrutineeType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
                break;
            }

            // Constant patterns, relational patterns, and discard patterns
            // do not introduce variables — nothing to collect.
        }
    }

    private static void CollectFromPropertySubpatternList(
        PropertySubpatternListNode subpatternList,
        IType patternType,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        CollectFromPropertySubpattern(subpatternList.PropertySubpattern, patternType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);

        foreach (var subpattern in subpatternList.PropertySubpatterns)
        {
            CollectFromPropertySubpattern(subpattern, patternType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void CollectFromPropertySubpattern(
        PropertySubpatternNode subpattern,
        IType patternType,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Determine the property's type from the pattern type's member scope.
        var propertyType = ErrorType.Instance as IType;
        var propertyName = subpattern.Name.Lexeme;

        if (patternType is ClassType classType)
        {
            var member = classType.MemberScope.Lookup(propertyName);
            if (member?.DeclaredType is not null)
                propertyType = member.DeclaredType;
        }
        else if (patternType is StructType structType)
        {
            var member = structType.MemberScope.Lookup(propertyName);
            if (member?.DeclaredType is not null)
                propertyType = member.DeclaredType;
        }
        else if (patternType is InterfaceType interfaceType)
        {
            var member = interfaceType.MemberScope.Lookup(propertyName);
            if (member?.DeclaredType is not null)
                propertyType = member.DeclaredType;
        }

        // Recursively collect from the subpattern's inner pattern.
        CollectFromPattern(subpattern.Pattern, propertyType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
    }

    private static void CollectFromListPatternElementList(
        ListPatternElementListNode elementList,
        IType elementType,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        CollectFromListPatternElement(elementList.ListPatternElement, elementType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);

        foreach (var element in elementList.ListPatternElements)
        {
            CollectFromListPatternElement(element, elementType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void CollectFromListPatternElement(
        ListPatternElementNode element,
        IType elementType,
        Scope thenScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (element)
        {
            case PatternElementListPatternElementNode patternElement:
                CollectFromPattern(patternElement.Pattern, elementType, thenScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);
                break;

            case SliceElementListPatternElementNode sliceElement:
                // Slice element with an identifier (`.. rest`): register the identifier.
                if (sliceElement.Name.HasValue)
                {
                    var name = sliceElement.Name.Value.Lexeme;
                    if (name != "_")
                    {
                        // The slice variable captures a sub-array, so its type
                        // is the same as the scrutinee array type (not the element type).
                        // For now, use ErrorType since we don't have the full
                        // array type here — the type checker will validate.
                        var varSpan = ToMistSourceSpan(sliceElement.Name.Value.Span);
                        var varSymbol = new Symbol(name, SymbolKind.LocalVariable, SymbolOrigin.UserCode, thenScope, ErrorType.Instance, varSpan);
                        thenScope.AddMember(varSymbol);
                        symbolTable.Register(varSymbol, sliceElement);
                    }
                }
                break;
        }
    }

    private static void ResolveForStatement(
        ForStatementNode forStmt,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Create a scope for the for statement (initializer variable is scoped here).
        var forScope = new Scope(parent: blockScope);

        if (forStmt.Initializer is ForInitializerVariableDeclarationForInitializerNode varInit)
        {
            var initType = ResolveTypeReference(varInit.VariableDeclaration, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
            var initExprType = ResolveExpression(varInit.Initializer, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

            var varName = varInit.Name.Lexeme;
            var varSpan = ToMistSourceSpan(varInit.Name.Span);
            var varSymbol = new Symbol(varName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, forScope, initType, varSpan);
            forScope.AddMember(varSymbol);
            symbolTable.Register(varSymbol, varInit);
        }
        else if (forStmt.Initializer is ForInitializerVarDeclarationForInitializerNode varDeclInit)
        {
            var initExprType = ResolveExpression(varDeclInit.Initializer, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

            var varName = varDeclInit.Name.Lexeme;
            var varSpan = ToMistSourceSpan(varDeclInit.Name.Span);
            var varSymbol = new Symbol(varName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, forScope, initExprType, varSpan);
            forScope.AddMember(varSymbol);
            symbolTable.Register(varSymbol, varDeclInit);
        }
        else if (forStmt.Initializer is ForInitializerExpressionForInitializerNode exprInit)
        {
            ResolveExpression(exprInit.Expression, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        if (forStmt.Condition is not null)
        {
            ResolveExpression(forStmt.Condition, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        if (forStmt.Iterator is not null)
        {
            ResolveExpression(forStmt.Iterator, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        ResolveBlock(forStmt.Body, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    private static void ResolveForeachStatement(
        ForeachStatementNode foreachStmt,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var elementType = ResolveTypeReference(foreachStmt.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        ResolveExpression(foreachStmt.Collection, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Create a scope for the foreach body with the iteration variable.
        var foreachScope = new Scope(parent: blockScope);
        var varName = foreachStmt.Name.Lexeme;
        var varSpan = ToMistSourceSpan(foreachStmt.Name.Span);
        var varSymbol = new Symbol(varName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, foreachScope, elementType, varSpan);
        foreachScope.AddMember(varSymbol);
        symbolTable.Register(varSymbol, foreachStmt);

        ResolveBlock(foreachStmt.Body, foreachScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    // ─── Refinable Type Expression Resolution ─────────────────────────────

    /// <summary>
    /// Resolves a <see cref="RefinableTypeExpressionNode"/> to an <see cref="IType"/>.
    /// For single types without refinements, delegates to <see cref="ResolveSingleTypeRef"/>.
    /// For single types with refinements, validates predicate compatibility and constructs
    /// a <see cref="Types.RefinementType"/>.
    /// For union types, builds a <see cref="UnionType"/> from the constituents (each of which
    /// may be a <see cref="Types.RefinementType"/> if it has a refinement predicate).
    /// </summary>
    private static IType ResolveRefinableTypeExpression(
        RefinableTypeExpressionNode refinableExpr,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics,
        string? aliasName = null)
    {
        switch (refinableExpr)
        {
            case RefinableSingleTypeRefinableTypeExpressionNode single:
                return ResolveRefinableSingleTypeExpr(
                    single.SingleType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics, aliasName);

            case RefinableUnionTypeRefinableTypeExpressionNode union:
            {
                var constituents = new List<IType>();
                var firstType = ResolveRefinableSingleTypeExpr(
                    union.UnionType.First, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics, aliasName);
                constituents.Add(firstType);

                foreach (var additional in union.UnionType.RefinableSingleTypeExprs)
                {
                    var additionalType = ResolveRefinableSingleTypeExpr(
                        additional, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics, aliasName);
                    constituents.Add(additionalType);
                }

                // If any constituent is an error, propagate.
                if (constituents.Any(c => c is ErrorType))
                    return ErrorType.Instance;

                return new UnionType(constituents.ToImmutableArray());
            }

            default:
                return ErrorType.Instance;
        }
    }

    /// <summary>
    /// Resolves a single refinable type expression (base type + optional refinement predicate).
    /// When a refinement is present, validates predicate compatibility and constructs a
    /// <see cref="Types.RefinementType"/>. When no refinement is present, resolves the
    /// base <see cref="SingleTypeRefNode"/> directly.
    /// </summary>
    private static IType ResolveRefinableSingleTypeExpr(
        RefinableSingleTypeExprNode singleExpr,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics,
        string? aliasName)
    {
        var baseType = ResolveSingleTypeRef(
            singleExpr.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        if (baseType is ErrorType)
            return ErrorType.Instance;

        // If no refinement predicate, return the plain base type.
        if (singleExpr.Refinement is null)
            return baseType;

        // Refinement is present — validate predicate compatibility with the base type.
        var andPatternNode = singleExpr.Refinement.Predicate;
        var predicate = new RefinementPredicate(andPatternNode);

        // Validate: relational patterns require a comparable type.
        if (PredicateContainsRelationalPattern(andPatternNode) && !IsComparableType(baseType))
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                IncompatibleRefinementPredicate.Value,
                DiagnosticSeverity.Error,
                $"Refinement predicate '{predicate.Description}' is not valid for base type '{baseType.DisplayName}'. Relational patterns require a comparable type.",
                singleExpr.Refinement,
                sourceText));
            return ErrorType.Instance;
        }

        // Construct the RefinementType.
        var name = aliasName ?? baseType.DisplayName;
        return new RefinementType(name, baseType, predicate);
    }

    /// <summary>
    /// Determines whether a type supports comparison operators (numeric types and char).
    /// Used to validate that relational refinement predicates are compatible with the base type.
    /// </summary>
    private static bool IsComparableType(IType type)
    {
        var displayName = type.DisplayName;
        return displayName switch
        {
            // Legacy primitive names.
            "int" or "uint" or "long" or "ulong" => true,
            "short" or "ushort" or "byte" or "sbyte" => true,
            "float" or "double" or "decimal" => true,
            "char" => true,
            // Stdlib names.
            "Int32" or "UInt32" or "Int64" or "UInt64" => true,
            "Int16" or "UInt16" or "Byte" or "SByte" => true,
            "Single" or "Double" or "Decimal" => true,
            "Char" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Checks whether an <see cref="AndPatternNode"/> contains any relational patterns
    /// (greater-than, less-than, greater-than-or-equal, less-than-or-equal).
    /// </summary>
    private static bool PredicateContainsRelationalPattern(AndPatternNode andPattern)
    {
        if (IsRelationalUnaryPattern(andPattern.Left))
            return true;

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            if (IsRelationalUnaryPattern(andPattern.Right))
                return true;
        }

        if (!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in andPattern.UnaryPatterns)
            {
                if (IsRelationalUnaryPattern(part))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a <see cref="UnaryPatternNode"/> is a relational pattern.
    /// </summary>
    private static bool IsRelationalUnaryPattern(UnaryPatternNode unary)
    {
        if (unary is not DirectPatternUnaryPatternNode direct)
            return false;

        return direct.Primary is GreaterThanOrEqualRelationalPatternPrimaryPatternNode
            or LessThanOrEqualRelationalPatternPrimaryPatternNode
            or GreaterThanRelationalPatternPrimaryPatternNode
            or LessThanRelationalPatternPrimaryPatternNode;
    }

    /// <summary>
    /// Checks whether a <see cref="UnionType"/> has at least one constituent that is a
    /// <see cref="Types.RefinementType"/>.
    /// </summary>
    private static bool HasRefinementConstituent(UnionType unionType)
    {
        foreach (var constituent in unionType.Constituents)
        {
            if (constituent is RefinementType)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether a <see cref="RefinableTypeExpressionNode"/> contains any refinement
    /// predicates. Used to suppress cascading diagnostics when an incompatible predicate
    /// already caused an error.
    /// </summary>
    private static bool HasRefinementPredicate(RefinableTypeExpressionNode refinableExpr)
    {
        switch (refinableExpr)
        {
            case RefinableSingleTypeRefinableTypeExpressionNode single:
                return single.SingleType.Refinement is not null;

            case RefinableUnionTypeRefinableTypeExpressionNode union:
            {
                if (union.UnionType.First.Refinement is not null)
                    return true;
                foreach (var additional in union.UnionType.RefinableSingleTypeExprs)
                {
                    if (additional.Refinement is not null)
                        return true;
                }
                return false;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Extracts a human-readable type name from a <see cref="RefinableTypeExpressionNode"/>
    /// for use in diagnostic messages.
    /// </summary>
    private static string ExtractRefinableTypeExpressionName(
        RefinableTypeExpressionNode refinableExpr,
        SourceText? sourceText)
    {
        switch (refinableExpr)
        {
            case RefinableSingleTypeRefinableTypeExpressionNode single:
                return ExtractSingleTypeRefName(single.SingleType.Type, sourceText);

            case RefinableUnionTypeRefinableTypeExpressionNode union:
                return ExtractSingleTypeRefName(union.UnionType.First.Type, sourceText);

            default:
                return "unknown";
        }
    }

    private static string ExtractSingleTypeRefName(SingleTypeRefNode singleTypeRef, SourceText? sourceText)
    {
        switch (singleTypeRef)
        {
            case NamedSingleTypeRefSingleTypeRefNode namedRef:
                var lexeme = namedRef.Name.IDENTIFIER.Lexeme;
                if (!string.IsNullOrEmpty(lexeme))
                    return lexeme;
                if (sourceText is not null)
                {
                    var span = namedRef.Name.Span;
                    if (span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
                        return sourceText.Contents.Substring(span.Start, span.Length);
                }
                return "";
            case PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef:
                return FormatPrimitiveType(primitiveRef.PrimitiveType);
            case VoidSingleTypeRefSingleTypeRefNode:
                return "void";
            default:
                return "unknown";
        }
    }

    // ─── Type Reference Resolution ───────────────────────────────────────

    /// <summary>
    /// Resolves a TypeReference AST node to an IType.
    /// Lookup order: primitives → string/void → scope chain → imported namespaces → ErrorType.
    /// </summary>
    private static IType ResolveTypeReference(
        TypeReferenceNode typeRef,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        IType baseType;

        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
                // Task 31 of the Mist Standard Library spec: primitive
                // keyword references (`int`, `bool`, `string`, …) now
                // resolve through ResolvePrimitiveKeyword, which walks
                // directly to the Standard namespace scope and returns
                // the stdlib IType instance. The resolution bypasses
                // every scope-chain / using-directive / user-type lookup
                // (Requirement 7.6): a user declaring `class Int32 { }`
                // in their own namespace cannot intercept the keyword
                // `int`.
                //
                // Fallback: when no stdlib has been loaded for this
                // compilation (unit tests that build a semantic model
                // from a minimal AST), LookupQualifiedStdlibType throws
                // StdlibMissingException. We catch and fall back to the
                // legacy LegacyPrimitiveTypes singletons
                // (LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String,
                // …) — NOT the public property accessors (which read the
                // global registry, whose state might reflect a previous
                // compilation's stdlib). The direct-to-legacy fallback
                // guarantees that a stdlib-less build never embeds
                // another build's stdlib IType instances in its own
                // expression-type map.
                IType primBase;
                try
                {
                    var primGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                    primBase = ResolvePrimitiveKeyword(primGlobalScope, symbolTable, primitiveRef.PrimitiveType);
                }
                catch (StdlibMissingException)
                {
                    primBase = ResolveLegacyPrimitive(primitiveRef.PrimitiveType);
                }
                if (primitiveRef.ArrayRank.HasValue)
                {
                    return new ArrayType(primBase);
                }
                return primBase;

            case VoidTypeRefTypeReferenceNode:
                // Resolve void through the stdlib path, same as primitives.
                try
                {
                    var voidGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                    return ResolveVoidKeyword(voidGlobalScope, symbolTable);
                }
                catch (StdlibMissingException)
                {
                    return LegacyPrimitiveTypes.Void;
                }

            case UnknownTypeRefTypeReferenceNode:
                // The user-facing `unknown` top type (unknown-type feature, Req 3.6, 7.1-7.3).
                // Always resolves to the UnknownType singleton — no stdlib lookup, no scope
                // walk. Legal in any TypeReference position at the grammar level; the storage
                // ban (Req 5), inheritance prohibition (Req 6), and member-filtering rules
                // (Req 10) are enforced later by Pass 1 checks and the Type Checker.
                return UnknownType.Instance;

            case NamedTypeRefTypeReferenceNode namedRef:
                var typeName = ExtractQualifiedName(namedRef.Name, sourceText);

                // Check if the identifier resolves to a TypeParameterType or
                // DependentTypeParameterType first. Type parameters don't accept
                // type arguments.
                var namedSymbol = fileScope.Lookup(typeName);
                if (namedSymbol is not null && namedSymbol.DeclaredType is TypeParameterType tpt)
                {
                    if (namedRef.TypeArgs is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.NotGeneric.Value,
                            DiagnosticSeverity.Error,
                            $"Type '{typeName}' is not generic and does not accept type arguments.",
                            namedRef,
                            sourceText));
                    }
                    return tpt;
                }

                if (namedSymbol is not null && namedSymbol.DeclaredType is DependentTypeParameterType dpt)
                {
                    if (namedRef.TypeArgs is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.NotGeneric.Value,
                            DiagnosticSeverity.Error,
                            $"Type '{typeName}' is not generic and does not accept type arguments.",
                            namedRef,
                            sourceText));
                    }
                    return dpt;
                }

                baseType = ResolveNamedType(typeName, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                // Handle type arguments if present.
                if (namedRef.TypeArgs is not null)
                {
                    baseType = ResolvePass2TypeArguments(baseType, typeName, namedRef.TypeArgs, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                }
                else if (baseType is TraitType tt && tt.TypeParameters.Count > 0)
                {
                    // Generic trait referenced without type arguments — emit diagnostic.
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.MissingTypeArguments.Value,
                        DiagnosticSeverity.Error,
                        $"Generic trait '{typeName}' requires type arguments.",
                        namedRef,
                        sourceText));
                }

                if (namedRef.ArrayRank.HasValue)
                {
                    return new ArrayType(baseType);
                }
                return baseType;

            case TupleTypeRefTypeReferenceNode tupleRef:
            {
                // Collect all elements: First + Rest + TupleTypeElements (additional).
                var elementCount = 2 + tupleRef.TupleTypeElements.Length;
                var elementTypes = new List<IType>(elementCount);
                var elementNames = new List<string?>(elementCount);
                var hasAnyName = false;

                // Resolve First element.
                elementTypes.Add(ResolveTypeReference(tupleRef.First.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics));
                var firstName = tupleRef.First.Name?.Lexeme;
                elementNames.Add(firstName);
                if (firstName is not null) hasAnyName = true;

                // Resolve Rest element.
                elementTypes.Add(ResolveTypeReference(tupleRef.Rest.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics));
                var restName = tupleRef.Rest.Name?.Lexeme;
                elementNames.Add(restName);
                if (restName is not null) hasAnyName = true;

                // Resolve additional elements.
                foreach (var additional in tupleRef.TupleTypeElements)
                {
                    elementTypes.Add(ResolveTypeReference(additional.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics));
                    var additionalName = additional.Name?.Lexeme;
                    elementNames.Add(additionalName);
                    if (additionalName is not null) hasAnyName = true;
                }

                return new TupleType(elementTypes, hasAnyName ? elementNames : null);
            }

            case UnionTypeRefTypeReferenceNode unionRef:
            {
                return ResolveUnionTypeRef(unionRef, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
            }

            default:
                return ErrorType.Instance;
        }
    }

    /// <summary>
    /// Resolves a <see cref="UnionTypeRefTypeReferenceNode"/> to an <see cref="IType"/>.
    /// Applies the construction rules from the design document:
    /// 1. Resolve each constituent type reference.
    /// 2. Flatten: if a constituent is itself a <see cref="UnionType"/>, inline its constituents.
    /// 3. Deduplicate: remove duplicate types by <see cref="IType.DisplayName"/> equality.
    /// 4. If only one distinct constituent remains, return that type directly (not a union).
    /// 5. Sort constituents by DisplayName for canonical form (handled by <see cref="UnionType"/> constructor).
    /// 6. If any constituent resolves to <see cref="ErrorType"/>, include it but still produce the union.
    /// </summary>
    private static IType ResolveUnionTypeRef(
        UnionTypeRefTypeReferenceNode unionRef,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Collect all constituent SingleTypeRefNodes.
        var singleTypeRefs = new List<SingleTypeRefNode>();
        singleTypeRefs.Add(unionRef.UnionType.First);
        foreach (var rest in unionRef.UnionType.SingleTypeRefs)
        {
            singleTypeRefs.Add(rest);
        }

        // Resolve each constituent and flatten nested unions.
        var flatConstituents = new List<IType>();
        foreach (var singleRef in singleTypeRefs)
        {
            var resolved = ResolveSingleTypeRef(singleRef, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

            // Flatten: if the resolved type is itself a UnionType, inline its constituents.
            if (resolved is UnionType nestedUnion)
            {
                foreach (var nested in nestedUnion.Constituents)
                {
                    flatConstituents.Add(nested);
                }
            }
            else
            {
                flatConstituents.Add(resolved);
            }
        }

        // Deduplicate by DisplayName equality.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<IType>();
        foreach (var type in flatConstituents)
        {
            if (seen.Add(type.DisplayName))
            {
                distinct.Add(type);
            }
        }

        // If only one distinct constituent remains, return that type directly.
        if (distinct.Count == 1)
        {
            // Task 6.3 / Req 12.1: Degenerate union — fewer than 2 distinct
            // types after deduplication. Emit a warning unless the reduction
            // is caused by ErrorType (unresolved constituent), which would
            // cascade from an already-reported diagnostic.
            var hasErrorType = false;
            foreach (var type in flatConstituents)
            {
                if (type is ErrorType)
                {
                    hasErrorType = true;
                    break;
                }
            }

            if (!hasErrorType && flatConstituents.Count > distinct.Count)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_TC_0075",
                    DiagnosticSeverity.Warning,
                    $"Union type has fewer than 2 distinct types after deduplication (resolved to '{distinct[0].DisplayName}'). A union type requires at least two distinct types.",
                    unionRef,
                    sourceText));
            }

            return distinct[0];
        }

        // Produce the union (UnionType constructor handles canonical sorting).
        return new UnionType(distinct);
    }

    /// <summary>
    /// Resolves a <see cref="SingleTypeRefNode"/> to an <see cref="IType"/>.
    /// Mirrors the logic of <see cref="ResolveTypeReference"/> but for the
    /// <c>SingleTypeRef</c> grammar alternatives (which exclude the union
    /// alternative to prevent infinite left-recursion).
    /// </summary>
    private static IType ResolveSingleTypeRef(
        SingleTypeRefNode singleTypeRef,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (singleTypeRef)
        {
            case PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef:
            {
                IType primBase;
                try
                {
                    var primGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                    primBase = ResolvePrimitiveKeyword(primGlobalScope, symbolTable, primitiveRef.PrimitiveType);
                }
                catch (StdlibMissingException)
                {
                    primBase = ResolveLegacyPrimitive(primitiveRef.PrimitiveType);
                }
                if (primitiveRef.ArrayRank.HasValue)
                {
                    return new ArrayType(primBase);
                }
                return primBase;
            }

            case VoidSingleTypeRefSingleTypeRefNode:
            {
                try
                {
                    var voidGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                    return ResolveVoidKeyword(voidGlobalScope, symbolTable);
                }
                catch (StdlibMissingException)
                {
                    return LegacyPrimitiveTypes.Void;
                }
            }

            case UnknownSingleTypeRefSingleTypeRefNode:
                return UnknownType.Instance;

            case NamedSingleTypeRefSingleTypeRefNode namedRef:
            {
                var typeName = ExtractQualifiedName(namedRef.Name, sourceText);

                // Check if the identifier resolves to a TypeParameterType first.
                var namedSymbol = fileScope.Lookup(typeName);
                if (namedSymbol is not null && namedSymbol.DeclaredType is TypeParameterType tpt)
                {
                    if (namedRef.TypeArgs is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.NotGeneric.Value,
                            DiagnosticSeverity.Error,
                            $"Type '{typeName}' is not generic and does not accept type arguments.",
                            namedRef,
                            sourceText));
                    }
                    return tpt;
                }

                var baseType = ResolveNamedType(typeName, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                // Handle type arguments if present.
                if (namedRef.TypeArgs is not null)
                {
                    baseType = ResolvePass2TypeArguments(baseType, typeName, namedRef.TypeArgs, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                }
                else if (baseType is TraitType tt && tt.TypeParameters.Count > 0)
                {
                    // Generic trait referenced without type arguments — emit diagnostic.
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.MissingTypeArguments.Value,
                        DiagnosticSeverity.Error,
                        $"Generic trait '{typeName}' requires type arguments.",
                        namedRef,
                        sourceText));
                }

                if (namedRef.ArrayRank.HasValue)
                {
                    return new ArrayType(baseType);
                }
                return baseType;
            }

            case ParenthesizedTypeRefSingleTypeRefNode parenRef:
            {
                // Resolve the inner type reference (which can be any TypeReference, including unions).
                var inner = ResolveTypeReference(parenRef.Inner, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                if (parenRef.ArrayRank.HasValue)
                {
                    return new ArrayType(inner);
                }
                return inner;
            }

            case TupleSingleTypeRefSingleTypeRefNode tupleRef:
            {
                var elementCount = 2 + tupleRef.TupleTypeElements.Length;
                var elementTypes = new List<IType>(elementCount);
                var elementNames = new List<string?>(elementCount);
                var hasAnyName = false;

                elementTypes.Add(ResolveTypeReference(tupleRef.First.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics));
                var firstName = tupleRef.First.Name?.Lexeme;
                elementNames.Add(firstName);
                if (firstName is not null) hasAnyName = true;

                elementTypes.Add(ResolveTypeReference(tupleRef.Rest.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics));
                var restName = tupleRef.Rest.Name?.Lexeme;
                elementNames.Add(restName);
                if (restName is not null) hasAnyName = true;

                foreach (var additional in tupleRef.TupleTypeElements)
                {
                    elementTypes.Add(ResolveTypeReference(additional.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics));
                    var additionalName = additional.Name?.Lexeme;
                    elementNames.Add(additionalName);
                    if (additionalName is not null) hasAnyName = true;
                }

                return new TupleType(elementTypes, hasAnyName ? elementNames : null);
            }

            default:
                return ErrorType.Instance;
        }
    }

    /// <summary>
    /// Maps a PrimitiveTypeNode to its well-known legacy fallback type.
    /// Used only when stdlib is not loaded (the primary path goes through
    /// ResolvePrimitiveKeyword). Post-Task 49: uses LegacyPrimitiveTypes
    /// instead of the deleted PrimitiveType singletons.
    /// </summary>
    private static IType ResolvePrimitiveType(PrimitiveTypeNode node)
    {
        return node switch
        {
            IntTypePrimitiveTypeNode => LegacyPrimitiveTypes.Int,
            UintTypePrimitiveTypeNode => LegacyPrimitiveTypes.Uint,
            LongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Long,
            UlongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ulong,
            ShortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Short,
            UshortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ushort,
            ByteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Byte,
            SbyteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Sbyte,
            FloatTypePrimitiveTypeNode => LegacyPrimitiveTypes.Float,
            DoubleTypePrimitiveTypeNode => LegacyPrimitiveTypes.Double,
            DecimalTypePrimitiveTypeNode => LegacyPrimitiveTypes.Decimal,
            BoolTypePrimitiveTypeNode => LegacyPrimitiveTypes.Bool,
            CharTypePrimitiveTypeNode => LegacyPrimitiveTypes.Char,
            StringTypePrimitiveTypeNode => LegacyPrimitiveTypes.String,
            _ => ErrorType.Instance,
        };
    }

    /// <summary>
    /// Pass-2 fallback for a primitive keyword reference when no stdlib
    /// has been loaded for the current compilation: returns the legacy
    /// fallback singleton DIRECTLY (bypassing the
    /// <see cref="PrimitiveTypeRegistry"/>-backed
    /// public property accessors). Using the legacy singletons here
    /// guarantees that a build without stdlib never embeds another
    /// parallel build's stdlib <see cref="IType"/> instances in its own
    /// expression-type map — a behaviour that would make reference-
    /// equality assertions order-dependent under parallel test
    /// execution.
    /// </summary>
    private static IType ResolveLegacyPrimitive(PrimitiveTypeNode node)
    {
        return node switch
        {
            IntTypePrimitiveTypeNode => LegacyPrimitiveTypes.Int,
            UintTypePrimitiveTypeNode => LegacyPrimitiveTypes.Uint,
            LongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Long,
            UlongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ulong,
            ShortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Short,
            UshortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ushort,
            ByteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Byte,
            SbyteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Sbyte,
            FloatTypePrimitiveTypeNode => LegacyPrimitiveTypes.Float,
            DoubleTypePrimitiveTypeNode => LegacyPrimitiveTypes.Double,
            DecimalTypePrimitiveTypeNode => LegacyPrimitiveTypes.Decimal,
            BoolTypePrimitiveTypeNode => LegacyPrimitiveTypes.Bool,
            CharTypePrimitiveTypeNode => LegacyPrimitiveTypes.Char,
            StringTypePrimitiveTypeNode => LegacyPrimitiveTypes.String,
            _ => ErrorType.Instance,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stdlib type lookup — direct, scope-chain-bypassing resolution
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a fully-qualified Standard Library type name (e.g.,
    /// <c>"Standard.Int32"</c>) by walking directly from the global scope to
    /// the target namespace scope and looking up the simple name among its
    /// members. Does <b>not</b> consult any file scope, <c>using</c>
    /// directive, or user-declared type — per Requirement 7.6 this path
    /// intentionally bypasses all ordinary name-resolution machinery so that
    /// a primitive keyword alias like <c>int</c> always reaches the stdlib
    /// type and cannot be shadowed by user code.
    ///
    /// <para>
    /// Throws <see cref="StdlibMissingException"/> (carrying diagnostic
    /// <c>MIST_STDLIB_MISSING</c>) if the namespace scope is missing (the
    /// stdlib has not been loaded into this compilation) or if the requested
    /// simple name does not resolve to a type symbol inside the namespace.
    /// </para>
    /// </summary>
    /// <param name="globalScope">
    /// The compilation's global scope (returned by the running
    /// <see cref="Build"/> invocation).
    /// </param>
    /// <param name="symbolTable">
    /// The symbol table that <see cref="FindExistingNamespaceScope"/> uses to
    /// locate namespace scopes.
    /// </param>
    /// <param name="qualifiedName">
    /// The fully-qualified stdlib type name in <c>Namespace.SimpleName</c>
    /// form (e.g., <c>"Standard.Int32"</c>).
    /// </param>
    internal static IType LookupQualifiedStdlibType(
        Scope globalScope,
        SymbolTable symbolTable,
        string qualifiedName)
    {
        // Split "Namespace.SimpleName" into its two parts. The stdlib keeps a
        // single-segment namespace ("Standard"); we intentionally split on
        // the LAST '.' so multi-segment namespaces like
        // "Standard.Collections.Generic" would still work if the stdlib ever
        // grows them.
        var lastDot = qualifiedName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= qualifiedName.Length - 1)
        {
            throw new StdlibMissingException(
                qualifiedName,
                $"MIST_STDLIB_MISSING: stdlib type name '{qualifiedName}' is not fully qualified.");
        }

        var namespaceName = qualifiedName.Substring(0, lastDot);
        var simpleName = qualifiedName.Substring(lastDot + 1);

        // Walk directly to the target namespace scope. No file scope, no
        // using-directive, no scope chain.
        var namespaceScope = FindExistingNamespaceScope(globalScope, symbolTable, namespaceName);
        if (namespaceScope is null)
        {
            throw new StdlibMissingException(
                qualifiedName,
                $"MIST_STDLIB_MISSING: Standard Library namespace '{namespaceName}' is not present in the semantic model (stdlib not loaded?).");
        }

        // Look up the simple name among the namespace's direct members.
        // `Scope.Lookup` would walk up to the parent on a miss; here we
        // iterate members directly so a miss stays a miss (we must not fall
        // through to an unrelated global-scope symbol).
        foreach (var member in namespaceScope.Members)
        {
            if (member.Name != simpleName) continue;
            if (member.Kind != SymbolKind.Class
                && member.Kind != SymbolKind.Struct
                && member.Kind != SymbolKind.Interface)
            {
                continue;
            }

            if (member.DeclaredType is IType declaredType)
            {
                return declaredType;
            }
        }

        throw new StdlibMissingException(
            qualifiedName,
            $"MIST_STDLIB_MISSING: Standard Library type '{qualifiedName}' is not present in the semantic model.");
    }

    /// <summary>
    /// Resolves a <see cref="PrimitiveTypeNode"/> (e.g., the AST node for the
    /// <c>int</c> keyword) to its corresponding Standard Library type,
    /// bypassing every scoping mechanism (file scope, <c>using</c> directives,
    /// user-declared types with matching simple names). Per Requirement 7.6
    /// this path is not overridable: the keyword <c>int</c> always resolves
    /// to the type named by <see cref="KeywordAliases.Map"/>
    /// (<c>Standard.Int32</c>), regardless of what else is in scope.
    ///
    /// <para>
    /// Task 31 of the Mist Standard Library spec flipped the primitive-
    /// type-reference path in <c>Pass2_ResolveTypes</c> to call this helper
    /// (with a fallback to the legacy <see cref="ResolvePrimitiveType"/>
    /// when no stdlib has been loaded, e.g., in unit tests that build a
    /// semantic model from a minimal AST). The fallback returns the same
    /// <see cref="IType"/> instance the compat-shim
    /// <c>PrimitiveType.Int</c> property would return (Task 32), so
    /// reference equality holds regardless of which path runs.
    /// </para>
    /// </summary>
    /// <param name="globalScope">The compilation's global scope.</param>
    /// <param name="symbolTable">The compilation's symbol table.</param>
    /// <param name="keyword">The primitive-keyword AST node to resolve.</param>
    internal static IType ResolvePrimitiveKeyword(
        Scope globalScope,
        SymbolTable symbolTable,
        PrimitiveTypeNode keyword)
    {
        // Extract the keyword lexeme (e.g., "int") using the existing
        // PrimitiveTypeNode → string mapping.
        var keywordText = FormatPrimitiveType(keyword);

        // Look it up in the single-source-of-truth Keyword_Alias_Mapping
        // (Requirement 7.5, Task 7).
        if (!KeywordAliases.Map.TryGetValue(keywordText, out var qualifiedName))
        {
            throw new StdlibMissingException(
                keywordText,
                $"MIST_STDLIB_MISSING: primitive keyword '{keywordText}' has no entry in KeywordAliases.Map.");
        }

        // Resolve the fully-qualified name directly against the stdlib
        // namespace — never through any scope chain or using-directive.
        return LookupQualifiedStdlibType(globalScope, symbolTable, qualifiedName);
    }

    /// <summary>
    /// Resolves the <c>void</c> keyword to its Standard Library type
    /// (<c>Standard.Void</c>), following the same pattern as
    /// <see cref="ResolvePrimitiveKeyword"/>. Bypasses all scoping.
    /// </summary>
    internal static IType ResolveVoidKeyword(Scope globalScope, SymbolTable symbolTable)
    {
        return LookupQualifiedStdlibType(globalScope, symbolTable, "Standard.Void");
    }

    /// <summary>
    /// Resolves the metatype <c>Type</c> to its Standard Library type
    /// (<c>Standard.Type</c>), following the same pattern as
    /// <see cref="ResolvePrimitiveKeyword"/>. Bypasses all scoping.
    /// </summary>
    internal static IType ResolveTypeKeyword(Scope globalScope, SymbolTable symbolTable)
    {
        return LookupQualifiedStdlibType(globalScope, symbolTable, "Standard.Type");
    }

    // (Task 49: PopulatePrimitiveTypeRegistry has been deleted along with
    // PrimitiveTypeRegistry. Stdlib types are resolved directly via
    // ResolvePrimitiveKeyword in Pass 2.)

    /// <summary>
    /// Resolves a named type by searching the scope chain and imported namespaces.
    /// </summary>
    private static IType ResolveNamedType(
        string typeName,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // 1. Check well-known primitive types by name.
        var wellKnown = TryResolveWellKnownType(typeName);
        if (wellKnown is not null) return wellKnown;

        // 2. Search current scope → parent scopes (local → file → namespace → global).
        var symbol = fileScope.Lookup(typeName);
        if (symbol is not null && (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait || symbol.Kind == SymbolKind.TypeAlias || symbol.Kind == SymbolKind.Enum || symbol.Kind == SymbolKind.Newtype))
        {
            // For type aliases that haven't been resolved yet, trigger
            // recursive resolution so that cycle detection via
            // _typeAliasResolutionStack can fire (Requirement 5.4).
            if (symbol.Kind == SymbolKind.TypeAlias && symbol.DeclaredType is null)
            {
                var aliasNode = symbolTable.GetDeclaringNode(symbol) as TypeAliasDeclarationNode;
                if (aliasNode is not null)
                {
                    ResolveTypeAliasDeclaration(aliasNode, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                }
            }
            // For newtypes that haven't been resolved yet, trigger resolution
            // so that forward references to newtypes work (newtypes declared
            // after the referencing class in the same namespace body).
            if (symbol.Kind == SymbolKind.Newtype && symbol.DeclaredType is null)
            {
                var newtypeNode = symbolTable.GetDeclaringNode(symbol) as NewtypeDeclarationNode;
                if (newtypeNode is not null)
                {
                    ResolveNewtypeDeclaration(newtypeNode, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                }
            }
            return symbol.DeclaredType ?? ErrorType.Instance;
        }

        // 3. Search imported namespaces from using declarations.
        var importedNamespaces = CollectImportedNamespaces(fileScope);

        // 3a. Search user-defined namespace scopes for the type.
        IType? userResolvedType = null;
        string? userResolvedNs = null;
        string? ambiguousNs = null;

        // Find the global scope by walking up from fileScope.
        var globalScope = FindGlobalScope(fileScope);

        foreach (var ns in importedNamespaces)
        {
            // Search user-defined namespace scopes.
            var nsScope = globalScope is not null
                ? FindExistingNamespaceScope(globalScope, symbolTable, ns)
                : null;

            if (nsScope is not null)
            {
                foreach (var member in nsScope.Members)
                {
                    if (member.Name == typeName &&
                        (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct || member.Kind == SymbolKind.Interface || member.Kind == SymbolKind.Trait || member.Kind == SymbolKind.TypeAlias || member.Kind == SymbolKind.Enum || member.Kind == SymbolKind.Newtype))
                    {
                        if (userResolvedType is not null)
                        {
                            // Ambiguity: same type name found in multiple imported namespaces.
                            ambiguousNs = ns;
                        }
                        else
                        {
                            userResolvedType = member.DeclaredType ?? ErrorType.Instance;
                            userResolvedNs = ns;
                        }
                        break;
                    }
                }
            }
        }

        if (ambiguousNs is not null && userResolvedNs is not null)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_AMBIGUOUS_TYPE"),
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"The type name '{typeName}' is ambiguous between namespaces '{userResolvedNs}' and '{ambiguousNs}'.",
                Location: default,
                CompilationUnitPath: sourceText?.Path ?? "<unknown>",
                AnalyzerName: null));
            return ErrorType.Instance;
        }

        if (userResolvedType is not null)
        {
            return userResolvedType;
        }

        // 3b. Search ambient namespaces via ambient resolver.
        if (ambientTypeResolver is not null)
        {
            foreach (var ns in importedNamespaces)
            {
                // Try ambient resolver for each imported namespace.
                var resolvedType = ambientTypeResolver.ResolveType(ns, typeName);
                if (resolvedType is not null)
                {
                    // Register the resolved ambient type in the scope hierarchy.
                    RegisterAmbientTypeInScope(resolvedType, typeName, ns, fileScope, ambientTypeResolver, symbolTable);
                    return resolvedType;
                }
            }
        }

        // 4. Not found → emit diagnostic, return ErrorType.
        diagnostics.Add(new Diagnostic(
            new DiagnosticId("MIST_UNDEFINED_TYPE"),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            $"The type '{typeName}' could not be found.",
            Location: default,
            CompilationUnitPath: sourceText?.Path ?? "<unknown>",
            AnalyzerName: null));

        return ErrorType.Instance;
    }

    /// <summary>
    /// Finds the global scope by walking up the parent chain from the given scope.
    /// The global scope is the one with no parent.
    /// </summary>
    private static Scope? FindGlobalScope(Scope scope)
    {
        IScope? current = scope;
        while (current is not null)
        {
            if (current is Scope s && s.Parent is null)
                return s;
            current = current.Parent;
        }
        return scope; // fallback
    }

    /// <summary>
    /// Finds the global scope by walking up the parent chain from an <see cref="IScope"/>.
    /// Used in Pass 1b constraint resolution where only an IScope is available.
    /// </summary>
    private static Scope? FindGlobalScopeFromIScope(IScope scope)
    {
        IScope? current = scope;
        while (current is not null)
        {
            if (current is Scope s && s.Parent is null)
                return s;
            current = current.Parent;
        }
        return scope as Scope;
    }

    /// <summary>
    /// Substitutes type parameters in a type with the corresponding type arguments.
    /// If the type is a <see cref="TypeParameterType"/> that matches one of the
    /// type parameters, returns the corresponding type argument. Otherwise returns
    /// the type unchanged. Handles constructed types recursively.
    /// </summary>
    private static IType SubstituteTypeParameters(
        IType type,
        IReadOnlyList<IType> typeParameters,
        IReadOnlyList<IType> typeArguments)
    {
        if (type is TypeParameterType tpt)
        {
            for (var i = 0; i < typeParameters.Count; i++)
            {
                if (ReferenceEquals(tpt, typeParameters[i]))
                    return typeArguments[i];
            }
            // Type parameter not in the substitution list — return as-is.
            return type;
        }

        if (type is ConstructedType ct)
        {
            var substitutedArgs = new List<IType>(ct.TypeArguments.Count);
            var anyChanged = false;
            foreach (var arg in ct.TypeArguments)
            {
                var substituted = SubstituteTypeParameters(arg, typeParameters, typeArguments);
                substitutedArgs.Add(substituted);
                if (!ReferenceEquals(substituted, arg))
                    anyChanged = true;
            }
            if (anyChanged)
                return new ConstructedType(ct.GenericDefinition, substitutedArgs);
            return type;
        }

        if (type is ArrayType arrayType)
        {
            var substitutedElement = SubstituteTypeParameters(arrayType.ElementType, typeParameters, typeArguments);
            if (!ReferenceEquals(substitutedElement, arrayType.ElementType))
                return new ArrayType(substitutedElement);
            return type;
        }

        return type;
    }

    /// <summary>
    /// Resolves type arguments on a type reference in Pass 2 context.
    /// Validates arity and emits diagnostics for mismatches and non-generic types.
    /// Returns a <see cref="ConstructedType"/> if the type is generic with matching arity,
    /// or the original type with diagnostics if there's an error.
    /// For type aliases, emits <c>MIST_SB_0063</c> on arity mismatch and returns
    /// the underlying type (transparent resolution).
    /// </summary>
    private static IType ResolvePass2TypeArguments(
        IType resolvedType,
        string typeName,
        TypeArgumentListNode typeArgList,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // If the resolved type is unknown, don't cascade errors.
        if (resolvedType is ErrorType)
            return resolvedType;

        var typeParams = GetTypeParameters(resolvedType);
        if (typeParams.Count == 0)
        {
            // The type is not generic — emit MIST_NOT_GENERIC.
            diagnostics.Add(MakeDiagnosticWithLocation(
                DiagnosticId.NotGeneric.Value,
                DiagnosticSeverity.Error,
                $"Type '{typeName}' is not generic and does not accept type arguments.",
                typeArgList,
                sourceText));
            return resolvedType;
        }

        // Resolve each type argument, handling dependent type argument disambiguation.
        var (resolvedTypeArgs, dependentArgs) = ResolvePass2TypeArgumentListWithDependents(
            typeArgList, typeParams, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        if (resolvedTypeArgs.Count != typeParams.Count)
        {
            // For type aliases, emit MIST_SB_0063 (Requirement 5.3).
            if (resolvedType is TypeAliasType aliasType)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_SB_0063",
                    DiagnosticSeverity.Error,
                    $"Type alias '{aliasType.Name}' expects {typeParams.Count} type argument(s) but was given {resolvedTypeArgs.Count}.",
                    typeArgList,
                    sourceText));
                return ErrorType.Instance;
            }

            // Arity mismatch — emit MIST_ARITY_MISMATCH.
            diagnostics.Add(MakeDiagnosticWithLocation(
                DiagnosticId.ArityMismatch.Value,
                DiagnosticSeverity.Error,
                $"Type '{typeName}' expects {typeParams.Count} type argument(s) but received {resolvedTypeArgs.Count}.",
                typeArgList,
                sourceText));
            return ErrorType.Instance;
        }

        // For type aliases, substitute type arguments into the underlying type
        // and return the result (transparent resolution). This ensures that
        // `Box<int>` where `type Box<T> = GenericBox<T>` resolves to
        // `GenericBox<int>` (a ConstructedType with concrete type arguments).
        if (resolvedType is TypeAliasType alias)
        {
            var underlying = alias.UnderlyingType;
            // Substitute each type parameter with the corresponding type argument.
            return SubstituteTypeInMember(underlying, alias.TypeParameters, resolvedTypeArgs);
        }

        // For generic traits, construct a ConstructedTraitType (analogous to
        // ConstructedType for classes/structs/interfaces).
        if (resolvedType is TraitType traitType)
        {
            return new ConstructedTraitType(traitType, resolvedTypeArgs);
        }

        return new ConstructedType(resolvedType, resolvedTypeArgs, dependentArgs);
    }

    /// <summary>
    /// Resolves a <see cref="TypeArgumentListNode"/> to a list of <see cref="IType"/> instances
    /// in Pass 2 context. Each type argument is resolved using the standard
    /// <see cref="ResolveTypeReference"/> method, supporting nested generics.
    /// Emits <c>MIST_UNDEFINED_TYPE_ARGUMENT</c> for unresolvable type arguments.
    /// </summary>
    private static IReadOnlyList<IType> ResolvePass2TypeArgumentList(
        TypeArgumentListNode typeArgList,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var resolvedArgs = new List<IType>();

        // Resolve the first type argument.
        var firstType = ResolvePass2SingleTypeArg(typeArgList.GetTypeReference(), fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        resolvedArgs.Add(firstType);

        // Resolve the rest of the type arguments.
        foreach (var typeRef in typeArgList.GetTypeReferences())
        {
            var argType = ResolvePass2SingleTypeArg(typeRef, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
            resolvedArgs.Add(argType);
        }

        return resolvedArgs;
    }

    /// <summary>
    /// Resolves a <see cref="TypeArgumentListNode"/> to a list of <see cref="IType"/> instances
    /// and a parallel list of <see cref="DependentValueArgument"/> entries, performing
    /// disambiguation of bare identifiers and literal type arguments against the
    /// corresponding type parameters.
    /// <para>
    /// When the type parameter at a given position is a <see cref="DependentTypeParameterType"/>:
    /// <list type="bullet">
    ///   <item>Literal type arguments (integer, boolean, char, hex, binary, negated) are parsed
    ///         and converted to <see cref="DependentValueArgument"/> instances.</item>
    ///   <item>A bare identifier (<see cref="TypeReferenceTypeArgumentTypeArgumentNode"/> with a
    ///         simple <see cref="NamedTypeRefTypeReferenceNode"/>) is looked up in scope:
    ///         if it resolves to a const/dependent-parameter → dependent value argument;
    ///         if it resolves to a type → regular type argument (type mismatch handled later by type checker);
    ///         if unresolved → diagnostic emitted.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static (IReadOnlyList<IType> TypeArgs, IReadOnlyList<DependentValueArgument?>? DependentArgs) ResolvePass2TypeArgumentListWithDependents(
        TypeArgumentListNode typeArgList,
        IReadOnlyList<IType> typeParams,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Collect all TypeArgumentNode entries in order.
        var argNodes = new List<TypeArgumentNode> { typeArgList.TypeArgument };
        foreach (var extra in typeArgList.TypeArguments)
            argNodes.Add(extra);

        var resolvedTypeArgs = new List<IType>(argNodes.Count);
        List<DependentValueArgument?>? dependentArgs = null;

        for (var i = 0; i < argNodes.Count; i++)
        {
            var argNode = argNodes[i];
            var correspondingParam = i < typeParams.Count ? typeParams[i] : null;
            var isDependentPosition = correspondingParam is DependentTypeParameterType;

            // Try to resolve as a dependent value argument if the position expects one.
            if (isDependentPosition)
            {
                var depParam = (DependentTypeParameterType)correspondingParam!;
                var depArg = TryResolveDependentValueArgument(argNode, depParam, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                if (depArg is not null)
                {
                    // Successfully resolved as a dependent value argument.
                    // Use the dependent parameter's value type as the type argument placeholder.
                    resolvedTypeArgs.Add(depParam.ValueType);
                    dependentArgs ??= new List<DependentValueArgument?>(new DependentValueArgument?[argNodes.Count]);
                    dependentArgs[i] = depArg;
                    continue;
                }
                // If TryResolveDependentValueArgument returned null, it means the argument
                // is a type reference (not a const/literal). Fall through to normal resolution.
            }

            // Normal type argument resolution (for regular type parameters or when
            // a dependent position has a type reference that isn't a const).
            if (argNode is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg)
            {
                var resolved = ResolvePass2SingleTypeArg(typeRefArg.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                resolvedTypeArgs.Add(resolved);
            }
            else
            {
                // A literal type argument in a non-dependent position — this is an error
                // that will be caught by the type checker later. For now, use ErrorType.
                resolvedTypeArgs.Add(ErrorType.Instance);
            }
        }

        return (resolvedTypeArgs, dependentArgs);
    }

    /// <summary>
    /// Attempts to resolve a <see cref="TypeArgumentNode"/> as a <see cref="DependentValueArgument"/>
    /// for a dependent type parameter position. Returns <c>null</c> if the argument is a type
    /// reference (not a const or literal), indicating normal type argument resolution should proceed.
    /// </summary>
    private static DependentValueArgument? TryResolveDependentValueArgument(
        TypeArgumentNode argNode,
        DependentTypeParameterType depParam,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (argNode)
        {
            case IntegerLiteralTypeArgumentTypeArgumentNode intLit:
            {
                var valueText = intLit.Literal.Lexeme;
                if (long.TryParse(valueText, out var value))
                    return new DependentValueArgument(value, depParam.ValueType, valueText);
                return new DependentValueArgument(0L, depParam.ValueType, valueText);
            }

            case NegatedIntegerTypeArgumentTypeArgumentNode negLit:
            {
                var valueText = negLit.Literal.Lexeme;
                if (long.TryParse(valueText, out var value))
                    return new DependentValueArgument(-value, depParam.ValueType, $"-{valueText}");
                return new DependentValueArgument(0L, depParam.ValueType, $"-{valueText}");
            }

            case HexLiteralTypeArgumentTypeArgumentNode hexLit:
            {
                var valueText = hexLit.Literal.Lexeme;
                var hexStr = valueText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? valueText[2..] : valueText;
                if (long.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out var value))
                    return new DependentValueArgument(value, depParam.ValueType, valueText);
                return new DependentValueArgument(0L, depParam.ValueType, valueText);
            }

            case BinaryLiteralTypeArgumentTypeArgumentNode binLit:
            {
                var valueText = binLit.Literal.Lexeme;
                var binStr = valueText.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
                    ? valueText[2..] : valueText;
                try
                {
                    var value = Convert.ToInt64(binStr, 2);
                    return new DependentValueArgument(value, depParam.ValueType, valueText);
                }
                catch
                {
                    return new DependentValueArgument(0L, depParam.ValueType, valueText);
                }
            }

            case TrueLiteralTypeArgumentTypeArgumentNode:
                return new DependentValueArgument(true, depParam.ValueType, "true");

            case FalseLiteralTypeArgumentTypeArgumentNode:
                return new DependentValueArgument(false, depParam.ValueType, "false");

            case CharLiteralTypeArgumentTypeArgumentNode charLit:
            {
                var valueText = charLit.Literal.Lexeme;
                // Strip surrounding quotes to get the char value.
                var charValue = valueText.Length >= 3 ? valueText[1] : '\0';
                return new DependentValueArgument(charValue, depParam.ValueType, valueText);
            }

            case TypeReferenceTypeArgumentTypeArgumentNode typeRefArg:
            {
                // Disambiguation: check if this is a bare identifier that resolves to a const.
                if (typeRefArg.Type is NamedTypeRefTypeReferenceNode namedRef
                    && namedRef.TypeArgs is null
                    && !namedRef.ArrayRank.HasValue)
                {
                    var argName = ExtractQualifiedName(namedRef.Name, sourceText);

                    // Don't treat well-known type names as const references.
                    if (TryResolveWellKnownType(argName) is not null)
                        return null; // It's a type, not a const.

                    // Look up in scope to disambiguate.
                    var symbol = fileScope.Lookup(argName);

                    if (symbol is not null)
                    {
                        // If the symbol's declared type is a DependentTypeParameterType,
                        // it's a forwarded dependent parameter (e.g., class Derived<N:int> : Base<N>).
                        if (symbol.DeclaredType is DependentTypeParameterType forwardedParam)
                        {
                            return new DependentValueArgument(argName, forwardedParam.ValueType, argName);
                        }

                        // If the symbol is a type (class, struct, interface, etc.), treat as regular type arg.
                        if (symbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                            or SymbolKind.Trait or SymbolKind.TypeAlias or SymbolKind.Enum)
                        {
                            return null; // Fall through to normal type resolution.
                        }

                        // If the symbol is a property with a DependentTypeParameterType value type
                        // (generated implicit property for dependent parameters), treat as const ref.
                        if (symbol.Kind == SymbolKind.Property
                            && symbol is Symbol propSym
                            && propSym.PropertyMetadata is { IsStatic: true, HasGet: true, HasSet: false })
                        {
                            // This is likely a dependent parameter property — treat as forwarded.
                            return new DependentValueArgument(argName, symbol.DeclaredType ?? depParam.ValueType, argName);
                        }

                        // If the symbol is a field or local variable (potential const),
                        // treat as a dependent value argument reference.
                        if (symbol.Kind is SymbolKind.Field or SymbolKind.LocalVariable)
                        {
                            return new DependentValueArgument(argName, symbol.DeclaredType ?? depParam.ValueType, argName);
                        }
                    }

                    // Check if it resolves to a type via namespace imports before emitting diagnostic.
                    var importedNamespaces = CollectImportedNamespaces(fileScope);
                    var globalScope = FindGlobalScope(fileScope);
                    foreach (var ns in importedNamespaces)
                    {
                        var nsScope = globalScope is not null
                            ? FindExistingNamespaceScope(globalScope, symbolTable, ns)
                            : null;
                        if (nsScope is not null)
                        {
                            foreach (var member in nsScope.Members)
                            {
                                if (member.Name == argName &&
                                    member.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                                        or SymbolKind.Trait or SymbolKind.TypeAlias or SymbolKind.Enum)
                                {
                                    return null; // It's a type from an imported namespace.
                                }
                            }
                        }
                    }

                    // Check ambient resolver.
                    if (ambientTypeResolver is not null)
                    {
                        foreach (var ns in importedNamespaces)
                        {
                            if (ambientTypeResolver.ResolveType(ns, argName) is not null)
                                return null; // It's an ambient type.
                        }
                    }

                    // Unresolved identifier in a dependent position — emit diagnostic.
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.UndefinedTypeArgument.Value,
                        DiagnosticSeverity.Error,
                        $"Type argument '{argName}' could not be resolved.",
                        typeRefArg,
                        sourceText));
                    return new DependentValueArgument(argName, depParam.ValueType, argName);
                }

                // Not a bare identifier (has type args or array rank) — treat as type reference.
                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves a single type argument in Pass 2 context.
    /// Uses <see cref="ResolveTypeReference"/> for full recursive resolution.
    /// Emits <c>MIST_UNDEFINED_TYPE_ARGUMENT</c> if the result is <see cref="ErrorType"/>
    /// and the type reference is a named type (not a primitive or void).
    /// </summary>
    private static IType ResolvePass2SingleTypeArg(
        TypeReferenceNode typeRef,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // For named type references, check if the name can be resolved before
        // calling the full ResolveTypeReference (which emits MIST_UNDEFINED_TYPE).
        // If it can't be resolved, emit MIST_UNDEFINED_TYPE_ARGUMENT instead.
        if (typeRef is NamedTypeRefTypeReferenceNode namedRef)
        {
            var argName = ExtractQualifiedName(namedRef.Name, sourceText);

            // Check if the name resolves to anything.
            var wellKnown = TryResolveWellKnownType(argName);
            if (wellKnown is null)
            {
                var symbol = fileScope.Lookup(argName);
                if (symbol is null || (symbol.Kind != SymbolKind.Class && symbol.Kind != SymbolKind.Struct && symbol.Kind != SymbolKind.Interface && symbol.Kind != SymbolKind.Trait && symbol.Kind != SymbolKind.TypeAlias && symbol.Kind != SymbolKind.Enum))
                {
                    // Check user-defined namespace scopes first.
                    var importedNamespaces = CollectImportedNamespaces(fileScope);
                    var globalScope = FindGlobalScope(fileScope);
                    var userResolved = false;

                    foreach (var ns in importedNamespaces)
                    {
                        var nsScope = globalScope is not null
                            ? FindExistingNamespaceScope(globalScope, symbolTable, ns)
                            : null;

                        if (nsScope is not null)
                        {
                            foreach (var member in nsScope.Members)
                            {
                                if (member.Name == argName &&
                                    (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct || member.Kind == SymbolKind.Interface || member.Kind == SymbolKind.Trait || member.Kind == SymbolKind.TypeAlias || member.Kind == SymbolKind.Enum))
                                {
                                    userResolved = true;
                                    break;
                                }
                            }
                            if (userResolved) break;
                        }
                    }

                    // Check ambient resolver.
                    IType? ambientResolved = null;
                    if (!userResolved && ambientTypeResolver is not null)
                    {
                        foreach (var ns in importedNamespaces)
                        {
                            ambientResolved = ambientTypeResolver.ResolveType(ns, argName);
                            if (ambientResolved is not null)
                                break;
                        }
                    }

                    if (!userResolved && ambientResolved is null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.UndefinedTypeArgument.Value,
                            DiagnosticSeverity.Error,
                            $"Type argument '{argName}' could not be resolved.",
                            namedRef,
                            sourceText));
                        return ErrorType.Instance;
                    }
                }
            }
        }

        // Use the standard type reference resolution for full recursive support.
        return ResolveTypeReference(typeRef, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
    }

    /// <summary>
    /// Tries to resolve a well-known type by name (primitives, string, void).
    /// </summary>
    private static IType? TryResolveWellKnownType(string name)
    {
        return name switch
        {
            "int" => LegacyPrimitiveTypes.Int,
            "uint" => LegacyPrimitiveTypes.Uint,
            "long" => LegacyPrimitiveTypes.Long,
            "ulong" => LegacyPrimitiveTypes.Ulong,
            "short" => LegacyPrimitiveTypes.Short,
            "ushort" => LegacyPrimitiveTypes.Ushort,
            "byte" => LegacyPrimitiveTypes.Byte,
            "sbyte" => LegacyPrimitiveTypes.Sbyte,
            "float" => LegacyPrimitiveTypes.Float,
            "double" => LegacyPrimitiveTypes.Double,
            "decimal" => LegacyPrimitiveTypes.Decimal,
            "bool" => LegacyPrimitiveTypes.Bool,
            "char" => LegacyPrimitiveTypes.Char,
            "string" => LegacyPrimitiveTypes.String,
            "void" => LegacyPrimitiveTypes.Void,
            "Void" => LegacyPrimitiveTypes.Void,
            _ => null,
        };
    }

    /// <summary>
    /// Creates a new scope that layers the type parameters from <paramref name="typeScope"/>
    /// on top of <paramref name="fileScope"/>. This ensures that type parameter names
    /// resolve to the correct TypeParameterType instance for the current generic
    /// declaration, without polluting the shared file scope with type parameters
    /// from multiple generic types.
    /// </summary>
    private static Scope CreateTypeParameterScope(Scope typeScope, Scope fileScope)
    {
        var hasTypeParams = false;
        foreach (var member in typeScope.Members)
        {
            if (member.DeclaredType is TypeParameterType or DependentTypeParameterType)
            {
                hasTypeParams = true;
                break;
            }
        }

        if (!hasTypeParams)
            return fileScope;

        var scope = new Scope(parent: fileScope);
        foreach (var member in typeScope.Members)
        {
            if (member.DeclaredType is TypeParameterType or DependentTypeParameterType)
            {
                scope.AddMember(member);
            }
        }
        return scope;
    }

    /// <summary>
    /// Collects all imported namespaces from the scope chain.
    /// </summary>
    private static List<string> CollectImportedNamespaces(Scope scope)
    {
        var namespaces = new List<string>();
        IScope? current = scope;
        while (current is not null)
        {
            if (current is Scope s)
            {
                foreach (var ns in s.ImportedNamespaces)
                {
                    if (!namespaces.Contains(ns))
                    {
                        namespaces.Add(ns);
                    }
                }
            }
            current = current.Parent;
        }
        return namespaces;
    }

    /// <summary>
    /// Registers an ambient type and its members in the scope hierarchy.
    /// </summary>
    private static void RegisterAmbientTypeInScope(
        IType type,
        string typeName,
        string namespaceName,
        Scope fileScope,
        IAmbientTypeResolver ambientTypeResolver,
        SymbolTable symbolTable)
    {
        // Check if already registered in the scope.
        var existing = fileScope.Lookup(typeName);
        if (existing is not null && (existing.Kind == SymbolKind.Class || existing.Kind == SymbolKind.Struct || existing.Kind == SymbolKind.Interface || existing.Kind == SymbolKind.Trait))
        {
            return; // Already registered.
        }

        // Create a type symbol with SymbolOrigin.Backend.
        var kind = type is ClassType ? SymbolKind.Class : type is InterfaceType ? SymbolKind.Interface : type is TraitType ? SymbolKind.Trait : SymbolKind.Struct;
        var typeSymbol = new Symbol(typeName, kind, SymbolOrigin.Backend, fileScope, type, declarationSpan: null);
        fileScope.AddMember(typeSymbol);

        // Register members in the type's member scope.
        var members = ambientTypeResolver.GetMembers(type);
        if (type is ClassType ct && ct.MemberScope is Scope memberScope)
        {
            foreach (var member in members)
            {
                // Only add if not already present.
                if (memberScope.Lookup(member.Name) is null)
                {
                    memberScope.AddMember(member);
                }
            }
        }
        else if (type is StructType st && st.MemberScope is Scope structMemberScope)
        {
            foreach (var member in members)
            {
                if (structMemberScope.Lookup(member.Name) is null)
                {
                    structMemberScope.AddMember(member);
                }
            }
        }
    }

    // ─── Expression Resolution ───────────────────────────────────────────

    /// <summary>
    /// Resolves the type of an expression and records it in the expression type map.
    /// Returns the resolved type.
    /// </summary>
    private static IType ResolveExpression(
        ExpressionNode expr,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // ExpressionNode wraps AssignmentNode.
        var type = ResolveAssignment(expr.Assignment, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        expressionTypes[expr] = type;
        return type;
    }

    private static IType ResolveAssignment(
        AssignmentNode assignment,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var targetType = ResolveLogicalOr(assignment.Target, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (assignment.Value is not null)
        {
            // This is an assignment expression: target = value.
            var valueType = ResolveAssignment(assignment.Value, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            expressionTypes[assignment] = targetType;
            return targetType;
        }

        expressionTypes[assignment] = targetType;
        return targetType;
    }

    private static IType ResolveLogicalOr(
        LogicalOrNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveLogicalAnd(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ResolveLogicalAnd(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveLogicalAnd(
        LogicalAndNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveEquality(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ResolveEquality(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveEquality(
        EqualityNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveRelational(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ResolveRelational(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveRelational(
        RelationalNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case RelationalComparisonRelationalNode cmp:
            {
                var leftType = ResolveAdditive(cmp.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                if (cmp.Right is not null && !ReferenceEquals(cmp.Right, cmp.Left))
                {
                    ResolveAdditive(cmp.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
                    expressionTypes[node] = result;
                    return result;
                }

                expressionTypes[node] = leftType;
                return leftType;
            }

            case IsExpressionRelationalNode isExpr:
            {
                // Resolve the left-hand side (scrutinee) additive expression.
                var scrutineeType = ResolveAdditive(isExpr.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                // Resolve type references and constant expressions within the pattern.
                ResolvePattern(isExpr.Pattern, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                // Record bool as the expression type — an `is` expression always evaluates to bool.
                var boolType = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
                expressionTypes[node] = boolType;
                return boolType;
            }

            default:
            {
                expressionTypes[node] = ErrorType.Instance;
                return ErrorType.Instance;
            }
        }
    }

    // ─── Pattern Resolution ─────────────────────────────────────────────
    // Recursively resolves type references within patterns (e.g., the type
    // in a type pattern, the type prefix in a property pattern) and records
    // expression types for constant sub-expressions.

    private static void ResolvePattern(
        PatternNode pattern,
        IType scrutineeType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        ResolveOrPattern(pattern.OrPattern, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    private static void ResolveOrPattern(
        OrPatternNode orPattern,
        IType scrutineeType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        ResolveAndPattern(orPattern.Left, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            ResolveAndPattern(orPattern.Right, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveAndPattern(
        AndPatternNode andPattern,
        IType scrutineeType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        ResolveUnaryPattern(andPattern.Left, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            ResolveUnaryPattern(andPattern.Right, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveUnaryPattern(
        UnaryPatternNode unaryPattern,
        IType scrutineeType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
                ResolvePrimaryPattern(notPattern.Operand, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case DirectPatternUnaryPatternNode directPattern:
                ResolvePrimaryPattern(directPattern.Primary, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
        }
    }

    private static void ResolvePrimaryPattern(
        PrimaryPatternNode primaryPattern,
        IType scrutineeType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                // Resolve the type reference to get the pattern type.
                ResolveTypeReference(typePattern.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                break;
            }

            case BareTypePatternPrimaryPatternNode bareTypePattern:
            {
                // Bare type pattern: resolve the type reference (no binding name).
                // Store the resolved type in the expression-type map so the
                // type checker can retrieve it via GetTypeOf(bareTypePattern.Type).
                //
                // Special case: if the type reference is a qualified name like
                // EnumName.MemberName, ResolveTypeReference will fail (it's not a type).
                // We detect this and resolve it as an enum member access pattern instead.
                if (bareTypePattern.Type is NamedTypeRefTypeReferenceNode namedPatternRef)
                {
                    var qualifiedName = ExtractQualifiedName(namedPatternRef.Name, sourceText);
                    var dotIndex = qualifiedName.LastIndexOf('.');
                    if (dotIndex > 0)
                    {
                        // Qualified name with a dot — try to resolve as EnumName.MemberName.
                        var enumName = qualifiedName.Substring(0, dotIndex);
                        var memberName = qualifiedName.Substring(dotIndex + 1);
                        var enumSymbol = fileScope.Lookup(enumName);
                        if (enumSymbol is not null && enumSymbol.Kind == SymbolKind.Enum && enumSymbol.DeclaredType is EnumType enumType)
                        {
                            var enumMember = enumType.MemberScope.Lookup(memberName);
                            if (enumMember is not null)
                            {
                                // Valid enum member pattern — store the EnumType.
                                expressionTypes[bareTypePattern.Type] = enumType;
                                break;
                            }
                        }
                    }
                }

                var resolvedType = ResolveTypeReference(bareTypePattern.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                expressionTypes[bareTypePattern.Type] = resolvedType;
                break;
            }

            case VarPatternPrimaryPatternNode:
            {
                // Var pattern: the type is the scrutinee type. No resolution needed.
                break;
            }

            case ConstantPatternPrimaryPatternNode constantPattern:
            {
                // Resolve the constant literal to record its type.
                ResolveConstantLiteral(constantPattern.Literal, fileScope, symbolTable, expressionTypes);
                break;
            }

            case LessThanRelationalPatternPrimaryPatternNode ltPattern:
            {
                // Resolve the constant expression.
                ResolveExpression(ltPattern.Constant, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }

            case GreaterThanRelationalPatternPrimaryPatternNode gtPattern:
            {
                ResolveExpression(gtPattern.Constant, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }

            case LessThanOrEqualRelationalPatternPrimaryPatternNode ltePattern:
            {
                ResolveExpression(ltePattern.Constant, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }

            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gtePattern:
            {
                ResolveExpression(gtePattern.Constant, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }

            case PropertyPatternPrimaryPatternNode propertyPattern:
            {
                // If there's a type name prefix, resolve it.
                var patternType = scrutineeType;
                if (propertyPattern.TypeName is not null)
                {
                    var typeName = ExtractQualifiedName(propertyPattern.TypeName, sourceText);
                    patternType = ResolveNamedType(typeName, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                }

                // Recursively resolve subpatterns.
                if (propertyPattern.Subpatterns is not null)
                {
                    ResolvePropertySubpatternList(propertyPattern.Subpatterns, patternType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }
                break;
            }

            case ListPatternPrimaryPatternNode listPattern:
            {
                // Determine the element type: if the scrutinee is an array, use its element type.
                var elementType = scrutineeType is ArrayType arrayType
                    ? arrayType.ElementType
                    : ErrorType.Instance;

                // Recursively resolve element patterns.
                if (listPattern.Elements is not null)
                {
                    ResolveListPatternElementList(listPattern.Elements, elementType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }
                break;
            }

            case ParenthesizedPatternPrimaryPatternNode parenPattern:
            {
                // Recursively resolve the inner pattern.
                ResolvePattern(parenPattern.Inner, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }
        }
    }

    /// <summary>
    /// Resolves a constant literal within a pattern, recording its type in the expression types map.
    /// </summary>
    private static void ResolveConstantLiteral(
        ConstantLiteralNode literal,
        Scope fileScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes)
    {
        IType type = literal switch
        {
            IntegerConstantConstantLiteralNode intConst =>
                ResolveIntegerLiteralType(intConst.Value.Lexeme, fileScope, symbolTable),
            FloatConstantConstantLiteralNode floatConst =>
                ResolveFloatLiteralType(floatConst.Value.Lexeme, fileScope, symbolTable),
            StringConstantConstantLiteralNode =>
                ResolvePrimitiveByKeyword("string", fileScope, symbolTable),
            CharConstantConstantLiteralNode =>
                ResolvePrimitiveByKeyword("char", fileScope, symbolTable),
            TrueConstantConstantLiteralNode =>
                ResolvePrimitiveByKeyword("bool", fileScope, symbolTable),
            FalseConstantConstantLiteralNode =>
                ResolvePrimitiveByKeyword("bool", fileScope, symbolTable),
            _ => ErrorType.Instance,
        };

        expressionTypes[literal] = type;
    }

    /// <summary>
    /// Resolves a property subpattern list, recursively resolving each subpattern.
    /// </summary>
    private static void ResolvePropertySubpatternList(
        PropertySubpatternListNode subpatternList,
        IType patternType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the first subpattern.
        ResolvePropertySubpattern(subpatternList.PropertySubpattern, patternType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Resolve additional subpatterns.
        foreach (var subpattern in subpatternList.PropertySubpatterns)
        {
            ResolvePropertySubpattern(subpattern, patternType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    /// <summary>
    /// Resolves a single property subpattern. The subpattern's inner pattern
    /// is resolved against the property's type (if known) or the scrutinee type.
    /// </summary>
    private static void ResolvePropertySubpattern(
        PropertySubpatternNode subpattern,
        IType patternType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Try to determine the property's type from the pattern type's member scope.
        var propertyType = ErrorType.Instance as IType;
        var propertyName = subpattern.Name.Lexeme;

        if (patternType is ClassType classType)
        {
            var member = classType.MemberScope.Lookup(propertyName);
            if (member is not null && member.DeclaredType is not null)
            {
                propertyType = member.DeclaredType;
            }
        }
        else if (patternType is StructType structType)
        {
            var member = structType.MemberScope.Lookup(propertyName);
            if (member is not null && member.DeclaredType is not null)
            {
                propertyType = member.DeclaredType;
            }
        }
        else if (patternType is InterfaceType interfaceType)
        {
            var member = interfaceType.MemberScope.Lookup(propertyName);
            if (member is not null && member.DeclaredType is not null)
            {
                propertyType = member.DeclaredType;
            }
        }

        // Recursively resolve the subpattern's inner pattern.
        ResolvePattern(subpattern.Pattern, propertyType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Resolves a list pattern element list, recursively resolving each element pattern.
    /// </summary>
    private static void ResolveListPatternElementList(
        ListPatternElementListNode elementList,
        IType elementType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the first element.
        ResolveListPatternElement(elementList.ListPatternElement, elementType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Resolve additional elements.
        foreach (var element in elementList.ListPatternElements)
        {
            ResolveListPatternElement(element, elementType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    /// <summary>
    /// Resolves a single list pattern element. Pattern elements are resolved
    /// against the array element type; slice elements need no resolution.
    /// </summary>
    private static void ResolveListPatternElement(
        ListPatternElementNode element,
        IType elementType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (element)
        {
            case PatternElementListPatternElementNode patternElement:
                ResolvePattern(patternElement.Pattern, elementType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case SliceElementListPatternElementNode:
                // Slice elements (`..` or `.. name`) need no type resolution.
                break;
        }
    }

    private static IType ResolveAdditive(
        AdditiveNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveMultiplicative(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            var rightType = ResolveMultiplicative(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

            // String concatenation: if either operand is string, result is string.
            // After Task 50, StringType has been deleted. String is now either
            // the stdlib Standard.String ClassType or the legacy
            // LegacyPrimitiveTypes.String ClassType fallback.
            if (IsStringLikeType(leftType) || IsStringLikeType(rightType))
            {
                // Return whichever string-like type we found (prefer left).
                var stringType = IsStringLikeType(leftType) ? leftType : rightType;
                expressionTypes[node] = stringType;
                return stringType;
            }

            var result = GetCommonNumericType(leftType, rightType);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveMultiplicative(
        MultiplicativeNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveUnary(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            var rightType = ResolveUnary(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            var result = GetCommonNumericType(leftType, rightType);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveUnary(
        UnaryNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case LogicalNotUnaryNode logicalNot:
            {
                ResolveUnary(logicalNot.Operand, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
                expressionTypes[node] = result;
                return result;
            }
            case NegateUnaryNode negate:
            {
                var operandType = ResolveUnary(negate.Operand, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                expressionTypes[node] = operandType;
                return operandType;
            }
            case CastExpressionUnaryNode castExpr:
                return ResolveCastExpression(castExpr, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            case PostfixExpressionUnaryNode postfix:
            {
                var type = ResolvePostfix(postfix.Primary, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                expressionTypes[node] = type;
                return type;
            }
            default:
                expressionTypes[node] = ErrorType.Instance;
                return ErrorType.Instance;
        }
    }

    /// <summary>
    /// Resolves a cast expression <c>(TargetType)operand</c> in Pass 2.
    /// Resolves the target type reference to an <see cref="IType"/>, resolves
    /// the operand expression, and records the target type as the expression
    /// type of the cast expression node.
    /// </summary>
    private static IType ResolveCastExpression(
        CastExpressionUnaryNode castExpr,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // 1. Resolve the target type reference to an IType.
        var targetType = ResolveTypeReference(castExpr.TargetType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        // 2. Resolve the operand expression to get its type.
        ResolveUnary(castExpr.Operand, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // 3. Record the target type as the expression type of the cast expression node.
        expressionTypes[castExpr] = targetType;

        // 4. Return the target type.
        return targetType;
    }

    private static IType ResolvePostfix(
        PostfixNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var targetType = ResolvePrimary(node.Target, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Apply each postfix suffix in order.
        foreach (var suffix in node.Suffix)
        {
            targetType = ResolvePostfixSuffix(suffix, targetType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        expressionTypes[node] = targetType;
        return targetType;
    }

    private static IType ResolvePostfixSuffix(
        PostfixSuffixNode suffix,
        IType targetType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (suffix)
        {
            case MethodCallSuffixPostfixSuffixNode methodCall:
            {
                var memberName = methodCall.Name.Lexeme;
                var memberType = LookupMemberType(targetType, memberName, ambientTypeResolver, fileScope, symbolTable, sourceText, diagnostics);

                // Handle type arguments on method invocations (e.g., obj.Method<int>()).
                if (methodCall.TypeArgs is not null)
                {
                    // Look up the method symbol to check its type parameters.
                    var methodSymbol = LookupMemberSymbol(targetType, memberName, ambientTypeResolver, fileScope, symbolTable);
                    if (methodSymbol is Symbol sym && sym.TypeParameters.Count > 0)
                    {
                        // Resolve the type arguments. Use the dependent-aware resolver
                        // if the method has any dependent type parameters.
                        var hasDependentParams = false;
                        foreach (var tp in sym.TypeParameters)
                        {
                            if (tp is DependentTypeParameterType)
                            {
                                hasDependentParams = true;
                                break;
                            }
                        }
                        IReadOnlyList<IType> resolvedTypeArgs;
                        if (hasDependentParams)
                        {
                            var (typeArgs, _) = ResolvePass2TypeArgumentListWithDependents(
                                methodCall.TypeArgs, sym.TypeParameters, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                            resolvedTypeArgs = typeArgs;
                        }
                        else
                        {
                            resolvedTypeArgs = ResolvePass2TypeArgumentList(methodCall.TypeArgs, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                        }

                        if (resolvedTypeArgs.Count != sym.TypeParameters.Count)
                        {
                            // Method arity mismatch.
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                DiagnosticId.MethodArityMismatch.Value,
                                DiagnosticSeverity.Error,
                                $"Method '{memberName}' expects {sym.TypeParameters.Count} type argument(s) but received {resolvedTypeArgs.Count}.",
                                methodCall.TypeArgs,
                                sourceText));
                            memberType = ErrorType.Instance;
                        }
                        else
                        {
                            // Substitute type parameters in the return type with
                            // the resolved type arguments.
                            memberType = SubstituteTypeParameters(memberType, sym.TypeParameters, resolvedTypeArgs);
                        }
                    }
                    else if (methodSymbol is not null)
                    {
                        // Method is not generic but type arguments were provided.
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.MethodArityMismatch.Value,
                            DiagnosticSeverity.Error,
                            $"Method '{memberName}' expects 0 type argument(s) but received {CountTypeArgs(methodCall.TypeArgs)}.",
                            methodCall.TypeArgs,
                            sourceText));
                    }
                }

                // Resolve arguments.
                if (methodCall.Arguments is not null)
                {
                    ResolveArgumentList(methodCall.Arguments, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }

                // The result type is the return type of the method (stored as DeclaredType on the member symbol).
                expressionTypes[suffix] = memberType;
                return memberType;
            }
            case MemberAccessSuffixPostfixSuffixNode memberAccess:
            {
                var memberName = memberAccess.Name.Lexeme;

                // Special case: Void.Void → void type instance expression.
                // The identifier "Void" resolves to the void type via
                // TryResolveWellKnownType, but the legacy fallback type has
                // no members. Until the Standard namespace is implicitly
                // imported, this special case bridges the gap.
                // We resolve through the stdlib path to get the same type
                // instance that the `void` keyword resolves to.
                if (targetType.IsVoid() && memberName == "Void")
                {
                    IType voidType;
                    try
                    {
                        var voidGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                        voidType = ResolveVoidKeyword(voidGlobalScope, symbolTable);
                    }
                    catch (StdlibMissingException)
                    {
                        voidType = targetType;
                    }
                    expressionTypes[suffix] = voidType;
                    return voidType;
                }

                var memberType = LookupMemberType(targetType, memberName, ambientTypeResolver, fileScope, symbolTable, sourceText, diagnostics);
                expressionTypes[suffix] = memberType;
                return memberType;
            }
            case IndexSuffixPostfixSuffixNode indexSuffix:
            {
                // Resolve the index expression.
                ResolveExpression(indexSuffix.Index, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                // The result type is the element type of the array.
                if (targetType is ArrayType arrayType)
                {
                    expressionTypes[suffix] = arrayType.ElementType;
                    return arrayType.ElementType;
                }

                expressionTypes[suffix] = ErrorType.Instance;
                return ErrorType.Instance;
            }
            case InvocationSuffixPostfixSuffixNode invocation:
            {
                // Direct invocation (e.g., function call without dot).
                if (invocation.Arguments is not null)
                {
                    ResolveArgumentList(invocation.Arguments, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }

                // The result type is the return type of the target (if it's a method).
                expressionTypes[suffix] = targetType;
                return targetType;
            }
            case SwitchExpressionSuffixPostfixSuffixNode switchExpr:
            {
                // The scrutinee type is the accumulated postfix expression type
                // (already resolved as targetType by the time this suffix is processed).
                var scrutineeType = targetType;

                if (switchExpr.Arms is not null)
                {
                    // Process the first arm.
                    ResolveSwitchArm(switchExpr.Arms.SwitchArm, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                    // Process remaining arms.
                    foreach (var arm in switchExpr.Arms.SwitchArms)
                    {
                        ResolveSwitchArm(arm, scrutineeType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    }
                }

                // Determine the unified result type from all arm result expressions.
                var resultType = DetermineUnifiedSwitchResultType(switchExpr, expressionTypes);
                expressionTypes[suffix] = resultType;
                return resultType;
            }
            default:
                return targetType;
        }
    }

    // ─── Switch Arm Resolution ───────────────────────────────────────────

    /// <summary>
    /// Resolves a single switch arm: creates a child scope for pattern variables,
    /// registers pattern variables from the arm's pattern, resolves the guard
    /// expression (if present), and resolves the result expression.
    /// </summary>
    private static void ResolveSwitchArm(
        SwitchArmNode arm,
        IType scrutineeType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Create a child scope for this arm's pattern variables.
        var armScope = new Scope(parent: currentScope);

        // Resolve type references within the pattern (e.g., the type in a type pattern).
        ResolvePattern(arm.Pattern, scrutineeType, armScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Register pattern variables introduced by the arm's pattern into the arm scope.
        // This reuses the same logic used by `is` expressions for pattern variable collection.
        CollectFromPattern(arm.Pattern, scrutineeType, armScope, symbolTable, expressionTypes, fileScope, ambientTypeResolver, sourceText, diagnostics);

        // Resolve the guard expression (if present) within the arm scope.
        if (arm.Guard is not null)
        {
            ResolveExpression(arm.Guard.Condition, armScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the result expression within the arm scope.
        ResolveExpression(arm.Result, armScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Determines the unified result type of a switch expression by collecting
    /// all arm result expression types and finding the common type.
    /// </summary>
    /// <remarks>
    /// Algorithm:
    /// <list type="bullet">
    ///   <item>If there are no arms, returns <see cref="UnknownType.Instance"/>.</item>
    ///   <item>If all arms have the same type, returns that type.</item>
    ///   <item>If types differ but share a common base class, returns the common base.</item>
    ///   <item>If no common type exists, returns <see cref="UnknownType.Instance"/>
    ///         (the type checker will report the incompatibility error).</item>
    /// </list>
    /// </remarks>
    private static IType DetermineUnifiedSwitchResultType(
        SwitchExpressionSuffixPostfixSuffixNode switchExpr,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes)
    {
        if (switchExpr.Arms is null)
            return UnknownType.Instance;

        // Collect all arm result expression types.
        var armTypes = new List<IType>();

        if (expressionTypes.TryGetValue(switchExpr.Arms.SwitchArm.Result, out var firstType))
        {
            armTypes.Add(firstType);
        }

        foreach (var arm in switchExpr.Arms.SwitchArms)
        {
            if (expressionTypes.TryGetValue(arm.Result, out var armType))
            {
                armTypes.Add(armType);
            }
        }

        if (armTypes.Count == 0)
            return UnknownType.Instance;

        // Check if all arms have the same type.
        var unified = armTypes[0];
        var allSame = true;
        for (var i = 1; i < armTypes.Count; i++)
        {
            if (!TypesMatch(unified, armTypes[i]))
            {
                allSame = false;
                break;
            }
        }

        if (allSame)
            return unified;

        // Types differ — attempt to find a common base class.
        // Walk the base type chain of the first type and check if all other
        // types are assignable to each ancestor. The first ancestor that all
        // types are assignable to is the common base.
        var commonBase = FindCommonBaseType(armTypes);
        return commonBase ?? UnknownType.Instance;
    }

    /// <summary>
    /// Returns true if two types are considered the same for result type unification.
    /// Uses display name comparison (consistent with the identity check in
    /// <see cref="TypeCheckerAnalyzer.IsAssignable"/>).
    /// </summary>
    private static bool TypesMatch(IType a, IType b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is ErrorType || b is ErrorType)
            return true; // Suppress cascading errors.
        return a.DisplayName == b.DisplayName;
    }

    /// <summary>
    /// Attempts to find a common base type for a list of types by walking
    /// the inheritance chain of the first type and checking if all other
    /// types share that ancestor.
    /// </summary>
    /// <returns>The common base type, or null if none exists.</returns>
    private static IType? FindCommonBaseType(List<IType> types)
    {
        if (types.Count == 0)
            return null;

        // Skip ErrorType entries — they should not influence the result.
        var nonErrorTypes = new List<IType>();
        foreach (var t in types)
        {
            if (t is not ErrorType)
                nonErrorTypes.Add(t);
        }

        if (nonErrorTypes.Count == 0)
            return ErrorType.Instance;
        if (nonErrorTypes.Count == 1)
            return nonErrorTypes[0];

        // Try to find a common base by walking the first type's ancestor chain.
        // For each candidate ancestor, check if all other types are assignable to it.
        var first = nonErrorTypes[0];
        if (first is ClassType firstClass)
        {
            var candidate = firstClass.BaseType as ClassType;
            while (candidate is not null)
            {
                var allAssignable = true;
                for (var i = 1; i < nonErrorTypes.Count; i++)
                {
                    if (!IsSubtypeOf(nonErrorTypes[i], candidate))
                    {
                        allAssignable = false;
                        break;
                    }
                }

                if (allAssignable)
                    return candidate;

                candidate = candidate.BaseType as ClassType;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if <paramref name="source"/> is a subtype of <paramref name="target"/>
    /// by walking the base type chain. Used for common base type resolution in
    /// switch expression result type unification.
    /// </summary>
    private static bool IsSubtypeOf(IType source, ClassType target)
    {
        if (ReferenceEquals(source, target))
            return true;
        if (source.DisplayName == target.DisplayName)
            return true;

        if (source is ClassType sourceClass)
        {
            var current = sourceClass.BaseType as ClassType;
            while (current is not null)
            {
                if (ReferenceEquals(current, target) || current.DisplayName == target.DisplayName)
                    return true;
                current = current.BaseType as ClassType;
            }
        }

        return false;
    }

    // ─── Primary Expression Resolution ───────────────────────────────────

    private static IType ResolvePrimary(
        PrimaryNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        IType result;

        switch (node)
        {
            case IntegerLiteralExpressionPrimaryNode intLit:
                result = ResolveIntegerLiteralType(intLit.IntegerLiteral.Lexeme, fileScope, symbolTable);
                break;

            case HexLiteralExpressionPrimaryNode hexLit:
                result = ResolveIntegerLiteralType(hexLit.HexLiteral.Lexeme, fileScope, symbolTable);
                break;

            case BinaryLiteralExpressionPrimaryNode binLit:
                result = ResolveIntegerLiteralType(binLit.BinaryLiteral.Lexeme, fileScope, symbolTable);
                break;

            case FloatLiteralExpressionPrimaryNode floatLit:
                result = ResolveFloatLiteralType(floatLit.FloatLiteral.Lexeme, fileScope, symbolTable);
                break;

            case StringLiteralExpressionPrimaryNode:
                result = ResolvePrimitiveByKeyword("string", fileScope, symbolTable);
                break;

            case InterpolatedStringExpressionPrimaryNode interpolated:
                // Resolve all expression parts within the interpolated string.
                foreach (var part in interpolated.InterpolatedString.Parts)
                {
                    if (part is ExpressionPartInterpolatedPartNode exprPart)
                    {
                        ResolveExpression(exprPart.Expression, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    }
                }
                result = ResolvePrimitiveByKeyword("string", fileScope, symbolTable);
                break;

            case CharLiteralExpressionPrimaryNode:
                result = ResolvePrimitiveByKeyword("char", fileScope, symbolTable);
                break;

            case TrueLiteralExpressionPrimaryNode:
                result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
                break;

            case FalseLiteralExpressionPrimaryNode:
                result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
                break;

            case ThisExpressionPrimaryNode:
                if (isStatic)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_INVALID_THIS",
                        DiagnosticSeverity.Error,
                        "'this' cannot be used in a static context.",
                        node,
                        sourceText));
                    result = ErrorType.Instance;
                }
                else
                {
                    result = enclosingType;
                }
                break;

            case FieldKeywordExpressionPrimaryNode:
            {
                // Resolve 'field' keyword to the backing field symbol of the enclosing property.
                // Walk the scope chain to find the enclosing property symbol.
                var backingFieldType = ResolveFieldKeyword(currentScope, symbolTable, sourceText, diagnostics);
                result = backingFieldType;
                break;
            }

            case ArrayCreationExpressionPrimaryNode arrayCreation:
            {
                var elementType = ResolveTypeReference(arrayCreation.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                ResolveExpression(arrayCreation.Size, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                result = new ArrayType(elementType);
                break;
            }

            case ObjectCreationExpressionPrimaryNode objCreation:
            {
                var typeName = ExtractQualifiedName(objCreation.Type, sourceText);
                var createdType = ResolveNamedType(typeName, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                // Handle type arguments on new expressions (e.g., new Box<int>()).
                if (objCreation.TypeArgs is not null)
                {
                    createdType = ResolvePass2TypeArguments(createdType, typeName, objCreation.TypeArgs, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                }

                if (objCreation.Arguments is not null)
                {
                    ResolveArgumentList(objCreation.Arguments, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }

                // Resolve value expressions in the property initializer block.
                if (objCreation.Initializer is not null && objCreation.Initializer.Assignments is not null)
                {
                    var assignments = objCreation.Initializer.Assignments;
                    ResolveExpression(assignments.PropertyAssignment.Value, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    foreach (var assignment in assignments.PropertyAssignments)
                    {
                        ResolveExpression(assignment.Value, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    }
                }

                result = createdType;
                break;
            }

            case ObjectCreationExpressionNoArgsPrimaryNode objCreationNoArgs:
            {
                var typeName = ExtractQualifiedName(objCreationNoArgs.Type, sourceText);
                var createdType = ResolveNamedType(typeName, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                // Handle type arguments on new expressions (e.g., new Box<int> { ... }).
                if (objCreationNoArgs.TypeArgs is not null)
                {
                    createdType = ResolvePass2TypeArguments(createdType, typeName, objCreationNoArgs.TypeArgs, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                }

                // Resolve value expressions in the property initializer block.
                if (objCreationNoArgs.Initializer.Assignments is not null)
                {
                    var assignments = objCreationNoArgs.Initializer.Assignments;
                    ResolveExpression(assignments.PropertyAssignment.Value, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    foreach (var assignment in assignments.PropertyAssignments)
                    {
                        ResolveExpression(assignment.Value, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    }
                }

                result = createdType;
                break;
            }

            case TupleLiteralExpressionPrimaryNode tupleLit:
            {
                // Collect all elements: First + Rest + Additional.
                var tupleElementCount = 2 + tupleLit.TupleLiteralElements.Length;
                var tupleElementTypes = new List<IType>(tupleElementCount);
                var tupleElementNames = new List<string?>(tupleElementCount);
                var tupleHasAnyName = false;

                void ResolveTupleLiteralElement(TupleLiteralElementNode element)
                {
                    switch (element)
                    {
                        case NamedTupleLiteralElementTupleLiteralElementNode named:
                            tupleElementTypes.Add(ResolveExpression(named.Value, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics));
                            tupleElementNames.Add(named.Name.Lexeme);
                            tupleHasAnyName = true;
                            break;
                        case UnnamedTupleLiteralElementTupleLiteralElementNode unnamed:
                            tupleElementTypes.Add(ResolveExpression(unnamed.Value, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics));
                            tupleElementNames.Add(null);
                            break;
                    }
                }

                ResolveTupleLiteralElement(tupleLit.First);
                ResolveTupleLiteralElement(tupleLit.Rest);
                foreach (var additional in tupleLit.TupleLiteralElements)
                {
                    ResolveTupleLiteralElement(additional);
                }

                result = new TupleType(tupleElementTypes, tupleHasAnyName ? tupleElementNames : null);
                break;
            }

            case ParenthesizedExpressionPrimaryNode paren:
                result = ResolveExpression(paren.Inner, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;

            case IdentifierExpressionPrimaryNode identifier:
            {
                var name = identifier.Name.Lexeme;
                var symbol = currentScope.Lookup(name);

                if (symbol is null)
                {
                    // Try to resolve as a type name (for static access like Console.WriteLine).
                    var typeAsSymbol = ResolveIdentifierAsType(name, fileScope, ambientTypeResolver, symbolTable, sourceText);
                    if (typeAsSymbol is not null)
                    {
                        result = typeAsSymbol;
                    }
                    // In a non-static instance context, bare identifiers may refer
                    // to inherited members from the base class chain. Walk the
                    // BaseType chain to find the member, mirroring what
                    // LookupMemberType does for explicit this.member access.
                    else if (!isStatic && enclosingType is ClassType enclosingClass)
                    {
                        IType? inheritedMemberType = null;
                        var ancestor = enclosingClass.BaseType as ClassType;
                        while (ancestor is not null)
                        {
                            foreach (var m in ancestor.MemberScope.Members)
                            {
                                if (m.Name == name)
                                {
                                    inheritedMemberType = m.DeclaredType ?? ErrorType.Instance;
                                    break;
                                }
                            }
                            if (inheritedMemberType is not null)
                                break;
                            ancestor = ancestor.BaseType as ClassType;
                        }

                        if (inheritedMemberType is not null)
                        {
                            result = inheritedMemberType;
                        }
                        else
                        {
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                "MIST_UNDEFINED_IDENTIFIER",
                                DiagnosticSeverity.Error,
                                $"The name '{name}' does not exist in the current context.",
                                identifier,
                                sourceText));
                            result = ErrorType.Instance;
                        }
                    }
                    else
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            "MIST_UNDEFINED_IDENTIFIER",
                            DiagnosticSeverity.Error,
                            $"The name '{name}' does not exist in the current context.",
                            identifier,
                            sourceText));
                        result = ErrorType.Instance;
                    }
                }
                else
                {
                    result = symbol.DeclaredType ?? ErrorType.Instance;
                }
                break;
            }

            case TypeLiteralExpressionPrimaryNode typeLiteral:
            {
                // Resolve the TypeReference to get the referenced type (for validation).
                var referencedType = ResolveTypeReference(
                    typeLiteral.TypeRef, fileScope, ambientTypeResolver,
                    symbolTable, sourceText, diagnostics);

                // The expression type of a type literal is always the metatype Type.
                try
                {
                    var typeGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                    result = ResolveTypeKeyword(typeGlobalScope, symbolTable);
                }
                catch (StdlibMissingException)
                {
                    result = LegacyPrimitiveTypes.Type;
                }
                break;
            }

            case DefaultExpressionPrimaryNode defaultExpr:
            {
                // Resolve the explicit type reference in default(TypeName).
                // The resolved type becomes the expression type. The type
                // checker will later validate that the type is defaultable.
                result = ResolveTypeReference(
                    defaultExpr.Type, fileScope, ambientTypeResolver,
                    symbolTable, sourceText, diagnostics);
                break;
            }

            case DefaultLiteralExpressionPrimaryNode:
            {
                // Bare 'default' — the type must be inferred from context
                // (e.g., assignment target type, return type, parameter type).
                // Record as ErrorType here; the type checker will resolve
                // the actual type from context or emit a diagnostic if
                // inference is not possible.
                result = ErrorType.Instance;
                break;
            }

            case EmptyArrayLiteralExpressionPrimaryNode:
            {
                // Empty array literal [] — if a target type is available (from variable
                // declaration annotation) and is an ArrayType, use the target element type.
                // Otherwise, produce ErrorType (the type checker will emit a diagnostic
                // indicating that empty array literals require an explicit element type).
                if (_emptyArrayTargetType is ArrayType targetArrayType)
                {
                    result = targetArrayType;
                }
                else
                {
                    result = ErrorType.Instance;
                }
                break;
            }

            case ArrayLiteralExpressionPrimaryNode arrayLiteral:
            {
                // Resolve all element expressions and produce an ArrayType
                // with the common element type.
                var elementType = ResolveExpression(arrayLiteral.First, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                foreach (var elem in arrayLiteral.Expressions)
                {
                    var elemType = ResolveExpression(elem, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    if (!TypesMatch(elementType, elemType))
                    {
                        // Types differ — attempt to find a common base type.
                        var commonBase = FindCommonBaseType(new List<IType> { elementType, elemType });
                        elementType = commonBase ?? elementType;
                    }
                }

                result = new ArrayType(elementType);
                break;
            }

            default:
                result = ErrorType.Instance;
                break;
        }

        expressionTypes[node] = result;
        return result;
    }

    /// <summary>
    /// Tries to resolve an identifier as a type name (for static member access).
    /// Returns the type if found, null otherwise.
    /// </summary>
    private static IType? ResolveIdentifierAsType(
        string name,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText)
    {
        // Check well-known types.
        var wellKnown = TryResolveWellKnownType(name);
        if (wellKnown is not null) return wellKnown;

        // Check scope chain for type symbols (local → file → namespace → global).
        var symbol = fileScope.Lookup(name);
        if (symbol is not null && (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait || symbol.Kind == SymbolKind.TypeAlias || symbol.Kind == SymbolKind.Enum))
        {
            return symbol.DeclaredType;
        }

        // Check imported namespaces — first user-defined, then ambient.
        var importedNamespaces = CollectImportedNamespaces(fileScope);
        var globalScope = FindGlobalScope(fileScope);

        // Check user-defined namespace scopes.
        foreach (var ns in importedNamespaces)
        {
            var nsScope = globalScope is not null
                ? FindExistingNamespaceScope(globalScope, symbolTable, ns)
                : null;

            if (nsScope is not null)
            {
                foreach (var member in nsScope.Members)
                {
                    if (member.Name == name &&
                        (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct || member.Kind == SymbolKind.Interface || member.Kind == SymbolKind.Trait || member.Kind == SymbolKind.TypeAlias || member.Kind == SymbolKind.Enum))
                    {
                        return member.DeclaredType;
                    }
                }
            }
        }

        // Check imported namespaces via ambient resolver.
        if (ambientTypeResolver is not null)
        {
            foreach (var ns in importedNamespaces)
            {
                var resolvedType = ambientTypeResolver.ResolveType(ns, name);
                if (resolvedType is not null)
                {
                    RegisterAmbientTypeInScope(resolvedType, name, ns, fileScope, ambientTypeResolver, symbolTable);
                    return resolvedType;
                }
            }
        }

        return null;
    }

    // ─── Helper Methods ──────────────────────────────────────────────────

    /// <summary>
    /// Looks up a member by name on a given type. Searches the type's member scope
    /// and, for ambient types, queries the ambient resolver.
    /// </summary>
    private static IType LookupMemberType(
        IType targetType,
        string memberName,
        IAmbientTypeResolver? ambientTypeResolver,
        Scope fileScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (targetType is ErrorType)
        {
            return ErrorType.Instance;
        }

        // Type aliases are transparent — unwrap to the underlying type
        // before member lookup so that alias-typed variables have full
        // member access (Requirement 5.1).
        if (targetType is TypeAliasType aliasTarget)
        {
            targetType = aliasTarget.UnderlyingType;
        }

        // ── Constrained type parameter member access (Requirements 6.1–6.4, 6.7) ──
        // When the receiver is a TypeParameterType with constraints, look up the
        // member in the constraint types (interfaces, traits, base class).
        if (targetType is TypeParameterType typeParam && typeParam.Constraints is not null)
        {
            var constraints = typeParam.Constraints;

            // Check interface constraints.
            foreach (var ifaceConstraint in constraints.InterfaceConstraints)
            {
                if (ifaceConstraint is InterfaceType iface)
                {
                    var member = iface.MemberScope.Lookup(memberName);
                    if (member is not null)
                        return member.DeclaredType ?? ErrorType.Instance;

                    // Also check inherited interfaces transitively.
                    var inherited = LookupInheritedInterfaceMember(iface, memberName);
                    if (inherited is not null)
                        return inherited;
                }
            }

            // Check trait constraints.
            foreach (var traitConstraint in constraints.TraitConstraints)
            {
                if (traitConstraint is TraitType trait)
                {
                    var member = trait.MemberScope.Lookup(memberName);
                    if (member is not null)
                        return member.DeclaredType ?? ErrorType.Instance;
                }
            }

            // Check base class constraint (including inherited members).
            if (constraints.BaseClassConstraint is ClassType baseClass)
            {
                ClassType? current = baseClass;
                while (current is not null)
                {
                    foreach (var m in current.MemberScope.Members)
                    {
                        if (m.Name == memberName)
                            return m.DeclaredType ?? ErrorType.Instance;
                    }

                    current = current.BaseType as ClassType;
                }
            }

            // If constraints exist but none provide the member, fall through
            // to the MIST_UNDEFINED_MEMBER diagnostic below.
        }

        // Enum member access: EnumName.MemberName resolves to the EnumType itself.
        // Enum members are static fields in the enum's MemberScope. If the member
        // exists, the result type is the EnumType (not the underlying type).
        // If the member does not exist, report MIST_SB_0082.
        if (targetType is EnumType enumTarget)
        {
            var enumMember = enumTarget.MemberScope.Lookup(memberName);
            if (enumMember is not null)
            {
                return enumTarget;
            }

            diagnostics.Add(new Diagnostic(
                UndefinedEnumMemberAccess,
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"Enum '{enumTarget.DisplayName}' does not contain a member '{memberName}'.",
                Location: default,
                CompilationUnitPath: sourceText?.Path ?? "<unknown>",
                AnalyzerName: null));

            return ErrorType.Instance;
        }

        // For ConstructedType, unwrap to the generic definition and look up the member,
        // then substitute type parameters with type arguments.
        IReadOnlyList<IType>? constructedTypeParams = null;
        IReadOnlyList<IType>? constructedTypeArgs = null;
        if (targetType is ConstructedType constructedTarget)
        {
            constructedTypeParams = constructedTarget.GenericDefinition switch
            {
                ClassType ct2 => ct2.TypeParameters,
                StructType st2 => st2.TypeParameters,
                InterfaceType it2 => it2.TypeParameters,
                _ => null,
            };
            constructedTypeArgs = constructedTarget.TypeArguments;
            targetType = constructedTarget.GenericDefinition;
        }
        // For ConstructedTraitType, unwrap to the generic trait definition and look up
        // the member, then substitute type parameters with type arguments (Task 10.2).
        else if (targetType is ConstructedTraitType constructedTraitTarget)
        {
            constructedTypeParams = constructedTraitTarget.GenericTraitDefinition.TypeParameters.Count > 0
                ? constructedTraitTarget.GenericTraitDefinition.TypeParameters
                : null;
            constructedTypeArgs = constructedTraitTarget.TypeArguments;
            targetType = constructedTraitTarget.GenericTraitDefinition;
        }

        // Get the member scope from the target type.
        IScope? memberScope = targetType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            TraitType tt => tt.MemberScope,
            NewtypeType nt => nt.MemberScope,
            ArrayType => null, // Arrays use special handling.
            _ => null,
        };

        // For string type with no members in its scope (legacy fallback
        // ClassType from LegacyPrimitiveTypes.String), look up members
        // via the stdlib Standard.String type or the ambient resolver.
        // When stdlib IS loaded, the string type is the stdlib ClassType
        // with a populated member scope, so this path only fires for the
        // legacy fallback in tests without stdlib.
        if (IsStringLikeType(targetType) && (memberScope is null || !memberScope.Members.Any(m => m.Name == memberName)))
        {
            var globalScope = FindGlobalScope(fileScope);
            if (globalScope is not null)
            {
                try
                {
                    var stdlibString = LookupQualifiedStdlibType(globalScope, symbolTable, "Standard.String");
                    if (stdlibString is ClassType stdlibStringClass)
                    {
                        foreach (var m in stdlibStringClass.MemberScope.Members)
                        {
                            if (m.Name == memberName)
                                return m.DeclaredType ?? ErrorType.Instance;
                        }
                    }
                }
                catch (StdlibMissingException)
                {
                    // Stdlib not loaded — fall through to ambient resolver fallback below.
                }
            }

            // Legacy fallback: try the ambient resolver (for backward compat
            // with tests that don't load stdlib).
            if (ambientTypeResolver is not null)
            {
                var resolvedStringType = ambientTypeResolver.ResolveType("System", "String");
                if (resolvedStringType is not null)
                {
                    var members = ambientTypeResolver.GetMembers(resolvedStringType);
                    foreach (var member in members)
                    {
                        if (member.Name == memberName)
                            return member.DeclaredType ?? ErrorType.Instance;
                    }
                }
            }
        }

        // For array types, look up Length via the stdlib Standard.Array<T> type.
        // After Task 42, Array is no longer an ambient type — it's a stdlib type.
        if (targetType is ArrayType)
        {
            var globalScope = FindGlobalScope(fileScope);
            if (globalScope is not null)
            {
                try
                {
                    var stdlibArray = LookupQualifiedStdlibType(globalScope, symbolTable, "Standard.Array");
                    if (stdlibArray is ClassType stdlibArrayClass)
                    {
                        foreach (var m in stdlibArrayClass.MemberScope.Members)
                        {
                            if (m.Name == memberName)
                                return m.DeclaredType ?? ErrorType.Instance;
                        }
                    }
                }
                catch (StdlibMissingException)
                {
                    // Stdlib not loaded — fall through to ambient resolver fallback below.
                }
            }

            // Legacy fallback: try the ambient resolver (for backward compat
            // with tests that don't load stdlib).
            if (ambientTypeResolver is not null)
            {
                var arrayBaseType = ambientTypeResolver.ResolveType("System", "Array");
                if (arrayBaseType is not null)
                {
                    var members = ambientTypeResolver.GetMembers(arrayBaseType);
                    foreach (var member in members)
                    {
                        if (member.Name == memberName)
                            return member.DeclaredType ?? ErrorType.Instance;
                    }
                }
            }
        }

        // Tuple element access: resolve ItemN positional access and named access.
        if (targetType is TupleType tupleTarget)
        {
            // Check positional access: Item1, Item2, ..., ItemN (1-indexed).
            if (memberName.StartsWith("Item", StringComparison.Ordinal)
                && int.TryParse(memberName.AsSpan(4), out var itemIndex)
                && itemIndex >= 1
                && itemIndex <= tupleTarget.Arity)
            {
                return tupleTarget.ElementTypes[itemIndex - 1];
            }

            // Check named access: match against declared element names.
            if (tupleTarget.ElementNames is not null)
            {
                for (var i = 0; i < tupleTarget.ElementNames.Count; i++)
                {
                    if (tupleTarget.ElementNames[i] == memberName)
                    {
                        return tupleTarget.ElementTypes[i];
                    }
                }
            }

            // No match — report MIST_TC_0063 (undefined tuple member access).
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_TC_0063"),
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"Tuple type '{tupleTarget.DisplayName}' does not contain a member '{memberName}'.",
                Location: default,
                CompilationUnitPath: sourceText?.Path ?? "<unknown>",
                AnalyzerName: null));

            return ErrorType.Instance;
        }

        // Search the type's member scope, walking the BaseType chain for class types.
        if (targetType is ClassType classTarget)
        {
            ClassType? current = classTarget;
            while (current is not null)
            {
                // Search only the direct members of this class's scope.
                foreach (var m in current.MemberScope.Members)
                {
                    if (m.Name == memberName)
                    {
                        var memberType = m.DeclaredType ?? ErrorType.Instance;
                        return SubstituteMemberTypeIfNeeded(memberType, constructedTypeParams, constructedTypeArgs);
                    }
                }

                // If the type has ambient members, try the ambient resolver.
                if (ambientTypeResolver is not null)
                {
                    var ambientMembers = ambientTypeResolver.GetMembers(current);
                    foreach (var m in ambientMembers)
                    {
                        if (m.Name == memberName)
                        {
                            var memberType = m.DeclaredType ?? ErrorType.Instance;
                            return SubstituteMemberTypeIfNeeded(memberType, constructedTypeParams, constructedTypeArgs);
                        }
                    }
                }

                current = current.BaseType as ClassType;
            }
        }
        else if (memberScope is not null)
        {
            var member = memberScope.Lookup(memberName);
            if (member is not null)
            {
                var memberType = member.DeclaredType ?? ErrorType.Instance;
                return SubstituteMemberTypeIfNeeded(memberType, constructedTypeParams, constructedTypeArgs);
            }

            // For interface types, walk inherited interfaces transitively.
            if (targetType is InterfaceType interfaceTarget)
            {
                var result = LookupInheritedInterfaceMember(interfaceTarget, memberName);
                if (result is not null)
                {
                    return SubstituteMemberTypeIfNeeded(result, constructedTypeParams, constructedTypeArgs);
                }
            }
        }

        // ── Extension member lookup fallback (Task 7.1 + 7.2 + 7.6) ────────
        // Before emitting MIST_UNDEFINED_MEMBER, check the extension registry
        // for applicable extension members matching the name.
        // Task 7.6: Do NOT fall back to extension lookup when the receiver type
        // is a trait or interface. Extensions are orthogonal to traits/interfaces
        // and should not be discoverable on trait-typed or interface-typed variables.
        // Task 7.2: Collect ALL candidates and apply specificity ordering.
        if (_extensionRegistryContext is not null
            && targetType is not TraitType
            && targetType is not InterfaceType)
        {
            // Use the original target type (before ConstructedType unwrapping)
            // for the extension registry query. If the original was a
            // ConstructedType, reconstruct it; otherwise use targetType as-is.
            var lookupType = (constructedTypeParams is not null && constructedTypeArgs is not null)
                ? (IType)new ConstructedType(targetType, constructedTypeArgs)
                : targetType;

            // Task 7.7: When the static type is `unknown`, only search universal extensions.
            // Type-specific extensions should NOT be included because we can't verify
            // they're applicable when the concrete type is not known at compile time.
            // Task 7.8: When the static type is a union type (A | B), only include
            // extensions applicable to ALL constituents. Universal extensions always apply.
            IReadOnlyList<ExtensionBlockInfo> applicableExtensions;
            if (lookupType is UnknownType)
            {
                applicableExtensions = _extensionRegistryContext.UniversalExtensions;
            }
            else if (lookupType is UnionType unionType)
            {
                applicableExtensions = _extensionRegistryContext.GetApplicableExtensionsForUnion(unionType.Constituents);
            }
            else
            {
                applicableExtensions = _extensionRegistryContext.GetApplicableExtensions(lookupType);
            }

            // Filter by namespace visibility: only include extensions whose
            // ContainingNamespace is null (top-level) or is in the current
            // file's imported namespaces.
            var importedNamespaces = CollectImportedNamespaces(fileScope);

            // Collect ALL matching extension candidates (receiver type + member type).
            var candidates = new List<(IType ReceiverType, IType MemberType)>();

            foreach (var ext in applicableExtensions)
            {
                // Namespace visibility check.
                if (ext.ContainingNamespace is not null && !importedNamespaces.Contains(ext.ContainingNamespace))
                    continue;

                // Search the extension block's member scope for a matching member.
                var extMember = ext.MemberScope.Lookup(memberName);
                if (extMember is not null)
                {
                    var memberType = extMember.DeclaredType ?? ErrorType.Instance;

                    // Task 7.3: Generic extension type inference.
                    // If the extension block has type parameters, attempt to unify
                    // the extension's receiver type pattern with the actual receiver type.
                    if (ext.TypeParameters.Count > 0)
                    {
                        var bindings = TryUnifyReceiverType(ext.ReceiverType, lookupType, ext.TypeParameters);
                        if (bindings is null)
                        {
                            // Unification failed — silently exclude this extension.
                            continue;
                        }

                        // Substitute the inferred type arguments into the member's declared type.
                        memberType = SubstituteTypeInMember(memberType, ext.TypeParameters, bindings);
                    }

                    candidates.Add((ext.ReceiverType, memberType));
                }
            }

            if (candidates.Count == 1)
            {
                // Single candidate — use it directly.
                return candidates[0].MemberType;
            }

            if (candidates.Count > 1)
            {
                // Apply specificity ordering: find the most-specific candidate.
                var bestIndex = 0;
                var ambiguous = false;

                for (var i = 1; i < candidates.Count; i++)
                {
                    var comparison = CompareReceiverSpecificity(candidates[bestIndex].ReceiverType, candidates[i].ReceiverType);
                    if (comparison > 0)
                    {
                        // Current best is less specific than candidate i → i becomes new best.
                        bestIndex = i;
                        ambiguous = false;
                    }
                    else if (comparison == 0)
                    {
                        // Equally specific → potential ambiguity.
                        ambiguous = true;
                    }
                    // comparison < 0 means current best is already more specific — keep it.
                }

                // Verify the best candidate is strictly more specific than ALL others.
                if (!ambiguous)
                {
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        if (i == bestIndex) continue;
                        var cmp = CompareReceiverSpecificity(candidates[bestIndex].ReceiverType, candidates[i].ReceiverType);
                        if (cmp >= 0)
                        {
                            ambiguous = true;
                            break;
                        }
                    }
                }

                if (!ambiguous)
                {
                    return candidates[bestIndex].MemberType;
                }

                // Ambiguity: emit MIST_TC_0082.
                var candidateDescriptions = string.Join(", ", candidates.Select(c => $"extension({c.ReceiverType.DisplayName})"));
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_TC_0082"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Ambiguous extension method resolution: member '{memberName}' is provided by multiple equally-specific extension blocks: {candidateDescriptions}.",
                    Location: default,
                    CompilationUnitPath: sourceText?.Path ?? "<unknown>",
                    AnalyzerName: null));

                return ErrorType.Instance;
            }

            // ── Static extension member lookup (Task 7.4) ─────────────────
            // When instance extension lookup finds no candidates, also search
            // for static extension members using exact type match only.
            // Static extension members are callable via TypeName.MemberName.
            var exactExtensions = _extensionRegistryContext.GetExactExtensions(lookupType);
            var staticCandidates = new List<(IType ReceiverType, IType MemberType)>();

            foreach (var ext in exactExtensions)
            {
                // Namespace visibility check.
                if (ext.ContainingNamespace is not null && !importedNamespaces.Contains(ext.ContainingNamespace))
                    continue;

                // Search the extension block's member scope for a matching member.
                var extMember = ext.MemberScope.Lookup(memberName);
                if (extMember is null)
                    continue;

                // Filter: only consider members marked static.
                var declaringNode = symbolTable.GetDeclaringNode(extMember);
                if (!IsExtensionMemberStatic(declaringNode))
                    continue;

                var memberType = extMember.DeclaredType ?? ErrorType.Instance;

                // Generic extension type inference (same rules as instance lookup).
                if (ext.TypeParameters.Count > 0)
                {
                    var bindings = TryUnifyReceiverType(ext.ReceiverType, lookupType, ext.TypeParameters);
                    if (bindings is null)
                        continue;

                    memberType = SubstituteTypeInMember(memberType, ext.TypeParameters, bindings);
                }

                staticCandidates.Add((ext.ReceiverType, memberType));
            }

            if (staticCandidates.Count == 1)
            {
                return staticCandidates[0].MemberType;
            }

            if (staticCandidates.Count > 1)
            {
                // Apply specificity ordering for static candidates.
                var bestIndex = 0;
                var ambiguous = false;

                for (var i = 1; i < staticCandidates.Count; i++)
                {
                    var comparison = CompareReceiverSpecificity(staticCandidates[bestIndex].ReceiverType, staticCandidates[i].ReceiverType);
                    if (comparison > 0)
                    {
                        bestIndex = i;
                        ambiguous = false;
                    }
                    else if (comparison == 0)
                    {
                        ambiguous = true;
                    }
                }

                if (!ambiguous)
                {
                    for (var i = 0; i < staticCandidates.Count; i++)
                    {
                        if (i == bestIndex) continue;
                        var cmp = CompareReceiverSpecificity(staticCandidates[bestIndex].ReceiverType, staticCandidates[i].ReceiverType);
                        if (cmp >= 0)
                        {
                            ambiguous = true;
                            break;
                        }
                    }
                }

                if (!ambiguous)
                {
                    return staticCandidates[bestIndex].MemberType;
                }

                // Ambiguity among static extension members.
                var staticCandidateDescriptions = string.Join(", ", staticCandidates.Select(c => $"extension({c.ReceiverType.DisplayName})"));
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_TC_0082"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Ambiguous extension method resolution: static member '{memberName}' is provided by multiple equally-specific extension blocks: {staticCandidateDescriptions}.",
                    Location: default,
                    CompilationUnitPath: sourceText?.Path ?? "<unknown>",
                    AnalyzerName: null));

                return ErrorType.Instance;
            }
        }

        // Use the original target type display name for the diagnostic (including type args).
        var diagnosticTypeName = (constructedTypeParams is not null && constructedTypeArgs is not null)
            ? $"{targetType.DisplayName}<{string.Join(", ", constructedTypeArgs.Select(a => a.DisplayName))}>"
            : targetType.DisplayName;

        diagnostics.Add(new Diagnostic(
            new DiagnosticId("MIST_UNDEFINED_MEMBER"),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            $"'{diagnosticTypeName}' does not contain a definition for '{memberName}'.",
            Location: default,
            CompilationUnitPath: sourceText?.Path ?? "<unknown>",
            AnalyzerName: null));

        return ErrorType.Instance;
    }

    /// <summary>
    /// Compares two extension receiver types for specificity.
    /// Returns:
    ///   negative value if <paramref name="r1"/> is more specific than <paramref name="r2"/>,
    ///   positive value if <paramref name="r2"/> is more specific than <paramref name="r1"/>,
    ///   zero if they are equally specific (ambiguous).
    /// </summary>
    /// <remarks>
    /// Specificity rules:
    /// - <c>unknown</c> is always less specific than any concrete type.
    /// - If R1 is assignable to R2 but not vice versa → R1 is more specific (return negative).
    /// - If R2 is assignable to R1 but not vice versa → R2 is more specific (return positive).
    /// - If neither is assignable to the other → equally specific (ambiguous, return 0).
    /// - If both are assignable to each other (same type) → equally specific (return 0).
    /// </remarks>
    private static int CompareReceiverSpecificity(IType r1, IType r2)
    {
        // Same type reference — equally specific.
        if (ReferenceEquals(r1, r2))
            return 0;

        // Special case: unknown is always less specific than any concrete type.
        var r1IsUnknown = r1 is UnknownType;
        var r2IsUnknown = r2 is UnknownType;

        if (r1IsUnknown && !r2IsUnknown)
            return 1; // r2 is more specific.
        if (r2IsUnknown && !r1IsUnknown)
            return -1; // r1 is more specific.
        if (r1IsUnknown && r2IsUnknown)
            return 0; // Both unknown — equally specific.

        // Check assignability in both directions.
        var r1AssignableToR2 = IsReceiverAssignableTo(r1, r2);
        var r2AssignableToR1 = IsReceiverAssignableTo(r2, r1);

        if (r1AssignableToR2 && !r2AssignableToR1)
            return -1; // r1 is more specific (it fits into r2 but not vice versa).
        if (r2AssignableToR1 && !r1AssignableToR2)
            return 1; // r2 is more specific.

        // Both assignable to each other, or neither assignable → equally specific (ambiguous).
        return 0;
    }

    /// <summary>
    /// Determines whether <paramref name="source"/> is assignable to <paramref name="target"/>
    /// for the purposes of extension receiver specificity comparison.
    /// A type is assignable to another if it is a subtype (via base type chain or interface implementation).
    /// </summary>
    private static bool IsReceiverAssignableTo(IType source, IType target)
    {
        // Same reference — trivially assignable.
        if (ReferenceEquals(source, target))
            return true;

        // Everything is assignable to unknown.
        if (target is UnknownType)
            return true;

        // Nothing is assignable from unknown to a concrete type.
        if (source is UnknownType)
            return false;

        // Check base type chain for class types.
        if (source is ClassType sourceClass)
        {
            // Walk the base type chain.
            var current = sourceClass.BaseType;
            while (current is not null)
            {
                if (ReferenceEquals(current, target))
                    return true;
                current = (current as ClassType)?.BaseType;
            }

            // Check interface implementations.
            foreach (var iface in sourceClass.Interfaces)
            {
                if (ReferenceEquals(iface, target))
                    return true;
            }
        }

        // Check struct interface implementations.
        if (source is StructType sourceStruct)
        {
            foreach (var iface in sourceStruct.Interfaces)
            {
                if (ReferenceEquals(iface, target))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether an extension member's declaring node has the <c>static</c> modifier.
    /// Used during static extension member lookup (Task 7.4) to filter for static members only.
    /// </summary>
    private static bool IsExtensionMemberStatic(Storm.Runtime.SyntaxNode? declaringNode)
    {
        return declaringNode switch
        {
            MethodDeclarationNode md => md.Modifiers.Any(m => m is StaticModifierModifierNode),
            BlockPropertyPropertyDeclarationNode bp => bp.Modifiers.Any(m => m is StaticModifierModifierNode),
            ExpressionBodiedPropertyPropertyDeclarationNode ep => ep.Modifiers.Any(m => m is StaticModifierModifierNode),
            _ => false,
        };
    }

    /// <summary>
    /// Attempts to unify an extension's receiver type pattern with the actual receiver type,
    /// extracting type argument bindings for the extension's type parameters.
    /// </summary>
    /// <param name="pattern">The extension's receiver type pattern (e.g., the resolved type for <c>List&lt;T&gt;</c>).</param>
    /// <param name="actual">The actual receiver type at the call site (e.g., <c>List&lt;int&gt;</c>).</param>
    /// <param name="typeParameters">The extension block's declared type parameters.</param>
    /// <returns>
    /// A list of concrete type bindings (one per type parameter, in order) if unification succeeds;
    /// <c>null</c> if unification fails.
    /// </returns>
    private static IReadOnlyList<IType>? TryUnifyReceiverType(
        IType pattern,
        IType actual,
        IReadOnlyList<IType> typeParameters)
    {
        // Build a dictionary to collect bindings: type parameter name → concrete type.
        var bindings = new Dictionary<string, IType>();

        if (!UnifyTypes(pattern, actual, typeParameters, bindings))
            return null;

        // Ensure all type parameters have been bound.
        var result = new IType[typeParameters.Count];
        for (var i = 0; i < typeParameters.Count; i++)
        {
            var (paramName, _) = GetTypeParameterNameAndOrdinal(typeParameters[i]);
            if (!bindings.TryGetValue(paramName, out var bound))
                return null; // Not all type parameters could be inferred.
            result[i] = bound;
        }

        return result;
    }

    /// <summary>
    /// Recursively unifies a type pattern with an actual type, collecting type parameter bindings.
    /// </summary>
    /// <returns><c>true</c> if unification succeeds; <c>false</c> otherwise.</returns>
    private static bool UnifyTypes(
        IType pattern,
        IType actual,
        IReadOnlyList<IType> typeParameters,
        Dictionary<string, IType> bindings)
    {
        // Case 1: The pattern is a type parameter — bind it to the actual type.
        if (pattern is TypeParameterType tpt)
        {
            // Verify this is one of the extension's own type parameters.
            var isExtensionTypeParam = false;
            for (var i = 0; i < typeParameters.Count; i++)
            {
                var (paramName, paramOrdinal) = GetTypeParameterNameAndOrdinal(typeParameters[i]);
                if (tpt.Name == paramName && tpt.Ordinal == paramOrdinal)
                {
                    isExtensionTypeParam = true;
                    break;
                }
            }

            if (isExtensionTypeParam)
            {
                // If already bound, check consistency.
                if (bindings.TryGetValue(tpt.Name, out var existing))
                {
                    return ReferenceEquals(existing, actual);
                }

                bindings[tpt.Name] = actual;
                return true;
            }

            // Not one of our type parameters — must match exactly.
            return ReferenceEquals(pattern, actual);
        }

        // Case 2: The pattern is a ConstructedType (e.g., List<T>) — the actual must also
        // be a ConstructedType with the same generic definition and matching arity.
        if (pattern is ConstructedType patternConstructed)
        {
            if (actual is ConstructedType actualConstructed)
            {
                // Generic definitions must match.
                if (!ReferenceEquals(patternConstructed.GenericDefinition, actualConstructed.GenericDefinition))
                    return false;

                // Arity must match.
                if (patternConstructed.TypeArguments.Count != actualConstructed.TypeArguments.Count)
                    return false;

                // Recursively unify each type argument.
                for (var i = 0; i < patternConstructed.TypeArguments.Count; i++)
                {
                    if (!UnifyTypes(patternConstructed.TypeArguments[i], actualConstructed.TypeArguments[i], typeParameters, bindings))
                        return false;
                }

                return true;
            }

            // The actual is not a ConstructedType — unification fails.
            return false;
        }

        // Case 3: The pattern is a plain type (ClassType, StructType, InterfaceType, etc.)
        // with type parameters on it (it's a generic definition used as the receiver pattern).
        // For example, extension<T>(List<T> self) where the receiver type resolves to the
        // generic definition ClassType "List" with TypeParameters [T].
        // The actual must be a ConstructedType whose GenericDefinition matches.
        if (pattern is ClassType patternClass && patternClass.TypeParameters.Count > 0)
        {
            if (actual is ConstructedType actualCt && ReferenceEquals(actualCt.GenericDefinition, patternClass))
            {
                // Unify the class's type parameters with the actual's type arguments.
                for (var i = 0; i < patternClass.TypeParameters.Count && i < actualCt.TypeArguments.Count; i++)
                {
                    if (!UnifyTypes(patternClass.TypeParameters[i], actualCt.TypeArguments[i], typeParameters, bindings))
                        return false;
                }
                return true;
            }
            return false;
        }

        if (pattern is StructType patternStruct && patternStruct.TypeParameters.Count > 0)
        {
            if (actual is ConstructedType actualCt && ReferenceEquals(actualCt.GenericDefinition, patternStruct))
            {
                for (var i = 0; i < patternStruct.TypeParameters.Count && i < actualCt.TypeArguments.Count; i++)
                {
                    if (!UnifyTypes(patternStruct.TypeParameters[i], actualCt.TypeArguments[i], typeParameters, bindings))
                        return false;
                }
                return true;
            }
            return false;
        }

        if (pattern is InterfaceType patternInterface && patternInterface.TypeParameters.Count > 0)
        {
            if (actual is ConstructedType actualCt && ReferenceEquals(actualCt.GenericDefinition, patternInterface))
            {
                for (var i = 0; i < patternInterface.TypeParameters.Count && i < actualCt.TypeArguments.Count; i++)
                {
                    if (!UnifyTypes(patternInterface.TypeParameters[i], actualCt.TypeArguments[i], typeParameters, bindings))
                        return false;
                }
                return true;
            }
            return false;
        }

        // Case 4: Non-generic, non-type-parameter types — must match by reference.
        return ReferenceEquals(pattern, actual);
    }

    /// <summary>
    /// Substitutes type parameters with type arguments in a member type if a substitution mapping is available.
    /// Used when looking up members on a <see cref="ConstructedType"/>.
    /// </summary>
    private static IType SubstituteMemberTypeIfNeeded(
        IType memberType,
        IReadOnlyList<IType>? typeParams,
        IReadOnlyList<IType>? typeArgs)
    {
        if (typeParams is null || typeArgs is null || typeParams.Count == 0)
            return memberType;

        return SubstituteTypeInMember(memberType, typeParams, typeArgs);
    }

    /// <summary>
    /// Substitutes type parameters with corresponding type arguments in a type.
    /// - If the type is a <see cref="TypeParameterType"/> matching one of the type params, returns the corresponding type arg.
    /// - If the type is a <see cref="ConstructedType"/>, recursively substitutes its type arguments.
    /// - Otherwise, returns the type unchanged.
    /// </summary>
    private static IType SubstituteTypeInMember(IType type, IReadOnlyList<IType> typeParams, IReadOnlyList<IType> typeArgs)
    {
        if (type is TypeParameterType tpt)
        {
            for (var i = 0; i < typeParams.Count; i++)
            {
                var (paramName, paramOrdinal) = GetTypeParameterNameAndOrdinal(typeParams[i]);
                if (tpt.Name == paramName && tpt.Ordinal == paramOrdinal)
                    return i < typeArgs.Count ? typeArgs[i] : type;
            }
            return type;
        }

        if (type is ConstructedType ct)
        {
            var substitutedArgs = new IType[ct.TypeArguments.Count];
            var changed = false;
            for (var i = 0; i < ct.TypeArguments.Count; i++)
            {
                substitutedArgs[i] = SubstituteTypeInMember(ct.TypeArguments[i], typeParams, typeArgs);
                if (!ReferenceEquals(substitutedArgs[i], ct.TypeArguments[i]))
                    changed = true;
            }
            return changed ? new ConstructedType(ct.GenericDefinition, substitutedArgs) : ct;
        }

        if (type is ConstructedTraitType ctt)
        {
            var substitutedArgs = new IType[ctt.TypeArguments.Count];
            var changed = false;
            for (var i = 0; i < ctt.TypeArguments.Count; i++)
            {
                substitutedArgs[i] = SubstituteTypeInMember(ctt.TypeArguments[i], typeParams, typeArgs);
                if (!ReferenceEquals(substitutedArgs[i], ctt.TypeArguments[i]))
                    changed = true;
            }
            return changed ? new ConstructedTraitType(ctt.GenericTraitDefinition, substitutedArgs) : ctt;
        }

        return type;
    }

    /// <summary>
    /// Walks the inherited interfaces of an <see cref="InterfaceType"/> transitively
    /// to find a member by name. Uses a visited set to handle diamond inheritance
    /// and prevent infinite loops.
    /// </summary>
    /// <returns>The member's declared type if found; <c>null</c> otherwise.</returns>
    private static IType? LookupInheritedInterfaceMember(
        InterfaceType interfaceType,
        string memberName)
    {
        var visited = new HashSet<InterfaceType>(ReferenceEqualityComparer.Instance);
        return LookupInheritedInterfaceMemberCore(interfaceType.Interfaces, memberName, visited);
    }

    private static IType? LookupInheritedInterfaceMemberCore(
        IReadOnlyList<IType> interfaces,
        string memberName,
        HashSet<InterfaceType> visited)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (!visited.Add(iface))
                continue;

            var member = iface.MemberScope.Lookup(memberName);
            if (member is not null)
            {
                return member.DeclaredType ?? ErrorType.Instance;
            }

            var inherited = LookupInheritedInterfaceMemberCore(iface.Interfaces, memberName, visited);
            if (inherited is not null)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>
    /// Looks up a member symbol by name in an interface's inherited interfaces transitively.
    /// Returns the <see cref="ISymbol"/> if found; <c>null</c> otherwise.
    /// Used for constrained type parameter member lookup in <see cref="LookupMemberSymbol"/>.
    /// </summary>
    private static ISymbol? LookupInheritedInterfaceMemberSymbol(
        InterfaceType interfaceType,
        string memberName)
    {
        var visited = new HashSet<InterfaceType>(ReferenceEqualityComparer.Instance);
        return LookupInheritedInterfaceMemberSymbolCore(interfaceType.Interfaces, memberName, visited);
    }

    private static ISymbol? LookupInheritedInterfaceMemberSymbolCore(
        IReadOnlyList<IType> interfaces,
        string memberName,
        HashSet<InterfaceType> visited)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (!visited.Add(iface))
                continue;

            var member = iface.MemberScope.Lookup(memberName);
            if (member is not null)
                return member;

            var inherited = LookupInheritedInterfaceMemberSymbolCore(iface.Interfaces, memberName, visited);
            if (inherited is not null)
                return inherited;
        }

        return null;
    }

    /// <summary>
    /// Looks up a member symbol by name on a target type.
    /// Similar to <see cref="LookupMemberType"/> but returns the <see cref="ISymbol"/> instead of the type.
    /// Used for checking method type parameters on generic method invocations.
    /// </summary>
    private static ISymbol? LookupMemberSymbol(
        IType targetType,
        string memberName,
        IAmbientTypeResolver? ambientTypeResolver,
        Scope fileScope,
        SymbolTable symbolTable)
    {
        if (targetType is ErrorType)
            return null;

        // ── Constrained type parameter member access (Requirements 6.1–6.4, 6.7) ──
        // When the receiver is a TypeParameterType with constraints, look up the
        // member in the constraint types (interfaces, traits, base class).
        if (targetType is TypeParameterType typeParam && typeParam.Constraints is not null)
        {
            var constraints = typeParam.Constraints;

            // Check interface constraints.
            foreach (var ifaceConstraint in constraints.InterfaceConstraints)
            {
                if (ifaceConstraint is InterfaceType iface)
                {
                    var member = iface.MemberScope.Lookup(memberName);
                    if (member is not null)
                        return member;

                    // Also check inherited interfaces transitively.
                    var inherited = LookupInheritedInterfaceMemberSymbol(iface, memberName);
                    if (inherited is not null)
                        return inherited;
                }
            }

            // Check trait constraints.
            foreach (var traitConstraint in constraints.TraitConstraints)
            {
                if (traitConstraint is TraitType trait)
                {
                    var member = trait.MemberScope.Lookup(memberName);
                    if (member is not null)
                        return member;
                }
            }

            // Check base class constraint (including inherited members).
            if (constraints.BaseClassConstraint is ClassType baseClass)
            {
                ClassType? current = baseClass;
                while (current is not null)
                {
                    foreach (var m in current.MemberScope.Members)
                    {
                        if (m.Name == memberName)
                            return m;
                    }

                    current = current.BaseType as ClassType;
                }
            }

            return null;
        }

        // For ConstructedType, look up in the generic definition's member scope.
        if (targetType is ConstructedType ct)
            targetType = ct.GenericDefinition;

        IScope? memberScope = targetType switch
        {
            ClassType classType => classType.MemberScope,
            StructType structType => structType.MemberScope,
            InterfaceType interfaceType => interfaceType.MemberScope,
            _ => null,
        };

        if (targetType is ClassType classTarget)
        {
            ClassType? current = classTarget;
            while (current is not null)
            {
                foreach (var m in current.MemberScope.Members)
                {
                    if (m.Name == memberName)
                        return m;
                }
                current = current.BaseType as ClassType;
            }
        }
        else if (memberScope is not null)
        {
            var member = memberScope.Lookup(memberName);
            if (member is not null)
                return member;
        }

        return null;
    }

    /// <summary>
    /// Counts the number of type arguments in a <see cref="TypeArgumentListNode"/>.
    /// </summary>
    private static int CountTypeArgs(TypeArgumentListNode typeArgList)
    {
        return 1 + typeArgList.TypeArguments.Length;
    }

    /// <summary>
    /// Resolves argument list expressions.
    /// </summary>
    private static void ResolveArgumentList(
        ArgumentListNode argList,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // The ArgumentListNode has a single Expression property (first argument)
        // and additional arguments via Children().
        ResolveExpression(argList.Expression, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Walk children to find additional argument expressions.
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode additionalExpr && !ReferenceEquals(additionalExpr, argList.Expression))
            {
                ResolveExpression(additionalExpr, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            }
        }
    }

    /// <summary>
    /// Determines the type of an integer literal based on its suffix.
    /// </summary>
    private static IType ResolveIntegerLiteralType(string lexeme, Scope fileScope, SymbolTable symbolTable)
    {
        var upper = lexeme.ToUpperInvariant();

        if (upper.EndsWith("UL") || upper.EndsWith("LU"))
            return ResolvePrimitiveByKeyword("ulong", fileScope, symbolTable);
        if (upper.EndsWith("L"))
            return ResolvePrimitiveByKeyword("long", fileScope, symbolTable);
        if (upper.EndsWith("U"))
            return ResolvePrimitiveByKeyword("uint", fileScope, symbolTable);

        return ResolvePrimitiveByKeyword("int", fileScope, symbolTable);
    }

    /// <summary>
    /// Determines the type of a float literal based on its suffix.
    /// </summary>
    private static IType ResolveFloatLiteralType(string lexeme, Scope fileScope, SymbolTable symbolTable)
    {
        var upper = lexeme.ToUpperInvariant();

        if (upper.EndsWith("F"))
            return ResolvePrimitiveByKeyword("float", fileScope, symbolTable);
        if (upper.EndsWith("M"))
            return ResolvePrimitiveByKeyword("decimal", fileScope, symbolTable);

        return ResolvePrimitiveByKeyword("double", fileScope, symbolTable);
    }

    /// <summary>
    /// Resolves a primitive keyword to its stdlib type when the Standard
    /// Library is loaded, falling back to the legacy
    /// <see cref="LegacyPrimitiveTypes"/> singleton otherwise. This
    /// replaces the deleted <c>PrimitiveType.Int</c> etc. property
    /// accessors that used to consult <c>PrimitiveTypeRegistry</c>.
    /// </summary>
    private static IType ResolvePrimitiveByKeyword(string keyword, Scope fileScope, SymbolTable symbolTable)
    {
        if (!KeywordAliases.Map.TryGetValue(keyword, out var qualifiedName))
        {
            // Not a primitive keyword — return legacy fallback.
            return TryResolveWellKnownType(keyword) ?? ErrorType.Instance;
        }

        try
        {
            var globalScope = FindGlobalScope(fileScope) ?? fileScope;
            return LookupQualifiedStdlibType(globalScope, symbolTable, qualifiedName);
        }
        catch (StdlibMissingException)
        {
            // Stdlib not loaded — return legacy fallback.
            return TryResolveWellKnownType(keyword) ?? ErrorType.Instance;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> represents the
    /// Mist <c>string</c> / <c>Standard.String</c> type, regardless of
    /// whether the instance is the legacy <see cref="LegacyPrimitiveTypes.String"/>
    /// fallback or the stdlib <see cref="ClassType"/>.
    /// </summary>
    private static bool IsStringLikeType(IType type)
    {
        if (type is ClassType ct)
        {
            if (ct.IsBackendBacked
                && ct.ContainingNamespace == "Standard"
                && ct.DisplayName == "String")
            {
                return true;
            }
            // Legacy fallback: LegacyPrimitiveTypes.String has DisplayName "string"
            if (ct.DisplayName == "string")
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the common numeric type for binary arithmetic operations.
    /// If either operand is ErrorType, returns ErrorType.
    /// </summary>
    private static IType GetCommonNumericType(IType left, IType right)
    {
        if (left is ErrorType || right is ErrorType)
            return ErrorType.Instance;

        // ── refinement-types feature: arithmetic erasure (Requirement 8.1) ──
        // When a RefinementType participates in arithmetic, the result is the
        // BaseType. Unwrap before numeric ranking so the correct base type is
        // used for the result.
        if (left is RefinementType leftRefinement)
            left = leftRefinement.BaseType;
        if (right is RefinementType rightRefinement)
            right = rightRefinement.BaseType;

        // Numeric widening: decimal > double > float > ulong > long > uint > int > smaller types.
        // Use DisplayName-based ranking to handle both legacy (e.g., "int")
        // and stdlib (e.g., "Int32") type instances.
        static int NumericRank(IType t) => t.DisplayName switch
        {
            "sbyte" or "SByte" => 1,
            "byte" or "Byte" => 2,
            "short" or "Int16" => 3,
            "ushort" or "UInt16" => 4,
            "int" or "Int32" => 5,
            "uint" or "UInt32" => 6,
            "long" or "Int64" => 7,
            "ulong" or "UInt64" => 8,
            "float" or "Single" => 9,
            "double" or "Double" => 10,
            "decimal" or "Decimal" => 11,
            _ => -1,
        };

        var leftRank = NumericRank(left);
        var rightRank = NumericRank(right);

        if (leftRank >= 0 && rightRank >= 0)
        {
            return leftRank >= rightRank ? left : right;
        }

        // If not both numeric, return the left type as a fallback.
        return left;
    }

    /// <summary>
    /// Resolves the <c>field</c> keyword to the backing field of the enclosing property.
    /// Walks the symbol table to find a property symbol whose backing field is in the
    /// current scope chain.
    /// </summary>
    private static IType ResolveFieldKeyword(
        Scope currentScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Walk the scope chain to find the type scope that contains a property with a backing field.
        // The 'field' keyword should resolve to the backing field of the enclosing property.
        // We look for property symbols in the type scope (parent of the accessor scope).
        IScope? scope = currentScope;
        while (scope is not null)
        {
            foreach (var member in scope.Members)
            {
                if (member.Kind == SymbolKind.Property && member is Symbol propSymbol && propSymbol.PropertyMetadata?.BackingField is Symbol backingField)
                {
                    return backingField.DeclaredType ?? ErrorType.Instance;
                }
            }
            scope = scope.Parent;
        }

        // 'field' keyword used outside a property accessor or in a computed property.
        // Return ErrorType — the type checker will report the appropriate diagnostic.
        return ErrorType.Instance;
    }

    /// <summary>
    /// Checks for duplicate local variable declarations in the current scope.
    /// </summary>
    private static void CheckDuplicateLocal(string name, Scope scope, SourceText? sourceText, DiagnosticBag diagnostics)
    {
        foreach (var member in scope.Members)
        {
            if (member.Name == name)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_DUPLICATE_DECLARATION"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"A local variable named '{name}' is already declared in this scope.",
                    Location: default,
                    CompilationUnitPath: sourceText?.Path ?? "<unknown>",
                    AnalyzerName: null));
                return;
            }
        }
    }

    /// <summary>
    /// Extracts the full qualified name from a QualifiedIdentifierNode.
    /// </summary>
    private static string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId, SourceText? sourceText)
    {
        if (sourceText is not null)
        {
            var span = qualifiedId.Span;
            if (span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
            {
                var extracted = sourceText.Contents.Substring(span.Start, span.Length);
                // Validate that the extracted text looks like a qualified identifier
                // (only letters, digits, underscores, and dots). If not, the source
                // text is from the wrong file (overlapping AST spans across files).
                if (IsValidQualifiedIdentifier(extracted))
                    return extracted;
            }
        }

        return qualifiedId.IDENTIFIER.Lexeme;
    }

    /// <summary>
    /// Checks whether a string looks like a valid qualified identifier (e.g., "Foo.Bar.Baz").
    /// Used to validate source text extraction results.
    /// </summary>
    private static bool IsValidQualifiedIdentifier(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (var ch in text)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.')
                return false;
        }
        return true;
    }

    // ─── Shared Utilities ────────────────────────────────────────────────

    /// <summary>
    /// Extracts all type names from a <see cref="BaseListNode"/> as a string array.
    /// The first entry comes from <c>First</c>, and additional entries from <c>BaseTypeRefs</c>.
    /// </summary>
    private static string[] ExtractBaseListNames(BaseListNode baseList)
    {
        var names = new List<string>();
        var firstName = baseList.First.Name.Lexeme;
        if (!string.IsNullOrEmpty(firstName))
        {
            names.Add(firstName);
        }
        foreach (var rest in baseList.BaseTypeRefs)
        {
            var restName = rest.Name.Lexeme;
            if (!string.IsNullOrEmpty(restName))
            {
                names.Add(restName);
            }
        }
        return names.ToArray();
    }

    private static string ExtractNamespaceName(
        NamespaceDeclarationNode namespaceNode,
        SourceText? sourceText)
    {
        QualifiedIdentifierNode? qualifiedId = namespaceNode switch
        {
            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Name,
            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Name,
            _ => null
        };

        if (qualifiedId is null)
        {
            return "<unknown>";
        }

        if (sourceText is not null)
        {
            var span = qualifiedId.Span;
            if (span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
            {
                return sourceText.Contents.Substring(span.Start, span.Length);
            }
        }

        return qualifiedId.IDENTIFIER.Lexeme;
    }

    private static ImmutableArray<TypeDeclarationNode> GetNamespaceBodyTypes(
        NamespaceDeclarationNode namespaceNode)
    {
        var bodyItems = namespaceNode switch
        {
            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
            _ => ImmutableArray<NamespaceBodyItemNode>.Empty
        };

        var builder = ImmutableArray.CreateBuilder<TypeDeclarationNode>();
        foreach (var item in bodyItems)
        {
            if (item is NamespaceTypeItemNamespaceBodyItemNode typeItem)
            {
                builder.Add(typeItem.TypeDeclaration);
            }
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<ExtensionBlockDeclarationNode> GetNamespaceBodyExtensions(
        NamespaceDeclarationNode namespaceNode)
    {
        var bodyItems = namespaceNode switch
        {
            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
            _ => ImmutableArray<NamespaceBodyItemNode>.Empty
        };

        var builder = ImmutableArray.CreateBuilder<ExtensionBlockDeclarationNode>();
        foreach (var item in bodyItems)
        {
            if (item is NamespaceExtensionItemNamespaceBodyItemNode extensionItem)
            {
                builder.Add(extensionItem.ExtensionDeclaration);
            }
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<TypeAliasDeclarationNode> GetNamespaceBodyTypeAliases(
        NamespaceDeclarationNode namespaceNode)
    {
        var bodyItems = namespaceNode switch
        {
            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
            _ => ImmutableArray<NamespaceBodyItemNode>.Empty
        };

        var builder = ImmutableArray.CreateBuilder<TypeAliasDeclarationNode>();
        foreach (var item in bodyItems)
        {
            if (item is NamespaceTypeAliasItemNamespaceBodyItemNode typeAliasItem)
            {
                builder.Add(typeAliasItem.TypeAliasDeclaration);
            }
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<NewtypeDeclarationNode> GetNamespaceBodyNewtypes(
        NamespaceDeclarationNode namespaceNode)
    {
        var bodyItems = namespaceNode switch
        {
            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
            _ => ImmutableArray<NamespaceBodyItemNode>.Empty
        };

        var builder = ImmutableArray.CreateBuilder<NewtypeDeclarationNode>();
        foreach (var item in bodyItems)
        {
            if (item is NamespaceNewtypeItemNamespaceBodyItemNode newtypeItem)
            {
                builder.Add(newtypeItem.NewtypeDeclaration);
            }
        }
        return builder.ToImmutable();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Post-Pass 2 — Defaultability Analysis
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The set of stdlib display names that are intrinsically defaultable.
    /// Covers both the stdlib <c>Standard.*</c> names and the legacy
    /// keyword-based names used when stdlib is not loaded.
    /// </summary>
    private static readonly HashSet<string> IntrinsicDefaultableNames = new(StringComparer.Ordinal)
    {
        // Stdlib names (Standard.*)
        "Int32", "UInt32", "Int64", "UInt64", "Int16", "UInt16",
        "Byte", "SByte", "Single", "Double", "Decimal",
        "Boolean", "Char", "String",
        // Legacy keyword names (when stdlib is not loaded)
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char", "string",
    };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> is intrinsically
    /// defaultable — i.e., a primitive type or <c>string</c>.
    /// </summary>
    private static bool IsIntrinsicallyDefaultable(IType type)
    {
        // Primitive types are StructType (stdlib) or StructType (legacy).
        // String is ClassType (stdlib) or ClassType (legacy).
        if (type is StructType or ClassType)
        {
            return IntrinsicDefaultableNames.Contains(type.DisplayName);
        }
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> is defaultable:
    /// either intrinsically defaultable (primitive/string), or a ClassType/StructType
    /// whose <see cref="ClassType.Defaultability"/> or <see cref="StructType.Defaultability"/>
    /// is <see cref="DefaultabilityStatus.Inferred"/> or <see cref="DefaultabilityStatus.Explicit"/>.
    /// </summary>
    private static bool IsTypeDefaultable(IType type)
    {
        if (IsIntrinsicallyDefaultable(type))
            return true;

        return type switch
        {
            ClassType ct => ct.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit,
            StructType st => st.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit,
            _ => false,
        };
    }

    /// <summary>
    /// Computes defaultability for all <see cref="ClassType"/> and <see cref="StructType"/>
    /// symbols in the symbol table. Processes types in topological order so that
    /// a type's field types are resolved before the type itself is processed.
    /// </summary>
    private static void ComputeDefaultability(
        SymbolTable symbolTable,
        DiagnosticBag diagnostics)
    {
        // Step 1: Collect all class and struct type symbols.
        var typeSymbols = new List<(ISymbol Symbol, IType Type)>();
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Struct)
            {
                var type = symbol.DeclaredType;
                if (type is ClassType or StructType)
                {
                    // Skip intrinsically defaultable types (stdlib primitives and string).
                    if (IsIntrinsicallyDefaultable(type))
                        continue;

                    // Skip generic type definitions (types with unbound type parameters).
                    // Generic types like Optional<T> cannot be defaulted without concrete
                    // type arguments — defaultability is only meaningful for closed types.
                    if (type is ClassType ct && ct.TypeParameters.Count > 0)
                        continue;
                    if (type is StructType st && st.TypeParameters.Count > 0)
                        continue;

                    typeSymbols.Add((symbol, type));
                }
            }
        }

        // Step 2: Build a dependency graph. A type depends on the types of its fields.
        // Map from type → index in typeSymbols list.
        var typeToIndex = new Dictionary<IType, int>();
        for (var i = 0; i < typeSymbols.Count; i++)
        {
            typeToIndex[typeSymbols[i].Type] = i;
        }

        // Adjacency list: dependencies[i] = set of indices that type i depends on.
        var dependencies = new List<HashSet<int>>();
        for (var i = 0; i < typeSymbols.Count; i++)
        {
            var deps = new HashSet<int>();
            var memberScope = typeSymbols[i].Type switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope is not null)
            {
                foreach (var member in memberScope.Members)
                {
                    if (member.Kind == SymbolKind.Field && member.DeclaredType is not null)
                    {
                        if (typeToIndex.TryGetValue(member.DeclaredType, out var depIndex))
                        {
                            deps.Add(depIndex);
                        }
                    }
                }
            }

            // Also depend on the base type if it's a class with a base.
            if (typeSymbols[i].Type is ClassType classType && classType.BaseType is not null)
            {
                if (typeToIndex.TryGetValue(classType.BaseType, out var baseIndex))
                {
                    deps.Add(baseIndex);
                }
            }

            dependencies.Add(deps);
        }

        // Step 3: Topological sort using Kahn's algorithm.
        var inDegree = new int[typeSymbols.Count];
        var reverseDeps = new List<List<int>>();
        for (var i = 0; i < typeSymbols.Count; i++)
        {
            reverseDeps.Add(new List<int>());
        }

        for (var i = 0; i < typeSymbols.Count; i++)
        {
            foreach (var dep in dependencies[i])
            {
                reverseDeps[dep].Add(i);
                inDegree[i]++;
            }
        }

        var queue = new Queue<int>();
        for (var i = 0; i < typeSymbols.Count; i++)
        {
            if (inDegree[i] == 0)
                queue.Enqueue(i);
        }

        var processingOrder = new List<int>();
        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            processingOrder.Add(idx);

            foreach (var dependent in reverseDeps[idx])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        // Any types not in processingOrder have circular dependencies.
        // Process them after the topological order (class fields are pointer-sized,
        // so circular references between classes are allowed per Requirement 8.6).
        for (var i = 0; i < typeSymbols.Count; i++)
        {
            if (!processingOrder.Contains(i))
                processingOrder.Add(i);
        }

        // Step 4: Process each type in topological order.
        for (var orderIdx = 0; orderIdx < processingOrder.Count; orderIdx++)
        {
            var idx = processingOrder[orderIdx];
            var (symbol, type) = typeSymbols[idx];

            switch (type)
            {
                case ClassType classType:
                    ComputeDefaultabilityForType(classType, symbol, symbolTable, diagnostics);
                    break;
                case StructType structType:
                    ComputeDefaultabilityForType(structType, symbol, symbolTable, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Computes defaultability for a single <see cref="ClassType"/>.
    /// </summary>
    private static void ComputeDefaultabilityForType(
        ClassType classType,
        ISymbol typeSymbol,
        SymbolTable symbolTable,
        DiagnosticBag diagnostics)
    {
        // If already set to Explicit by Pass 1 (has a default block), keep it.
        // We still need to compute DefaultValues.
        var hasExplicitBlock = classType.Defaultability == DefaultabilityStatus.Explicit;

        // Check base class defaultability.
        if (classType.BaseType is ClassType baseClass)
        {
            if (!IsIntrinsicallyDefaultable(baseClass) &&
                baseClass.Defaultability is not (DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit))
            {
                classType.Defaultability = DefaultabilityStatus.NotDefaultable;
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.DefaultBaseNotDefaultable,
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Type '{classType.DisplayName}' cannot be defaultable because its base class '{baseClass.DisplayName}' is not defaultable.",
                    Location: default,
                    CompilationUnitPath: "<unknown>",
                    AnalyzerName: null));
                return;
            }
        }

        // Collect fields from this type's member scope.
        var fields = new List<ISymbol>();
        foreach (var member in classType.MemberScope.Members)
        {
            if (member.Kind == SymbolKind.Field)
                fields.Add(member);
        }

        // Check if all fields are defaultable (considering the explicit default block for type bindings).
        if (!hasExplicitBlock)
        {
            // No explicit block — check if all fields have defaultable types.
            foreach (var field in fields)
            {
                if (field.DeclaredType is null)
                    continue;

                if (!IsTypeDefaultable(field.DeclaredType))
                {
                    classType.Defaultability = DefaultabilityStatus.NotDefaultable;

                    // Only emit the diagnostic for interface/trait fields that need
                    // a type binding. Other non-defaultable field types (type parameters,
                    // void, etc.) silently make the type not-defaultable.
                    if (field.DeclaredType is InterfaceType or TraitType)
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticId.DefaultRequiresTypeBinding,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"Field '{field.Name}' on type '{classType.DisplayName}' has type '{field.DeclaredType.DisplayName}' which is not defaultable. Add a default block with a type binding for this field.",
                            Location: default,
                            CompilationUnitPath: "<unknown>",
                            AnalyzerName: null));
                    }
                    return;
                }
            }

            // All fields are defaultable — mark as Inferred.
            classType.Defaultability = DefaultabilityStatus.Inferred;
        }

        // Compute default values.
        var defaultValues = ComputeDefaultValues(classType.MemberScope, classType.BaseType as ClassType,
            classType.DefaultBlockNode, symbolTable);
        classType.DefaultValues = defaultValues;
    }

    /// <summary>
    /// Computes defaultability for a single <see cref="StructType"/>.
    /// </summary>
    private static void ComputeDefaultabilityForType(
        StructType structType,
        ISymbol typeSymbol,
        SymbolTable symbolTable,
        DiagnosticBag diagnostics)
    {
        // If already set to Explicit by Pass 1 (has a default block), keep it.
        var hasExplicitBlock = structType.Defaultability == DefaultabilityStatus.Explicit;

        // Structs don't have base types in Mist, so no base class check needed.

        // Collect fields from this type's member scope.
        var fields = new List<ISymbol>();
        foreach (var member in structType.MemberScope.Members)
        {
            if (member.Kind == SymbolKind.Field)
                fields.Add(member);
        }

        // Check if all fields are defaultable.
        if (!hasExplicitBlock)
        {
            foreach (var field in fields)
            {
                if (field.DeclaredType is null)
                    continue;

                if (!IsTypeDefaultable(field.DeclaredType))
                {
                    structType.Defaultability = DefaultabilityStatus.NotDefaultable;

                    // Only emit the diagnostic for interface/trait fields that need
                    // a type binding. Other non-defaultable field types (type parameters,
                    // void, etc.) silently make the type not-defaultable.
                    if (field.DeclaredType is InterfaceType or TraitType)
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticId.DefaultRequiresTypeBinding,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"Field '{field.Name}' on type '{structType.DisplayName}' has type '{field.DeclaredType.DisplayName}' which is not defaultable. Add a default block with a type binding for this field.",
                            Location: default,
                            CompilationUnitPath: "<unknown>",
                            AnalyzerName: null));
                    }
                    return;
                }
            }

            structType.Defaultability = DefaultabilityStatus.Inferred;
        }

        // Compute default values.
        var defaultValues = ComputeDefaultValues(structType.MemberScope, baseClass: null,
            structType.DefaultBlockNode, symbolTable);
        structType.DefaultValues = defaultValues;
    }

    /// <summary>
    /// Computes the <see cref="DefaultFieldValue"/> dictionary for a defaultable type.
    /// Combines inherited base defaults, field initializers, zero-initialization,
    /// explicit overrides, and type bindings.
    /// </summary>
    private static Dictionary<string, DefaultFieldValue> ComputeDefaultValues(
        IScope memberScope,
        ClassType? baseClass,
        DefaultBlockDeclarationNode? defaultBlockNode,
        SymbolTable symbolTable)
    {
        var result = new Dictionary<string, DefaultFieldValue>();

        // Step 1: Inherit base class defaults.
        if (baseClass is not null && baseClass.DefaultValues is not null)
        {
            foreach (var (fieldName, baseDefault) in baseClass.DefaultValues)
            {
                result[fieldName] = new DefaultFieldValue
                {
                    FieldName = baseDefault.FieldName,
                    FieldType = baseDefault.FieldType,
                    ValueKind = DefaultValueKind.InheritedDefault,
                    ConstantValue = baseDefault.ConstantValue,
                    ConcreteType = baseDefault.ConcreteType,
                    NestedOverrides = baseDefault.NestedOverrides,
                };
            }
        }

        // Step 2: Compute defaults for this type's own fields.
        foreach (var member in memberScope.Members)
        {
            if (member.Kind != SymbolKind.Field || member.DeclaredType is null)
                continue;

            // Skip fields that were inherited (already in result from base).
            if (result.ContainsKey(member.Name))
                continue;

            // Skip static fields — they are not part of instance default values.
            var declaringNode = symbolTable.GetDeclaringNode(member);
            if (declaringNode is FieldDeclarationNode staticCheck
                && staticCheck.Modifiers.Any(m => m is StaticModifierModifierNode))
                continue;

            // Skip backing fields for static properties (auto-properties generate
            // backing fields named _PropertyName_k__BackingField registered against
            // the property AST node, not a FieldDeclarationNode).
            if (declaringNode is BlockPropertyPropertyDeclarationNode propCheck
                && propCheck.Modifiers.Any(m => m is StaticModifierModifierNode))
                continue;

            var fieldType = member.DeclaredType;

            // Check if the field has an initializer expression.
            object? initializerValue = null;
            var hasInitializer = false;

            if (declaringNode is FieldDeclarationNode fieldDecl && fieldDecl.Initializer is ExpressionNode initExpr)
            {
                initializerValue = TryExtractConstantValue(initExpr);
                hasInitializer = true;
            }

            if (hasInitializer)
            {
                result[member.Name] = new DefaultFieldValue
                {
                    FieldName = member.Name,
                    FieldType = fieldType,
                    ValueKind = DefaultValueKind.FieldInitializer,
                    ConstantValue = initializerValue,
                };
            }
            else if (IsIntrinsicallyDefaultable(fieldType))
            {
                result[member.Name] = new DefaultFieldValue
                {
                    FieldName = member.Name,
                    FieldType = fieldType,
                    ValueKind = DefaultValueKind.ZeroInitialized,
                    ConstantValue = GetZeroValue(fieldType),
                };
            }
            else if (fieldType is ClassType nestedClass && IsTypeDefaultable(nestedClass))
            {
                result[member.Name] = new DefaultFieldValue
                {
                    FieldName = member.Name,
                    FieldType = fieldType,
                    ValueKind = DefaultValueKind.NestedTypeDefault,
                    ConcreteType = nestedClass,
                };
            }
            else if (fieldType is StructType nestedStruct && IsTypeDefaultable(nestedStruct))
            {
                result[member.Name] = new DefaultFieldValue
                {
                    FieldName = member.Name,
                    FieldType = fieldType,
                    ValueKind = DefaultValueKind.NestedTypeDefault,
                    ConcreteType = nestedStruct,
                };
            }
            else
            {
                // Non-defaultable field type — this should only happen for
                // types with explicit default blocks that provide type bindings.
                // The field will be handled by the explicit block processing below.
                result[member.Name] = new DefaultFieldValue
                {
                    FieldName = member.Name,
                    FieldType = fieldType,
                    ValueKind = DefaultValueKind.ZeroInitialized,
                    ConstantValue = null,
                };
            }
        }

        // Step 3: Apply explicit default block overrides and type bindings.
        if (defaultBlockNode is not null)
        {
            foreach (var entry in defaultBlockNode.Entries)
            {
                switch (entry)
                {
                    case DefaultFieldOverrideDefaultFieldInitializerNode fieldOverride:
                    {
                        var fieldName = fieldOverride.FieldName.Lexeme;
                        var overrideValue = TryExtractConstantValue(fieldOverride.Value);

                        // Find the field's type from the existing result or member scope.
                        IType? fieldType = null;
                        if (result.TryGetValue(fieldName, out var existing))
                        {
                            fieldType = existing.FieldType;
                        }
                        else
                        {
                            // Look up in member scope (including inherited).
                            var fieldSymbol = memberScope.Lookup(fieldName);
                            fieldType = fieldSymbol?.DeclaredType;
                        }

                        result[fieldName] = new DefaultFieldValue
                        {
                            FieldName = fieldName,
                            FieldType = fieldType!,
                            ValueKind = DefaultValueKind.ExplicitOverride,
                            ConstantValue = overrideValue,
                        };
                        break;
                    }

                    case DefaultTypeBindingDefaultFieldInitializerNode typeBinding:
                    {
                        var fieldName = typeBinding.FieldName.Lexeme;

                        // Find the field's type.
                        IType? fieldType = null;
                        if (result.TryGetValue(fieldName, out var existing))
                        {
                            fieldType = existing.FieldType;
                        }
                        else
                        {
                            var fieldSymbol = memberScope.Lookup(fieldName);
                            fieldType = fieldSymbol?.DeclaredType;
                        }

                        // Extract the concrete type name from the type binding.
                        // The type reference has already been resolved in Pass 2,
                        // but we need to find the concrete type by name.
                        var concreteTypeName = ExtractTypeReferenceName(typeBinding.TypeName);
                        IType? concreteType = FindTypeByName(memberScope, concreteTypeName);

                        // Process nested overrides if present.
                        Dictionary<string, DefaultFieldValue>? nestedOverrides = null;
                        if (typeBinding.NestedEntries.Length > 0)
                        {
                            nestedOverrides = new Dictionary<string, DefaultFieldValue>();
                            foreach (var nested in typeBinding.NestedEntries)
                            {
                                var nestedFieldName = nested.FieldName.Lexeme;
                                var nestedValue = TryExtractConstantValue(nested.Value);

                                // Try to find the nested field's type from the concrete type.
                                IType? nestedFieldType = null;
                                if (concreteType is ClassType concreteCt)
                                {
                                    var nestedSymbol = concreteCt.MemberScope.Lookup(nestedFieldName);
                                    nestedFieldType = nestedSymbol?.DeclaredType;
                                }
                                else if (concreteType is StructType concreteSt)
                                {
                                    var nestedSymbol = concreteSt.MemberScope.Lookup(nestedFieldName);
                                    nestedFieldType = nestedSymbol?.DeclaredType;
                                }

                                nestedOverrides[nestedFieldName] = new DefaultFieldValue
                                {
                                    FieldName = nestedFieldName,
                                    FieldType = nestedFieldType ?? ErrorType.Instance,
                                    ValueKind = DefaultValueKind.ExplicitOverride,
                                    ConstantValue = nestedValue,
                                };
                            }
                        }

                        result[fieldName] = new DefaultFieldValue
                        {
                            FieldName = fieldName,
                            FieldType = fieldType ?? ErrorType.Instance,
                            ValueKind = DefaultValueKind.TypeBinding,
                            ConcreteType = concreteType,
                            NestedOverrides = nestedOverrides,
                        };
                        break;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the zero-initialization value for an intrinsically defaultable type.
    /// </summary>
    private static object? GetZeroValue(IType type)
    {
        return type.DisplayName switch
        {
            // Integer types
            "int" or "Int32" => 0,
            "uint" or "UInt32" => 0u,
            "long" or "Int64" => 0L,
            "ulong" or "UInt64" => 0UL,
            "short" or "Int16" => (short)0,
            "ushort" or "UInt16" => (ushort)0,
            "byte" or "Byte" => (byte)0,
            "sbyte" or "SByte" => (sbyte)0,
            // Floating-point types
            "float" or "Single" => 0.0f,
            "double" or "Double" => 0.0d,
            "decimal" or "Decimal" => 0.0m,
            // Boolean
            "bool" or "Boolean" => false,
            // Char
            "char" or "Char" => '\0',
            // String
            "string" or "String" => "",
            _ => null,
        };
    }

    /// <summary>
    /// Attempts to extract a compile-time constant value from a literal expression.
    /// Returns <c>null</c> if the expression is not a recognized literal.
    /// In v1, only literal expressions are compile-time constants.
    /// </summary>
    private static object? TryExtractConstantValue(ExpressionNode expression)
    {
        // Unwrap the expression to get to the primary node.
        // Expression → UnaryExpression → ... → Primary
        var primary = UnwrapToPrimary(expression);
        if (primary is null)
            return null;

        return primary switch
        {
            IntegerLiteralExpressionPrimaryNode intLit =>
                TryParseIntegerLiteral(intLit.IntegerLiteral.Lexeme),
            HexLiteralExpressionPrimaryNode hexLit =>
                TryParseIntegerLiteral(hexLit.HexLiteral.Lexeme),
            BinaryLiteralExpressionPrimaryNode binLit =>
                TryParseIntegerLiteral(binLit.BinaryLiteral.Lexeme),
            FloatLiteralExpressionPrimaryNode floatLit =>
                TryParseFloatLiteral(floatLit.FloatLiteral.Lexeme),
            StringLiteralExpressionPrimaryNode stringLit =>
                ExtractStringLiteralValue(stringLit.StringLiteral.Lexeme),
            CharLiteralExpressionPrimaryNode charLit =>
                ExtractCharLiteralValue(charLit.CharLiteral.Lexeme),
            TrueLiteralExpressionPrimaryNode => true,
            FalseLiteralExpressionPrimaryNode => false,
            _ => null,
        };
    }

    /// <summary>
    /// Unwraps an expression node down to its innermost <see cref="PrimaryNode"/>.
    /// Returns <c>null</c> if the expression structure is not a simple primary.
    /// </summary>
    private static PrimaryNode? UnwrapToPrimary(ExpressionNode expression)
    {
        // Walk down the expression tree to find the innermost PrimaryNode.
        // The Storm AST wraps primaries in multiple layers of expression nodes
        // (Expression → ConditionalExpression → ... → UnaryExpression → Primary).
        Storm.Runtime.SyntaxNode current = expression;

        for (var depth = 0; depth < 20; depth++)
        {
            if (current is PrimaryNode primary)
                return primary;

            // Look for a single non-token child to follow.
            // Skip null children that may be yielded by generated AST nodes
            // whose optional properties are typed as non-nullable.
            Storm.Runtime.SyntaxNode? next = null;
            var childCount = 0;
            foreach (var child in current.Children())
            {
                if (child is null)
                    continue;

                childCount++;
                if (child is PrimaryNode childPrimary)
                    return childPrimary;

                // Follow the first (and hopefully only) syntax node child.
                if (next is null)
                    next = child;
            }

            // If there are multiple children, this is not a simple literal expression.
            if (childCount > 1 || next is null)
                return null;

            current = next;
        }

        return null;
    }

    /// <summary>
    /// Attempts to parse an integer literal string into a numeric value.
    /// </summary>
    private static object? TryParseIntegerLiteral(string lexeme)
    {
        // Remove underscores (Mist allows _ as digit separator).
        var cleaned = lexeme.Replace("_", "");
        var upper = cleaned.ToUpperInvariant();

        // Check for suffix.
        if (upper.EndsWith("UL") || upper.EndsWith("LU"))
        {
            if (ulong.TryParse(cleaned[..^2], out var val)) return val;
        }
        else if (upper.EndsWith("L"))
        {
            if (long.TryParse(cleaned[..^1], out var val)) return val;
        }
        else if (upper.EndsWith("U"))
        {
            if (uint.TryParse(cleaned[..^1], out var val)) return val;
        }
        else if (upper.StartsWith("0X"))
        {
            if (long.TryParse(cleaned[2..], System.Globalization.NumberStyles.HexNumber, null, out var val))
                return val;
        }
        else if (upper.StartsWith("0B"))
        {
            try { return Convert.ToInt64(cleaned[2..], 2); }
            catch { return null; }
        }
        else
        {
            if (int.TryParse(cleaned, out var val)) return val;
            if (long.TryParse(cleaned, out var lval)) return lval;
        }

        return null;
    }

    /// <summary>
    /// Attempts to parse a float literal string into a numeric value.
    /// </summary>
    private static object? TryParseFloatLiteral(string lexeme)
    {
        var cleaned = lexeme.Replace("_", "");
        var upper = cleaned.ToUpperInvariant();

        if (upper.EndsWith("F"))
        {
            if (float.TryParse(cleaned[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
        }
        else if (upper.EndsWith("M"))
        {
            if (decimal.TryParse(cleaned[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
        }
        else
        {
            if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
        }

        return null;
    }

    /// <summary>
    /// Extracts the string value from a string literal lexeme (removes surrounding quotes).
    /// </summary>
    private static string ExtractStringLiteralValue(string lexeme)
    {
        if (lexeme.Length >= 2 && lexeme[0] == '"' && lexeme[^1] == '"')
            return lexeme[1..^1];
        return lexeme;
    }

    /// <summary>
    /// Extracts the char value from a char literal lexeme (removes surrounding quotes).
    /// </summary>
    private static char ExtractCharLiteralValue(string lexeme)
    {
        if (lexeme.Length >= 3 && lexeme[0] == '\'' && lexeme[^1] == '\'')
        {
            var inner = lexeme[1..^1];
            if (inner.Length == 1)
                return inner[0];
            // Handle escape sequences.
            if (inner.StartsWith('\\') && inner.Length >= 2)
            {
                return inner[1] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '\'' => '\'',
                    '0' => '\0',
                    _ => inner[1],
                };
            }
        }
        return '\0';
    }

    /// <summary>
    /// Extracts the simple type name from a <see cref="TypeReferenceNode"/>.
    /// </summary>
    private static string ExtractTypeReferenceName(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            NamedTypeRefTypeReferenceNode named => named.Name.IDENTIFIER.Lexeme,
            PrimitiveTypeRefTypeReferenceNode primitive => FormatPrimitiveType(primitive.PrimitiveType),
            TupleTypeRefTypeReferenceNode tuple => FormatTupleTypeReference(tuple),
            _ => "",
        };
    }

    /// <summary>
    /// Finds a type by simple name by searching the scope chain upward.
    /// </summary>
    private static IType? FindTypeByName(IScope scope, string typeName)
    {
        var current = scope;
        while (current is not null)
        {
            foreach (var member in current.Members)
            {
                if (member.Kind is SymbolKind.Class or SymbolKind.Struct
                    && member.Name == typeName
                    && member.DeclaredType is not null)
                {
                    return member.DeclaredType;
                }
            }
            current = current.Parent;
        }
        return null;
    }

    private static Diagnostic MakeDiagnosticWithLocation(
        string id,
        DiagnosticSeverity severity,
        string message,
        Storm.Runtime.SyntaxNode node,
        SourceText? sourceText)
    {
        LinePositionSpan location = default;
        string compilationUnitPath = sourceText?.Path ?? "<unknown>";

        if (sourceText is not null && node.Span.Length > 0)
        {
            var mistSpan = new SourceSpan(node.Span.Start, node.Span.Length);
            location = sourceText.ToLinePositionSpan(mistSpan);
        }

        return new Diagnostic(
            new DiagnosticId(id),
            severity,
            PipelineStage.SemanticModelConstruction,
            message,
            Location: location,
            CompilationUnitPath: compilationUnitPath,
            AnalyzerName: null);
    }

    private static Common.Text.SourceSpan ToMistSourceSpan(Storm.Runtime.SourceSpan span)
    {
        return new Common.Text.SourceSpan(span.Start, span.Length);
    }
}
