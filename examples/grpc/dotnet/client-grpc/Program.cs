using Example.Hello;
using Grpc.Net.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5001";
var name = args.Length > 1 ? args[1] : "world";

using var channel = GrpcChannel.ForAddress(endpoint);
var grpcClient = new HelloServiceGrpcClient(channel);

var grpcHello = await grpcClient.SayHelloAsync(new SayHelloInput(name));
Console.WriteLine($"gRPC SayHello => {grpcHello.Message} from {grpcHello.From}");

var grpcPing = await grpcClient.PingAsync(new PingInput(name));
Console.WriteLine($"gRPC Ping => {grpcPing.Message} from {grpcPing.From}");
