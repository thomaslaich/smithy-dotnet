using Example.Hello;
using Grpc.Net.Client;
using SmithyNet.Client;
using GrpcHello = Example.Hello.Grpc;

var httpEndpoint = args.Length > 0 ? args[0] : "http://localhost:5000";
var grpcEndpoint = args.Length > 1 ? args[1] : "http://localhost:5001";
var name = args.Length > 2 ? args[2] : "world";

var httpClient = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri(httpEndpoint) }
);
var httpHello = await httpClient.SayHelloAsync(new SayHelloInput(name));
Console.WriteLine($"HTTP SayHello => {httpHello.Message} from {httpHello.From}");

var httpPing = await httpClient.PingAsync(new PingInput(name));
Console.WriteLine($"HTTP Ping => {httpPing.Message} from {httpPing.From}");

using var channel = GrpcChannel.ForAddress(grpcEndpoint);
var grpcClient = new GrpcHello.HelloService.HelloServiceClient(channel);

var grpcHello = await grpcClient.SayHelloAsync(new GrpcHello.SayHelloInput { Name = name });
Console.WriteLine($"gRPC SayHello => {grpcHello.Message} from {grpcHello.From}");

var grpcPing = await grpcClient.PingAsync(new GrpcHello.PingInput { Name = name });
Console.WriteLine($"gRPC Ping => {grpcPing.Message} from {grpcPing.From}");
