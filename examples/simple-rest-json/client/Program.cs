using Example.Hello;
using NSmithy.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";
var name = args.Length > 1 ? args[1] : "world";

var client = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri(endpoint) }
);

var hello = await client.SayHelloAsync(new SayHelloInput(name));
Console.WriteLine($"SayHello => {hello.Message} from {hello.From}");

var ping = await client.PingAsync(new PingInput(name));
Console.WriteLine($"Ping => {ping.Message} from {ping.From}");
