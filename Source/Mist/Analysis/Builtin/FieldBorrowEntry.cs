using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Records an active borrow relationship for a specific field within a parent binding,
/// including the tier of the borrow and the scope depth at which it was created.
/// When the scope exits, field borrows at that depth are released and the field's
/// state is restored to <see cref="VariableOwnershipState.Live"/> (Req 16 AC 7–8).
/// </summary>
/// <param name="Parent">The parent binding symbol whose field is being lent.</param>
/// <param name="Field">The field symbol that is being borrowed.</param>
/// <param name="Borrower">The symbol that is borrowing the field.</param>
/// <param name="BorrowTier">The ownership tier of the borrow (Borrowed, Frozen, or ReadonlyRef).</param>
/// <param name="ScopeDepth">The lexical scope depth at which this field borrow was created.</param>
public sealed record FieldBorrowEntry(
    ISymbol Parent,
    ISymbol Field,
    ISymbol Borrower,
    OwnershipTier BorrowTier,
    int ScopeDepth);
