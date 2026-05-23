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

    public static implicit operator Fahrenheit(Celsius c)
    {
        return new Fahrenheit(c.GetDegrees() * 1.8 + 32.0);
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
public class Fahrenheit
{
    private double degrees;

    public Fahrenheit(double degrees)
    {
        this.degrees = degrees;
    }

    public string Describe()
    {
        return $"{degrees} F";
    }

    protected Fahrenheit()
    {
        this.degrees = 0;
    }

    public static Fahrenheit __CreateDefault()
    {
        var inst = new Fahrenheit();
        return inst;
    }
}
