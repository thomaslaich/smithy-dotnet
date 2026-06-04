using System.Net;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Functional;
using NSmithy.Http;
using NSmithy.Protocols.Rest;
using NSmithy.Protocols.RestJson;

namespace NSmithy.Tests.Runtime;

public sealed class FunctionalRestJsonProtocolTests
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
    public void FunctionalRestJsonProtocolSerializesLabelsHeadersAndBodySeparately()
    {
        var inputSchema = FunctionalSchemas
            .Structure<UpdateUserInput, UpdateUserInputBuilder>(
                new ShapeId("example", "UpdateUserInput")
            )
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpLabelTrait]
            )
            .Optional(
                "requestToken",
                static input => input.RequestToken!,
                static (builder, value) => builder.RequestToken = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpHeaderTrait("X-Request-Token")]
            )
            .Required(
                "displayName",
                static input => input.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                FunctionalSchemas.String
            )
            .Build(
                static () => new UpdateUserInputBuilder(),
                static builder => new UpdateUserInput(
                    builder.UserId!,
                    builder.RequestToken,
                    builder.DisplayName!
                )
            );
        var outputSchema = FunctionalSchemas
            .Structure<UpdateUserOutput, UpdateUserOutputBuilder>(
                new ShapeId("example", "UpdateUserOutput")
            )
            .Build(static () => new UpdateUserOutputBuilder(), static _ => new UpdateUserOutput());
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "UpdateUser"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("PUT", "/users/{userId}")]
        );
        var input = new UpdateUserInput("ada lovelace", "token-123", "Ada");

        var request = FunctionalRestJsonProtocol.SerializeRequest(operation, input);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/users/ada%20lovelace", request.RequestUri);
        Assert.Equal("token-123", request.Headers["X-Request-Token"].Single());
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("{\"displayName\":\"Ada\"}", Encoding.UTF8.GetString(request.Content!));
    }

    [Fact]
    public void FunctionalRestJsonProtocolDeserializesLabelsHeadersAndBodySeparately()
    {
        var inputSchema = FunctionalSchemas
            .Structure<UpdateUserInput, UpdateUserInputBuilder>(
                new ShapeId("example", "UpdateUserInput")
            )
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpLabelTrait]
            )
            .Optional(
                "requestToken",
                static input => input.RequestToken!,
                static (builder, value) => builder.RequestToken = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpHeaderTrait("X-Request-Token")]
            )
            .Required(
                "displayName",
                static input => input.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                FunctionalSchemas.String
            )
            .Build(
                static () => new UpdateUserInputBuilder(),
                static builder => new UpdateUserInput(
                    builder.UserId!,
                    builder.RequestToken,
                    builder.DisplayName!
                )
            );
        var outputSchema = FunctionalSchemas
            .Structure<UpdateUserOutput, UpdateUserOutputBuilder>(
                new ShapeId("example", "UpdateUserOutput")
            )
            .Build(static () => new UpdateUserOutputBuilder(), static _ => new UpdateUserOutput());
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "UpdateUser"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("PUT", "/users/{userId}")]
        );
        var request = new SmithyHttpRequest(HttpMethod.Put, "/users/ada%20lovelace")
        {
            Content = Encoding.UTF8.GetBytes("{\"displayName\":\"Ada\"}"),
            ContentType = "application/json",
        };
        request.Headers["X-Request-Token"] = ["token-123"];

        var input = FunctionalRestJsonProtocol.DeserializeRequest(operation, request);

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
    public void FunctionalRestJsonProtocolSerializesAllRequestHttpBindings()
    {
        var tagListSchema = FunctionalSchemas.List(
            new ShapeId("example", "TagList"),
            FunctionalSchemas.String
        );
        var stringMapSchema = FunctionalSchemas.Map(
            new ShapeId("example", "StringMap"),
            FunctionalSchemas.String
        );
        var inputSchema = FunctionalSchemas
            .Structure<GetUserInput, GetUserInputBuilder>(new ShapeId("example", "GetUserInput"))
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpLabelTrait]
            )
            .Required(
                "includeDetails",
                static input => input.IncludeDetails,
                static (builder, value) => builder.IncludeDetails = value,
                FunctionalSchemas.Boolean,
                traits: [FunctionalRestTraits.HttpQueryTrait("includeDetails")]
            )
            .Required(
                "tags",
                static input => input.Tags,
                static (builder, value) => builder.Tags = value,
                tagListSchema,
                traits: [FunctionalRestTraits.HttpQueryTrait("tag")]
            )
            .Required(
                "extraQuery",
                static input => input.ExtraQuery,
                static (builder, value) => builder.ExtraQuery = value,
                stringMapSchema,
                traits: [FunctionalRestTraits.HttpQueryParamsTrait]
            )
            .Required(
                "extraHeaders",
                static input => input.ExtraHeaders,
                static (builder, value) => builder.ExtraHeaders = value,
                stringMapSchema,
                traits: [FunctionalRestTraits.HttpPrefixHeadersTrait("X-Extra-")]
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
        var outputSchema = FunctionalSchemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserOutput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "GetUser"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("GET", "/users/{userId}")]
        );
        var input = new GetUserInput(
            "ada lovelace",
            true,
            ["admin", "staff"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["debug"] = "true" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Trace"] = "abc" }
        );

        var request = FunctionalRestJsonProtocol.SerializeRequest(operation, input);

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
    public void FunctionalRestJsonProtocolDeserializesAllRequestHttpBindings()
    {
        var tagListSchema = FunctionalSchemas.List(
            new ShapeId("example", "TagList"),
            FunctionalSchemas.String
        );
        var stringMapSchema = FunctionalSchemas.Map(
            new ShapeId("example", "StringMap"),
            FunctionalSchemas.String
        );
        var inputSchema = FunctionalSchemas
            .Structure<GetUserInput, GetUserInputBuilder>(new ShapeId("example", "GetUserInput"))
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpLabelTrait]
            )
            .Required(
                "includeDetails",
                static input => input.IncludeDetails,
                static (builder, value) => builder.IncludeDetails = value,
                FunctionalSchemas.Boolean,
                traits: [FunctionalRestTraits.HttpQueryTrait("includeDetails")]
            )
            .Required(
                "tags",
                static input => input.Tags,
                static (builder, value) => builder.Tags = value,
                tagListSchema,
                traits: [FunctionalRestTraits.HttpQueryTrait("tag")]
            )
            .Required(
                "extraQuery",
                static input => input.ExtraQuery,
                static (builder, value) => builder.ExtraQuery = value,
                stringMapSchema,
                traits: [FunctionalRestTraits.HttpQueryParamsTrait]
            )
            .Required(
                "extraHeaders",
                static input => input.ExtraHeaders,
                static (builder, value) => builder.ExtraHeaders = value,
                stringMapSchema,
                traits: [FunctionalRestTraits.HttpPrefixHeadersTrait("X-Extra-")]
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
        var outputSchema = FunctionalSchemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserOutput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "GetUser"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("GET", "/users/{userId}")]
        );
        var request = new SmithyHttpRequest(
            HttpMethod.Get,
            "/users/ada%20lovelace?includeDetails=true&tag=admin&tag=staff&debug=true"
        );
        request.Headers["X-Extra-Trace"] = ["abc"];

        var input = FunctionalRestJsonProtocol.DeserializeRequest(operation, request);

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

    [Fact]
    public void FunctionalRestJsonProtocolSerializesRequestHttpValueParity()
    {
        var tagListSchema = FunctionalSchemas.List(
            new ShapeId("example", "ParityTagList"),
            FunctionalSchemas.String
        );
        var inputSchema = FunctionalSchemas
            .Structure<RequestValueParityInput, RequestValueParityInputBuilder>(
                new ShapeId("example", "RequestValueParityInput")
            )
            .Required(
                "key",
                static input => input.Key,
                static (builder, value) => builder.Key = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpLabelTrait]
            )
            .Required(
                "modified",
                static input => input.Modified,
                static (builder, value) => builder.Modified = value,
                FunctionalSchemas.Timestamp,
                traits: [FunctionalRestTraits.HttpHeaderTrait("Last-Modified")]
            )
            .Required(
                "tags",
                static input => input.Tags,
                static (builder, value) => builder.Tags = value,
                tagListSchema,
                traits: [FunctionalRestTraits.HttpHeaderTrait("X-Tags")]
            )
            .Required(
                "media",
                static input => input.Media,
                static (builder, value) => builder.Media = value,
                FunctionalSchemas.String,
                traits:
                [
                    FunctionalRestTraits.HttpQueryTrait("media"),
                    FunctionalRestTraits.MediaTypeTrait("text/plain"),
                ]
            )
            .Required(
                "ratio",
                static input => input.Ratio,
                static (builder, value) => builder.Ratio = value,
                FunctionalSchemas.Float,
                traits: [FunctionalRestTraits.HttpQueryTrait("ratio")]
            )
            .Required(
                "score",
                static input => input.Score,
                static (builder, value) => builder.Score = value,
                FunctionalSchemas.Double,
                traits: [FunctionalRestTraits.HttpQueryTrait("score")]
            )
            .Required(
                "created",
                static input => input.Created,
                static (builder, value) => builder.Created = value,
                FunctionalSchemas.Timestamp,
                traits:
                [
                    FunctionalRestTraits.HttpQueryTrait("created"),
                    FunctionalRestTraits.TimestampFormatTrait("epoch-seconds"),
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
        var outputSchema = FunctionalSchemas
            .Structure<RequestValueParityOutput, RequestValueParityOutputBuilder>(
                new ShapeId("example", "RequestValueParityOutput")
            )
            .Build(
                static () => new RequestValueParityOutputBuilder(),
                static _ => new RequestValueParityOutput()
            );
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "RequestValueParity"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("GET", "/objects/{key+}")]
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

        var request = FunctionalRestJsonProtocol.SerializeRequest(operation, input);

        Assert.Equal(
            "/objects/folder/photo%201.jpg?media=aGVsbG8gd29ybGQ%3D&ratio=NaN&score=Infinity&created=1577836800.25",
            request.RequestUri
        );
        Assert.Equal("Wed, 01 Jan 2020 00:00:00 GMT", request.Headers["Last-Modified"].Single());
        Assert.Equal("\"a,b\", plain", request.Headers["X-Tags"].Single());
    }

    [Fact]
    public void FunctionalRestJsonProtocolDeserializesRequestHttpValueParity()
    {
        var tagListSchema = FunctionalSchemas.List(
            new ShapeId("example", "ParityTagList"),
            FunctionalSchemas.String
        );
        var inputSchema = FunctionalSchemas
            .Structure<RequestValueParityInput, RequestValueParityInputBuilder>(
                new ShapeId("example", "RequestValueParityInput")
            )
            .Required(
                "key",
                static input => input.Key,
                static (builder, value) => builder.Key = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpLabelTrait]
            )
            .Required(
                "modified",
                static input => input.Modified,
                static (builder, value) => builder.Modified = value,
                FunctionalSchemas.Timestamp,
                traits: [FunctionalRestTraits.HttpHeaderTrait("Last-Modified")]
            )
            .Required(
                "tags",
                static input => input.Tags,
                static (builder, value) => builder.Tags = value,
                tagListSchema,
                traits: [FunctionalRestTraits.HttpHeaderTrait("X-Tags")]
            )
            .Required(
                "media",
                static input => input.Media,
                static (builder, value) => builder.Media = value,
                FunctionalSchemas.String,
                traits:
                [
                    FunctionalRestTraits.HttpQueryTrait("media"),
                    FunctionalRestTraits.MediaTypeTrait("text/plain"),
                ]
            )
            .Required(
                "ratio",
                static input => input.Ratio,
                static (builder, value) => builder.Ratio = value,
                FunctionalSchemas.Float,
                traits: [FunctionalRestTraits.HttpQueryTrait("ratio")]
            )
            .Required(
                "score",
                static input => input.Score,
                static (builder, value) => builder.Score = value,
                FunctionalSchemas.Double,
                traits: [FunctionalRestTraits.HttpQueryTrait("score")]
            )
            .Required(
                "created",
                static input => input.Created,
                static (builder, value) => builder.Created = value,
                FunctionalSchemas.Timestamp,
                traits:
                [
                    FunctionalRestTraits.HttpQueryTrait("created"),
                    FunctionalRestTraits.TimestampFormatTrait("epoch-seconds"),
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
        var outputSchema = FunctionalSchemas
            .Structure<RequestValueParityOutput, RequestValueParityOutputBuilder>(
                new ShapeId("example", "RequestValueParityOutput")
            )
            .Build(
                static () => new RequestValueParityOutputBuilder(),
                static _ => new RequestValueParityOutput()
            );
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "RequestValueParity"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("GET", "/objects/{key+}")]
        );
        var request = new SmithyHttpRequest(
            HttpMethod.Get,
            "/objects/folder/photo%201.jpg?media=aGVsbG8gd29ybGQ%3D&ratio=NaN&score=Infinity&created=1577836800.25"
        );
        request.Headers["Last-Modified"] = ["Wed, 01 Jan 2020 00:00:00 GMT"];
        request.Headers["X-Tags"] = ["\"a,b\", plain"];

        var input = FunctionalRestJsonProtocol.DeserializeRequest(operation, request);

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
    public void FunctionalRestJsonProtocolSerializesHttpPayload()
    {
        var inputSchema = FunctionalSchemas
            .Structure<UploadUserAvatarInput, UploadUserAvatarInputBuilder>(
                new ShapeId("example", "UploadUserAvatarInput")
            )
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpLabelTrait]
            )
            .Required(
                "checksum",
                static input => input.Checksum,
                static (builder, value) => builder.Checksum = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpHeaderTrait("X-Checksum")]
            )
            .Required(
                "payload",
                static input => input.Payload,
                static (builder, value) => builder.Payload = value,
                FunctionalSchemas.Blob,
                traits: [FunctionalRestTraits.HttpPayloadTrait]
            )
            .Build(
                static () => new UploadUserAvatarInputBuilder(),
                static builder => new UploadUserAvatarInput(
                    builder.UserId!,
                    builder.Checksum!,
                    builder.Payload!
                )
            );
        var outputSchema = FunctionalSchemas
            .Structure<UploadUserAvatarOutput, UploadUserAvatarOutputBuilder>(
                new ShapeId("example", "UploadUserAvatarOutput")
            )
            .Build(
                static () => new UploadUserAvatarOutputBuilder(),
                static _ => new UploadUserAvatarOutput()
            );
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "UploadUserAvatar"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("PUT", "/users/{userId}/avatar")]
        );
        var payload = "avatar bytes"u8.ToArray();
        var input = new UploadUserAvatarInput("ada", "abc123", payload);

        var request = FunctionalRestJsonProtocol.SerializeRequest(operation, input);

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
    public void FunctionalRestJsonProtocolSerializesResponseBindingsAndBody()
    {
        var inputSchema = FunctionalSchemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserInput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var stringMapSchema = FunctionalSchemas.Map(
            new ShapeId("example", "StringMap"),
            FunctionalSchemas.String
        );
        var outputSchema = FunctionalSchemas
            .Structure<GetUserProfileOutput, GetUserProfileOutputBuilder>(
                new ShapeId("example", "GetUserProfileOutput")
            )
            .Required(
                "statusCode",
                static output => output.StatusCode,
                static (builder, value) => builder.StatusCode = value,
                FunctionalSchemas.Integer,
                traits: [FunctionalRestTraits.HttpResponseCodeTrait]
            )
            .Required(
                "eTag",
                static output => output.ETag,
                static (builder, value) => builder.ETag = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpHeaderTrait("ETag")]
            )
            .Required(
                "extraHeaders",
                static output => output.ExtraHeaders,
                static (builder, value) => builder.ExtraHeaders = value,
                stringMapSchema,
                traits: [FunctionalRestTraits.HttpPrefixHeadersTrait("X-Extra-")]
            )
            .Required(
                "displayName",
                static output => output.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                FunctionalSchemas.String
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
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "GetUserProfile"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("GET", "/users/{userId}")]
        );
        var output = new GetUserProfileOutput(
            201,
            "etag-1",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Trace"] = "abc" },
            "Ada"
        );

        var response = FunctionalRestJsonProtocol.SerializeResponse(operation, output);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("etag-1", response.Headers["ETag"].Single());
        Assert.Equal("abc", response.Headers["X-Extra-Trace"].Single());
        Assert.Equal("application/json", response.ContentHeaders["Content-Type"].Single());
        Assert.Equal("{\"displayName\":\"Ada\"}", response.ContentText);
    }

    [Fact]
    public void FunctionalRestJsonProtocolDeserializesResponseBindingsAndBody()
    {
        var inputSchema = FunctionalSchemas
            .Structure<GetUserOutput, GetUserOutputBuilder>(new ShapeId("example", "GetUserInput"))
            .Build(static () => new GetUserOutputBuilder(), static _ => new GetUserOutput());
        var stringMapSchema = FunctionalSchemas.Map(
            new ShapeId("example", "StringMap"),
            FunctionalSchemas.String
        );
        var outputSchema = FunctionalSchemas
            .Structure<GetUserProfileOutput, GetUserProfileOutputBuilder>(
                new ShapeId("example", "GetUserProfileOutput")
            )
            .Required(
                "statusCode",
                static output => output.StatusCode,
                static (builder, value) => builder.StatusCode = value,
                FunctionalSchemas.Integer,
                traits: [FunctionalRestTraits.HttpResponseCodeTrait]
            )
            .Required(
                "eTag",
                static output => output.ETag,
                static (builder, value) => builder.ETag = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpHeaderTrait("ETag")]
            )
            .Required(
                "extraHeaders",
                static output => output.ExtraHeaders,
                static (builder, value) => builder.ExtraHeaders = value,
                stringMapSchema,
                traits: [FunctionalRestTraits.HttpPrefixHeadersTrait("X-Extra-")]
            )
            .Required(
                "displayName",
                static output => output.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                FunctionalSchemas.String
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
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "GetUserProfile"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("GET", "/users/{userId}")]
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

        var output = FunctionalRestJsonProtocol.DeserializeResponse(operation, response);

        Assert.Equal(new GetUserProfileOutput(201, "etag-1", output.ExtraHeaders, "Ada"), output);
        Assert.Equal("abc", output.ExtraHeaders["Trace"]);
    }
}
