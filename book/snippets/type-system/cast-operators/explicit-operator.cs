using Standard;

namespace Snippet;

public class Celsius
{
    private double degrees;

    public Celsius(double degrees)
    {
        this.degrees = default(double);
        this.degrees = degrees;
    }

    public double GetDegrees()
    {
        return degrees;
    }

    public static explicit operator Snippet.Kelvin(Snippet.Celsius c)
    {
        return new Snippet.Kelvin(c.GetDegrees() + 273.15);
    }
}

public class Kelvin
{
    private double degrees;

    public Kelvin(double degrees)
    {
        this.degrees = default(double);
        this.degrees = degrees;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"{degrees} K");
    }
}
