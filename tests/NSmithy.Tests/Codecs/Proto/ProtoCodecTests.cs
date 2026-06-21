using NSmithy.Codecs.Proto;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Codecs.Proto;

/// <summary>
/// Verifies the schema-driven protobuf codec against the canonical proto3 wire format. The
/// byte-level assertions use hand-computed encodings that any protobuf implementation would produce
/// for the same field numbers, which is what makes the output gRPC-interoperable.
/// </summary>
public sealed class ProtoCodecTests
{
    private static readonly ShapeId ProtoIndex = ShapeId.Parse("alloy.proto#protoIndex");
    private static readonly ShapeId ProtoNumType = ShapeId.Parse("alloy.proto#protoNumType");

    private static Trait Index(int value) => new(ProtoIndex, Document.From(value));

    private static IEnumerable<Trait> Field(int index) => [Index(index)];

    private static IEnumerable<Trait> Field(int index, string numType) =>
        [Index(index), new Trait(ProtoNumType, Document.From(numType))];

    // ---- message M { string name = 1; int32 value = 2; } ----

    public sealed record Simple(string Name, int Value);

    public sealed class SimpleBuilder
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    private static Schema<Simple> SimpleSchema { get; } =
        Schemas
            .Structure<Simple, SimpleBuilder>(ShapeId.Parse("test#Simple"))
            .Required("name", x => x.Name, (b, v) => b.Name = v, Schemas.String, Field(1))
            .Required("value", x => x.Value, (b, v) => b.Value = v, Schemas.Integer, Field(2))
            .Build(() => new SimpleBuilder(), b => new Simple(b.Name!, b.Value));

    [Fact]
    public void EncodesScalarsToCanonicalProtoBytes()
    {
        var codec = ProtoCodec.FromSchema(SimpleSchema);

        var bytes = codec.Serialize(new Simple("hi", 300));

        // 0A 02 'h' 'i'   (field 1, LEN, "hi")
        // 10 AC 02         (field 2, varint, 300)
        byte[] expected = [0x0A, 0x02, 0x68, 0x69, 0x10, 0xAC, 0x02];
        Assert.Equal(expected, bytes);
        Assert.Equal(new Simple("hi", 300), codec.Deserialize(bytes));
    }

    // ---- message Z { sint32 a = 1; } with @protoNumType("SIGNED") ----

    public sealed record IntHolder(int A);

    public sealed class IntHolderBuilder
    {
        public int A { get; set; }
    }

    [Fact]
    public void EncodesSignedIntegerAsZigZag()
    {
        var schema = Schemas
            .Structure<IntHolder, IntHolderBuilder>(ShapeId.Parse("test#Signed"))
            .Required("a", x => x.A, (b, v) => b.A = v, Schemas.Integer, Field(1, "SIGNED"))
            .Build(() => new IntHolderBuilder(), b => new IntHolder(b.A));
        var codec = ProtoCodec.FromSchema(schema);

        // zigzag(-1) == 1 → 08 01
        byte[] expected = [0x08, 0x01];
        Assert.Equal(expected, codec.Serialize(new IntHolder(-1)));
        Assert.Equal(new IntHolder(-1), codec.Deserialize(codec.Serialize(new IntHolder(-1))));
        Assert.Equal(new IntHolder(75), codec.Deserialize(codec.Serialize(new IntHolder(75))));
    }

    [Fact]
    public void EncodesFixedIntegerAsLittleEndian()
    {
        var schema = Schemas
            .Structure<IntHolder, IntHolderBuilder>(ShapeId.Parse("test#Fixed"))
            .Required("a", x => x.A, (b, v) => b.A = v, Schemas.Integer, Field(1, "FIXED"))
            .Build(() => new IntHolderBuilder(), b => new IntHolder(b.A));
        var codec = ProtoCodec.FromSchema(schema);

        // field 1, I32 wire (0x0D), 1 little-endian
        byte[] expected = [0x0D, 0x01, 0x00, 0x00, 0x00];
        Assert.Equal(expected, codec.Serialize(new IntHolder(1)));
        Assert.Equal(
            new IntHolder(70000),
            codec.Deserialize(codec.Serialize(new IntHolder(70000)))
        );
    }

    // ---- message L { repeated int32 nums = 1; } ----

    public sealed record Repeated(IReadOnlyList<int> Nums);

    public sealed class RepeatedBuilder
    {
        public IReadOnlyList<int>? Nums { get; set; }
    }

    [Fact]
    public void PacksRepeatedScalars()
    {
        var schema = Schemas
            .Structure<Repeated, RepeatedBuilder>(ShapeId.Parse("test#Repeated"))
            .Required(
                "nums",
                x => x.Nums,
                (b, v) => b.Nums = v,
                Schemas.List(ShapeId.Parse("test#IntList"), Schemas.Integer),
                Field(1)
            )
            .Build(() => new RepeatedBuilder(), b => new Repeated(b.Nums!));
        var codec = ProtoCodec.FromSchema(schema);

        // field 1, LEN (0x0A), len 3, packed varints 1 2 3
        var bytes = codec.Serialize(new Repeated([1, 2, 3]));
        byte[] expected = [0x0A, 0x03, 0x01, 0x02, 0x03];
        int[] expectedNums = [1, 2, 3];
        Assert.Equal(expected, bytes);
        Assert.Equal(expectedNums, codec.Deserialize(bytes).Nums);
    }

    [Fact]
    public void DecodesPackedScalarsWithProtoNumType()
    {
        var schema = Schemas
            .Structure<Repeated, RepeatedBuilder>(ShapeId.Parse("test#RepeatedSigned"))
            .Required(
                "nums",
                x => x.Nums,
                (b, v) => b.Nums = v,
                Schemas.List(ShapeId.Parse("test#SignedIntList"), Schemas.Integer),
                Field(1, "SIGNED")
            )
            .Build(() => new RepeatedBuilder(), b => new Repeated(b.Nums!));
        var codec = ProtoCodec.FromSchema(schema);

        // field 1, LEN, packed sint32 values: zigzag(-1)=1, zigzag(75)=150.
        byte[] bytes = [0x0A, 0x03, 0x01, 0x96, 0x01];
        int[] expectedNums = [-1, 75];

        Assert.Equal(expectedNums, codec.Deserialize(bytes).Nums);
        Assert.Equal(bytes, codec.Serialize(new Repeated(expectedNums)));
    }

    public sealed record EmptyCollections(
        IReadOnlyList<int> Nums,
        IReadOnlyDictionary<string, string> Metadata
    );

    public sealed class EmptyCollectionsBuilder
    {
        public IReadOnlyList<int>? Nums { get; set; }
        public IReadOnlyDictionary<string, string>? Metadata { get; set; }
    }

    [Fact]
    public void DecodesAbsentRepeatedAndMapFieldsAsEmptyCollections()
    {
        var schema = Schemas
            .Structure<EmptyCollections, EmptyCollectionsBuilder>(
                ShapeId.Parse("test#EmptyCollections")
            )
            .Required(
                "nums",
                x => x.Nums,
                (b, v) => b.Nums = v,
                Schemas.List(ShapeId.Parse("test#RequiredIntList"), Schemas.Integer),
                Field(1)
            )
            .Required(
                "metadata",
                x => x.Metadata,
                (b, v) => b.Metadata = v,
                Schemas.Map(ShapeId.Parse("test#RequiredStringMap"), Schemas.String),
                Field(2)
            )
            .Build(
                () => new EmptyCollectionsBuilder(),
                b => new EmptyCollections(b.Nums!, b.Metadata!)
            );
        var codec = ProtoCodec.FromSchema(schema);

        var result = codec.Deserialize([]);

        Assert.Empty(result.Nums);
        Assert.Empty(result.Metadata);
        Assert.Empty(codec.Serialize(result));
    }

    // ---- a rich message exercising nested/map/enum/timestamp/optional presence ----

    public enum Category
    {
        Unspecified = 0,
        Fiction = 1,
        Science = 3,
    }

    public sealed record Nested(int Value);

    public sealed class NestedBuilder
    {
        public int Value { get; set; }
    }

    public sealed record Book(
        string Id,
        int? PageCount,
        long? Checksum,
        IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, string> Metadata,
        Nested? Detail,
        Category Cat,
        DateTimeOffset? PublishedAt
    );

    public sealed class BookBuilder
    {
        public string? Id { get; set; }
        public int? PageCount { get; set; }
        public long? Checksum { get; set; }
        public IReadOnlyList<string>? Tags { get; set; }
        public IReadOnlyDictionary<string, string>? Metadata { get; set; }
        public Nested? Detail { get; set; }
        public Category Cat { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private static Schema<Nested> NestedSchema { get; } =
        Schemas
            .Structure<Nested, NestedBuilder>(ShapeId.Parse("test#Nested"))
            .Required("value", x => x.Value, (b, v) => b.Value = v, Schemas.Integer, Field(1))
            .Build(() => new NestedBuilder(), b => new Nested(b.Value));

    private static Schema<Book> BookSchema { get; } =
        Schemas
            .Structure<Book, BookBuilder>(ShapeId.Parse("test#Book"))
            .Required("id", x => x.Id, (b, v) => b.Id = v, Schemas.String, Field(1))
            .Optional(
                "pageCount",
                x => x.PageCount,
                (b, v) => b.PageCount = v,
                Schemas.Nullable(Schemas.Integer),
                Field(4, "UNSIGNED")
            )
            .Optional(
                "checksum",
                x => x.Checksum,
                (b, v) => b.Checksum = v,
                Schemas.Nullable(Schemas.Long),
                Field(5, "FIXED")
            )
            .Required(
                "tags",
                x => x.Tags,
                (b, v) => b.Tags = v,
                Schemas.List(ShapeId.Parse("test#Tags"), Schemas.String),
                Field(8)
            )
            .Required(
                "metadata",
                x => x.Metadata,
                (b, v) => b.Metadata = v,
                Schemas.Map(ShapeId.Parse("test#Meta"), Schemas.String),
                Field(9)
            )
            .Optional(
                "detail",
                x => x.Detail,
                (b, v) => b.Detail = v,
                Schemas.NullableReference(NestedSchema),
                Field(10)
            )
            .Required(
                "cat",
                x => x.Cat,
                (b, v) => b.Cat = v,
                Schemas.IntEnum<Category>(ShapeId.Parse("test#Category")),
                Field(7)
            )
            .Optional(
                "publishedAt",
                x => x.PublishedAt,
                (b, v) => b.PublishedAt = v,
                Schemas.Nullable(Schemas.Timestamp),
                Field(11)
            )
            .Build(
                () => new BookBuilder(),
                b => new Book(
                    b.Id!,
                    b.PageCount,
                    b.Checksum,
                    b.Tags!,
                    b.Metadata!,
                    b.Detail,
                    b.Cat,
                    b.PublishedAt
                )
            );

    [Fact]
    public void RoundTripsRichMessage()
    {
        var codec = ProtoCodec.FromSchema(BookSchema);
        var book = new Book(
            Id: "abc",
            PageCount: 534,
            Checksum: unchecked((long)0xF05CA1A5CA1A5CA1UL),
            Tags: ["fp", "scala"],
            Metadata: new Dictionary<string, string> { ["publisher"] = "Manning" },
            Detail: new Nested(42),
            Cat: Category.Science,
            PublishedAt: new DateTimeOffset(2023, 8, 29, 0, 0, 0, TimeSpan.Zero)
        );

        var result = codec.Deserialize(codec.Serialize(book));

        Assert.Equal(book.Id, result.Id);
        Assert.Equal(book.PageCount, result.PageCount);
        Assert.Equal(book.Checksum, result.Checksum);
        Assert.Equal(book.Tags, result.Tags);
        Assert.Equal(book.Metadata, result.Metadata);
        Assert.Equal(book.Detail, result.Detail);
        Assert.Equal(book.Cat, result.Cat);
        Assert.Equal(book.PublishedAt, result.PublishedAt);
    }

    [Fact]
    public void EncodesPreUnixFractionalTimestampCanonically()
    {
        var codec = ProtoCodec.FromSchema(BookSchema);
        var book = new Book(
            Id: "pre",
            PageCount: null,
            Checksum: null,
            Tags: [],
            Metadata: new Dictionary<string, string>(),
            Detail: null,
            Cat: Category.Unspecified,
            PublishedAt: new DateTimeOffset(1969, 12, 31, 23, 59, 59, 500, TimeSpan.Zero)
        );

        var bytes = codec.Serialize(book);

        // field 11 (timestamp), LEN 17:
        //   seconds = -1 as int64 varint (10 bytes)
        //   nanos = 500000000
        byte[] timestampField =
        [
            0x5A,
            0x11,
            0x08,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0x01,
            0x10,
            0x80,
            0xCA,
            0xB5,
            0xEE,
            0x01,
        ];
        Assert.True(bytes.AsSpan().IndexOf(timestampField) >= 0);
        Assert.Equal(book.PublishedAt, codec.Deserialize(bytes).PublishedAt);
    }

    [Fact]
    public void OmitsAbsentOptionalFields()
    {
        var codec = ProtoCodec.FromSchema(BookSchema);
        var book = new Book(
            Id: "x",
            PageCount: null,
            Checksum: null,
            Tags: [],
            Metadata: new Dictionary<string, string>(),
            Detail: null,
            Cat: Category.Unspecified,
            PublishedAt: null
        );

        var bytes = codec.Serialize(book);

        // Only id (field 1, "x") and cat (field 7, value 0) are present.
        // id:  0A 01 78    cat: 38 00
        byte[] expected = [0x0A, 0x01, 0x78, 0x38, 0x00];
        Assert.Equal(expected, bytes);
        var result = codec.Deserialize(bytes);
        Assert.Null(result.PageCount);
        Assert.Null(result.Detail);
        Assert.Null(result.PublishedAt);
    }

    // ---- string enum → proto enum ordinal ----

    public readonly record struct Color(string Value) : IStringEnumValue<Color>
    {
        public static Color FromValue(string value) => new(value);

        public static Color RED { get; } = new("RED");
        public static Color GREEN { get; } = new("GREEN");
        public static Color BLUE { get; } = new("BLUE");
    }

    public sealed record Painting(Color Shade);

    public sealed class PaintingBuilder
    {
        public Color Shade { get; set; }
    }

    private static Trait SyntheticEnum(params string[] values) =>
        new(
            ShapeId.Parse("smithy.synthetic#enum"),
            Document.From(
                values.Select(v =>
                    Document.From(
                        new Dictionary<string, Document>
                        {
                            ["value"] = Document.From(v),
                            ["name"] = Document.From(v),
                        }
                    )
                )
            )
        );

    [Fact]
    public void EncodesStringEnumAsProtoOrdinal()
    {
        var schema = Schemas
            .Structure<Painting, PaintingBuilder>(ShapeId.Parse("test#Painting"))
            .Required(
                "shade",
                x => x.Shade,
                (b, v) => b.Shade = v,
                Schemas.StringEnum<Color>(
                    ShapeId.Parse("test#Color"),
                    [SyntheticEnum("RED", "GREEN", "BLUE")]
                ),
                Field(1)
            )
            .Build(() => new PaintingBuilder(), b => new Painting(b.Shade));
        var codec = ProtoCodec.FromSchema(schema);

        // UNSPECIFIED=0, RED=1, GREEN=2, BLUE=3 → field 1 varint 2 for GREEN.
        byte[] expected = [0x08, 0x02];
        Assert.Equal(expected, codec.Serialize(new Painting(Color.GREEN)));
        Assert.Equal(
            new Painting(Color.BLUE),
            codec.Deserialize(codec.Serialize(new Painting(Color.BLUE)))
        );
    }

    // ---- @sparse map → google.protobuf.Value (null round-trips) ----

    public sealed record Attributed(IReadOnlyDictionary<string, string?> Attrs);

    public sealed class AttributedBuilder
    {
        public IReadOnlyDictionary<string, string?>? Attrs { get; set; }
    }

    [Fact]
    public void RoundTripsSparseMapWithNullValues()
    {
        var schema = Schemas
            .Structure<Attributed, AttributedBuilder>(ShapeId.Parse("test#Attributed"))
            .Required(
                "attrs",
                x => x.Attrs,
                (b, v) => b.Attrs = v,
                Schemas.Map(
                    ShapeId.Parse("test#Attrs"),
                    Schemas.NullableReference(Schemas.String),
                    [new Trait(ShapeId.Parse("smithy.api#sparse"))]
                ),
                Field(1)
            )
            .Build(() => new AttributedBuilder(), b => new Attributed(b.Attrs!));
        var codec = ProtoCodec.FromSchema(schema);

        var input = new Attributed(
            new Dictionary<string, string?> { ["subtitle"] = "An Intro", ["series"] = null }
        );

        var result = codec.Deserialize(codec.Serialize(input));

        Assert.Equal("An Intro", result.Attrs["subtitle"]);
        Assert.Null(result.Attrs["series"]);
    }

    // ---- @protoInlinedOneOf ----

    public abstract record Filter
    {
        public sealed record ById(string Value) : Filter;

        public sealed record ByNum(int Value) : Filter;
    }

    public sealed record Query(int? Page, Filter? Filter);

    public sealed class QueryBuilder
    {
        public int? Page { get; set; }
        public Filter? Filter { get; set; }
    }

    private static Schema<Filter> FilterSchema { get; } =
        Schemas
            .Union<Filter>(
                ShapeId.Parse("test#Filter"),
                [new Trait(ShapeId.Parse("alloy.proto#protoInlinedOneOf"))]
            )
            .Case(
                "byId",
                f => f is Filter.ById,
                f => ((Filter.ById)f).Value,
                v => new Filter.ById(v),
                Schemas.String,
                Field(3)
            )
            .Case(
                "byNum",
                f => f is Filter.ByNum,
                f => ((Filter.ByNum)f).Value,
                v => new Filter.ByNum(v),
                Schemas.Integer,
                Field(4)
            )
            .Build();

    [Fact]
    public void InlinesOneOfIntoParentMessage()
    {
        var schema = Schemas
            .Structure<Query, QueryBuilder>(ShapeId.Parse("test#Query"))
            .Optional(
                "page",
                x => x.Page,
                (b, v) => b.Page = v,
                Schemas.Nullable(Schemas.Integer),
                Field(1)
            )
            .Optional("filter", x => x.Filter!, (b, v) => b.Filter = v, FilterSchema, Field(2))
            .Build(() => new QueryBuilder(), b => new Query(b.Page, b.Filter));
        var codec = ProtoCodec.FromSchema(schema);

        // filter=byId("z") is written at the case's own field 3 (LEN), not the member's field 2,
        // and not wrapped in a sub-message: 1A 01 7A
        byte[] expected = [0x1A, 0x01, 0x7A];
        Assert.Equal(expected, codec.Serialize(new Query(null, new Filter.ById("z"))));

        var result = codec.Deserialize(codec.Serialize(new Query(7, new Filter.ByNum(42))));
        Assert.Equal(7, result.Page);
        Assert.Equal(new Filter.ByNum(42), result.Filter);
    }

    // ---- Document → google.protobuf.Value ----

    public sealed record Envelope(Document Payload);

    public sealed class EnvelopeBuilder
    {
        public Document Payload { get; set; }
    }

    [Fact]
    public void RoundTripsDocumentAsValue()
    {
        var schema = Schemas
            .Structure<Envelope, EnvelopeBuilder>(ShapeId.Parse("test#Envelope"))
            .Required(
                "payload",
                x => x.Payload,
                (b, v) => b.Payload = v,
                Schemas.Document,
                Field(1)
            )
            .Build(() => new EnvelopeBuilder(), b => new Envelope(b.Payload));
        var codec = ProtoCodec.FromSchema(schema);

        var payload = Document.From(
            new Dictionary<string, Document>
            {
                ["name"] = Document.From("ada"),
                ["age"] = Document.From(36m),
                ["admin"] = Document.From(true),
                ["tags"] = Document.From([Document.From("x"), Document.From("y")]),
            }
        );

        var result = codec.Deserialize(codec.Serialize(new Envelope(payload))).Payload.AsObject();

        Assert.Equal("ada", result["name"].AsString());
        Assert.Equal(36m, result["age"].AsNumber());
        Assert.True(result["admin"].AsBoolean());
        Assert.Equal("y", result["tags"].AsArray()[1].AsString());
    }
}
