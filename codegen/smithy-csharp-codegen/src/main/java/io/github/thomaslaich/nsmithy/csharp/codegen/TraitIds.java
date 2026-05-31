/*
 * Trait shape IDs that aren't part of smithy-model's prelude (e.g. alloy traits).
 */
package io.github.thomaslaich.nsmithy.csharp.codegen;

import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class TraitIds {
  public static final ShapeId SIMPLE_REST_JSON = ShapeId.from("alloy#simpleRestJson");
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

  /** Applied to an operation: marks its output as server-streamed (gRPC server streaming). */
  public static final ShapeId GRPC_SERVER_STREAM = ShapeId.from("nsmithy.proto#grpcServerStream");

  /** Applied to an operation: marks its input as client-streamed (gRPC client streaming). */
  public static final ShapeId GRPC_CLIENT_STREAM = ShapeId.from("nsmithy.proto#grpcClientStream");

  private TraitIds() {}
}
