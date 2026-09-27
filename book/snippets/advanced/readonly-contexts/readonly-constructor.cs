using Standard;

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
            int sum = X + Y;
        }
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"PrivilegeRemovalDemo(X={X}, Y={Y})");
    }
}
