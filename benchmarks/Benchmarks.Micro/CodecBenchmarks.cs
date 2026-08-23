using System.Buffers;
using System.Text.Json;
using Bench.Corpus;
using Bench.Domain;
using Bench.Stacks.MinimalApi;
using BenchmarkDotNet.Attributes;
using Nsmithy.Bench;
using NSmithy.Codecs.Cbor;
using NSmithy.Codecs.Json;
using NSmithy.Codecs.Proto;
using NSmithy.Codecs.Xml;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace Bench.Micro;

/// <summary>
/// The codec suite, write side: typed object to bytes, with no ASP.NET in the
/// measurement.
/// </summary>
/// <remarks>
/// The server suite tells you a stack got slower. This tells you whether the codec is
/// why. Nothing here touches routing, model binding, DI, or the HTTP pipeline,
/// and the domain-to-DTO mapping happens once in setup rather than per
/// iteration, so what remains is serialization alone.
/// <para>
/// The System.Text.Json source-generated path is the baseline, so the ratio
/// column reads directly as "how many times the hand-written ceiling this
/// costs".
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private static readonly IJsonCodec<ListItemsOutput> ListCodec = JsonCodec.FromSchema(
        ListItemsOutputSchema.Schema
    );

    private ListItemsOutput smithyList = null!;
    private ListItemsResponse stjList = null!;

    /// <summary>Response element count. Separates fixed cost from per-element cost.</summary>
    [Params(1, 100, 10_000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var items = BenchDomain.ListItems(ItemCount);

        var smithySummaries = new ItemSummary[items.Count];
        var stjSummaries = new ItemSummaryDto[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            smithySummaries[i] = new ItemSummary(
                InStock: item.InStock,
                ItemId: item.ItemId,
                Name: item.Name,
                PriceCents: item.PriceCents,
                Category: item.Category,
                Tags: item.Tags is null ? null : new StringList(item.Tags)
            );
            stjSummaries[i] = new ItemSummaryDto
            {
                ItemId = item.ItemId,
                Name = item.Name,
                PriceCents = item.PriceCents,
                InStock = item.InStock,
                Category = item.Category,
                Tags = item.Tags,
            };
        }

        smithyList = new ListItemsOutput(new ItemSummaries(smithySummaries));
        stjList = new ListItemsResponse { Items = stjSummaries };
    }

    [Benchmark(Baseline = true, Description = "STJ source-gen")]
    public byte[] Stj() =>
        JsonSerializer.SerializeToUtf8Bytes(
            stjList,
            MinimalApiJsonContext.Default.ListItemsResponse
        );

    [Benchmark(Description = "NSmithy schema codec")]
    public byte[] Smithy() => ListCodec.Serialize(smithyList);
}

/// <summary>
/// CBOR write cost for the same generated nested structure as the JSON codec benchmark.
/// </summary>
[MemoryDiagnoser]
public class CborSerializationBenchmarks
{
    private static readonly ICborCodec<ListItemsOutput> Codec = CborCodec.FromSchema(
        ListItemsOutputSchema.Schema
    );

    private ListItemsOutput value = null!;

    [Params(1, 100)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup() => value = CodecBenchmarkValues.CreateListItemsOutput(ItemCount);

    [Benchmark]
    public byte[] Serialize() => Codec.Serialize(value);
}

/// <summary>XML write cost for the same generated nested structure as the JSON codec benchmark.</summary>
[MemoryDiagnoser]
public class XmlSerializationBenchmarks
{
    private static readonly IXmlCodec<ListItemsOutput> Codec = XmlCodec.FromSchema(
        ListItemsOutputSchema.Schema
    );

    private ListItemsOutput value = null!;

    [Params(1, 100)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup() => value = CodecBenchmarkValues.CreateListItemsOutput(ItemCount);

    [Benchmark]
    public byte[] Serialize() => Codec.Serialize(value);
}

/// <summary>Generated-style protobuf structure serialization with canonical field indexes.</summary>
[MemoryDiagnoser]
public class ProtoSerializationBenchmarks
{
    private static readonly ShapeId ProtoIndex = ShapeId.Parse("alloy.proto#protoIndex");

    private static readonly Schema<ProtoValue> ValueSchema = Schemas
        .Structure<ProtoValue, ProtoValueBuilder>(ShapeId.Parse("bench#ProtoValue"))
        .Required(
            "name",
            value => value.Name,
            (builder, value) => builder.Name = value,
            Schemas.String,
            [new Trait(ProtoIndex, Document.From(1))]
        )
        .Required(
            "count",
            value => value.Count,
            (builder, value) => builder.Count = value,
            Schemas.Integer,
            [new Trait(ProtoIndex, Document.From(2))]
        )
        .Build(
            static () => new ProtoValueBuilder(),
            static builder => new ProtoValue(builder.Name!, builder.Count),
            new ProtoValueSerializer()
        );

    private static readonly IProtoCodec<ProtoValue> Codec = ProtoCodec.FromSchema(ValueSchema);
    private readonly ProtoValue value = new("benchmark-value", 42);

    [Benchmark]
    public byte[] Serialize() => Codec.Serialize(value);

    public sealed record ProtoValue(string Name, int Count);

    public sealed class ProtoValueBuilder
    {
        public string? Name { get; set; }

        public int Count { get; set; }
    }

    public sealed class ProtoValueSerializer : IStructValueSerializer<ProtoValue>
    {
        public void WriteMembers<TWriter>(ProtoValue value, ref TWriter writer)
            where TWriter : struct, IStructMemberWriter
        {
            writer.WriteMember(0, value.Name);
            writer.WriteMember(1, value.Count);
        }
    }
}

/// <summary>
/// The write-side execution cost after removing buffer rental, writer construction, and the final
/// output copy. All three paths use the same reusable destination and writer.
/// </summary>
[MemoryDiagnoser]
public class SerializationExecutionBenchmarks : IDisposable
{
    private static readonly JsonEncodedText ItemsName = JsonEncodedText.Encode("items");
    private static readonly JsonEncodedText ItemIdName = JsonEncodedText.Encode("itemId");
    private static readonly JsonEncodedText NameName = JsonEncodedText.Encode("name");
    private static readonly JsonEncodedText PriceCentsName = JsonEncodedText.Encode("priceCents");
    private static readonly JsonEncodedText InStockName = JsonEncodedText.Encode("inStock");
    private static readonly JsonEncodedText CategoryName = JsonEncodedText.Encode("category");
    private static readonly JsonEncodedText TagsName = JsonEncodedText.Encode("tags");

    private static readonly IJsonValueWriter<ListItemsOutput> SchemaWriter =
        JsonWriterCompiler.Compile(ListItemsOutputSchema.Schema);

    private readonly ArrayBufferWriter<byte> destination = new();
    private Utf8JsonWriter writer = null!;
    private ListItemsOutput smithyList = null!;
    private ListItemsResponse stjList = null!;

    [Params(1, 100, 10_000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var items = BenchDomain.ListItems(ItemCount);
        var smithySummaries = new ItemSummary[items.Count];
        var stjSummaries = new ItemSummaryDto[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            smithySummaries[i] = new ItemSummary(
                InStock: item.InStock,
                ItemId: item.ItemId,
                Name: item.Name,
                PriceCents: item.PriceCents,
                Category: item.Category,
                Tags: item.Tags is null ? null : new StringList(item.Tags)
            );
            stjSummaries[i] = new ItemSummaryDto
            {
                ItemId = item.ItemId,
                Name = item.Name,
                PriceCents = item.PriceCents,
                InStock = item.InStock,
                Category = item.Category,
                Tags = item.Tags,
            };
        }

        smithyList = new ListItemsOutput(new ItemSummaries(smithySummaries));
        stjList = new ListItemsResponse { Items = stjSummaries };
        writer = new Utf8JsonWriter(destination);

        Stj();
        var expected = destination.WrittenSpan.ToArray();
        Schema();
        if (!destination.WrittenSpan.SequenceEqual(expected))
        {
            throw new InvalidOperationException("Schema writer output differs from STJ.");
        }

        Handwritten();
        if (!destination.WrittenSpan.SequenceEqual(expected))
        {
            throw new InvalidOperationException("Handwritten writer output differs from STJ.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        writer?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true, Description = "STJ source-gen execution")]
    public int Stj()
    {
        ResetWriter();
        JsonSerializer.Serialize(writer, stjList, MinimalApiJsonContext.Default.ListItemsResponse);
        writer.Flush();
        return destination.WrittenCount;
    }

    [Benchmark(Description = "NSmithy schema execution")]
    public int Schema()
    {
        ResetWriter();
        SchemaWriter.Write(writer, smithyList);
        writer.Flush();
        return destination.WrittenCount;
    }

    [Benchmark(Description = "NSmithy handwritten execution")]
    public int Handwritten()
    {
        ResetWriter();
        WriteHandwritten(writer, smithyList);
        writer.Flush();
        return destination.WrittenCount;
    }

    private void ResetWriter()
    {
        destination.Clear();
        writer.Reset(destination);
    }

    private static void WriteHandwritten(Utf8JsonWriter writer, ListItemsOutput value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(ItemsName);
        writer.WriteStartArray();
        foreach (var item in value.Items.Values)
        {
            writer.WriteStartObject();
            writer.WriteString(ItemIdName, item.ItemId);
            writer.WriteString(NameName, item.Name);
            writer.WriteNumber(PriceCentsName, item.PriceCents);
            writer.WriteBoolean(InStockName, item.InStock);
            if (item.Category is not null)
            {
                writer.WriteString(CategoryName, item.Category);
            }

            if (item.Tags is not null)
            {
                writer.WritePropertyName(TagsName);
                writer.WriteStartArray();
                foreach (var tag in item.Tags.Values)
                {
                    writer.WriteStringValue(tag);
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

/// <summary>
/// The codec suite, read side: bytes to typed object.
/// </summary>
/// <remarks>
/// Uses the corpus order bodies directly, so the bytes parsed here are the same
/// bytes the server benchmarks post.
/// </remarks>
[MemoryDiagnoser]
public class DeserializationBenchmarks
{
    private static readonly IJsonCodec<CreateOrderInput> OrderCodec = JsonCodec.FromSchema(
        CreateOrderInputSchema.Schema
    );

    private byte[] payload = null!;

    /// <summary>Corpus scenario supplying the request body: small, or ~1 MB.</summary>
    [Params("create-order-small", "create-order-large")]
    public string Scenario { get; set; } = "create-order-small";

    [GlobalSetup]
    public void Setup() =>
        payload =
            BenchCorpus.ByName(Scenario).Body
            ?? throw new InvalidOperationException($"Scenario '{Scenario}' has no request body.");

    [Benchmark(Baseline = true, Description = "STJ source-gen")]
    public CreateOrderRequest? Stj() =>
        JsonSerializer.Deserialize(payload, MinimalApiJsonContext.Default.CreateOrderRequest);

    [Benchmark(Description = "NSmithy schema codec")]
    public CreateOrderInput Smithy() => OrderCodec.Deserialize(payload);
}

internal static class CodecBenchmarkValues
{
    public static ListItemsOutput CreateListItemsOutput(int itemCount)
    {
        var items = BenchDomain.ListItems(itemCount);
        return new ListItemsOutput(
            new ItemSummaries(
                items.Select(item => new ItemSummary(
                    InStock: item.InStock,
                    ItemId: item.ItemId,
                    Name: item.Name,
                    PriceCents: item.PriceCents,
                    Category: item.Category,
                    Tags: item.Tags is null ? null : new StringList(item.Tags)
                ))
            )
        );
    }
}
