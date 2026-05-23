using Standard;
using System;

namespace EnumDemo;

public record class Color(byte Value)
{
    public static readonly Color Red = new Color((byte)0);
    public static readonly Color Green = new Color((byte)1);
    public static readonly Color Blue = new Color((byte)2);

    public static implicit operator byte(Color e) => e.Value;
}
