using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using Nsmithy.Tests.Validation;

namespace NSmithy.Tests.Core;

public sealed class SmithyValidatorTests
{
    private const string FixtureNamespace = "nsmithy.tests.validation";
    private static readonly ShapeId LengthTrait = new("smithy.api", "length");
    private static readonly ShapeId RangeTrait = new("smithy.api", "range");
    private static readonly ShapeId PatternTrait = new("smithy.api", "pattern");
    private static readonly ShapeId UniqueItemsTrait = new("smithy.api", "uniqueItems");

    [Fact]
    public void ValidateAcceptsValueThatSatisfiesConstraints()
    {
        var validator = SmithyValidator.FromSchema(ProfileSchema.Schema)!;

        validator.Validate(NewProfile("Ada", 36, "math", "logic"));
    }

    [Fact]
    public void GetErrorsReportsScalarAndAggregateConstraintFailures()
    {
        var validator = SmithyValidator.FromSchema(ProfileSchema.Schema)!;

        var errors = validator.GetErrors(NewProfile("a", 151, "dup", "dup"));

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
        var validator = SmithyValidator.FromSchema(ProfileSchema.Schema)!;

        var ex = Assert.Throws<ValidationException>(() =>
            validator.Validate(NewProfile("Ada!", 36, "math"))
        );

        var field = Assert.Single(ex.FieldList);
        Assert.Equal("/Name", field.Path);
        Assert.Contains("/Name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRecursesIntoNestedStructures()
    {
        var validator = SmithyValidator.FromSchema(AccountSchema.Schema)!;

        var errors = validator.GetErrors(new Account(NewProfile("Ada!", 36, "math")));

        var error = Assert.Single(errors);
        Assert.Equal("/profile/Name", error.Path);
        Assert.Equal(PatternTrait, error.ConstraintId);
    }

    [Fact]
    public void ValidateReportsNullRequiredMember()
    {
        var validator = SmithyValidator.FromSchema(ProfileSchema.Schema)!;

        var errors = validator.GetErrors(NewProfile(null!, 36, "math"));

        var error = Assert.Single(errors);
        Assert.Equal("/Name", error.Path);
        Assert.Equal(new ShapeId("smithy.api", "required"), error.ConstraintId);
    }

    [Fact]
    public void FromSchemaReturnsNullWhenNothingRequiresValidation()
    {
        Assert.Null(SmithyValidator.FromSchema(Schemas.String));
        Assert.Null(SmithyValidator.FromSchema(NoteSchema.Schema));
    }

    [Fact]
    public void FromSchemaSupportsRecursiveSchemas()
    {
        var validator = SmithyValidator.FromSchema(TreeNodeSchema.Schema)!;

        var errors = validator.GetErrors(new TreeNode("ok", new TreeNode("too long!")));

        var error = Assert.Single(errors);
        Assert.Equal("/Child/Label", error.Path);
        Assert.Equal(LengthTrait, error.ConstraintId);
    }

    [Fact]
    public void ValidateAppliesCollectionMemberTraits()
    {
        var validator = SmithyValidator.FromSchema(CodesSchema.Schema)!;

        var errors = validator.GetErrors(new Codes(["a"]));

        var error = Assert.Single(errors);
        Assert.Equal("/0", error.Path);
        Assert.Equal(LengthTrait, error.ConstraintId);
        Assert.Equal(new ShapeId(FixtureNamespace, "Codes", "member"), error.ShapeId);
    }

    [Fact]
    public void ValidateAppliesMapKeyAndValueMemberTraits()
    {
        var validator = SmithyValidator.FromSchema(ScoresSchema.Schema)!;

        var errors = validator.GetErrors(
            new Scores(new Dictionary<string, int>(StringComparer.Ordinal) { ["x"] = 11 })
        );

        Assert.Equal(2, errors.Count);
        // A key violation is reported at the map itself, not at the entry's pointer.
        Assert.Contains(
            errors,
            error =>
                error.Path == ""
                && error.ConstraintId == LengthTrait
                && error.ShapeId == new ShapeId(FixtureNamespace, "Scores", "key")
        );
        Assert.Contains(
            errors,
            error =>
                error.Path == "/x"
                && error.ConstraintId == RangeTrait
                && error.ShapeId == new ShapeId(FixtureNamespace, "Scores", "value")
        );
    }

    [Fact]
    public void ValidateAppliesRangeToOptionalNumericMember()
    {
        // An optional numeric member is typed Nullable<T>, which must not hide the constraint.
        var validator = SmithyValidator.FromSchema(MeasurementSchema.Schema)!;

        var errors = validator.GetErrors(new Measurement(Score: 5, Rating: 100));

        var error = Assert.Single(errors);
        Assert.Equal("/rating", error.Path);
        Assert.Equal(RangeTrait, error.ConstraintId);
    }

    [Fact]
    public void ValidateSkipsRangeForAbsentOptionalNumericMember()
    {
        var validator = SmithyValidator.FromSchema(MeasurementSchema.Schema)!;

        Assert.Empty(validator.GetErrors(new Measurement(Score: 5)));
    }

    [Fact]
    public void ValidateReportsRangeForValueBeyondDecimalPrecision()
    {
        // decimal cannot hold 1e300; converting it would throw on the request path.
        var validator = SmithyValidator.FromSchema(MeasurementSchema.Schema)!;

        var errors = validator.GetErrors(new Measurement(Score: 1e300));

        var error = Assert.Single(errors);
        Assert.Equal("/score", error.Path);
        Assert.Equal(RangeTrait, error.ConstraintId);
    }

    [Fact]
    public void ValidateIgnoresStreamingBlobMembers()
    {
        // A streaming blob has no validation visitor case; reaching it used to throw on the first
        // request, after compilation had already been deferred past construction.
        var validator = SmithyValidator.FromSchema(UploadInputSchema.Schema)!;

        var error = Assert.Single(
            validator.GetErrors(new UploadInput(Body: new MemoryStream(), Name: "a"))
        );
        Assert.Equal("/name", error.Path);
    }

    [Fact]
    public void FromSchemaSkipsLengthOnStreamingBlobMembers()
    {
        // A streaming blob is a blob by kind, so @length reaches the length compiler, which has no
        // way to measure an unread stream. Skipped rather than rejected: building the schema must
        // not throw, on a client that never validates least of all.
        var validator = SmithyValidator.FromSchema(LimitedUploadInputSchema.Schema)!;

        Assert.Empty(
            validator.GetErrors(
                new LimitedUploadInput(Body: new MemoryStream([1, 2, 3, 4, 5, 6]), Name: "Ada")
            )
        );
    }

    [Fact]
    public void ValidateComparesBlobsByContentForUniqueItems()
    {
        var validator = SmithyValidator.FromSchema(BlobsSchema.Schema)!;

        var errors = validator.GetErrors(
            new Blobs([
                [1, 2],
                [1, 2],
            ])
        );

        Assert.Single(errors, error => error.ConstraintId == UniqueItemsTrait);
        Assert.Empty(
            validator.GetErrors(
                new Blobs([
                    [1, 2],
                    [3, 4],
                ])
            )
        );
    }

    [Fact]
    public void ValidateEscapesMapKeysInPointerPaths()
    {
        var validator = SmithyValidator.FromSchema(RangedScoresSchema.Schema)!;

        var errors = validator.GetErrors(
            new RangedScores(new Dictionary<string, int>(StringComparer.Ordinal) { ["a/b~c"] = 11 })
        );

        var error = Assert.Single(errors);
        Assert.Equal("/a~1b~0c", error.Path);
    }

    [Fact]
    public void ValidateRejectsValueOutsideStringEnum()
    {
        var validator = SmithyValidator.FromSchema(ColourSchema.Schema)!;

        Assert.Empty(validator.GetErrors(Colour.RED));
        var error = Assert.Single(validator.GetErrors(new Colour("MAUVE")));
        Assert.Equal(new ShapeId("smithy.api", "enum"), error.ConstraintId);
    }

    [Fact]
    public void ValidateRejectsValueOutsideIntEnum()
    {
        var validator = SmithyValidator.FromSchema(RankSchema.Schema)!;

        Assert.Empty(validator.GetErrors(Rank.Second));
        Assert.Single(validator.GetErrors((Rank)7));
    }

    [Fact]
    public void ValidateRejectsMapKeyOutsideItsEnum()
    {
        // A map key is a string whatever it targets, so the value set can only come from the shape
        // the key member targets: there is nowhere on a string for it to live.
        var validator = SmithyValidator.FromSchema(PaletteSchema.Schema)!;

        Assert.Empty(
            validator.GetErrors(
                new Palette(new Dictionary<string, string> { ["RED"] = "a", ["PUCE"] = "b" })
            )
        );

        var error = Assert.Single(
            validator.GetErrors(new Palette(new Dictionary<string, string> { ["MAUVE"] = "a" }))
        );
        // Reported at the map, not the entry: the key is not a value sitting at the entry's pointer.
        Assert.Equal("", error.Path);
        Assert.Equal(new ShapeId("smithy.api", "enum"), error.ConstraintId);
        // PUCE is accepted above but withheld here: an @internal value is not advertised.
        Assert.EndsWith(
            "Member must satisfy enum value set: [RED, GREEN]",
            error.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ValidateComparesStructuresByContentForUniqueItems()
    {
        // Generated structures are records, but a record compares a list member by reference, so
        // .NET equality alone would let these two duplicates through.
        var validator = SmithyValidator.FromSchema(BasketsSchema.Schema)!;

        var errors = validator.GetErrors(new Baskets([NewBasket("a"), NewBasket("a")]));

        Assert.Single(errors, error => error.ConstraintId == UniqueItemsTrait);
        Assert.Empty(validator.GetErrors(new Baskets([NewBasket("a"), NewBasket("b")])));
    }

    private static Profile NewProfile(string name, int age, params string[] tags) =>
        new(Age: age, Name: name, Tags: new Tags(tags));

    private static Basket NewBasket(params string[] items) => new(new Items(items));
}
