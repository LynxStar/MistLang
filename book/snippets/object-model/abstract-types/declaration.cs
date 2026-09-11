using System;

namespace Snippet;

interface IShape
{
    public string Name { get; }

    string Describe();

    int Area();
}
