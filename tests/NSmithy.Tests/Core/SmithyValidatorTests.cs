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
            error => error.Path == "$.Name" && error.ConstraintId == LengthTrait
        );
        Assert.Contains(errors, error => error.Path == "$.Age" && error.ConstraintId == RangeTrait);
        Assert.Contains(
            errors,
            error => error.Path == "$.Tags" && error.ConstraintId == UniqueItemsTrait
        );
    }

    [Fact]
    public void ValidateThrowsValidationExceptionWithErrors()
    {
        var validator = SmithyValidator.FromSchema(ProfileSchema())!;

        var ex = Assert.Throws<SmithyValidationException>(() =>
            validator.Validate(new Profile("Ada!", 36, ["math"]))
        );

        var error = Assert.Single(ex.Errors);
        Assert.Equal("$.Name", error.Path);
        Assert.Equal(PatternTrait, error.ConstraintId);
        Assert.Contains("$.Name", ex.Message, StringComparison.Ordinal);
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
        Assert.Equal("$.profile.Name", error.Path);
        Assert.Equal(PatternTrait, error.ConstraintId);
    }

    [Fact]
    public void ValidateReportsNullRequiredMember()
    {
        var validator = SmithyValidator.FromSchema(ProfileSchema())!;

        var errors = validator.GetErrors(new Profile(null!, 36, ["math"]));

        var error = Assert.Single(errors);
        Assert.Equal("$.Name", error.Path);
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
        Assert.Equal("$.Child.Label", error.Path);
        Assert.Equal(LengthTrait, error.ConstraintId);
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
