using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

/// <summary>Which HTTP message a structure is bound to; the same trait set reads differently on each.</summary>
internal enum HttpBindingSide
{
    Request,
    Response,
}

internal delegate void RestPayloadReader<in TBuilder>(
    byte[]? content,
    Stream? streamingContent,
    TBuilder builder
);

/// <summary>
/// One structure's HTTP bindings, compiled once: every member bound to the URI, headers, status code
/// or payload has a plan, and the members left over are the body projection. Serves operation inputs
/// and outputs and error shapes alike.
/// </summary>
internal sealed class RestStructBinding<T, TBuilder>
{
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    public required IStructSchema<T, TBuilder> Schema { get; init; }

    public required IHttpLabelWriter<T>[] LabelWriters { get; init; }

    public required IHttpLabelReader<TBuilder>[] LabelReaders { get; init; }

    public required IHttpQueryWriter<T>[] QueryWriters { get; init; }

    public required IHttpQueryReader<TBuilder>[] QueryReaders { get; init; }

    public IHttpQueryParamsWriter<T>? QueryParamsWriter { get; init; }

    public IHttpQueryParamsReader<TBuilder>? QueryParamsReader { get; init; }

    public required HashSet<string> BoundQueryNames { get; init; }

    public required IHttpHeaderWriter<T>[] HeaderWriters { get; init; }

    public required IHttpHeaderReader<TBuilder>[] HeaderReaders { get; init; }

    public IHttpPrefixHeaderWriter<T>? PrefixHeaderWriter { get; init; }

    public IHttpPrefixHeaderReader<TBuilder>? PrefixHeaderReader { get; init; }

    public IHttpStatusCodeWriter<T>? StatusCodeWriter { get; init; }

    public IHttpStatusCodeReader<TBuilder>? StatusCodeReader { get; init; }

    public Func<T, RestBody>? PayloadWriter { get; init; }

    public RestPayloadReader<TBuilder>? PayloadReader { get; init; }

    /// <summary>The <c>@httpPayload</c> member, whose target decides the message's media type.</summary>
    public IMemberSchema? PayloadMember { get; init; }

    public required int MemberCount { get; init; }

    /// <summary>The members no binding claimed: what the body carries.</summary>
    public required IReadOnlySet<string> BodyMemberNames { get; init; }

    /// <summary>Codec for the body projection; null when the message has no structured body.</summary>
    public IProjectionCodec<T, TBuilder>? BodyCodec { get; set; }

    public IProjectionCodec<T, TBuilder> CompileBodyCodec(
        IRestBodyCodecFactory codecFactory,
        CodecFactoryOptions options
    ) => codecFactory.FromProjection(Schemas.Project(Schema, BodyMemberNames), options);

    /// <param name="uriTemplate">The operation's <c>@http(uri)</c>, which decides greedy labels.</param>
    internal static RestStructBinding<T, TBuilder> Compile(
        IStructSchema<T, TBuilder> schema,
        HttpBindingSide side,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool emptyStructOnNullPayload,
        string uriTemplate = ""
    )
    {
        var classifier = new Classifier(
            side,
            uriTemplate,
            codecFactory,
            rawStringPayloads,
            emptyStructOnNullPayload
        );
        schema.VisitMembers(classifier);
        return new RestStructBinding<T, TBuilder>
        {
            Schema = schema,
            LabelWriters = [.. classifier.LabelWriters],
            LabelReaders = [.. classifier.LabelReaders],
            QueryWriters = [.. classifier.QueryWriters],
            QueryReaders = [.. classifier.QueryReaders],
            QueryParamsWriter = classifier.QueryParamsWriter,
            QueryParamsReader = classifier.QueryParamsReader,
            BoundQueryNames = classifier.BoundQueryNames,
            HeaderWriters = [.. classifier.HeaderWriters],
            HeaderReaders = [.. classifier.HeaderReaders],
            PrefixHeaderWriter = classifier.PrefixHeaderWriter,
            PrefixHeaderReader = classifier.PrefixHeaderReader,
            StatusCodeWriter = classifier.StatusCodeWriter,
            StatusCodeReader = classifier.StatusCodeReader,
            PayloadWriter = classifier.PayloadWriter,
            PayloadReader = classifier.PayloadReader,
            PayloadMember = classifier.PayloadMember,
            MemberCount = classifier.MemberCount,
            BodyMemberNames = classifier.BodyMemberNames,
        };
    }

    private sealed class Classifier(
        HttpBindingSide side,
        string uriTemplate,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool emptyStructOnNullPayload
    ) : IMemberVisitor<T, TBuilder>
    {
        public List<IHttpLabelWriter<T>> LabelWriters { get; } = [];
        public List<IHttpLabelReader<TBuilder>> LabelReaders { get; } = [];
        public List<IHttpQueryWriter<T>> QueryWriters { get; } = [];
        public List<IHttpQueryReader<TBuilder>> QueryReaders { get; } = [];
        public IHttpQueryParamsWriter<T>? QueryParamsWriter { get; private set; }
        public IHttpQueryParamsReader<TBuilder>? QueryParamsReader { get; private set; }
        public HashSet<string> BoundQueryNames { get; } = new(StringComparer.Ordinal);
        public List<IHttpHeaderWriter<T>> HeaderWriters { get; } = [];
        public List<IHttpHeaderReader<TBuilder>> HeaderReaders { get; } = [];
        public IHttpPrefixHeaderWriter<T>? PrefixHeaderWriter { get; private set; }
        public IHttpPrefixHeaderReader<TBuilder>? PrefixHeaderReader { get; private set; }
        public IHttpStatusCodeWriter<T>? StatusCodeWriter { get; private set; }
        public IHttpStatusCodeReader<TBuilder>? StatusCodeReader { get; private set; }
        public Func<T, RestBody>? PayloadWriter { get; private set; }
        public RestPayloadReader<TBuilder>? PayloadReader { get; private set; }
        public IMemberSchema? PayloadMember { get; private set; }
        public int MemberCount { get; private set; }
        public HashSet<string> BodyMemberNames { get; } = new(StringComparer.Ordinal);

        public void Visit<TValue>(IMemberSchema<T, TBuilder, TValue> member)
        {
            MemberCount++;
            var traits = member.MemberTraits;

            if (side == HttpBindingSide.Request && traits.ContainsKey(RestTraits.HttpLabel))
            {
                var plan = new LabelPlan<T, TBuilder, TValue>(
                    member,
                    Codec(member),
                    greedy: uriTemplate.Contains("{" + member.Name + "+}", StringComparison.Ordinal)
                );
                LabelWriters.Add(plan);
                LabelReaders.Add(plan);
                return;
            }

            if (
                side == HttpBindingSide.Request
                && traits.TryGetValue(RestTraits.HttpQuery, out var queryTrait)
            )
            {
                var name = queryTrait.Value.AsString();
                var plan = new QueryPlan<T, TBuilder, TValue>(member, Codec(member), name);
                QueryWriters.Add(plan);
                QueryReaders.Add(plan);
                BoundQueryNames.Add(name);
                return;
            }

            if (side == HttpBindingSide.Request && traits.ContainsKey(RestTraits.HttpQueryParams))
            {
                var plan = MapPlan(member, prefix: string.Empty);
                QueryParamsWriter = plan;
                QueryParamsReader = plan;
                return;
            }

            if (side == HttpBindingSide.Response && traits.ContainsKey(RestTraits.HttpResponseCode))
            {
                (StatusCodeWriter, StatusCodeReader) = StatusCodePlan<T, TBuilder>.Compile(member);
                return;
            }

            if (traits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                var plan = new HeaderPlan<T, TBuilder, TValue>(
                    member,
                    Codec(member),
                    headerTrait.Value.AsString()
                );
                HeaderWriters.Add(plan);
                HeaderReaders.Add(plan);
                return;
            }

            if (traits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait))
            {
                var plan = MapPlan(member, prefixTrait.Value.AsString());
                PrefixHeaderWriter = plan;
                PrefixHeaderReader = plan;
                return;
            }

            if (traits.ContainsKey(RestTraits.HttpPayload))
            {
                PayloadMember = member;
                PayloadWriter = CompilePayloadWriter(
                    member,
                    codecFactory,
                    rawStringPayloads,
                    emptyStructOnNullPayload
                );
                PayloadReader = CompilePayloadReader(member, codecFactory, rawStringPayloads);
                return;
            }

            BodyMemberNames.Add(member.Name);
        }

        private static IHttpValueCodec<TValue> Codec<TValue>(
            IMemberSchema<T, TBuilder, TValue> member
        ) => HttpBindingCompiler.Compile(member.TypedTarget, member.MemberTraits);

        private static IMapBindingPlan<T, TBuilder> MapPlan<TValue>(
            IMemberSchema<T, TBuilder, TValue> member,
            string prefix
        ) =>
            member.TypedTarget.Resolved.Accept(
                new MapBindingPlanCompiler<T, TBuilder, TValue>(member, prefix)
            );
    }

    /// <summary>
    /// Builds — once, at binding construction — a delegate that serializes an <c>@httpPayload</c>
    /// member to its wire body. The blob/text/codec decision and the compiled body codec are baked
    /// in, so nothing is recompiled per request. <paramref name="emptyStructOnNull"/> separates the
    /// request side (a null struct payload still emits <c>{}</c>) from the response side (emits
    /// nothing).
    /// </summary>
    private static Func<T, RestBody> CompilePayloadWriter<TValue>(
        IMemberSchema<T, TBuilder, TValue> member,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool emptyStructOnNull
    )
    {
        var target = member.TypedTarget;
        var traits = member.MemberTraits;
        var mediaType = RestProtocol.GetMediaType(target, traits);
        var kind = HttpBindingPlans.UnwrapNullable(target).Kind;

        if (target.Resolved is IEventStreamSchema)
        {
            return target
                .Resolved.Accept(new EventStreamPayloadCompiler<TValue>(member, codecFactory))
                .Writer;
        }

        if (kind == ShapeKind.Blob && traits.ContainsKey(RestTraits.Streaming))
        {
            var contentType = mediaType ?? codecFactory.BlobContentType;
            var requiresLength = traits.ContainsKey(RestTraits.RequiresLength);
            return container =>
            {
                if (member.GetValue(container) is not { } value)
                {
                    return RestBody.None;
                }

                var stream = (Stream)(object)value;
                if (!requiresLength)
                {
                    return new RestBody([], contentType, stream);
                }

                if (!stream.CanSeek)
                {
                    throw new InvalidOperationException(
                        "Streaming blob payloads with @requiresLength require a seekable stream."
                    );
                }

                return new RestBody([], contentType, stream, stream.Length - stream.Position);
            };
        }

        if (kind == ShapeKind.Blob)
        {
            var contentType = mediaType ?? codecFactory.BlobContentType;
            return container =>
                member.GetValue(container) is { } value
                    ? new RestBody((byte[])(object)value, contentType)
                    : RestBody.None;
        }

        if (
            (kind == ShapeKind.String || kind == ShapeKind.Enum)
            && (
                mediaType is not null
                || !RestProtocol.UseBodyCodecForPayload(target, traits, rawStringPayloads)
            )
        )
        {
            var contentType = mediaType ?? "text/plain";
            return container =>
            {
                var value = member.GetValue(container);
                if (value is null)
                {
                    return RestBody.None;
                }

                var text =
                    kind == ShapeKind.Enum
                        ? ((IStringEnumValue)(object)value).Value
                        : (string)(object)value;
                return new RestBody(Encoding.UTF8.GetBytes(text), contentType);
            };
        }

        // Codec path: structures, unions, documents, and string/enum that go through the body
        // codec (alloy simpleRestJson, or members carrying an explicit @default). The codec is
        // compiled once here; the empty-struct value for a null request payload is built lazily
        // (only if a null actually arrives) so binding construction never materializes a struct
        // with required members.
        var codec = codecFactory.FromMember(member);
        var bodyContentType = codecFactory.ContentType;
        var emptyStruct = emptyStructOnNull
            ? HttpBindingPlans.UnwrapNullable(target) as IStructSchema<TValue>
            : null;
        var isDefault = CompileDefaultCheck(target, traits);

        return container =>
        {
            var value = member.GetValue(container);
            if (value is null)
            {
                return emptyStruct is not null
                    ? new RestBody(codec.Serialize(emptyStruct.BuildEmpty()), bodyContentType)
                    : RestBody.None;
            }

            return isDefault(value)
                ? RestBody.None
                : new RestBody(codec.Serialize(value), bodyContentType);
        };
    }

    // A payload equal to its modelled default is not written, as a body codec would not write it.
    private static Func<TValue, bool> CompileDefaultCheck<TValue>(
        Schema<TValue> target,
        IReadOnlyDictionary<ShapeId, Trait> traits
    )
    {
        if (!DefaultValues.TryCompile(target, traits, honorClientOptional: false, out var create))
        {
            return static _ => false;
        }

        var defaultValue = create();
        if (defaultValue is byte[] defaultBytes)
        {
            return value => value is byte[] bytes && bytes.AsSpan().SequenceEqual(defaultBytes);
        }

        var comparer = EqualityComparer<TValue>.Default;
        return value => comparer.Equals(value, defaultValue);
    }

    /// <summary>Builds — once — a delegate that reads an <c>@httpPayload</c> member from the body.</summary>
    private static RestPayloadReader<TBuilder> CompilePayloadReader<TValue>(
        IMemberSchema<T, TBuilder, TValue> member,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    )
    {
        var target = member.TypedTarget;
        var traits = member.MemberTraits;
        var unwrapped = HttpBindingPlans.UnwrapNullable(target);

        if (target.Resolved is IEventStreamSchema)
        {
            return target
                .Resolved.Accept(new EventStreamPayloadCompiler<TValue>(member, codecFactory))
                .Reader;
        }

        if (unwrapped.Kind == ShapeKind.Blob && traits.ContainsKey(RestTraits.Streaming))
        {
            var defaultsToEmpty = traits.ContainsKey(DefaultTrait);
            return (content, streamingContent, builder) =>
            {
                if (streamingContent is not null)
                {
                    member.SetValue(builder, (TValue)(object)streamingContent);
                }
                else if (content is null or { Length: 0 })
                {
                    if (defaultsToEmpty)
                    {
                        member.SetValue(builder, (TValue)(object)Stream.Null);
                    }
                }
                else
                {
                    member.SetValue(
                        builder,
                        (TValue)(object)new MemoryStream(content, writable: false)
                    );
                }
            };
        }

        var applyDefault = CompileDefault(member);

        if (unwrapped.Kind == ShapeKind.Blob)
        {
            return (content, streamingContent, builder) =>
            {
                if (content is null or { Length: 0 })
                {
                    applyDefault(builder);
                }
                else
                {
                    member.SetValue(builder, (TValue)(object)content);
                }
            };
        }

        if (!RestProtocol.UseBodyCodecForPayload(target, traits, rawStringPayloads))
        {
            var text = HttpBindingCompiler.Compile(target, memberTraits: null);
            return (content, streamingContent, builder) =>
            {
                if (content is null or { Length: 0 })
                {
                    applyDefault(builder);
                    return;
                }

                member.SetValue(builder, text.Parse(Encoding.UTF8.GetString(content)));
            };
        }

        var codec = codecFactory.FromMember(member);
        return (content, streamingContent, builder) =>
        {
            if (content is null or { Length: 0 })
            {
                applyDefault(builder);
            }
            else
            {
                member.SetValue(builder, codec.Deserialize(content));
            }
        };
    }

    private static Action<TBuilder> CompileDefault<TValue>(
        IMemberSchema<T, TBuilder, TValue> member
    ) =>
        DefaultValues.TryCompile(
            member.TypedTarget,
            member.MemberTraits,
            honorClientOptional: false,
            out var create
        )
            ? builder => member.SetValue(builder, create())
            : static _ => { };

    /// <summary>An event stream payload frames each event by the name of the union case it holds.</summary>
    private sealed class EventStreamPayloadCompiler<TValue>(
        IMemberSchema<T, TBuilder, TValue> member,
        IRestBodyCodecFactory codecFactory
    ) : PartialSchemaVisitor<(Func<T, RestBody> Writer, RestPayloadReader<TBuilder> Reader)>
    {
        public override (
            Func<T, RestBody> Writer,
            RestPayloadReader<TBuilder> Reader
        ) VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema)
        {
            var typed = (IMemberSchema<T, TBuilder, IAsyncEnumerable<TEvent>>)(object)member;
            var eventSchema = schema.TypedEventSchema;
            var codec = codecFactory.FromSchema(eventSchema);
            var eventTypeOf = Schemas.CompileCaseName(
                eventSchema.Resolved as IUnionSchema<TEvent>
                    ?? throw new InvalidOperationException(
                        "REST event stream payloads must target a union schema."
                    )
            );
            var payloadContentType = codecFactory.ContentType;

            Func<T, RestBody> writer = container =>
                typed.GetValue(container) is { } events
                    ? new RestBody(
                        [],
                        RestProtocol.EventStreamContentType,
                        EventStreamingContent: RestProtocol.FrameEventsAsync(
                            events,
                            codec,
                            eventTypeOf,
                            payloadContentType
                        )
                    )
                    : RestBody.None;

            RestPayloadReader<TBuilder> reader = (content, streamingContent, builder) =>
            {
                var stream =
                    streamingContent
                    ?? (
                        content is { Length: > 0 }
                            ? new MemoryStream(content, writable: false)
                            : Stream.Null
                    );
                typed.SetValue(
                    builder,
                    RestProtocol.ReadEventsAsync(stream, codec, payloadContentType)
                );
            };

            return (writer, reader);
        }
    }
}
