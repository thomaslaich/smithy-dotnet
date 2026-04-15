package example.hello

import cats.effect.IO
import cats.effect.IOApp
import com.comcast.ip4s.host
import com.comcast.ip4s.port
import org.http4s.Uri
import org.http4s.ember.client.EmberClientBuilder
import org.http4s.ember.server.EmberServerBuilder
import smithy4s.http4s.SimpleRestJsonBuilder

object Main extends IOApp.Simple {
  private val serviceName = sys.env.getOrElse("SERVICE_NAME", "scala-service")

  private val service: HelloService[IO] = new HelloService[IO] {
    override def sayHello(name: String): IO[SayHelloOutput] =
      IO.pure(SayHelloOutput(s"Hello, $name!", serviceName))

    override def ping(targetUrl: String, name: String): IO[PingOutput] =
      EmberClientBuilder.default[IO].build.use { httpClient =>
        SimpleRestJsonBuilder(HelloService)
          .client(httpClient)
          .uri(Uri.unsafeFromString(targetUrl))
          .resource
          .use { client =>
            client.sayHello(name).map(response => PingOutput(response.message, response.from))
          }
      }
  }

  override val run: IO[Unit] =
    SimpleRestJsonBuilder(HelloService).routes(service).resource.use { routes =>
      EmberServerBuilder
        .default[IO]
        .withHost(host"0.0.0.0")
        .withPort(port"8080")
        .withHttpApp(routes.orNotFound)
        .build
        .useForever
    }
}
