using JavaHello = Example.Java.Hello;
using ScalaHello = Example.Scala.Hello;
using SmithyNet.Client;

var name = args.Length > 0 ? args[0] : "world";
var javaEndpoint = args.Length > 1 ? args[1] : "http://localhost:8082";
var scalaEndpoint = args.Length > 2 ? args[2] : "http://localhost:8081";

var javaClient = new JavaHello.HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri(javaEndpoint) }
);

var javaHello = await javaClient.SayHelloAsync(new JavaHello.SayHelloInput(name));
Console.WriteLine($"Java SayHello => {javaHello.Message} from {javaHello.From}");

var javaShout = await javaClient.ShoutHelloAsync(new JavaHello.ShoutHelloInput(name));
Console.WriteLine($"Java ShoutHello => {javaShout.Message} from {javaShout.From}");

var scalaClient = new ScalaHello.HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri(scalaEndpoint) }
);

var scalaHello = await scalaClient.SayHelloAsync(new ScalaHello.SayHelloInput(name));
Console.WriteLine($"Scala SayHello => {scalaHello.Message} from {scalaHello.From}");

var scalaPing = await scalaClient.PingAsync(new ScalaHello.PingInput(name));
Console.WriteLine($"Scala Ping => {scalaPing.Message} from {scalaPing.From}");
