using Standard;

namespace Snippet;

public sealed record class HttpStatus(int Value)
{
    public static readonly HttpStatus Ok = new HttpStatus(200);
    public static readonly HttpStatus Created = new HttpStatus(201);
    public static readonly HttpStatus BadRequest = new HttpStatus(400);
    public static readonly HttpStatus NotFound = new HttpStatus(401);
    public static readonly HttpStatus InternalError = new HttpStatus(500);

    public static implicit operator int(HttpStatus e) => e.Value;

    public static explicit operator HttpStatus?(int value)
    {
        if(Equals(value, HttpStatus.Ok.Value)) return HttpStatus.Ok;
        if(Equals(value, HttpStatus.Created.Value)) return HttpStatus.Created;
        if(Equals(value, HttpStatus.BadRequest.Value)) return HttpStatus.BadRequest;
        if(Equals(value, HttpStatus.NotFound.Value)) return HttpStatus.NotFound;
        if(Equals(value, HttpStatus.InternalError.Value)) return HttpStatus.InternalError;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, HttpStatus.Ok)) return "Ok";
        if(ReferenceEquals(this, HttpStatus.Created)) return "Created";
        if(ReferenceEquals(this, HttpStatus.BadRequest)) return "BadRequest";
        if(ReferenceEquals(this, HttpStatus.NotFound)) return "NotFound";
        if(ReferenceEquals(this, HttpStatus.InternalError)) return "InternalError";
        return Value.ToString();
    }
}
