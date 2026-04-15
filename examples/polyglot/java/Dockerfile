FROM gradle:8.14.3-jdk21 AS build

WORKDIR /app
COPY java/ java/
WORKDIR /app/java
RUN gradle --no-daemon :server:installDist

FROM eclipse-temurin:21-jre-jammy
WORKDIR /app
COPY --from=build /app/java/server/build/install/server .
EXPOSE 8080
ENTRYPOINT ["bin/server"]
