---
title: bote Protocols Overview
description: Kafka and Redis messaging protocols from the bote trait library.
---

[bote](https://github.com/thomaslaich/bote) is a Smithy trait library for
messaging contracts. It defines protocol traits for Kafka (`@kafkaJson`,
`@kafkaAvro`, `@kafkaProtobuf`) and Redis, plus the broker-agnostic vocabulary
they share. The optional `NSmithy.Bote` package implements C#
generation for [`bote#kafkaJson`](/smithy-dotnet/protocols/bote-kafka-json/),
`bote#redisStreamsJson`, and `bote#redisPubSubJson`. Status: **Experimental**.

## The Contract Model

A bote contract is modeled from the owner's perspective: the service defines
the events it emits and the commands it accepts. Operations are capabilities
offered to clients.

- `@kafkaProduce(topic: "...")`: clients may produce the operation's input, a
  `@command` structure, to the topic. The owner consumes it. Produce
  operations have no output.
- `@kafkaConsume(topic: "...")`: clients may consume the operation's events
  from the topic. The operation output targets a `@streaming` union whose
  members are `@event` structures. The owner emits them.

The topic is carried by the operation trait. Message payloads are plain
structures classified by broker-agnostic message-kind traits (`@command`,
`@event`, and the reserved `@reply`); the payload shapes stay
transport-neutral while the operation traits are Kafka-specific.

Topic provisioning (partitions, replication, retention) is not part of the
contract. `bote.infra#kafkaTopicConfig` lives in a separate namespace and is
attached with `apply`, typically from a separate model file, so a platform
team can own infrastructure settings independently of the contract owner.

## Generated Surface

A supported Bote service does not generate the HTTP client/server pair described
in [Client & Server Usage](/smithy-dotnet/protocols/usage/). Kafka services
generate a typed SDK over
[Confluent.Kafka](https://github.com/confluentinc/confluent-kafka-dotnet), while
Redis services generate Streams or Pub/Sub surfaces over StackExchange.Redis.
See [Kafka JSON](/smithy-dotnet/protocols/bote-kafka-json/) and
[Redis JSON](/smithy-dotnet/protocols/bote-redis-json/) for the generated APIs.

## AsyncAPI Documentation

bote includes a smithy-build plugin that renders
[AsyncAPI 3.1](https://www.asyncapi.com/) documents from bote services.
NSmithy.Bote runs it when the `SmithyGenerateAsyncApi` MSBuild property is set and
serves the result with `MapSmithyAsyncApi()`. The document is rendered from
the owner's perspective by default (commands are `receive`, events are
`send`); setting `"perspective": "client"` in the plugin configuration flips
the actions.

## Maturity

bote and its NSmithy extension are experimental. Kafka Avro and Protobuf do not
yet have C# generators, and there is no conformance suite; the generated
[Kafka JSON](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/kafkajson)
and [Redis JSON](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/redisjson)
examples live in this repository.
See [Protocol Status](/smithy-dotnet/protocols/status/).
