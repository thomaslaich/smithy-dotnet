FROM sbtscala/scala-sbt:eclipse-temurin-21.0.7_6_1.10.11_3.3.5 AS build

WORKDIR /app
COPY scala/ scala/
WORKDIR /app/scala
RUN sbt stage

FROM eclipse-temurin:21-jre-jammy
WORKDIR /app
COPY --from=build /app/scala/target/universal/stage .
EXPOSE 8080
ENTRYPOINT ["bin/polyglot-scala"]
