using System;

namespace Snippet;

public interface IStringable
{
    string Stringify();
}
public interface IResettable
{
    string GetState();
}
public interface IStatefulProducer<TState>
    where TState : IStringable, IResettable
{
    string GetProducerName();
}
