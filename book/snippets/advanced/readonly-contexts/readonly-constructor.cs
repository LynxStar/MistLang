using System;

namespace ReadonlyContexts;

public class PrivilegeRemovalDemo
{
    public int X;
    public int Y;

    public PrivilegeRemovalDemo(int x, int y)
    {
        this.X = x;
        this.Y = y;

        // Readonly block emits as a regular block
        {
            var sum = X + Y;
        }
    }

    public string Describe()
    {
        return $"PrivilegeRemovalDemo(X={X}, Y={Y})";
    }
}
