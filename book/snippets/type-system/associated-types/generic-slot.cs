using System;

namespace Snippet;

interface ITransform<TIn, TOut>
{
    TOut Transform(TIn input);
}

interface IMapper<TIn>
{
    Snippet.ITransform<TIn, TOut> GetMapper<TOut>();
}
