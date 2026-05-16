using System;

namespace DependentTypes;

[DependentType("Size", typeof(int))]
interface IFixedCollection<TSize>
    where TSize : IConstInt
{
    int GetSize();
    string GetInfo();
}

public class TinyCollection : IFixedCollection<Const_Int_8>
{
    public int GetSize() { return 8; }
    public string GetInfo() { return $"TinyCollection(size={this.GetSize()})"; }
}
