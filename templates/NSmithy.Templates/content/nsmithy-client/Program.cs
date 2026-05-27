using Example.Hello;
//#endif
//#if (IsGrpc)
using Grpc.Net.Client;
//#if (!IsGrpc)
using NSmithy.Client;

//#endif

//#if (!IsGrpc)
var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";

var client = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri(endpoint) }
);

//#endif
//#if (IsGrpc)
var endpoint = args.Length > 0 ? args[0] : "http://localhost:5001";

using var channel = GrpcChannel.ForAddress(endpoint);
var client = new HelloServiceGrpcClient(channel);

//#endif

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message);
