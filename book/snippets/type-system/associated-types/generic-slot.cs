using System;

namespace Snippet;

public interface ITransform<TIn, TOut>
{
    TOut Transform(TIn input);
}
public interface IMapper<TIn>
{
    ITransform<TIn, TOut> GetMapper<TOut>();
}
