using System;

namespace Snippet;

public interface IDescribable
{
    string Describe();
}
public interface IFullyDescribable : IDescribable
{
    string DetailedDescribe();
}
