using System;

namespace Newtypes;

public readonly struct UserId
{
    public readonly int Value;

    public UserId(int value)
    {
        Value = value;
    }

    public static explicit operator UserId(int value) => new UserId(value);
    public static explicit operator int(UserId id) => id.Value;
}
public readonly struct Label
{
    public readonly string Value;

    public Label(string value)
    {
        Value = value;
    }

    public static explicit operator Label(string value) => new Label(value);
    public static explicit operator string(Label id) => id.Value;
}
public readonly struct Flag
{
    public readonly bool Value;

    public Flag(bool value)
    {
        Value = value;
    }

    public static explicit operator Flag(bool value) => new Flag(value);
    public static explicit operator bool(Flag id) => id.Value;
}
