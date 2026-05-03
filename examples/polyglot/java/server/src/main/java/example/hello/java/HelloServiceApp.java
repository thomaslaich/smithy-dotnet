package example.hello.java;

import example.hello.java.service.HelloService;
import java.net.URI;
import java.util.concurrent.ExecutionException;
import java.util.logging.Logger;
import software.amazon.smithy.java.server.Server;

public final class HelloServiceApp implements Runnable {
  private static final Logger LOGGER = Logger.getLogger(HelloServiceApp.class.getName());

  public static void main(String... args) throws InterruptedException, ExecutionException {
    new HelloServiceApp().run();
  }

  @Override
  public void run() {
    var serviceName = System.getenv().getOrDefault("SERVICE_NAME", "java-service");
    var server =
        Server.builder()
            .endpoints(URI.create("http://0.0.0.0:8080"))
            .addService(
                HelloService.builder()
                    .addSayHelloOperation(new SayHello(serviceName))
                    .addShoutHelloOperation(new ShoutHello(serviceName))
                    .build())
            .build();

    LOGGER.info("Starting HelloService on http://0.0.0.0:8080");
    server.start();
    try {
      Thread.currentThread().join();
    } catch (InterruptedException e) {
      LOGGER.info("Stopping HelloService");
      Thread.currentThread().interrupt();
      try {
        server.shutdown().get();
      } catch (InterruptedException shutdownInterrupted) {
        Thread.currentThread().interrupt();
        throw new RuntimeException(shutdownInterrupted);
      } catch (ExecutionException shutdownFailed) {
        throw new RuntimeException(shutdownFailed);
      }
    }
  }
}
