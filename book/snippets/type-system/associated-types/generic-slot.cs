using System;

namespace Snippet;

interface ITransform<TIn, TOut>
{
    TOut Transform(TIn input);
}
interface IMapper<TIn>
{
    ITransform<TIn, TOut> GetMapper<TOut>();
}
