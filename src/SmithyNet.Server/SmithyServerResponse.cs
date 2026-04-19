namespace SmithyNet.Server;

public sealed record SmithyServerResponse(string ServiceName, string OperationName, object? Output);
