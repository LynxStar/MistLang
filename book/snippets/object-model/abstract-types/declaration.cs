using System;

namespace Snippet;

internal interface IShape
{
    public string Name { get; }

    string Describe();

    int Area();
}
