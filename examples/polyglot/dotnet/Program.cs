using JavaHello = Example.Java.Hello;

var name = args.Length > 0 ? args[0] : "world";
var javaEndpoint = args.Length > 1 ? args[1] : "http://localhost:8082";

var javaClient = new JavaHello.HelloServiceClient(new Uri(javaEndpoint));

var javaHello = await javaClient.SayHelloAsync(new JavaHello.SayHelloInput(name));
Console.WriteLine($"Java SayHello => {javaHello.Message} from {javaHello.From}");

var javaShout = await javaClient.ShoutHelloAsync(new JavaHello.ShoutHelloInput(name));
Console.WriteLine($"Java ShoutHello => {javaShout.Message} from {javaShout.From}");
