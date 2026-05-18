namespace Snippet;

public struct Color
{
    public int r;

    public int g;

    public int b;

    public Color(int r, int g, int b)
    {
        this.r = r;
        this.g = g;
        this.b = b;
    }

    public readonly int Brightness()
    {
        return (this.r + this.b) / 3;
    }

    public static Color __CreateDefault()
    {
        var inst = new Color();
        inst.r = 0;
        inst.g = 0;
        inst.b = 0;
        return inst;
    }
}
