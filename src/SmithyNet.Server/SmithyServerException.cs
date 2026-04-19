namespace SmithyNet.Server;

public class SmithyServerException : Exception
{
    public SmithyServerException() { }

    public SmithyServerException(string? message)
        : base(message) { }

    public SmithyServerException(string? message, Exception? innerException)
        : base(message, innerException) { }
}
