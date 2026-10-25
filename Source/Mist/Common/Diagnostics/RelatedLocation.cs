namespace Mist.Common.Diagnostics;

public enum RelatedRole
{
    PreviousDeclaration,
    Import,
    Constraint,
    CloneDefinition,
    Instantiation,
    GeneratedOutput,
    DependencyDeclaration,
}

public readonly record struct RelatedLocation(RelatedRole Role, Location Location);