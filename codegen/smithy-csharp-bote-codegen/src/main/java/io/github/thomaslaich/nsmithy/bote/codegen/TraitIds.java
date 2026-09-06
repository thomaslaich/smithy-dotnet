package io.github.thomaslaich.nsmithy.bote.codegen;

import software.amazon.smithy.model.shapes.ShapeId;

public final class TraitIds {
  public static final ShapeId KAFKA_JSON = ShapeId.from("bote#kafkaJson");
  public static final ShapeId KAFKA_AVRO = ShapeId.from("bote#kafkaAvro");
  public static final ShapeId KAFKA_PROTOBUF = ShapeId.from("bote#kafkaProtobuf");
  public static final ShapeId REDIS_STREAMS_JSON = ShapeId.from("bote#redisStreamsJson");
  public static final ShapeId REDIS_PUB_SUB_JSON = ShapeId.from("bote#redisPubSubJson");
  public static final ShapeId KAFKA_PRODUCE = ShapeId.from("bote#kafkaProduce");
  public static final ShapeId KAFKA_CONSUME = ShapeId.from("bote#kafkaConsume");
  public static final ShapeId REDIS_STREAM_ADD = ShapeId.from("bote#redisStreamAdd");
  public static final ShapeId REDIS_STREAM_READ = ShapeId.from("bote#redisStreamRead");
  public static final ShapeId REDIS_PUBLISH = ShapeId.from("bote#redisPublish");
  public static final ShapeId REDIS_SUBSCRIBE = ShapeId.from("bote#redisSubscribe");
  public static final ShapeId EVENT = ShapeId.from("bote#event");
  public static final ShapeId COMMAND = ShapeId.from("bote#command");
  public static final ShapeId REPLY = ShapeId.from("bote#reply");
  public static final ShapeId KAFKA_TOPIC_CONFIG = ShapeId.from("bote.infra#kafkaTopicConfig");
  public static final ShapeId KAFKA_KEY = ShapeId.from("bote#kafkaKey");
  public static final ShapeId KAFKA_HEADER = ShapeId.from("bote#kafkaHeader");
  public static final ShapeId STREAMING = ShapeId.from("smithy.api#streaming");

  private TraitIds() {}
}
