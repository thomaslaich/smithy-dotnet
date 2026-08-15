using Example.Hello;
//#if (IsGrpc)
using NSmithy.Protocols.Grpc;
//#endif

//#if (IsGrpc)
var endpoint = args.Length > 0 ? args[0] : "http://localhost:5001";

// Native gRPC: setting Protocol selects gRPC and configures the HTTP/2 HttpClient it requires.
var client = new HelloServiceClient(new Uri(endpoint), new() { Protocol = new GrpcProtocol() });

//#else
var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";

var client = new HelloServiceClient(new Uri(endpoint));

//#endif

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message);
