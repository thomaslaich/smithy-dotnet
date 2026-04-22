using Example.Hello;
using Grpc.Net.Client;
using SmithyNet.Client;
using GrpcHello = Example.Hello.Grpc;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5001";
var name = args.Length > 1 ? args[1] : "world";

using var channel = GrpcChannel.ForAddress(endpoint);
var grpcClient = new GrpcHello.HelloService.HelloServiceClient(channel);

var grpcHello = await grpcClient.SayHelloAsync(new GrpcHello.SayHelloInput { Name = name });
Console.WriteLine($"gRPC SayHello => {grpcHello.Message} from {grpcHello.From}");

var grpcPing = await grpcClient.PingAsync(new GrpcHello.PingInput { Name = name });
Console.WriteLine($"gRPC Ping => {grpcPing.Message} from {grpcPing.From}");
