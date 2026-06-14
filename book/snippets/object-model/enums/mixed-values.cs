using System;

namespace Snippet;

public record class HttpStatus(int Value)
{
    public static readonly HttpStatus Ok = new HttpStatus(200);
    public static readonly HttpStatus Created = new HttpStatus(201);
    public static readonly HttpStatus BadRequest = new HttpStatus(400);
    public static readonly HttpStatus NotFound = new HttpStatus(401);
    public static readonly HttpStatus InternalError = new HttpStatus(500);

    public static implicit operator int(HttpStatus e) => e.Value;

    public override string ToString()
    {
        if (ReferenceEquals(this, Ok)) return "Ok";
        else if (ReferenceEquals(this, Created)) return "Created";
        else if (ReferenceEquals(this, BadRequest)) return "BadRequest";
        else if (ReferenceEquals(this, NotFound)) return "NotFound";
        else if (ReferenceEquals(this, InternalError)) return "InternalError";
        return Value.ToString();
    }
}
