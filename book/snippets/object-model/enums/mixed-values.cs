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
}
