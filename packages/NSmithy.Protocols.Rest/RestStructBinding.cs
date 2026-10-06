using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.EventStream;

namespace NSmithy.Protocols.Rest;

/// <summary>Which HTTP message a structure is bound to; the same trait set reads differently on each.</summary>
internal enum HttpBindingSide
{
    Request,
    Response,
}

/// <summary>
/// One structure's HTTP bindings, compiled once: every member bound to the URI, headers, status code
/// or payload has a plan, and the members left over are the body projection. Serves operation inputs
/// and outputs and error shapes alike.
/// </summary>
internal sealed class RestStructBinding<T, TBuilder>
{
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    public required IStructSchema<T, TBuilder> Schema { get; init; }

    /// <summary>Every member's plan, indexed by the member's position.</summary>
    public required HttpMemberPlan[] Plans { get; init; }

    public required HttpMemberPlan[] Labels { get; init; }

    public required HttpMemberPlan[] Queries { get; init; }

    public required HttpMemberPlan[] Headers { get; init; }

    public HttpMemberPlan? QueryParams { get; init; }

    public HttpMemberPlan? PrefixHeaders { get; init; }

    public HttpMemberPlan? StatusCode { get; init; }

    public HttpMemberPlan? Payload { get; init; }

    public required HashSet<string> BoundQueryNames { get; init; }

    /// <summary>The <c>@httpPayload</c> member, whose target decides the message's media type.</summary>
    public IMemberSchema? PayloadMember => Payload?.Member;

    public int MemberCount => Plans.Length;

    /// <summary>The members no binding claimed: what the body carries.</summary>
    public required IReadOnlySet<string> BodyMemberNames { get; init; }

    /// <summary>Codec for the body projection; null when the message has no structured body.</summary>
    public IProjectionCodec<T, TBuilder>? BodyCodec { get; set; }

    public IProjectionCodec<T, TBuilder> CompileBodyCodec(
        IRestBodyCodecFactory codecFactory,
        CodecFactoryOptions options
    ) => codecFactory.FromProjection(Schemas.Project(Schema, BodyMemberNames), options);

    /// <summary>Collects the bound members of <paramref name="value"/> into <paramref name="parts"/>.</summary>
    public void Write(T value, HttpMessageParts parts)
    {
        var serializer = new HttpBindingSerializer(Plans, parts);
        Schema.SerializeMembers(value, ref serializer);
    }

    public void ReadText(TBuilder builder, HttpMemberPlan plan, string text)
    {
        var deserializer = HttpTextDeserializer.Single(plan.Format, text);
        Schema.DeserializeMember(builder, plan.Index, ref deserializer);
    }

    public void ReadHeader(TBuilder builder, HttpMemberPlan plan, string text)
    {
        var deserializer = HttpTextDeserializer.Header(plan.Format, text);
        Schema.DeserializeMember(builder, plan.Index, ref deserializer);
    }

    public void ReadQuery(TBuilder builder, HttpMemberPlan plan, IReadOnlyList<string> values)
    {
        var deserializer = HttpTextDeserializer.Many(plan.Format, values);
        Schema.DeserializeMember(builder, plan.Index, ref deserializer);
    }

    public void ReadPrefixHeaders(
        TBuilder builder,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> headers
    )
    {
        if (PrefixHeaders is not { } plan)
        {
            return;
        }

        var prefix = plan.Name;
        List<KeyValuePair<string, IReadOnlyList<string>>> entries = [];
        foreach (var header in headers)
        {
            if (
                header.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !(prefix.Length == 0 && IsTransportManagedHeader(header.Key))
                && header.Value.Count > 0
            )
            {
                entries.Add(new(header.Key[prefix.Length..], header.Value));
            }
        }

        var deserializer = HttpTextDeserializer.Map(plan.Format, entries);
        Schema.DeserializeMember(builder, plan.Index, ref deserializer);
    }

    public void ReadQueryParams(TBuilder builder, Dictionary<string, IReadOnlyList<string>> query)
    {
        if (QueryParams is not { } plan)
        {
            return;
        }

        var deserializer = HttpTextDeserializer.Map(
            plan.Format,
            query.Where(entry => entry.Value.Count > 0)
        );
        Schema.DeserializeMember(builder, plan.Index, ref deserializer);
    }

    public void ReadStatusCode(TBuilder builder, int statusCode)
    {
        if (StatusCode is { } plan)
        {
            ReadText(
                builder,
                plan,
                statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
        }
    }

    /// <summary>
    /// Reads the <c>@httpPayload</c> member from the body. An empty body leaves the member unset,
    /// or set to its modeled default, except that an event stream is always present.
    /// </summary>
    public void ReadPayload(TBuilder builder, byte[]? content, Stream? streamingContent)
    {
        if (Payload is not { } plan)
        {
            return;
        }

        if (plan.PayloadKind == PayloadKind.EventStream)
        {
            var stream =
                streamingContent
                ?? (
                    content is { Length: > 0 }
                        ? new MemoryStream(content, writable: false)
                        : Stream.Null
                );
            var events = new HttpPayloadDeserializer(plan, [], stream);
            Schema.DeserializeMember(builder, plan.Index, ref events);
            return;
        }

        if (plan.PayloadKind == PayloadKind.StreamingBlob && streamingContent is not null)
        {
            var streaming = new HttpPayloadDeserializer(plan, [], streamingContent);
            Schema.DeserializeMember(builder, plan.Index, ref streaming);
            return;
        }

        if (content is null or { Length: 0 })
        {
            if (plan.PayloadKind == PayloadKind.StreamingBlob)
            {
                if (plan.Default is not null)
                {
                    var empty = new HttpPayloadDeserializer(plan, [], Stream.Null);
                    Schema.DeserializeMember(builder, plan.Index, ref empty);
                }
            }
            else if (plan.Default is { } defaultValue)
            {
                var deserializer = new DocumentDeserializer(defaultValue);
                Schema.DeserializeMember(builder, plan.Index, ref deserializer);
            }

            return;
        }

        var payload = new HttpPayloadDeserializer(
            plan,
            content,
            plan.PayloadKind == PayloadKind.StreamingBlob
                ? new MemoryStream(content, writable: false)
                : null
        );
        Schema.DeserializeMember(builder, plan.Index, ref payload);
    }

    private static bool IsTransportManagedHeader(string name) =>
        name.Equals("Host", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);

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
        var members = schema.Members;
        var plans = new HttpMemberPlan[members.Count];
        var boundQueryNames = new HashSet<string>(StringComparer.Ordinal);
        var bodyMemberNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            var plan = Classify(
                index,
                member,
                side,
                uriTemplate,
                codecFactory,
                rawStringPayloads,
                emptyStructOnNullPayload
            );
            plans[index] = plan;
            switch (plan.Binding)
            {
                case HttpBinding.Query:
                    boundQueryNames.Add(plan.Name);
                    break;
                case HttpBinding.Body:
                    bodyMemberNames.Add(member.Name);
                    break;
            }
        }

        return new RestStructBinding<T, TBuilder>
        {
            Schema = schema,
            Plans = plans,
            Labels = [.. plans.Where(plan => plan.Binding == HttpBinding.Label)],
            Queries = [.. plans.Where(plan => plan.Binding == HttpBinding.Query)],
            Headers = [.. plans.Where(plan => plan.Binding == HttpBinding.Header)],
            QueryParams = plans.LastOrDefault(plan => plan.Binding == HttpBinding.QueryParams),
            PrefixHeaders = plans.LastOrDefault(plan => plan.Binding == HttpBinding.PrefixHeaders),
            StatusCode = plans.LastOrDefault(plan => plan.Binding == HttpBinding.StatusCode),
            Payload = plans.LastOrDefault(plan => plan.Binding == HttpBinding.Payload),
            BoundQueryNames = boundQueryNames,
            BodyMemberNames = bodyMemberNames,
        };
    }

    private static HttpMemberPlan Classify(
        int index,
        IMemberSchema member,
        HttpBindingSide side,
        string uriTemplate,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool emptyStructOnNullPayload
    )
    {
        var traits = member.MemberTraits;
        var target = HttpBindingPlans.UnwrapNullable(member.Target);

        HttpMemberPlan Plan(HttpBinding binding, string name = "", HttpTextFormat? format = null) =>
            new()
            {
                Index = index,
                Binding = binding,
                Member = member,
                Target = target,
                Name = name,
                Format = format ?? HttpTextFormat.For(member.Target, traits),
            };

        if (side == HttpBindingSide.Request && traits.ContainsKey(RestTraits.HttpLabel))
        {
            var greedy = uriTemplate.Contains("{" + member.Name + "+}", StringComparison.Ordinal);
            var plan = Plan(HttpBinding.Label);
            return new HttpMemberPlan
            {
                Index = index,
                Binding = HttpBinding.Label,
                Member = member,
                Target = target,
                Format = plan.Format,
                Greedy = greedy,
                Placeholder = greedy ? "{" + member.Name + "+}" : "{" + member.Name + "}",
            };
        }

        if (
            side == HttpBindingSide.Request
            && traits.TryGetValue(RestTraits.HttpQuery, out var query)
        )
        {
            return Plan(HttpBinding.Query, query.Value.AsString());
        }

        if (side == HttpBindingSide.Request && traits.ContainsKey(RestTraits.HttpQueryParams))
        {
            return Plan(HttpBinding.QueryParams, format: MapValueFormat(member, target));
        }

        if (side == HttpBindingSide.Response && traits.ContainsKey(RestTraits.HttpResponseCode))
        {
            if (target.Kind != ShapeKind.Integer)
            {
                throw new InvalidOperationException(
                    $"@httpResponseCode member '{member.Name}' must target an integer."
                );
            }

            return Plan(HttpBinding.StatusCode);
        }

        if (traits.TryGetValue(RestTraits.HttpHeader, out var header))
        {
            var name = header.Value.AsString();
            return new HttpMemberPlan
            {
                Index = index,
                Binding = HttpBinding.Header,
                Member = member,
                Target = target,
                Name = name,
                Format = HttpTextFormat.For(member.Target, traits),
                Slot =
                    string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)
                        ? HeaderSlot.ContentType
                    : string.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase)
                        ? HeaderSlot.ContentHeaders
                    : HeaderSlot.Headers,
            };
        }

        if (traits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefix))
        {
            return Plan(
                HttpBinding.PrefixHeaders,
                prefix.Value.AsString(),
                MapValueFormat(member, target)
            );
        }

        if (traits.ContainsKey(RestTraits.HttpPayload))
        {
            return PayloadPlan(
                index,
                member,
                target,
                codecFactory,
                rawStringPayloads,
                emptyStructOnNullPayload
            );
        }

        return new HttpMemberPlan
        {
            Index = index,
            Binding = HttpBinding.Body,
            Member = member,
            Target = target,
        };
    }

    /// <summary>
    /// An <c>@httpPrefixHeaders</c> or <c>@httpQueryParams</c> member targets a map, whose values
    /// are the scalars written as text; the member's own traits do not reach them.
    /// </summary>
    private static HttpTextFormat MapValueFormat(IMemberSchema member, Schema target) =>
        target is IMapSchema map
            ? HttpTextFormat.For(map.Value, memberTraits: null)
            : throw new InvalidOperationException(
                $"HTTP binding member '{member.Name}' must target a map schema."
            );

    /// <summary>
    /// Decides once, at binding construction, how an <c>@httpPayload</c> member becomes the body:
    /// as raw bytes or text, through the body codec, or as an event stream.
    /// </summary>
    private static HttpMemberPlan PayloadPlan(
        int index,
        IMemberSchema member,
        Schema target,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool emptyStructOnNullPayload
    )
    {
        var traits = member.MemberTraits;
        var mediaType = RestProtocol.GetMediaType(member.Target, traits);
        var kind = target.Kind;
        var (payloadKind, contentType) =
            member.Target.Resolved is IEventStreamSchema
                ? (PayloadKind.EventStream, codecFactory.ContentType)
            : kind == ShapeKind.Blob && traits.ContainsKey(RestTraits.Streaming)
                ? (PayloadKind.StreamingBlob, mediaType ?? codecFactory.BlobContentType)
            : kind == ShapeKind.Blob ? (PayloadKind.Blob, mediaType ?? codecFactory.BlobContentType)
            : kind is ShapeKind.String or ShapeKind.Enum
            && (
                mediaType is not null
                || !RestProtocol.UseBodyCodecForPayload(member.Target, traits, rawStringPayloads)
            )
                ? (PayloadKind.Text, mediaType ?? "text/plain")
            : (PayloadKind.Codec, codecFactory.ContentType);

        return new HttpMemberPlan
        {
            Index = index,
            Binding = HttpBinding.Payload,
            Member = member,
            Target = target,
            PayloadKind = payloadKind,
            ContentType = contentType,
            RequiresLength = traits.ContainsKey(RestTraits.RequiresLength),
            Default = traits.TryGetValue(DefaultTrait, out var defaultTrait)
                ? defaultTrait.Value
                : null,
            // The empty value for a null request payload is only built if a null actually
            // arrives, so binding construction never materializes a struct with required members.
            EmptyStructOnNull =
                emptyStructOnNullPayload
                && payloadKind == PayloadKind.Codec
                && target is IStructSchema,
            CodecFactory = codecFactory,
        };
    }
}

/// <summary>
/// Writes a structure's bound members into the parts of an HTTP message. Members the body carries
/// are skipped: the body codec writes those.
/// </summary>
internal readonly struct HttpBindingSerializer(HttpMemberPlan[] plans, HttpMessageParts parts)
    : IShapeSerializer
{
    /// <summary>The plan of a member bound to text, with the scratch space cleared for it; else null.</summary>
    private HttpMemberPlan? Text(int member)
    {
        var plan = plans[member];
        if (plan.Binding is HttpBinding.Body or HttpBinding.Payload or HttpBinding.StatusCode)
        {
            return null;
        }

        parts.Texts.Clear();
        parts.Entries.Clear();
        return plan;
    }

    private HttpTextSerializer TextSerializer(HttpMemberPlan plan) =>
        new(
            plan.Format,
            parts.Texts,
            plan.Binding is HttpBinding.QueryParams or HttpBinding.PrefixHeaders
                ? parts.Entries
                : null
        );

    private void Place(HttpMemberPlan plan)
    {
        var texts = parts.Texts;
        switch (plan.Binding)
        {
            case HttpBinding.Label:
                parts.Uri!.ReplaceLabel(
                    plan.Placeholder,
                    plan.Greedy
                        ? HttpValueText.EscapeGreedyLabel(texts[0])
                        : Uri.EscapeDataString(texts[0])
                );
                break;
            case HttpBinding.Query:
                foreach (var text in texts)
                {
                    parts.Query.Add(new(plan.Name, text));
                }

                break;
            case HttpBinding.Header:
                parts.Headers.Add(new(plan, plan.IsList ? JoinHeader(plan, texts) : texts[0]));
                break;
            case HttpBinding.QueryParams:
                foreach (var entry in parts.Entries)
                {
                    foreach (var text in entry.Value)
                    {
                        parts.QueryParams.Add(new(entry.Key, text));
                    }
                }

                break;
            case HttpBinding.PrefixHeaders:
                foreach (var entry in parts.Entries)
                {
                    if (entry.Value.Count > 0)
                    {
                        parts.PrefixHeaders.Add(new(plan.Name + entry.Key, entry.Value[0]));
                    }
                }

                break;
        }
    }

    private static string JoinHeader(HttpMemberPlan plan, List<string> texts) =>
        string.Join(
            ", ",
            plan.Format.QuoteHeaderElements
                ? texts.Select(HttpValueText.QuoteHeaderListElement)
                : texts
        );

    public void WriteNull(int member)
    {
        var plan = plans[member];
        switch (plan.Binding)
        {
            case HttpBinding.Label:
                throw new InvalidOperationException(
                    $"HTTP label member '{plan.MemberName}' cannot be null."
                );
            case HttpBinding.Payload when plan.EmptyStructOnNull:
                var serializer = this;
                ((IStructSchema)plan.Target).WriteEmpty(member, ref serializer);
                break;
        }
    }

    public void WriteBoolean(int member, bool value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteBoolean(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteByte(int member, sbyte value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteByte(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteShort(int member, short value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteShort(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteInteger(int member, int value)
    {
        if (plans[member].Binding == HttpBinding.StatusCode)
        {
            parts.StatusCode = value;
        }
        else if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteInteger(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteLong(int member, long value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteLong(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteFloat(int member, float value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteFloat(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteDouble(int member, double value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteDouble(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteBigInteger(int member, BigInteger value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteBigInteger(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteBigDecimal(int member, decimal value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteBigDecimal(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteString(int member, string value)
    {
        var payload = plans[member];
        if (payload.Binding == HttpBinding.Payload)
        {
            WriteTextPayload(payload, value);
        }
        else if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteString(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteBlob(int member, byte[] value)
    {
        var payload = plans[member];
        if (payload.Binding == HttpBinding.Payload)
        {
            parts.Payload = new RestBody(value, payload.ContentType);
        }
        else if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteBlob(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteTimestamp(int member, DateTimeOffset value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteTimestamp(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteDocument(int member, Document value)
    {
        var payload = plans[member];
        if (payload.Binding == HttpBinding.Payload)
        {
            if (!payload.IsDefault(value))
            {
                WriteCodecPayload(payload, value, payload.DocumentSchema);
            }
        }
        else if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteDocument(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteStringEnum(int member, string value)
    {
        var payload = plans[member];
        if (payload.Binding == HttpBinding.Payload)
        {
            WriteTextPayload(payload, value);
        }
        else if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteStringEnum(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteIntEnum(int member, int value)
    {
        if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteIntEnum(MemberIndex.Root, value);
            Place(plan);
        }
    }

    public void WriteStream(int member, Stream value)
    {
        var plan = plans[member];
        if (plan.Binding != HttpBinding.Payload)
        {
            return;
        }

        if (!plan.RequiresLength)
        {
            parts.Payload = new RestBody([], plan.ContentType, value);
            return;
        }

        if (!value.CanSeek)
        {
            throw new InvalidOperationException(
                "Streaming blob payloads with @requiresLength require a seekable stream."
            );
        }

        parts.Payload = new RestBody([], plan.ContentType, value, value.Length - value.Position);
    }

    // An event stream payload frames each event by the name of the union case it holds.
    public void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    )
    {
        var plan = plans[member];
        if (plan.Binding != HttpBinding.Payload)
        {
            return;
        }

        parts.Payload = new RestBody(
            [],
            RestProtocol.EventStreamContentType,
            EventStreamingContent: EventStreamEvents.EncodeAsync(
                events,
                plan.EventTypeOf(eventSchema),
                plan.EventCodecFor(eventSchema).Serialize,
                plan.CodecFactory.ContentType
            )
        );
    }

    public void WriteStruct<TValue>(int member, TValue value, IStructSchema<TValue> schema)
    {
        var plan = plans[member];
        if (plan.Binding == HttpBinding.Payload)
        {
            WriteCodecPayload(plan, value, (Schema<TValue>)schema);
        }
        else if (Text(member) is { } text)
        {
            TextSerializer(text).WriteStruct(MemberIndex.Root, value, schema);
        }
    }

    public void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    )
    {
        var payload = plans[member];
        if (payload.Binding == HttpBinding.Payload)
        {
            WriteCodecPayload(payload, value, (Schema<TCollection>)schema);
        }
        else if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteList(MemberIndex.Root, value, schema);
            Place(plan);
        }
    }

    public void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    )
    {
        var payload = plans[member];
        if (payload.Binding == HttpBinding.Payload)
        {
            WriteCodecPayload(payload, value, (Schema<TDictionary>)schema);
        }
        else if (Text(member) is { } plan)
        {
            var text = TextSerializer(plan);
            text.WriteMap(MemberIndex.Root, value, schema);
            Place(plan);
        }
    }

    public void WriteUnion<TValue>(int member, TValue value, IUnionSchema<TValue> schema)
    {
        var plan = plans[member];
        if (plan.Binding == HttpBinding.Payload)
        {
            WriteCodecPayload(plan, value, (Schema<TValue>)schema);
        }
        else if (Text(member) is { } text)
        {
            TextSerializer(text).WriteUnion(MemberIndex.Root, value, schema);
        }
    }

    private void WriteTextPayload(HttpMemberPlan plan, string value)
    {
        if (plan.PayloadKind == PayloadKind.Text)
        {
            parts.Payload = new RestBody(Encoding.UTF8.GetBytes(value), plan.ContentType);
        }
        else if (!plan.IsDefault(value))
        {
            WriteCodecPayload(plan, value, plan.StringSchema);
        }
    }

    private void WriteCodecPayload<TValue>(
        HttpMemberPlan plan,
        TValue value,
        Schema<TValue> schema
    ) => parts.Payload = new RestBody(plan.CodecFor(schema).Serialize(value), plan.ContentType);
}

/// <summary>Reads an <c>@httpPayload</c> member from the body bytes, or the body stream.</summary>
internal readonly struct HttpPayloadDeserializer(
    HttpMemberPlan plan,
    byte[] content,
    Stream? stream
) : IShapeDeserializer
{
    public bool TryReadNull() => false;

    public bool ReadBoolean() => throw Unsupported();

    public sbyte ReadByte() => throw Unsupported();

    public short ReadShort() => throw Unsupported();

    public int ReadInteger() => throw Unsupported();

    public long ReadLong() => throw Unsupported();

    public float ReadFloat() => throw Unsupported();

    public double ReadDouble() => throw Unsupported();

    public BigInteger ReadBigInteger() => throw Unsupported();

    public decimal ReadBigDecimal() => throw Unsupported();

    public string ReadString() =>
        plan.PayloadKind == PayloadKind.Codec
            ? plan.CodecFor(plan.StringSchema).Deserialize(content)
            : Encoding.UTF8.GetString(content);

    public byte[] ReadBlob() => content;

    public DateTimeOffset ReadTimestamp() => throw Unsupported();

    public Document ReadDocument() => plan.CodecFor(plan.DocumentSchema).Deserialize(content);

    public string ReadStringEnum() => ReadString();

    public int ReadIntEnum() => throw Unsupported();

    public Stream ReadStream() => stream ?? throw Unsupported();

    public IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        RestProtocol.ReadEventsAsync(
            stream ?? throw Unsupported(),
            plan.EventCodecFor(eventSchema),
            plan.CodecFactory.ContentType
        );

    public TValue ReadStruct<TValue, TBuilder>(IStructSchema<TValue, TBuilder> schema) =>
        plan.CodecFor((Schema<TValue>)schema).Deserialize(content);

    public TCollection ReadList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => plan.CodecFor((Schema<TCollection>)schema).Deserialize(content);

    public TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => plan.CodecFor((Schema<TDictionary>)schema).Deserialize(content);

    public TValue ReadUnion<TValue>(IUnionSchema<TValue> schema) =>
        plan.CodecFor((Schema<TValue>)schema).Deserialize(content);

    private NotSupportedException Unsupported() =>
        new($"HTTP payload member '{plan.MemberName}' cannot target '{plan.Target.Kind}'.");
}
