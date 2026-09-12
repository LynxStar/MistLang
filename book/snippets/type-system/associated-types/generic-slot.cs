using Standard;

namespace Snippet;

internal interface ITransform<TIn, TOut>
{
    TOut Transform(TIn input);
}

internal interface IMapper<TIn>
{
    Snippet.ITransform<TIn, TOut> GetMapper<TOut>();
}
