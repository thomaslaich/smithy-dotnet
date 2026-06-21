using Example.Hello;

var endpoint = args.Length > 0 ? new Uri(args[0]) : new Uri("http://localhost:5001");
var name = args.Length > 1 ? args[1] : "world";

var client = new HelloServiceClient(endpoint);

try
{
    var response = await client.SayHelloAsync(new SayHelloInput(name));
    Console.WriteLine($"Hello response from {response.From}: {response.Message}");
}
catch (InvalidName error)
{
    Console.WriteLine($"Server rejected name: {error.Message}");
}
