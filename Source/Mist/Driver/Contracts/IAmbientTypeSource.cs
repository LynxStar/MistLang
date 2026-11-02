using Mist.ProgramModel.Contracts;

namespace Mist.Driver.Contracts;

/// <summary>
/// The ambient types of one compile: projected into the model before binding, looked up by CLR
/// metadata name during emission, and released with the build. The pipeline takes its source from
/// the caller because the real one loads every assembly of the reference pack, a cost only a Tier 5
/// test may pay; a seam test hands the driver a stub instead.
/// </summary>
public interface IAmbientTypeSource : IAmbientTypeProvider, IDisposable
{
    Type? FindClrType(string fullName);
}