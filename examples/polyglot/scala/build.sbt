val http4sVersion = "0.23.33"

lazy val root = (project in file("."))
  .enablePlugins(Smithy4sCodegenPlugin, JavaAppPackaging)
  .settings(
    name := "polyglot-scala",
    scalaVersion := "3.3.5",
    Compile / mainClass := Some("example.scala.hello.Main"),
    Compile / run / fork := true,
    libraryDependencies ++= Seq(
      "com.disneystreaming.smithy4s" %% "smithy4s-http4s"     % smithy4sVersion.value,
      "org.http4s"                   %% "http4s-ember-server" % http4sVersion,
      "org.typelevel"                %% "cats-effect"         % "3.5.7",
      "ch.qos.logback"                % "logback-classic"     % "1.5.18",
    ),
    Compile / smithy4sInputDirs := {
      val modelDir = ((ThisBuild / baseDirectory).value / "model").getCanonicalFile
      Seq(modelDir)
    },
  )
