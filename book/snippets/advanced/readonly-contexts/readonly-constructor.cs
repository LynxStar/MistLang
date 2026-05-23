using System;

namespace Snippet;

public class PrivilegeRemovalDemo
{
    public readonly int X;

    public readonly int Y;

    public PrivilegeRemovalDemo(int x, int y)
    {
        this.X = x;
        this.Y = y;
        {
            var sum = X + Y;
        }
    }

    public string Describe()
    {
        return $"PrivilegeRemovalDemo(X={X}, Y={Y})";
    }

    protected PrivilegeRemovalDemo()
    {
        this.X = 0;
        this.Y = 0;
    }

    public static PrivilegeRemovalDemo __CreateDefault()
    {
        var inst = new PrivilegeRemovalDemo();
        return inst;
    }
}
