using Example.Hello;
using SmithyNet.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";
var name = args.Length > 1 ? args[1] : "world";

var httpClient = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri(endpoint) }
);
var httpHello = await httpClient.SayHelloAsync(new SayHelloInput(name));
Console.WriteLine($"HTTP SayHello => {httpHello.Message} from {httpHello.From}");

var httpPing = await httpClient.PingAsync(new PingInput(name));
Console.WriteLine($"HTTP Ping => {httpPing.Message} from {httpPing.From}");
