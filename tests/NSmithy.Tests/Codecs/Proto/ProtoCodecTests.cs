using NSmithy.Codecs.Proto;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using Nsmithy.Tests.Proto;

namespace NSmithy.Tests.Codecs.Proto;

/// <summary>
/// Verifies the schema-driven protobuf codec against the canonical proto3 wire format. The
/// byte-level assertions use hand-computed encodings that any protobuf implementation would produce
/// for the same field numbers, which is what makes the output gRPC-interoperable.
/// </summary>
public sealed class ProtoCodecTests
{
    [Fact]
    public void EncodesScalarsToCanonicalProtoBytes()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(SimpleSchema.Schema);

        var bytes = codec.Serialize(new Simple("hi", 300));

        // 0A 02 'h' 'i'   (field 1, LEN, "hi")
        // 10 AC 02         (field 2, varint, 300)
        byte[] expected = [0x0A, 0x02, 0x68, 0x69, 0x10, 0xAC, 0x02];
        Assert.Equal(expected, bytes);
        Assert.Equal(new Simple("hi", 300), codec.Deserialize(bytes));
    }

    [Fact]
    public void EncodesSignedIntegerAsZigZag()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(SignedSchema.Schema);

        // zigzag(-1) == 1 → 08 01
        byte[] expected = [0x08, 0x01];
        Assert.Equal(expected, codec.Serialize(new Signed(-1)));
        Assert.Equal(new Signed(-1), codec.Deserialize(codec.Serialize(new Signed(-1))));
        Assert.Equal(new Signed(75), codec.Deserialize(codec.Serialize(new Signed(75))));
    }

    [Fact]
    public void EncodesFixedIntegerAsLittleEndian()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(FixedSchema.Schema);

        // field 1, I32 wire (0x0D), 1 little-endian
        byte[] expected = [0x0D, 0x01, 0x00, 0x00, 0x00];
        Assert.Equal(expected, codec.Serialize(new Fixed(1)));
        Assert.Equal(new Fixed(70000), codec.Deserialize(codec.Serialize(new Fixed(70000))));
    }

    [Fact]
    public void PacksRepeatedScalars()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(RepeatedSchema.Schema);

        // field 1, LEN (0x0A), len 3, packed varints 1 2 3
        var bytes = codec.Serialize(new Repeated(new IntList([1, 2, 3])));
        byte[] expected = [0x0A, 0x03, 0x01, 0x02, 0x03];
        int[] expectedNums = [1, 2, 3];
        Assert.Equal(expected, bytes);
        Assert.Equal(expectedNums, codec.Deserialize(bytes).Nums.Values);
    }

    [Fact]
    public void DecodesPackedScalarsWithProtoNumType()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(RepeatedSignedSchema.Schema);

        // field 1, LEN, packed sint32 values: zigzag(-1)=1, zigzag(75)=150.
        byte[] bytes = [0x0A, 0x03, 0x01, 0x96, 0x01];
        int[] expectedNums = [-1, 75];

        Assert.Equal(expectedNums, codec.Deserialize(bytes).Nums.Values);
        Assert.Equal(bytes, codec.Serialize(new RepeatedSigned(new SignedIntList(expectedNums))));
    }

    [Fact]
    public void EncodesMapValuesWithValueProtoNumType()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(IntMapHolderSchema.Schema);

        // field 1, LEN, entry { key: "a", value: sint32(-1) }
        byte[] bytes = [0x0A, 0x05, 0x0A, 0x01, 0x61, 0x10, 0x01];
        var expected = new Dictionary<string, int> { ["a"] = -1 };

        Assert.Equal(bytes, codec.Serialize(new IntMapHolder(new SignedIntMap(expected))));
        Assert.Equal(expected, codec.Deserialize(bytes).Values.Values);
    }

    [Fact]
    public void DecodesAbsentRepeatedAndMapFieldsAsEmptyCollections()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(EmptyCollectionsSchema.Schema);

        var result = codec.Deserialize([]);

        Assert.Empty(result.Nums.Values);
        Assert.Empty(result.Metadata.Values);
        Assert.Empty(codec.Serialize(result));
    }

    // ---- a rich message exercising nested/map/enum/timestamp/optional presence ----

    private static Book EmptyBook(string id, DateTimeOffset? publishedAt) =>
        new(
            Cat: Category.UNSPECIFIED,
            Id: id,
            Metadata: new StringMap(new Dictionary<string, string>()),
            Tags: new Tags([]),
            PublishedAt: publishedAt
        );

    [Fact]
    public void RoundTripsRichMessage()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(BookSchema.Schema);
        var book = new Book(
            Id: "abc",
            PageCount: 534,
            Checksum: unchecked((long)0xF05CA1A5CA1A5CA1UL),
            Tags: new Tags(["fp", "scala"]),
            Metadata: new StringMap(new Dictionary<string, string> { ["publisher"] = "Manning" }),
            Detail: new Nested(42),
            Cat: Category.SCIENCE,
            PublishedAt: new DateTimeOffset(2023, 8, 29, 0, 0, 0, TimeSpan.Zero)
        );

        var result = codec.Deserialize(codec.Serialize(book));

        Assert.Equal(book.Id, result.Id);
        Assert.Equal(book.PageCount, result.PageCount);
        Assert.Equal(book.Checksum, result.Checksum);
        Assert.Equal(book.Tags.Values, result.Tags.Values);
        Assert.Equal(book.Metadata.Values, result.Metadata.Values);
        Assert.Equal(book.Detail, result.Detail);
        Assert.Equal(book.Cat, result.Cat);
        Assert.Equal(book.PublishedAt, result.PublishedAt);
    }

    [Fact]
    public void EncodesPreUnixFractionalTimestampCanonically()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(BookSchema.Schema);
        var book = EmptyBook(
            "pre",
            new DateTimeOffset(1969, 12, 31, 23, 59, 59, 500, TimeSpan.Zero)
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
        var codec = ProtoCodecFactory.Default.FromSchema(BookSchema.Schema);
        var book = EmptyBook("x", publishedAt: null);

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

    [Fact]
    public void EncodesStringEnumAsProtoOrdinal()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(PaintingSchema.Schema);

        // UNSPECIFIED=0, RED=1, GREEN=2, BLUE=3 → field 1 varint 2 for GREEN.
        byte[] expected = [0x08, 0x02];
        Assert.Equal(expected, codec.Serialize(new Painting(Color.GREEN)));
        Assert.Equal(
            new Painting(Color.BLUE),
            codec.Deserialize(codec.Serialize(new Painting(Color.BLUE)))
        );
    }

    [Fact]
    public void RoundTripsSparseMapWithNullValues()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(AttributedSchema.Schema);
        var input = new Attributed(
            new Attrs(
                new Dictionary<string, string?> { ["subtitle"] = "An Intro", ["series"] = null }
            )
        );

        var result = codec.Deserialize(codec.Serialize(input));

        Assert.Equal("An Intro", result.Attrs.Values["subtitle"]);
        Assert.Null(result.Attrs.Values["series"]);
    }

    [Fact]
    public void InlinesOneOfIntoParentMessage()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(QuerySchema.Schema);

        // filter=byId("z") is written at the case's own field 3 (LEN), not the member's field 2,
        // and not wrapped in a sub-message: 1A 01 7A
        byte[] expected = [0x1A, 0x01, 0x7A];
        Assert.Equal(expected, codec.Serialize(new Query(Filter: new Filter.ById("z"))));

        var result = codec.Deserialize(codec.Serialize(new Query(new Filter.ByNum(42), 7)));
        Assert.Equal(7, result.Page);
        Assert.Equal(new Filter.ByNum(42), result.Filter);
    }

    [Fact]
    public void RoundTripsDocumentAsValue()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(EnvelopeSchema.Schema);

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

    [Fact]
    public void BackpatchesMultiByteNestedMessageLengths()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(EnvelopeSchema.Schema);
        var payload = Document.From(new string('x', 128));

        var bytes = codec.Serialize(new Envelope(payload));

        Assert.Equal([0x0A, 0x83, 0x01, 0x1A, 0x80, 0x01], bytes[..6]);
        Assert.Equal(payload, codec.Deserialize(bytes).Payload);
    }

    /// <summary>
    /// A framework shape carries no <c>@protoIndex</c> — it comes from the runtime, not a model file
    /// — so the codec numbers its members by declaration order instead. The server runtime can
    /// return one from any operation, so a gRPC service has to be able to put it on the wire.
    /// </summary>
    [Fact]
    public void NumbersFrameworkShapeMembersByDeclarationOrder()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(ValidationExceptionSchema.Schema);

        var bytes = codec.Serialize(
            new ValidationException("nope", [new ValidationExceptionField("/a", "bad")])
        );

        // 0A 04 'n' 'o' 'p' 'e'                  (message = field 1, LEN)
        // 12 09 0A 02 '/' 'a' 12 03 'b' 'a' 'd'  (fieldList = field 2, holding path=1, message=2)
        Assert.Equal(
            "0A046E6F706512090A022F611203626164",
            Convert.ToHexString(bytes),
            StringComparer.Ordinal
        );

        var round = codec.Deserialize(bytes);
        Assert.Equal("nope", round.Message);
        Assert.Equal("/a", Assert.Single(round.FieldList).Path);
    }

    /// <summary>A modeled shape still has to say so: order is not a number a model may omit.</summary>
    [Fact]
    public void RejectsAModeledMemberWithNoProtoIndex()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProtoCodecFactory.Default.FromSchema(UnnumberedSchema.Schema)
        );

        Assert.Contains(
            "nsmithy.tests.proto#Unnumbered$name",
            ex.Message,
            StringComparison.Ordinal
        );
    }
}
