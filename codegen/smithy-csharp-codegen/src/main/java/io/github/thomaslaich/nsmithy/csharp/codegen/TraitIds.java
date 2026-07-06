/*
 * Trait shape IDs that aren't part of smithy-model's prelude (e.g. alloy traits).
 */
package io.github.thomaslaich.nsmithy.csharp.codegen;

import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class TraitIds {
  public static final ShapeId SIMPLE_REST_JSON = ShapeId.from("alloy#simpleRestJson");
  public static final ShapeId AWS_JSON_1_0 = ShapeId.from("aws.protocols#awsJson1_0");
  public static final ShapeId AWS_JSON_1_1 = ShapeId.from("aws.protocols#awsJson1_1");
  public static final ShapeId AWS_QUERY = ShapeId.from("aws.protocols#awsQuery");
  public static final ShapeId EC2_QUERY = ShapeId.from("aws.protocols#ec2Query");
  public static final ShapeId AWS_QUERY_ERROR = ShapeId.from("aws.protocols#awsQueryError");
  public static final ShapeId REST_JSON_1 = ShapeId.from("aws.protocols#restJson1");
  public static final ShapeId REST_XML = ShapeId.from("aws.protocols#restXml");
  public static final ShapeId RPC_V2_CBOR = ShapeId.from("smithy.protocols#rpcv2Cbor");
  public static final ShapeId MEDIA_TYPE = ShapeId.from("smithy.api#mediaType");
  public static final ShapeId REQUEST_COMPRESSION = ShapeId.from("smithy.api#requestCompression");
  public static final ShapeId HTTP_CHECKSUM_REQUIRED =
      ShapeId.from("smithy.api#httpChecksumRequired");
  public static final ShapeId GRPC = ShapeId.from("alloy.proto#grpc");
  public static final ShapeId PROTO_INDEX = ShapeId.from("alloy.proto#protoIndex");
  public static final ShapeId XML_NAME = ShapeId.from("smithy.api#xmlName");
  public static final ShapeId PROMPTS = ShapeId.from("smithy.ai#prompts");
  public static final ShapeId STREAMING = ShapeId.from("smithy.api#streaming");

  // bote protocol + messaging traits
  public static final ShapeId KAFKA_JSON = ShapeId.from("bote#kafkaJson");
  public static final ShapeId KAFKA_AVRO = ShapeId.from("bote#kafkaAvro");
  public static final ShapeId KAFKA_PROTOBUF = ShapeId.from("bote#kafkaProtobuf");
  public static final ShapeId REDIS_STREAMS_JSON = ShapeId.from("bote#redisStreamsJson");
  public static final ShapeId REDIS_PUB_SUB_JSON = ShapeId.from("bote#redisPubSubJson");
  // Kafka capability traits: they carry the topic on the operation.
  public static final ShapeId KAFKA_PRODUCE = ShapeId.from("bote#kafkaProduce");
  public static final ShapeId KAFKA_CONSUME = ShapeId.from("bote#kafkaConsume");
  // Message-kind traits classifying payload structures.
  public static final ShapeId EVENT = ShapeId.from("bote#event");
  public static final ShapeId COMMAND = ShapeId.from("bote#command");
  public static final ShapeId REPLY = ShapeId.from("bote#reply");
  // Kafka decoration traits.
  public static final ShapeId KAFKA_TOPIC_CONFIG = ShapeId.from("bote.infra#kafkaTopicConfig");
  public static final ShapeId KAFKA_KEY = ShapeId.from("bote#kafkaKey");
  public static final ShapeId KAFKA_HEADER = ShapeId.from("bote#kafkaHeader");

  private TraitIds() {}
}
