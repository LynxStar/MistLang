using System;

namespace CastOperators;

public class Celsius
{
    private double degrees;

    public Celsius(double degrees)
    {
        this.degrees = degrees;
    }

    public double GetDegrees()
    {
        return degrees;
    }

    public static explicit operator Kelvin(Celsius c)
    {
        return new Kelvin(c.GetDegrees() + 273.15);
    }

    protected Celsius()
    {
        this.degrees = 0;
    }

    public static Celsius __CreateDefault()
    {
        var inst = new Celsius();
        return inst;
    }
}
public class Kelvin
{
    private double degrees;

    public Kelvin(double degrees)
    {
        this.degrees = degrees;
    }

    public string Describe()
    {
        return $"{degrees} K";
    }

    protected Kelvin()
    {
        this.degrees = 0;
    }

    public static Kelvin __CreateDefault()
    {
        var inst = new Kelvin();
        return inst;
    }
}
