namespace AlGreenMES.BuildingBlocks.Common.Exceptions;

public class ForbiddenException : DomainException
{
    public ForbiddenException(string message)
        : base("FORBIDDEN", message)
    {
    }

    public ForbiddenException()
        : base("FORBIDDEN", "You do not have permission to perform this action.")
    {
    }

    /// <summary>
    /// Authz throw with a specific error code (e.g. "FORBIDDEN_ROLE_CHANGE")
    /// so the FE can show a per-code toast. Maps to HTTP 403 via
    /// GlobalExceptionHandlerMiddleware.
    /// </summary>
    public ForbiddenException(string code, string message)
        : base(code, message)
    {
    }
}
