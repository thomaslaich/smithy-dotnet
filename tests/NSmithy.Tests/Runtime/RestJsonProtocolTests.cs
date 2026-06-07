using System.Net;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Rest;
using NSmithy.Protocols.RestJson;

namespace NSmithy.Tests.Runtime;

public sealed class RestJsonProtocolTests
{
    public sealed record UpdateUserInput(string UserId, string? RequestToken, string DisplayName);

    public sealed class UpdateUserInputBuilder
    {
        public string? UserId { get; set; }

        public string? RequestToken { get; set; }

        public string? DisplayName { get; set; }
    }

    public sealed record UpdateUserOutput;

    public sealed class UpdateUserOutputBuilder { }

    [Fact]
    public void RestJsonProtocolSerializesLabelsHeadersAndBodySeparately()
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

        var request = RestJsonProtocol.SerializeRequest(operation, input);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/users/ada%20lovelace", request.RequestUri);
        Assert.Equal("token-123", request.Headers["X-Request-Token"].Single());
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("{\"displayName\":\"Ada\"}", Encoding.UTF8.GetString(request.Content!));
    }

    [Fact]
    public void RestJsonProtocolDeserializesLabelsHeadersAndBodySeparately()
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
            Content = Encoding.UTF8.GetBytes("{\"displayName\":\"Ada\"}"),
            ContentType = "application/json",
        };
        request.Headers["X-Request-Token"] = ["token-123"];

        var input = RestJsonProtocol.DeserializeRequest(operation, request);

        Assert.Equal(new UpdateUserInput("ada lovelace", "token-123", "Ada"), input);
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
    public void RestJsonProtocolSerializesAllRequestHttpBindings()
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

        var request = RestJsonProtocol.SerializeRequest(operation, input);

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/users/ada%20lovelace?includeDetails=true&tag=admin&tag=staff&debug=true",
            request.RequestUri
        );
        Assert.Equal("abc", request.Headers["X-Extra-Trace"].Single());
        Assert.Null(request.Content);
        Assert.Null(request.ContentType);
    }

    [Fact]
    public void RestJsonProtocolDeserializesAllRequestHttpBindings()
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
        var request = new SmithyHttpRequest(
            HttpMethod.Get,
            "/users/ada%20lovelace?includeDetails=true&tag=admin&tag=staff&debug=true"
        );
        request.Headers["X-Extra-Trace"] = ["abc"];

        var input = RestJsonProtocol.DeserializeRequest(operation, request);

        Assert.Equal("ada lovelace", input.UserId);
        Assert.True(input.IncludeDetails);
        Assert.Equal(["admin", "staff"], input.Tags);
        Assert.Equal("true", input.ExtraQuery["debug"]);
        Assert.DoesNotContain("includeDetails", input.ExtraQuery.Keys);
        Assert.DoesNotContain("tag", input.ExtraQuery.Keys);
        Assert.Equal("abc", input.ExtraHeaders["Trace"]);
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
    public void RestJsonProtocolRoundTripsStringEnumHeaderListWithQuotedComma()
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

        var request = RestJsonProtocol.SerializeRequest(operation, input);
        var decoded = RestJsonProtocol.DeserializeRequest(operation, request);

        Assert.Equal("\"ACTIVE,BLUE\", PENDING", request.Headers["X-Status"].Single());
        Assert.Equal(input.Statuses, decoded.Statuses);
    }

    [Fact]
    public void RestJsonProtocolSerializesRequestHttpValueParity()
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

        var request = RestJsonProtocol.SerializeRequest(operation, input);

        Assert.Equal(
            "/objects/folder/photo%201.jpg?media=aGVsbG8gd29ybGQ%3D&ratio=NaN&score=Infinity&created=1577836800.25",
            request.RequestUri
        );
        Assert.Equal("Wed, 01 Jan 2020 00:00:00 GMT", request.Headers["Last-Modified"].Single());
        Assert.Equal("\"a,b\", plain", request.Headers["X-Tags"].Single());
    }

    [Fact]
    public void RestJsonProtocolDeserializesRequestHttpValueParity()
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

        var input = RestJsonProtocol.DeserializeRequest(operation, request);

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

    [Fact]
    public void RestJsonProtocolSerializesHttpPayload()
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

        var request = RestJsonProtocol.SerializeRequest(operation, input);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/users/ada/avatar", request.RequestUri);
        Assert.Equal("abc123", request.Headers["X-Checksum"].Single());
        Assert.Equal("application/octet-stream", request.ContentType);
        Assert.Equal(payload, request.Content);
    }

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
    public void RestJsonProtocolSerializesResponseBindingsAndBody()
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

        var response = RestJsonProtocol.SerializeResponse(operation, output);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("etag-1", response.Headers["ETag"].Single());
        Assert.Equal("abc", response.Headers["X-Extra-Trace"].Single());
        Assert.Equal("application/json", response.ContentHeaders["Content-Type"].Single());
        Assert.Equal("{\"displayName\":\"Ada\"}", response.ContentText);
    }

    [Fact]
    public void RestJsonProtocolDeserializesResponseBindingsAndBody()
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
        var response = new SmithyHttpResponse(
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

        var output = RestJsonProtocol.DeserializeResponse(operation, response);

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
        var request = RestJsonProtocol.SerializeRequest(
            operation,
            new ListInput(NextToken: "page2-token", PageSize: 10)
        );

        Assert.Contains("nextToken=page2-token", request.RequestUri);
        Assert.Contains("pageSize=10", request.RequestUri);

        // Server deserializes the same request
        var deserializedInput = RestJsonProtocol.DeserializeRequest(operation, request);

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
        var request = RestJsonProtocol.SerializeRequest(operation, new ListInput(NextToken: null));

        Assert.DoesNotContain("nextToken", request.RequestUri);

        var input = RestJsonProtocol.DeserializeRequest(operation, request);
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

        var input = RestJsonProtocol.DeserializeRequest(operation, request);

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

        var request =
            await NSmithy.Server.AspNetCore.SmithyAspNetCoreProtocol.CreateSmithyHttpRequestAsync(
                httpContext
            );

        Assert.Equal("/cities?pageSize=3&nextToken=LAX", request.RequestUri);
    }
}
