using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;

namespace NSmithy.Tests.Core;

public sealed class SmithyValidatorTests
{
    private static readonly ShapeId LengthTrait = new("smithy.api", "length");
    private static readonly ShapeId RangeTrait = new("smithy.api", "range");
    private static readonly ShapeId PatternTrait = new("smithy.api", "pattern");
    private static readonly ShapeId UniqueItemsTrait = new("smithy.api", "uniqueItems");

    public sealed record Profile(string Name, int Age, IReadOnlyList<string> Tags);

    public sealed class ProfileBuilder
    {
        public string? Name { get; set; }

        public int Age { get; set; }

        public IReadOnlyList<string>? Tags { get; set; }
    }

    [Fact]
    public void ValidateAcceptsValueThatSatisfiesConstraints()
    {
        var validator = SmithyValidator.FromSchema(ProfileSchema())!;

        validator.Validate(new Profile("Ada", 36, ["math", "logic"]));
    }

    [Fact]
    public void GetErrorsReportsScalarAndAggregateConstraintFailures()
    {
        var validator = SmithyValidator.FromSchema(ProfileSchema())!;

        var errors = validator.GetErrors(new Profile("a", 151, ["dup", "dup"]));

        Assert.Equal(3, errors.Count);
        Assert.Contains(
            errors,
            error => error.Path == "/Name" && error.ConstraintId == LengthTrait
        );
        Assert.Contains(errors, error => error.Path == "/Age" && error.ConstraintId == RangeTrait);
        Assert.Contains(
            errors,
            error => error.Path == "/Tags" && error.ConstraintId == UniqueItemsTrait
        );
    }

    [Fact]
    public void ValidateThrowsValidationExceptionWithErrors()
    {
        var validator = SmithyValidator.FromSchema(ProfileSchema())!;

        var ex = Assert.Throws<ValidationException>(() =>
            validator.Validate(new Profile("Ada!", 36, ["math"]))
        );

        var field = Assert.Single(ex.FieldList);
        Assert.Equal("/Name", field.Path);
        Assert.Contains("/Name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRecursesIntoNestedStructures()
    {
        var validator = SmithyValidator.FromSchema(
            Schemas
                .Structure<Account, AccountBuilder>(new ShapeId("example", "Account"))
                .Required(
                    "profile",
                    static value => value.Profile,
                    static (builder, value) => builder.Profile = value,
                    ProfileSchema()
                )
                .Build(
                    static () => new AccountBuilder(),
                    static builder => new Account(builder.Profile!)
                )
        )!;

        var errors = validator.GetErrors(new Account(new Profile("Ada!", 36, ["math"])));

        var error = Assert.Single(errors);
        Assert.Equal("/profile/Name", error.Path);
        Assert.Equal(PatternTrait, error.ConstraintId);
    }

    [Fact]
    public void ValidateReportsNullRequiredMember()
    {
        var validator = SmithyValidator.FromSchema(ProfileSchema())!;

        var errors = validator.GetErrors(new Profile(null!, 36, ["math"]));

        var error = Assert.Single(errors);
        Assert.Equal("/Name", error.Path);
        Assert.Equal(new ShapeId("smithy.api", "required"), error.ConstraintId);
    }

    [Fact]
    public void FromSchemaReturnsNullWhenNothingRequiresValidation()
    {
        Assert.Null(SmithyValidator.FromSchema(Schemas.String));
        Assert.Null(
            SmithyValidator.FromSchema(
                Schemas
                    .Structure<Note, NoteBuilder>(new ShapeId("example", "Note"))
                    .Optional(
                        "Text",
                        static value => value.Text,
                        static (builder, value) => builder.Text = value,
                        Schemas.NullableReference(Schemas.String)
                    )
                    .Build(static () => new NoteBuilder(), static builder => new Note(builder.Text))
            )
        );
    }

    [Fact]
    public void FromSchemaSupportsRecursiveSchemas()
    {
        var validator = SmithyValidator.FromSchema(TreeSchema())!;

        var errors = validator.GetErrors(new TreeNode("ok", new TreeNode("too long!", null)));

        var error = Assert.Single(errors);
        Assert.Equal("/Child/Label", error.Path);
        Assert.Equal(LengthTrait, error.ConstraintId);
    }

    [Fact]
    public void ValidateAppliesCollectionMemberTraits()
    {
        var validator = SmithyValidator.FromSchema(
            Schemas.List(
                new ShapeId("example", "Codes"),
                Schemas.String,
                elementTraits: [Length(2, 4)]
            )
        )!;

        var errors = validator.GetErrors(["a"]);

        var error = Assert.Single(errors);
        Assert.Equal("/0", error.Path);
        Assert.Equal(LengthTrait, error.ConstraintId);
        Assert.Equal(new ShapeId("example", "Codes", "member"), error.ShapeId);
    }

    [Fact]
    public void ValidateAppliesMapKeyAndValueMemberTraits()
    {
        var validator = SmithyValidator.FromSchema(
            Schemas.Map(
                new ShapeId("example", "Scores"),
                Schemas.Integer,
                keyTraits: [Length(2, 4)],
                valueTraits: [Range(0, 10)]
            )
        )!;

        var errors = validator.GetErrors(
            new Dictionary<string, int>(StringComparer.Ordinal) { ["x"] = 11 }
        );

        Assert.Equal(2, errors.Count);
        Assert.Contains(
            errors,
            error =>
                error.Path == "/x"
                && error.ConstraintId == LengthTrait
                && error.ShapeId == new ShapeId("example", "Scores", "key")
        );
        Assert.Contains(
            errors,
            error =>
                error.Path == "/x"
                && error.ConstraintId == RangeTrait
                && error.ShapeId == new ShapeId("example", "Scores", "value")
        );
    }

    public sealed record Note(string? Text);

    public sealed class NoteBuilder
    {
        public string? Text { get; set; }
    }

    public sealed record TreeNode(string Label, TreeNode? Child);

    public sealed class TreeNodeBuilder
    {
        public string? Label { get; set; }

        public TreeNode? Child { get; set; }
    }

    private static Schema<TreeNode> TreeSchema()
    {
        Schema<TreeNode>? schema = null;
        schema = Schemas
            .Structure<TreeNode, TreeNodeBuilder>(new ShapeId("example", "TreeNode"))
            .Required(
                "Label",
                static value => value.Label,
                static (builder, value) => builder.Label = value,
                Schemas.String,
                [Length(1, 5)]
            )
            .Optional(
                "Child",
                static value => value.Child,
                static (builder, value) => builder.Child = value,
                Schemas.NullableReference(Schemas.Lazy(() => schema!))
            )
            .Build(
                static () => new TreeNodeBuilder(),
                static builder => new TreeNode(builder.Label!, builder.Child)
            );
        return schema;
    }

    public sealed record Account(Profile Profile);

    public sealed class AccountBuilder
    {
        public Profile? Profile { get; set; }
    }

    private static Schema<Profile> ProfileSchema()
    {
        var tagsSchema = Schemas.List(
            new ShapeId("example", "Tags"),
            Schemas.String,
            [new Trait(UniqueItemsTrait)]
        );

        return Schemas
            .Structure<Profile, ProfileBuilder>(new ShapeId("example", "Profile"))
            .Required(
                "Name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String,
                [Length(2, 10), new Trait(PatternTrait, Document.From("^[A-Za-z]+$"))]
            )
            .Required(
                "Age",
                static value => value.Age,
                static (builder, value) => builder.Age = value,
                Schemas.Integer,
                [Range(0, 150)]
            )
            .Required(
                "Tags",
                static value => value.Tags,
                static (builder, value) => builder.Tags = value,
                tagsSchema
            )
            .Build(
                static () => new ProfileBuilder(),
                static builder => new Profile(builder.Name!, builder.Age, builder.Tags!)
            );
    }

    public sealed record Measurement(int? Rating, double Score);

    public sealed class MeasurementBuilder
    {
        public int? Rating { get; set; }

        public double Score { get; set; }
    }

    private static Schema<Measurement> MeasurementSchema() =>
        Schemas
            .Structure<Measurement, MeasurementBuilder>(new ShapeId("example", "Measurement"))
            .Optional(
                "rating",
                static value => value.Rating,
                static (builder, value) => builder.Rating = value,
                Schemas.Nullable(Schemas.Integer),
                [Range(0, 10)]
            )
            .Required(
                "score",
                static value => value.Score,
                static (builder, value) => builder.Score = value,
                Schemas.Double,
                [Range(0, 10)]
            )
            .Build(
                static () => new MeasurementBuilder(),
                static builder => new Measurement(builder.Rating, builder.Score)
            );

    [Fact]
    public void ValidateAppliesRangeToOptionalNumericMember()
    {
        // An optional numeric member is typed Nullable<T>, which must not hide the constraint.
        var validator = SmithyValidator.FromSchema(MeasurementSchema())!;

        var errors = validator.GetErrors(new Measurement(100, 5));

        var error = Assert.Single(errors);
        Assert.Equal("/rating", error.Path);
        Assert.Equal(RangeTrait, error.ConstraintId);
    }

    [Fact]
    public void ValidateSkipsRangeForAbsentOptionalNumericMember()
    {
        var validator = SmithyValidator.FromSchema(MeasurementSchema())!;

        Assert.Empty(validator.GetErrors(new Measurement(null, 5)));
    }

    [Fact]
    public void ValidateReportsRangeForValueBeyondDecimalPrecision()
    {
        // decimal cannot hold 1e300; converting it would throw on the request path.
        var validator = SmithyValidator.FromSchema(MeasurementSchema())!;

        var errors = validator.GetErrors(new Measurement(null, 1e300));

        var error = Assert.Single(errors);
        Assert.Equal("/score", error.Path);
        Assert.Equal(RangeTrait, error.ConstraintId);
    }

    public sealed record Upload(string Name, Stream Body);

    public sealed class UploadBuilder
    {
        public string? Name { get; set; }

        public Stream? Body { get; set; }
    }

    [Fact]
    public void ValidateIgnoresStreamingBlobMembers()
    {
        // A streaming blob has no validation visitor case; reaching it used to throw on the first
        // request, after compilation had already been deferred past construction.
        var schema = Schemas
            .Structure<Upload, UploadBuilder>(new ShapeId("example", "Upload"))
            .Required(
                "body",
                static value => value.Body,
                static (builder, value) => builder.Body = value,
                Schemas.StreamingBlob
            )
            .Required(
                "name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String,
                [Length(2, 10)]
            )
            .Build(
                static () => new UploadBuilder(),
                static builder => new Upload(builder.Name!, builder.Body!)
            );

        var validator = SmithyValidator.FromSchema(schema)!;

        var error = Assert.Single(validator.GetErrors(new Upload("a", new MemoryStream())));
        Assert.Equal("/name", error.Path);
    }

    [Fact]
    public void ValidateComparesBlobsByContentForUniqueItems()
    {
        var validator = SmithyValidator.FromSchema(
            Schemas.List(
                new ShapeId("example", "Blobs"),
                Schemas.Blob,
                [new Trait(UniqueItemsTrait)]
            )
        )!;

        var errors = validator.GetErrors([
            [1, 2],
            [1, 2],
        ]);

        Assert.Single(errors, error => error.ConstraintId == UniqueItemsTrait);
        Assert.Empty(
            validator.GetErrors([
                [1, 2],
                [3, 4],
            ])
        );
    }

    [Fact]
    public void ValidateEscapesMapKeysInPointerPaths()
    {
        var validator = SmithyValidator.FromSchema(
            Schemas.Map(
                new ShapeId("example", "Scores"),
                Schemas.Integer,
                valueTraits: [Range(0, 10)]
            )
        )!;

        var errors = validator.GetErrors(
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a/b~c"] = 11 }
        );

        var error = Assert.Single(errors);
        Assert.Equal("/a~1b~0c", error.Path);
    }

    public readonly record struct Colour(string Value) : IStringEnumValue<Colour>
    {
        public static Colour FromValue(string value) => new(value);
    }

    public enum Rank
    {
        First = 1,
        Second = 2,
    }

    [Fact]
    public void ValidateRejectsValueOutsideStringEnum()
    {
        var validator = SmithyValidator.FromSchema(
            Schemas.StringEnum<Colour>(new ShapeId("example", "Colour"), values: ["RED", "GREEN"])
        )!;

        Assert.Empty(validator.GetErrors(new Colour("RED")));
        var error = Assert.Single(validator.GetErrors(new Colour("MAUVE")));
        Assert.Equal(new ShapeId("smithy.api", "enum"), error.ConstraintId);
    }

    [Fact]
    public void ValidateRejectsValueOutsideIntEnum()
    {
        var validator = SmithyValidator.FromSchema(
            Schemas.IntEnum<Rank>(new ShapeId("example", "Rank"), values: [1, 2])
        )!;

        Assert.Empty(validator.GetErrors(Rank.Second));
        Assert.Single(validator.GetErrors((Rank)7));
    }

    [Fact]
    public void FromSchemaSkipsEnumsWithoutDeclaredValues()
    {
        // A schema built without them cannot tell a modeled value from an invented one, so it must
        // not pretend to.
        Assert.Null(SmithyValidator.FromSchema(Schemas.StringEnum<Colour>(new ShapeId("x", "C"))));
    }

    public sealed record Basket(IReadOnlyList<string> Items);

    public sealed class BasketBuilder
    {
        public IReadOnlyList<string>? Items { get; set; }
    }

    [Fact]
    public void ValidateComparesStructuresByContentForUniqueItems()
    {
        // Generated structures are records, but a record compares a list member by reference, so
        // .NET equality alone would let these two duplicates through.
        var basketSchema = Schemas
            .Structure<Basket, BasketBuilder>(new ShapeId("example", "Basket"))
            .Required(
                "items",
                static value => value.Items,
                static (builder, value) => builder.Items = value,
                Schemas.List(new ShapeId("example", "Items"), Schemas.String)
            )
            .Build(static () => new BasketBuilder(), static builder => new Basket(builder.Items!));

        var validator = SmithyValidator.FromSchema(
            Schemas.List(
                new ShapeId("example", "Baskets"),
                basketSchema,
                [new Trait(UniqueItemsTrait)]
            )
        )!;

        var errors = validator.GetErrors([new Basket(["a"]), new Basket(["a"])]);

        Assert.Single(errors, error => error.ConstraintId == UniqueItemsTrait);
        Assert.Empty(validator.GetErrors([new Basket(["a"]), new Basket(["b"])]));
    }

    private static Trait Length(int min, int max) =>
        new(
            LengthTrait,
            Document.From(
                new Dictionary<string, Document>(StringComparer.Ordinal)
                {
                    ["min"] = Document.From(min),
                    ["max"] = Document.From(max),
                }
            )
        );

    private static Trait Range(int min, int max) =>
        new(
            RangeTrait,
            Document.From(
                new Dictionary<string, Document>(StringComparer.Ordinal)
                {
                    ["min"] = Document.From(min),
                    ["max"] = Document.From(max),
                }
            )
        );
}
