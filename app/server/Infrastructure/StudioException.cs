namespace TfStudio.Server.Infrastructure;

public enum StudioErrorKind
{
    /// <summary>Invalid input or a precondition the user can fix (HTTP 400).</summary>
    BadRequest,

    /// <summary>HTTP 404.</summary>
    NotFound,

    /// <summary>Another run is in progress (HTTP 409).</summary>
    Conflict,

    /// <summary>A required tool is missing or failed (HTTP 503).</summary>
    Unavailable,
}

/// <summary>An expected failure whose message is safe and useful to show in the UI.</summary>
public sealed class StudioException : Exception
{
    public StudioException()
        : this(StudioErrorKind.BadRequest, "TF Studio error.")
    {
    }

    public StudioException(string message)
        : this(StudioErrorKind.BadRequest, message)
    {
    }

    public StudioException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public StudioException(StudioErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public StudioErrorKind Kind { get; }
}
