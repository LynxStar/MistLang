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

    public static explicit operator HttpStatus?(int value)
    {
        if (Equals(value, Ok.Value)) return Ok;
        else if (Equals(value, Created.Value)) return Created;
        else if (Equals(value, BadRequest.Value)) return BadRequest;
        else if (Equals(value, NotFound.Value)) return NotFound;
        else if (Equals(value, InternalError.Value)) return InternalError;
        return null;
    }

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
