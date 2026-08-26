using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.EventStream;
using NSmithy.Http;
using NSmithy.Protocols.Rest;
using NSmithy.Protocols.RestJson;

namespace NSmithy.Tests.Protocols.RestJson;

public sealed class RestJson1ProtocolTests
{
    // REST ignores the service schema (its bindings come from @http on each operation), so a
    // placeholder service is fine. The per-operation protocol is what these tests exercise.
    private static readonly IServiceProtocol RestService = new RestJson1Protocol().ForService(
        Schemas.Service(ShapeId.Parse("test#Service"))
    );

    private static IOperationProtocol<TIn, TOut> Protocol<TIn, TOut>(
        OperationSchema<TIn, TOut> operation
    ) => RestService.ForOperation(operation);

    public sealed record UpdateUserInput(string UserId, string? RequestToken, string DisplayName);

    public sealed class UpdateUserInputBuilder
    {
        public string? UserId { get; set; }

        public string? RequestToken { get; set; }

        public string? DisplayName { get; set; }
    }

    public sealed record UpdateUserOutput;

    public sealed class UpdateUserOutputBuilder { }

    public sealed record Echo(string Message);

    public abstract record ChatEvent
    {
        private ChatEvent() { }

        public sealed record Message(Echo Value) : ChatEvent;
    }

    public sealed class EchoBuilder
    {
        public string? Message { get; set; }
    }

    public sealed class EventEnvelopeBuilder
    {
        public string? StreamId { get; set; }

        public IAsyncEnumerable<ChatEvent>? Events { get; set; }
    }

    public sealed record EventEnvelope(string StreamId, IAsyncEnumerable<ChatEvent> Events);

    [Fact]
    public void RestJson1ProtocolSerializesLabelsHeadersAndBodySeparately()
    {
        var inputSchema = Schemas
            .Structure<UpdateUserInput, UpdateUserInputBuilder>(
                new ShapeId("example", "UpdateUserInput")
            )
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Optional(
                "requestToken",
                static input => input.RequestToken!,
                static (builder, value) => builder.RequestToken = value,
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("X-Request-Token")]
            )
            .Required(
                "displayName",
                static input => input.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                Schemas.String
            )
            .Build(
                static () => new UpdateUserInputBuilder(),
                static builder => new UpdateUserInput(
                    builder.UserId!,
                    builder.RequestToken,
                    builder.DisplayName!
                )
            );
        var outputSchema = Schemas
            .Structure<UpdateUserOutput, UpdateUserOutputBuilder>(
                new ShapeId("example", "UpdateUserOutput")
            )
            .Build(static () => new UpdateUserOutputBuilder(), static _ => new UpdateUserOutput());
        var operation = Schemas.Operation(
            new ShapeId("example", "UpdateUser"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("PUT", "/users/{userId}")]
        );
        var input = new UpdateUserInput("ada lovelace", "token-123", "Ada");

        var request = Protocol(operation).SerializeRequest(input);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/users/ada%20lovelace", request.RequestUri);
        Assert.Equal("token-123", request.Headers["X-Request-Token"].Single());
        Assert.Equal("application/json", request.ContentType);
        var body = Assert.IsType<SmithyHttpBody.Bytes>(request.Body);
        Assert.Equal("{\"displayName\":\"Ada\"}", Encoding.UTF8.GetString(body.Content));
    }

    [Fact]
    public void RestJson1ProtocolDeserializesLabelsHeadersAndBodySeparately()
    {
        var inputSchema = Schemas
            .Structure<UpdateUserInput, UpdateUserInputBuilder>(
                new ShapeId("example", "UpdateUserInput")
            )
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Optional(
                "requestToken",
                static input => input.RequestToken!,
                static (builder, value) => builder.RequestToken = value,
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("X-Request-Token")]
            )
            .Required(
                "displayName",
                static input => input.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                Schemas.String
            )
            .Build(
                static () => new UpdateUserInputBuilder(),
                static builder => new UpdateUserInput(
                    builder.UserId!,
                    builder.RequestToken,
                    builder.DisplayName!
                )
            );
        var outputSchema = Schemas
            .Structure<UpdateUserOutput, UpdateUserOutputBuilder>(
                new ShapeId("example", "UpdateUserOutput")
            )
            .Build(static () => new UpdateUserOutputBuilder(), static _ => new UpdateUserOutput());
        var operation = Schemas.Operation(
            new ShapeId("example", "UpdateUser"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("PUT", "/users/{userId}")]
        );
        var request = new SmithyHttpRequest(HttpMethod.Put, "/users/ada%20lovelace")
        {
            Body = new SmithyHttpBody.Bytes(Encoding.UTF8.GetBytes("{\"displayName\":\"Ada\"}")),
            ContentType = "application/json",
        };
        request.Headers["X-Request-Token"] = ["token-123"];

        var input = Protocol(operation).DeserializeRequest(request);

        Assert.Equal(new UpdateUserInput("ada lovelace", "token-123", "Ada"), input);
    }

    // A required member bound to a header or query string never reaches the body codec, and a
    // required value-type member has already defaulted by the time the validator runs, so this is
    // the only layer that can tell the caller they left one out.
    public sealed record CountInput(int Limit, string Tenant);

    public sealed class CountInputBuilder
    {
        public int? Limit { get; set; }

        public string? Tenant { get; set; }
    }

    private static OperationSchema<CountInput, UpdateUserOutput> CountOperation()
    {
        var inputSchema = Schemas
            .Structure<CountInput, CountInputBuilder>(new ShapeId("example", "CountInput"))
            .Required(
                "limit",
                static input => input.Limit,
                static (builder, value) => builder.Limit = value,
                Schemas.Nullable(Schemas.Integer),
                traits: [RestTraits.HttpQueryTrait("limit")]
            )
            .Required(
                "tenant",
                static input => input.Tenant,
                static (builder, value) => builder.Tenant = value,
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("X-Tenant")]
            )
            .Build(
                static () => new CountInputBuilder(),
                static builder => new CountInput(builder.Limit.GetValueOrDefault(), builder.Tenant!)
            );
        var outputSchema = Schemas
            .Structure<UpdateUserOutput, UpdateUserOutputBuilder>(
                new ShapeId("example", "CountOutput")
            )
            .Build(static () => new UpdateUserOutputBuilder(), static _ => new UpdateUserOutput());
        return Schemas.Operation(
            new ShapeId("example", "Count"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/count")]
        );
    }

    [Fact]
    public void RestJson1ProtocolRejectsAMissingRequiredQueryMember()
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "/count");
        request.Headers["X-Tenant"] = ["acme"];

        var exception = Assert.Throws<MissingRequiredMemberException>(() =>
            Protocol(CountOperation()).DeserializeRequest(request)
        );

        Assert.Equal("limit", exception.MemberName);
    }

    [Fact]
    public void RestJson1ProtocolRejectsAMissingRequiredHeaderMember()
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "/count?limit=10");

        var exception = Assert.Throws<MissingRequiredMemberException>(() =>
            Protocol(CountOperation()).DeserializeRequest(request)
        );

        Assert.Equal("tenant", exception.MemberName);
    }

    [Fact]
    public void RestJson1ProtocolAcceptsRequiredBoundMembersWhenPresent()
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "/count?limit=10");
        request.Headers["X-Tenant"] = ["acme"];

        var input = Protocol(CountOperation()).DeserializeRequest(request);

        Assert.Equal(new CountInput(10, "acme"), input);
    }

    public sealed record GetUserInput(
        string UserId,
        bool IncludeDetails,
        IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, string> ExtraQuery,
        IReadOnlyDictionary<string, string> ExtraHeaders
    );

    public sealed class GetUserInputBuilder
    {
        public string? UserId { get; set; }

        public bool IncludeDetails { get; set; }

        public IReadOnlyList<string>? Tags { get; set; }

        public IReadOnlyDictionary<string, string>? ExtraQuery { get; set; }

        public IReadOnlyDictionary<string, string>? ExtraHeaders { get; set; }
    }

    public sealed record GetUserOutput;

    public sealed class GetUserOutputBuilder { }

    [Fact]
    public void RestJson1ProtocolSerializesAllRequestHttpBindings()
    {
        var tagListSchema = Schemas.List(new ShapeId("example", "TagList"), Schemas.String);
        var stringMapSchema = Schemas.Map(new ShapeId("example", "StringMap"), Schemas.String);
        var inputSchema = Schemas
            .Structure<GetUserInput, GetUserInputBuilder>(new ShapeId("example", "GetUserInput"))
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Required(
                "includeDetails",
                static input => input.IncludeDetails,
                static (builder, value) => builder.IncludeDetails = value,
                Schemas.Boolean,
                traits: [RestTraits.HttpQueryTrait("includeDetails")]
            )
            .Required(
                "tags",
                static input => input.Tags,
                static (builder, value) => builder.Tags = value,
                tagListSchema,
                traits: [RestTraits.HttpQueryTrait("tag")]
            )
            .Required(
                "extraQuery",
                static input => input.ExtraQuery,
                static (builder, value) => builder.ExtraQuery = value,
                stringMapSchema,
                traits: [RestTraits.HttpQueryParamsTrait]
            )
            .Required(
                "extraHeaders",
                static input => input.ExtraHeaders,
                static (builder, value) => builder.ExtraHeaders = value,
                stringMapSchema,
                traits: [RestTraits.HttpPrefixHeadersTrait("X-Extra-")]
            )
            .Build(
                static () => new GetUserInputBuilder(),
                static builder => new GetUserInput(
                    builder.UserId!,
                    builder.IncludeDetails,
                    builder.Tags!,
                    builder.ExtraQuery!,
                    builder.ExtraHeaders!
                )
            );
        var outputSchema = Schemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserOutput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var operation = Schemas.Operation(
            new ShapeId("example", "GetUser"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/users/{userId}")]
        );
        var input = new GetUserInput(
            "ada lovelace",
            true,
            ["admin", "staff"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["debug"] = "true" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Trace"] = "abc" }
        );

        var request = Protocol(operation).SerializeRequest(input);

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/users/ada%20lovelace?includeDetails=true&tag=admin&tag=staff&debug=true",
            request.RequestUri
        );
        Assert.Equal("abc", request.Headers["X-Extra-Trace"].Single());
        Assert.Same(SmithyHttpBody.Empty, request.Body);
        Assert.Null(request.ContentType);
    }

    [Fact]
    public void RestJson1ProtocolDeserializesAllRequestHttpBindings()
    {
        var tagListSchema = Schemas.List(new ShapeId("example", "TagList"), Schemas.String);
        var stringMapSchema = Schemas.Map(new ShapeId("example", "StringMap"), Schemas.String);
        var inputSchema = Schemas
            .Structure<GetUserInput, GetUserInputBuilder>(new ShapeId("example", "GetUserInput"))
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Required(
                "includeDetails",
                static input => input.IncludeDetails,
                static (builder, value) => builder.IncludeDetails = value,
                Schemas.Boolean,
                traits: [RestTraits.HttpQueryTrait("includeDetails")]
            )
            .Required(
                "tags",
                static input => input.Tags,
                static (builder, value) => builder.Tags = value,
                tagListSchema,
                traits: [RestTraits.HttpQueryTrait("tag")]
            )
            .Required(
                "extraQuery",
                static input => input.ExtraQuery,
                static (builder, value) => builder.ExtraQuery = value,
                stringMapSchema,
                traits: [RestTraits.HttpQueryParamsTrait]
            )
            .Required(
                "extraHeaders",
                static input => input.ExtraHeaders,
                static (builder, value) => builder.ExtraHeaders = value,
                stringMapSchema,
                traits: [RestTraits.HttpPrefixHeadersTrait("")]
            )
            .Build(
                static () => new GetUserInputBuilder(),
                static builder => new GetUserInput(
                    builder.UserId!,
                    builder.IncludeDetails,
                    builder.Tags!,
                    builder.ExtraQuery!,
                    builder.ExtraHeaders!
                )
            );
        var outputSchema = Schemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserOutput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var operation = Schemas.Operation(
            new ShapeId("example", "GetUser"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/users/{userId}")]
        );
        var request = new SmithyHttpRequest(
            HttpMethod.Get,
            "/users/ada%20lovelace?includeDetails=true&tag=admin&tag=staff&debug=true"
        );
        request.Headers["X-Extra-Trace"] = ["abc"];
        request.Headers["Host"] = ["example.com"];
        request.Headers["Content-Length"] = ["0"];

        var input = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("ada lovelace", input.UserId);
        Assert.True(input.IncludeDetails);
        Assert.Equal(["admin", "staff"], input.Tags);
        Assert.Equal("true", input.ExtraQuery["debug"]);
        Assert.Equal("true", input.ExtraQuery["includeDetails"]);
        Assert.Equal("admin", input.ExtraQuery["tag"]);
        Assert.Equal("abc", input.ExtraHeaders["X-Extra-Trace"]);
        Assert.DoesNotContain("Host", input.ExtraHeaders.Keys);
        Assert.DoesNotContain("Content-Length", input.ExtraHeaders.Keys);
    }

    public sealed record RequestValueParityInput(
        string Key,
        DateTimeOffset Modified,
        IReadOnlyList<string> Tags,
        string Media,
        float Ratio,
        double Score,
        DateTimeOffset Created
    );

    public sealed class RequestValueParityInputBuilder
    {
        public string? Key { get; set; }

        public DateTimeOffset Modified { get; set; }

        public IReadOnlyList<string>? Tags { get; set; }

        public string? Media { get; set; }

        public float Ratio { get; set; }

        public double Score { get; set; }

        public DateTimeOffset Created { get; set; }
    }

    public sealed record RequestValueParityOutput;

    public sealed class RequestValueParityOutputBuilder { }

    public readonly record struct HeaderStatus(string Value) : IStringEnumValue<HeaderStatus>
    {
        public static readonly HeaderStatus ActiveBlue = new("ACTIVE,BLUE");

        public static readonly HeaderStatus Pending = new("PENDING");

        public static HeaderStatus FromValue(string value) => new(value);
    }

    public sealed record EnumHeaderListInput(IReadOnlyList<HeaderStatus> Statuses);

    public sealed class EnumHeaderListInputBuilder
    {
        public IReadOnlyList<HeaderStatus>? Statuses { get; set; }
    }

    public sealed record EnumHeaderListOutput;

    public sealed class EnumHeaderListOutputBuilder { }

    [Fact]
    public void RestJson1ProtocolRoundTripsStringEnumHeaderListWithQuotedComma()
    {
        var statusSchema = Schemas.StringEnum<HeaderStatus>(new ShapeId("example", "HeaderStatus"));
        var statusListSchema = Schemas.List(
            new ShapeId("example", "HeaderStatusList"),
            statusSchema
        );
        var inputSchema = Schemas
            .Structure<EnumHeaderListInput, EnumHeaderListInputBuilder>(
                new ShapeId("example", "EnumHeaderListInput")
            )
            .Required(
                "statuses",
                static input => input.Statuses,
                static (builder, value) => builder.Statuses = value,
                statusListSchema,
                traits: [RestTraits.HttpHeaderTrait("X-Status")]
            )
            .Build(
                static () => new EnumHeaderListInputBuilder(),
                static builder => new EnumHeaderListInput(builder.Statuses!)
            );
        var outputSchema = Schemas
            .Structure<EnumHeaderListOutput, EnumHeaderListOutputBuilder>(
                new ShapeId("example", "EnumHeaderListOutput")
            )
            .Build(
                static () => new EnumHeaderListOutputBuilder(),
                static _ => new EnumHeaderListOutput()
            );
        var operation = Schemas.Operation(
            new ShapeId("example", "EnumHeaderList"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/statuses")]
        );
        var input = new EnumHeaderListInput([HeaderStatus.ActiveBlue, HeaderStatus.Pending]);

        var request = Protocol(operation).SerializeRequest(input);
        var decoded = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("\"ACTIVE,BLUE\", PENDING", request.Headers["X-Status"].Single());
        Assert.Equal(input.Statuses, decoded.Statuses);
    }

    [Fact]
    public void RestJson1ProtocolSerializesRequestHttpValueParity()
    {
        var tagListSchema = Schemas.List(new ShapeId("example", "ParityTagList"), Schemas.String);
        var inputSchema = Schemas
            .Structure<RequestValueParityInput, RequestValueParityInputBuilder>(
                new ShapeId("example", "RequestValueParityInput")
            )
            .Required(
                "key",
                static input => input.Key,
                static (builder, value) => builder.Key = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Required(
                "modified",
                static input => input.Modified,
                static (builder, value) => builder.Modified = value,
                Schemas.Timestamp,
                traits: [RestTraits.HttpHeaderTrait("Last-Modified")]
            )
            .Required(
                "tags",
                static input => input.Tags,
                static (builder, value) => builder.Tags = value,
                tagListSchema,
                traits: [RestTraits.HttpHeaderTrait("X-Tags")]
            )
            .Required(
                "media",
                static input => input.Media,
                static (builder, value) => builder.Media = value,
                Schemas.String,
                traits:
                [
                    RestTraits.HttpQueryTrait("media"),
                    RestTraits.MediaTypeTrait("text/plain"),
                ]
            )
            .Required(
                "ratio",
                static input => input.Ratio,
                static (builder, value) => builder.Ratio = value,
                Schemas.Float,
                traits: [RestTraits.HttpQueryTrait("ratio")]
            )
            .Required(
                "score",
                static input => input.Score,
                static (builder, value) => builder.Score = value,
                Schemas.Double,
                traits: [RestTraits.HttpQueryTrait("score")]
            )
            .Required(
                "created",
                static input => input.Created,
                static (builder, value) => builder.Created = value,
                Schemas.Timestamp,
                traits:
                [
                    RestTraits.HttpQueryTrait("created"),
                    RestTraits.TimestampFormatTrait("epoch-seconds"),
                ]
            )
            .Build(
                static () => new RequestValueParityInputBuilder(),
                static builder => new RequestValueParityInput(
                    builder.Key!,
                    builder.Modified,
                    builder.Tags!,
                    builder.Media!,
                    builder.Ratio,
                    builder.Score,
                    builder.Created
                )
            );
        var outputSchema = Schemas
            .Structure<RequestValueParityOutput, RequestValueParityOutputBuilder>(
                new ShapeId("example", "RequestValueParityOutput")
            )
            .Build(
                static () => new RequestValueParityOutputBuilder(),
                static _ => new RequestValueParityOutput()
            );
        var operation = Schemas.Operation(
            new ShapeId("example", "RequestValueParity"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/objects/{key+}")]
        );
        var modified = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var created = modified.AddMilliseconds(250);
        var input = new RequestValueParityInput(
            "folder/photo 1.jpg",
            modified,
            ["a,b", "plain"],
            "hello world",
            float.NaN,
            double.PositiveInfinity,
            created
        );

        var request = Protocol(operation).SerializeRequest(input);

        Assert.Equal(
            "/objects/folder/photo%201.jpg?media=aGVsbG8gd29ybGQ%3D&ratio=NaN&score=Infinity&created=1577836800.25",
            request.RequestUri
        );
        Assert.Equal("Wed, 01 Jan 2020 00:00:00 GMT", request.Headers["Last-Modified"].Single());
        Assert.Equal("\"a,b\", plain", request.Headers["X-Tags"].Single());
    }

    [Fact]
    public void RestJson1ProtocolDeserializesRequestHttpValueParity()
    {
        var tagListSchema = Schemas.List(new ShapeId("example", "ParityTagList"), Schemas.String);
        var inputSchema = Schemas
            .Structure<RequestValueParityInput, RequestValueParityInputBuilder>(
                new ShapeId("example", "RequestValueParityInput")
            )
            .Required(
                "key",
                static input => input.Key,
                static (builder, value) => builder.Key = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Required(
                "modified",
                static input => input.Modified,
                static (builder, value) => builder.Modified = value,
                Schemas.Timestamp,
                traits: [RestTraits.HttpHeaderTrait("Last-Modified")]
            )
            .Required(
                "tags",
                static input => input.Tags,
                static (builder, value) => builder.Tags = value,
                tagListSchema,
                traits: [RestTraits.HttpHeaderTrait("X-Tags")]
            )
            .Required(
                "media",
                static input => input.Media,
                static (builder, value) => builder.Media = value,
                Schemas.String,
                traits:
                [
                    RestTraits.HttpQueryTrait("media"),
                    RestTraits.MediaTypeTrait("text/plain"),
                ]
            )
            .Required(
                "ratio",
                static input => input.Ratio,
                static (builder, value) => builder.Ratio = value,
                Schemas.Float,
                traits: [RestTraits.HttpQueryTrait("ratio")]
            )
            .Required(
                "score",
                static input => input.Score,
                static (builder, value) => builder.Score = value,
                Schemas.Double,
                traits: [RestTraits.HttpQueryTrait("score")]
            )
            .Required(
                "created",
                static input => input.Created,
                static (builder, value) => builder.Created = value,
                Schemas.Timestamp,
                traits:
                [
                    RestTraits.HttpQueryTrait("created"),
                    RestTraits.TimestampFormatTrait("epoch-seconds"),
                ]
            )
            .Build(
                static () => new RequestValueParityInputBuilder(),
                static builder => new RequestValueParityInput(
                    builder.Key!,
                    builder.Modified,
                    builder.Tags!,
                    builder.Media!,
                    builder.Ratio,
                    builder.Score,
                    builder.Created
                )
            );
        var outputSchema = Schemas
            .Structure<RequestValueParityOutput, RequestValueParityOutputBuilder>(
                new ShapeId("example", "RequestValueParityOutput")
            )
            .Build(
                static () => new RequestValueParityOutputBuilder(),
                static _ => new RequestValueParityOutput()
            );
        var operation = Schemas.Operation(
            new ShapeId("example", "RequestValueParity"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/objects/{key+}")]
        );
        var request = new SmithyHttpRequest(
            HttpMethod.Get,
            "/objects/folder/photo%201.jpg?media=aGVsbG8gd29ybGQ%3D&ratio=NaN&score=Infinity&created=1577836800.25"
        );
        request.Headers["Last-Modified"] = ["Wed, 01 Jan 2020 00:00:00 GMT"];
        request.Headers["X-Tags"] = ["\"a,b\", plain"];

        var input = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("folder/photo 1.jpg", input.Key);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), input.Modified);
        Assert.Equal(["a,b", "plain"], input.Tags);
        Assert.Equal("hello world", input.Media);
        Assert.True(float.IsNaN(input.Ratio));
        Assert.Equal(double.PositiveInfinity, input.Score);
        Assert.Equal(
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(250),
            input.Created
        );
    }

    public sealed record UploadUserAvatarInput(string UserId, string Checksum, byte[] Payload);

    public sealed class UploadUserAvatarInputBuilder
    {
        public string? UserId { get; set; }

        public string? Checksum { get; set; }

        public byte[]? Payload { get; set; }
    }

    public sealed record UploadUserAvatarOutput;

    public sealed class UploadUserAvatarOutputBuilder { }

    public sealed record UploadUserAvatarStreamInput(
        string UserId,
        string Checksum,
        Stream Payload
    );

    public sealed class UploadUserAvatarStreamInputBuilder
    {
        public string? UserId { get; set; }

        public string? Checksum { get; set; }

        public Stream? Payload { get; set; }
    }

    public sealed record GetUserAvatarStreamOutput(string ETag, Stream Payload);

    public sealed class GetUserAvatarStreamOutputBuilder
    {
        public string? ETag { get; set; }

        public Stream? Payload { get; set; }
    }

    [Fact]
    public void RestJson1ProtocolSerializesHttpPayload()
    {
        var inputSchema = Schemas
            .Structure<UploadUserAvatarInput, UploadUserAvatarInputBuilder>(
                new ShapeId("example", "UploadUserAvatarInput")
            )
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Required(
                "checksum",
                static input => input.Checksum,
                static (builder, value) => builder.Checksum = value,
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("X-Checksum")]
            )
            .Required(
                "payload",
                static input => input.Payload,
                static (builder, value) => builder.Payload = value,
                Schemas.Blob,
                traits: [RestTraits.HttpPayloadTrait]
            )
            .Build(
                static () => new UploadUserAvatarInputBuilder(),
                static builder => new UploadUserAvatarInput(
                    builder.UserId!,
                    builder.Checksum!,
                    builder.Payload!
                )
            );
        var outputSchema = Schemas
            .Structure<UploadUserAvatarOutput, UploadUserAvatarOutputBuilder>(
                new ShapeId("example", "UploadUserAvatarOutput")
            )
            .Build(
                static () => new UploadUserAvatarOutputBuilder(),
                static _ => new UploadUserAvatarOutput()
            );
        var operation = Schemas.Operation(
            new ShapeId("example", "UploadUserAvatar"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("PUT", "/users/{userId}/avatar")]
        );
        var payload = "avatar bytes"u8.ToArray();
        var input = new UploadUserAvatarInput("ada", "abc123", payload);

        var request = Protocol(operation).SerializeRequest(input);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/users/ada/avatar", request.RequestUri);
        Assert.Equal("abc123", request.Headers["X-Checksum"].Single());
        Assert.Equal("application/octet-stream", request.ContentType);
        var body = Assert.IsType<SmithyHttpBody.Bytes>(request.Body);
        Assert.Equal(payload, body.Content);
    }

    [Fact]
    public void RestJson1ProtocolSerializesStreamingBlobPayloadWithoutBuffering()
    {
        var inputSchema = StreamingUploadInputSchema();
        var outputSchema = Schemas
            .Structure<UploadUserAvatarOutput, UploadUserAvatarOutputBuilder>(
                new ShapeId("example", "UploadUserAvatarOutput")
            )
            .Build(
                static () => new UploadUserAvatarOutputBuilder(),
                static _ => new UploadUserAvatarOutput()
            );
        var operation = Schemas.Operation(
            new ShapeId("example", "UploadUserAvatar"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("PUT", "/users/{userId}/avatar")]
        );
        var payload = new MemoryStream("avatar bytes"u8.ToArray());
        payload.Position = 2;
        var input = new UploadUserAvatarStreamInput("ada", "abc123", payload);

        var request = Protocol(operation).SerializeRequest(input);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/users/ada/avatar", request.RequestUri);
        Assert.Equal("abc123", request.Headers["X-Checksum"].Single());
        Assert.Equal("application/octet-stream", request.ContentType);
        var body = Assert.IsType<SmithyHttpBody.Streaming>(request.Body);
        Assert.Same(payload, body.Content);
        Assert.Equal(payload.Length - 2, body.ContentLength);
        Assert.False(request.ExpectStreamingResponse);
    }

    [Fact]
    public void RestJson1ProtocolDeserializesStreamingBlobPayloadWithoutBuffering()
    {
        var inputSchema = StreamingUploadInputSchema();
        var outputSchema = Schemas
            .Structure<UploadUserAvatarOutput, UploadUserAvatarOutputBuilder>(
                new ShapeId("example", "UploadUserAvatarOutput")
            )
            .Build(
                static () => new UploadUserAvatarOutputBuilder(),
                static _ => new UploadUserAvatarOutput()
            );
        var operation = Schemas.Operation(
            new ShapeId("example", "UploadUserAvatar"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("PUT", "/users/{userId}/avatar")]
        );
        var payload = new MemoryStream("avatar bytes"u8.ToArray());
        var request = new SmithyHttpRequest(HttpMethod.Put, "/users/ada/avatar")
        {
            Body = new SmithyHttpBody.Streaming(payload, payload.Length),
            ContentType = "application/octet-stream",
        };
        request.Headers["X-Checksum"] = ["abc123"];

        var input = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("ada", input.UserId);
        Assert.Equal("abc123", input.Checksum);
        Assert.Same(payload, input.Payload);
    }

    [Fact]
    public void RestJson1ProtocolDeserializesStreamingBlobResponseWithoutBuffering()
    {
        var inputSchema = Schemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserInput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var outputSchema = StreamingAvatarOutputSchema();
        var operation = Schemas.Operation(
            new ShapeId("example", "GetUserAvatar"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/users/{userId}/avatar")]
        );
        var payload = new MemoryStream("avatar bytes"u8.ToArray());
        var response = new SmithyHttpClientResponse(
            HttpStatusCode.OK,
            null,
            new SmithyHttpBody.Streaming(payload),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ETag"] = ["etag-1"],
            },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/octet-stream"],
            }
        );

        var output = Protocol(operation).DeserializeResponse(response);

        Assert.Equal("etag-1", output.ETag);
        Assert.Same(payload, output.Payload);
    }

    [Fact]
    public async Task RestJson1ProtocolSerializesStreamingBlobResponseWithoutBuffering()
    {
        var inputSchema = Schemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserInput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var outputSchema = StreamingAvatarOutputSchema();
        var operation = Schemas.Operation(
            new ShapeId("example", "GetUserAvatar"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/users/{userId}/avatar")]
        );
        var payload = new MemoryStream("avatar bytes"u8.ToArray());
        var output = new GetUserAvatarStreamOutput("etag-1", payload);

        var response = Protocol(operation).SerializeResponse(output);

        Assert.Equal((int)HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("etag-1", response.Headers["ETag"].Single());
        Assert.Equal("application/octet-stream", response.Headers["Content-Type"].Single());
        Assert.Equal(payload.Length, response.ContentLength);
        Assert.Equal("avatar bytes", Encoding.UTF8.GetString(await DrainAsync(response)));
    }

    [Fact]
    public async Task RestJson1ProtocolSerializesAndReadsOutputEventStream()
    {
        var protocol = Protocol(OutputEventStreamOperation("Watch"));

        var request = protocol.SerializeRequest(new Echo("ada"));

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/streams/ada", request.RequestUri);
        Assert.Equal(["application/vnd.amazon.eventstream"], request.Headers["Accept"]);
        Assert.True(request.ExpectStreamingResponse);

        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(
                new EventEnvelope("s-1", ToAsync([new ChatEvent.Message(new Echo("one"))]))
            )
        );

        var output = await protocol.DeserializeResponseAsync(response);
        Assert.Equal("s-1", output.StreamId);
        var message = Assert.IsType<ChatEvent.Message>(
            Assert.Single(await CollectAsync(output.Events))
        );
        Assert.Equal(new Echo("one"), message.Value);
    }

    [Fact]
    public async Task RestJson1ProtocolSerializesInputEventStreamRequest()
    {
        var protocol = Protocol(InputEventStreamOperation("Upload"));

        var request = protocol.SerializeRequest(
            new EventEnvelope("s-1", ToAsync([new ChatEvent.Message(new Echo("one"))]))
        );

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/streams/s-1", request.RequestUri);
        Assert.Equal("application/vnd.amazon.eventstream", request.ContentType);
        Assert.Equal(["application/json"], request.Headers["Accept"]);
        Assert.False(request.ExpectStreamingResponse);

        var messages = await ReadMessagesAsync(await BodyBytesAsync(request.Body));
        var message = Assert.Single(messages);
        Assert.Equal("event", message.StringHeader(":message-type"));
        Assert.Equal("message", message.StringHeader(":event-type"));
        Assert.Equal("application/json", message.StringHeader(":content-type"));

        var input = protocol.DeserializeRequest(
            new SmithyHttpRequest(HttpMethod.Post, "/streams/s-1")
            {
                Body = new SmithyHttpBody.Streaming(
                    new MemoryStream(await BodyBytesAsync(request.Body))
                ),
                ContentType = "application/vnd.amazon.eventstream",
            }
        );
        var chat = Assert.IsType<ChatEvent.Message>(
            Assert.Single(await CollectAsync(input.Events))
        );
        Assert.Equal(new Echo("one"), chat.Value);
    }

    [Fact]
    public async Task RestJson1ProtocolUsesEventStreamForDuplexRequestAndResponse()
    {
        var protocol = Protocol(DuplexEventStreamOperation("Chat"));

        var request = protocol.SerializeRequest(
            new EventEnvelope("s-1", ToAsync([new ChatEvent.Message(new Echo("in"))]))
        );

        Assert.Equal("application/vnd.amazon.eventstream", request.ContentType);
        Assert.Equal(["application/vnd.amazon.eventstream"], request.Headers["Accept"]);
        Assert.True(request.ExpectStreamingResponse);
        Assert.Single(await ReadMessagesAsync(await BodyBytesAsync(request.Body)));

        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(
                new EventEnvelope("s-2", ToAsync([new ChatEvent.Message(new Echo("out"))]))
            )
        );

        var output = await protocol.DeserializeResponseAsync(response);
        Assert.Equal("s-2", output.StreamId);
        var message = Assert.IsType<ChatEvent.Message>(
            Assert.Single(await CollectAsync(output.Events))
        );
        Assert.Equal(new Echo("out"), message.Value);
    }

    private static async Task<byte[]> DrainAsync(SmithyHttpServerResponse response)
    {
        var buffer = new MemoryStream();
        await foreach (var chunk in response.Body)
        {
            buffer.Write(chunk.Span);
        }

        return buffer.ToArray();
    }

    private static async Task<SmithyHttpClientResponse> ToClientResponseAsync(
        SmithyHttpServerResponse response
    )
    {
        var headers = response.Headers.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase
        );
        return new SmithyHttpClientResponse(
            (HttpStatusCode)response.StatusCode,
            null,
            new SmithyHttpBody.Streaming(new MemoryStream(await DrainAsync(response))),
            headers,
            headers
        );
    }

    private static async Task<byte[]> BodyBytesAsync(SmithyHttpBody body)
    {
        var buffer = new MemoryStream();
        await foreach (var chunk in BodyChunks(body))
        {
            buffer.Write(chunk.Span);
        }

        return buffer.ToArray();
    }

    private static IAsyncEnumerable<ReadOnlyMemory<byte>> BodyChunks(SmithyHttpBody body) =>
        body switch
        {
            SmithyHttpBody.EventStreaming eventStreaming => eventStreaming.Content,
            SmithyHttpBody.Bytes bytes => ToAsyncBytes([bytes.Content]),
            _ => ToAsyncBytes([]),
        };

    private static async Task<List<EventStreamMessage>> ReadMessagesAsync(byte[] framed)
    {
        var messages = new List<EventStreamMessage>();
        await foreach (
            var message in EventStreamMessageReader.ReadAllAsync(new MemoryStream(framed))
        )
        {
            messages.Add(message);
        }

        return messages;
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ToAsyncBytes(
        IEnumerable<byte[]> values
    )
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        var result = new List<T>();
        await foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }

    private static Schema<Echo> EchoSchema(string name) =>
        Schemas
            .Structure<Echo, EchoBuilder>(new ShapeId("example", name))
            .Required(
                "message",
                static value => value.Message,
                static (builder, value) => builder.Message = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Build(static () => new EchoBuilder(), static b => new Echo(b.Message!));

    private static Schema<ChatEvent> ChatEventSchema(string name) =>
        Schemas
            .Union<ChatEvent>(new ShapeId("example", name))
            .Case(
                "message",
                static value => value is ChatEvent.Message,
                static value => ((ChatEvent.Message)value).Value,
                static value => new ChatEvent.Message(value!),
                EchoSchema($"{name}Message")
            )
            .Build();

    private static Schema<EventEnvelope> EventEnvelopeSchema(
        string name,
        IReadOnlyList<Trait> streamIdTraits
    ) =>
        Schemas
            .Structure<EventEnvelope, EventEnvelopeBuilder>(new ShapeId("example", name))
            .Required(
                "streamId",
                static value => value.StreamId,
                static (builder, value) => builder.StreamId = value,
                Schemas.String,
                traits: streamIdTraits
            )
            .Required(
                "events",
                static value => value.Events,
                static (builder, value) => builder.Events = value,
                Schemas.EventStream(ChatEventSchema($"{name}Event")),
                traits: [RestTraits.HttpPayloadTrait, new Trait(RestTraits.Streaming)]
            )
            .Build(
                static () => new EventEnvelopeBuilder(),
                static builder => new EventEnvelope(builder.StreamId!, builder.Events!)
            );

    private static OperationSchema<Echo, EventEnvelope> OutputEventStreamOperation(string name) =>
        Schemas.Operation(
            new ShapeId("example", name),
            EchoSchema($"{name}Input"),
            EventEnvelopeSchema($"{name}Output", [RestTraits.HttpHeaderTrait("X-Stream-Id")]),
            traits: [RestTraits.HttpTrait("GET", "/streams/{message}")]
        );

    private static OperationSchema<EventEnvelope, Echo> InputEventStreamOperation(string name) =>
        Schemas.Operation(
            new ShapeId("example", name),
            EventEnvelopeSchema($"{name}Input", [RestTraits.HttpLabelTrait]),
            EchoSchema($"{name}Output"),
            traits: [RestTraits.HttpTrait("POST", "/streams/{streamId}")]
        );

    private static OperationSchema<EventEnvelope, EventEnvelope> DuplexEventStreamOperation(
        string name
    ) =>
        Schemas.Operation(
            new ShapeId("example", name),
            EventEnvelopeSchema($"{name}Input", [RestTraits.HttpLabelTrait]),
            EventEnvelopeSchema($"{name}Output", [RestTraits.HttpHeaderTrait("X-Stream-Id")]),
            traits: [RestTraits.HttpTrait("POST", "/streams/{streamId}")]
        );

    private static StructSchema<
        UploadUserAvatarStreamInput,
        UploadUserAvatarStreamInputBuilder
    > StreamingUploadInputSchema() =>
        Schemas
            .Structure<UploadUserAvatarStreamInput, UploadUserAvatarStreamInputBuilder>(
                new ShapeId("example", "UploadUserAvatarInput")
            )
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Required(
                "checksum",
                static input => input.Checksum,
                static (builder, value) => builder.Checksum = value,
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("X-Checksum")]
            )
            .Required(
                "payload",
                static input => input.Payload,
                static (builder, value) => builder.Payload = value,
                Schemas.StreamingBlob,
                traits:
                [
                    RestTraits.HttpPayloadTrait,
                    new Trait(RestTraits.Streaming),
                    new Trait(RestTraits.RequiresLength),
                ]
            )
            .Build(
                static () => new UploadUserAvatarStreamInputBuilder(),
                static builder => new UploadUserAvatarStreamInput(
                    builder.UserId!,
                    builder.Checksum!,
                    builder.Payload!
                )
            );

    private static StructSchema<
        GetUserAvatarStreamOutput,
        GetUserAvatarStreamOutputBuilder
    > StreamingAvatarOutputSchema() =>
        Schemas
            .Structure<GetUserAvatarStreamOutput, GetUserAvatarStreamOutputBuilder>(
                new ShapeId("example", "GetUserAvatarOutput")
            )
            .Required(
                "eTag",
                static output => output.ETag,
                static (builder, value) => builder.ETag = value,
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("ETag")]
            )
            .Required(
                "payload",
                static output => output.Payload,
                static (builder, value) => builder.Payload = value,
                Schemas.StreamingBlob,
                traits:
                [
                    RestTraits.HttpPayloadTrait,
                    new Trait(RestTraits.Streaming),
                    new Trait(RestTraits.RequiresLength),
                ]
            )
            .Build(
                static () => new GetUserAvatarStreamOutputBuilder(),
                static builder => new GetUserAvatarStreamOutput(builder.ETag!, builder.Payload!)
            );

    public sealed record GetUserProfileOutput(
        int StatusCode,
        string ETag,
        IReadOnlyDictionary<string, string> ExtraHeaders,
        string DisplayName
    );

    public sealed class GetUserProfileOutputBuilder
    {
        public int StatusCode { get; set; }

        public string? ETag { get; set; }

        public IReadOnlyDictionary<string, string>? ExtraHeaders { get; set; }

        public string? DisplayName { get; set; }
    }

    [Fact]
    public async Task RestJson1ProtocolSerializesResponseBindingsAndBody()
    {
        var inputSchema = Schemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserInput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var stringMapSchema = Schemas.Map(new ShapeId("example", "StringMap"), Schemas.String);
        var outputSchema = Schemas
            .Structure<GetUserProfileOutput, GetUserProfileOutputBuilder>(
                new ShapeId("example", "GetUserProfileOutput")
            )
            .Required(
                "statusCode",
                static output => output.StatusCode,
                static (builder, value) => builder.StatusCode = value,
                Schemas.Integer,
                traits: [RestTraits.HttpResponseCodeTrait]
            )
            .Required(
                "eTag",
                static output => output.ETag,
                static (builder, value) => builder.ETag = value,
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("ETag")]
            )
            .Required(
                "extraHeaders",
                static output => output.ExtraHeaders,
                static (builder, value) => builder.ExtraHeaders = value,
                stringMapSchema,
                traits: [RestTraits.HttpPrefixHeadersTrait("X-Extra-")]
            )
            .Required(
                "displayName",
                static output => output.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                Schemas.String
            )
            .Build(
                static () => new GetUserProfileOutputBuilder(),
                static builder => new GetUserProfileOutput(
                    builder.StatusCode,
                    builder.ETag!,
                    builder.ExtraHeaders!,
                    builder.DisplayName!
                )
            );
        var operation = Schemas.Operation(
            new ShapeId("example", "GetUserProfile"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/users/{userId}")]
        );
        var output = new GetUserProfileOutput(
            201,
            "etag-1",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Trace"] = "abc" },
            "Ada"
        );

        var response = Protocol(operation).SerializeResponse(output);

        Assert.Equal((int)HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("etag-1", response.Headers["ETag"].Single());
        Assert.Equal("abc", response.Headers["X-Extra-Trace"].Single());
        Assert.Equal("application/json", response.Headers["Content-Type"].Single());
        Assert.Equal(
            "{\"displayName\":\"Ada\"}",
            Encoding.UTF8.GetString(await DrainAsync(response))
        );
    }

    [Fact]
    public void RestJson1ProtocolDeserializesResponseBindingsAndBody()
    {
        var inputSchema = Schemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserInput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var stringMapSchema = Schemas.Map(new ShapeId("example", "StringMap"), Schemas.String);
        var outputSchema = Schemas
            .Structure<GetUserProfileOutput, GetUserProfileOutputBuilder>(
                new ShapeId("example", "GetUserProfileOutput")
            )
            .Required(
                "statusCode",
                static output => output.StatusCode,
                static (builder, value) => builder.StatusCode = value,
                Schemas.Integer,
                traits: [RestTraits.HttpResponseCodeTrait]
            )
            .Required(
                "eTag",
                static output => output.ETag,
                static (builder, value) => builder.ETag = value,
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("ETag")]
            )
            .Required(
                "extraHeaders",
                static output => output.ExtraHeaders,
                static (builder, value) => builder.ExtraHeaders = value,
                stringMapSchema,
                traits: [RestTraits.HttpPrefixHeadersTrait("X-Extra-")]
            )
            .Required(
                "displayName",
                static output => output.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                Schemas.String
            )
            .Build(
                static () => new GetUserProfileOutputBuilder(),
                static builder => new GetUserProfileOutput(
                    builder.StatusCode,
                    builder.ETag!,
                    builder.ExtraHeaders!,
                    builder.DisplayName!
                )
            );
        var operation = Schemas.Operation(
            new ShapeId("example", "GetUserProfile"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/users/{userId}")]
        );
        var response = new SmithyHttpClientResponse(
            HttpStatusCode.Created,
            null,
            Encoding.UTF8.GetBytes("{\"displayName\":\"Ada\"}"),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ETag"] = ["etag-1"],
                ["X-Extra-Trace"] = ["abc"],
            },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/json"],
            }
        );

        var output = Protocol(operation).DeserializeResponse(response);

        Assert.Equal(new GetUserProfileOutput(201, "etag-1", output.ExtraHeaders, "Ada"), output);
        Assert.Equal("abc", output.ExtraHeaders["Trace"]);
    }

    public sealed record ListInput(string? NextToken = null, int? PageSize = null);

    public sealed class ListInputBuilder
    {
        public string? NextToken { get; set; }
        public int? PageSize { get; set; }
    }

    public sealed record ListOutput(string? NextToken = null);

    public sealed class ListOutputBuilder
    {
        public string? NextToken { get; set; }
    }

    [Fact]
    public void QueryParameterRoundTripPreservesNonNullToken()
    {
        var inputSchema = Schemas
            .Structure<ListInput, ListInputBuilder>(new ShapeId("example", "ListInput"))
            .Optional(
                "nextToken",
                static i => i.NextToken!,
                static (b, v) => b.NextToken = v,
                Schemas.NullableReference(Schemas.String),
                traits: [RestTraits.HttpQueryTrait("nextToken")]
            )
            .Optional(
                "pageSize",
                static i => i.PageSize,
                static (b, v) => b.PageSize = v,
                Schemas.Nullable(Schemas.Integer),
                traits: [RestTraits.HttpQueryTrait("pageSize")]
            )
            .Build(
                static () => new ListInputBuilder(),
                static b => new ListInput(b.NextToken, b.PageSize)
            );
        var outputSchema = Schemas
            .Structure<ListOutput, ListOutputBuilder>(new ShapeId("example", "ListOutput"))
            .Optional(
                "nextToken",
                static o => o.NextToken!,
                static (b, v) => b.NextToken = v,
                Schemas.NullableReference(Schemas.String)
            )
            .Build(static () => new ListOutputBuilder(), static b => new ListOutput(b.NextToken));
        var operation = Schemas.Operation(
            new ShapeId("example", "ListItems"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/items")]
        );

        // Client serializes request with non-null nextToken
        var request = Protocol(operation)
            .SerializeRequest(new ListInput(NextToken: "page2-token", PageSize: 10));

        Assert.Contains("nextToken=page2-token", request.RequestUri);
        Assert.Contains("pageSize=10", request.RequestUri);

        // Server deserializes the same request
        var deserializedInput = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("page2-token", deserializedInput.NextToken);
        Assert.Equal(10, deserializedInput.PageSize);
    }

    [Fact]
    public void QueryParameterRoundTripHandlesNullToken()
    {
        var inputSchema = Schemas
            .Structure<ListInput, ListInputBuilder>(new ShapeId("example", "ListInput2"))
            .Optional(
                "nextToken",
                static i => i.NextToken!,
                static (b, v) => b.NextToken = v,
                Schemas.NullableReference(Schemas.String),
                traits: [RestTraits.HttpQueryTrait("nextToken")]
            )
            .Build(static () => new ListInputBuilder(), static b => new ListInput(b.NextToken));
        var outputSchema = Schemas
            .Structure<ListOutput, ListOutputBuilder>(new ShapeId("example", "ListOutput2"))
            .Build(static () => new ListOutputBuilder(), static b => new ListOutput());
        var operation = Schemas.Operation(
            new ShapeId("example", "ListItems2"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/items")]
        );

        // First page: no nextToken
        var request = Protocol(operation).SerializeRequest(new ListInput(NextToken: null));

        Assert.DoesNotContain("nextToken", request.RequestUri);

        var input = Protocol(operation).DeserializeRequest(request);
        Assert.Null(input.NextToken);
    }

    [Fact]
    public void DeserializeReadsQueryFromServerStyleRequestUri()
    {
        var inputSchema = Schemas
            .Structure<ListInput, ListInputBuilder>(new ShapeId("example", "ListInput3"))
            .Optional(
                "nextToken",
                static i => i.NextToken!,
                static (b, v) => b.NextToken = v,
                Schemas.NullableReference(Schemas.String),
                traits: [RestTraits.HttpQueryTrait("nextToken")]
            )
            .Optional(
                "pageSize",
                static i => i.PageSize,
                static (b, v) => b.PageSize = v,
                Schemas.Nullable(Schemas.Integer),
                traits: [RestTraits.HttpQueryTrait("pageSize")]
            )
            .Build(
                static () => new ListInputBuilder(),
                static b => new ListInput(b.NextToken, b.PageSize)
            );
        var outputSchema = Schemas
            .Structure<ListOutput, ListOutputBuilder>(new ShapeId("example", "ListOutput3"))
            .Build(static () => new ListOutputBuilder(), static b => new ListOutput());
        var operation = Schemas.Operation(
            new ShapeId("example", "ListItems3"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("GET", "/cities")]
        );

        // Exactly what SmithyAspNetCoreProtocol.CreateSmithyHttpRequestAsync builds:
        // a relative path + query string, query params in the order the client sent them.
        var request = new SmithyHttpRequest(HttpMethod.Get, "/cities?pageSize=3&nextToken=LAX");

        var input = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("LAX", input.NextToken);
        Assert.Equal(3, input.PageSize);
    }

    [Fact]
    public async Task CreateSmithyHttpRequestPreservesQueryString()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/cities";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(
            "?pageSize=3&nextToken=LAX"
        );

        var request = await NSmithy.Server.AspNetCore.SmithyAspNetCoreHost.ToSmithyRequestAsync(
            httpContext
        );

        Assert.Equal("/cities?pageSize=3&nextToken=LAX", request.RequestUri);
    }
}
