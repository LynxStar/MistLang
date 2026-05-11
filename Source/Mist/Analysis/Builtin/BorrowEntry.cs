using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Records an active borrow relationship between a lender and borrower variable,
/// including the tier of the borrow and the scope depth at which it was created.
/// When the scope exits, borrows at that depth are released and lender states are restored.
/// </summary>
/// <param name="Lender">The symbol whose variable is being lent.</param>
/// <param name="Borrower">The symbol that is borrowing access.</param>
/// <param name="BorrowTier">The ownership tier of the borrow (Borrowed, Frozen, or ReadonlyRef).</param>
/// <param name="ScopeDepth">The lexical scope depth at which this borrow was created.</param>
public sealed record BorrowEntry(
    ISymbol Lender,
    ISymbol Borrower,
    OwnershipTier BorrowTier,
    int ScopeDepth);
