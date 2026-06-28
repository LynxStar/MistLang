using System;

namespace Snippet;

public interface IShape
{
    string Name { get; }
    string Describe();
    int Area();
}
