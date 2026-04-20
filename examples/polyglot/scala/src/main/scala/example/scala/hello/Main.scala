package example.scala.hello

import cats.effect.IO
import cats.effect.IOApp
import com.comcast.ip4s.host
import com.comcast.ip4s.port
import org.http4s.ember.server.EmberServerBuilder
import smithy4s.http4s.SimpleRestJsonBuilder

object Main extends IOApp.Simple {
  private val serviceName = sys.env.getOrElse("SERVICE_NAME", "scala-service")

  private val service: HelloService[IO] = new HelloService[IO] {
    override def sayHello(name: String): IO[SayHelloOutput] =
      IO.pure(SayHelloOutput(s"Hello, $name!", serviceName))

    override def ping(name: String): IO[PingOutput] =
      IO.pure(PingOutput(s"Pong, $name!", serviceName))
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
