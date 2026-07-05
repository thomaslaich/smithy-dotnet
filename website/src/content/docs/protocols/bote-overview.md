---
title: bote Protocols Overview
description: Messaging protocols from the bote trait library for modeling Kafka contracts in Smithy.
---

[bote](https://github.com/thomaslaich/bote) is a Smithy trait library for
messaging contracts. It defines protocol traits for Kafka (`@kafkaJson`,
`@kafkaAvro`, `@kafkaProtobuf`) and Redis, plus the broker-agnostic vocabulary
they share. NSmithy implements code generation for
[`bote#kafkaJson`](/smithy-dotnet/protocols/bote-kafka-json/) only. Status:
**Experimental**.

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

A `@kafkaJson` service does not generate the HTTP client/server pair described
in [Client & Server Usage](/smithy-dotnet/protocols/usage/). It generates a
typed Kafka SDK over
[Confluent.Kafka](https://github.com/confluentinc/confluent-kafka-dotnet): a
producer plus command and event consumers, shared by both contract roles. See
[kafkaJson](/smithy-dotnet/protocols/bote-kafka-json/) for the generated
surfaces.

## AsyncAPI Documentation

bote includes a smithy-build plugin that renders
[AsyncAPI 3.1](https://www.asyncapi.com/) documents from bote services.
NSmithy runs it when the `SmithyGenerateAsyncApi` MSBuild property is set and
serves the result with `MapSmithyAsyncApi()`. The document is rendered from
the owner's perspective by default (commands are `receive`, events are
`send`); setting `"perspective": "client"` in the plugin configuration flips
the actions.

## Maturity

bote and NSmithy's Kafka support are experimental. Only `kafkaJson` has a
generator, and there is no conformance suite; behavior is validated through
the
[`examples/kafka`](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/kafka)
project. See [Protocol Status](/smithy-dotnet/protocols/status/).
