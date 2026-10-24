using System.Collections.Immutable;

namespace Mist.Backends.Contracts;

internal sealed record DeploymentPlan(ImmutableArray<FileCopy> Copies, int RuntimeAssetsDeployed);