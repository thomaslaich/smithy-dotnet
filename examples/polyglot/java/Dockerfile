FROM gradle:9.7.1-jdk21 AS build

WORKDIR /app
COPY java/ java/
WORKDIR /app/java
RUN gradle --no-daemon :server:installDist

FROM eclipse-temurin:25-jre-jammy
WORKDIR /app
COPY --from=build /app/java/server/build/install/server .
EXPOSE 8080
ENTRYPOINT ["bin/server"]
