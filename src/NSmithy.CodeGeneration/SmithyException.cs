namespace NSmithy.CodeGeneration;

public class SmithyException : Exception
{
    public SmithyException(string message)
        : base(message) { }

    public SmithyException(string message, Exception innerException)
        : base(message, innerException) { }
}
