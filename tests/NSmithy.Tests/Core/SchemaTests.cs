using System.Text;
using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Protocols.Rest;
using Nsmithy.Tests.Core;

namespace NSmithy.Tests.Core;

public sealed class SchemaTests
{
    [Fact]
    public void OperationSchemaCarriesOperationAndMemberTraits()
    {
        var operation = UpdateUserSchema.Schema;
        var inputSchema = Assert.IsAssignableFrom<IStructSchema>(UpdateUserInputSchema.Schema);

        Assert.Equal(ShapeKind.Operation, operation.Kind);
        Assert.Same(UpdateUserInputSchema.Schema, operation.Input.Resolved);
        Assert.Same(UpdateUserOutputSchema.Schema, operation.Output.Resolved);
        Assert.False(operation.IsStreaming);
        Assert.Equal(
            "PUT",
            operation.GetTrait(RestTraits.Http)!.Value.Value.AsObject()["method"].AsString()
        );
        Assert.True(
            inputSchema.GetMember("userId")!.MemberTraits.ContainsKey(RestTraits.HttpLabel)
        );
        Assert.Equal(
            "X-Request-Token",
            inputSchema
                .GetMember("requestToken")!
                .MemberTraits[RestTraits.HttpHeader]
                .Value.AsString()
        );
        Assert.False(
            inputSchema.GetMember("displayName")!.MemberTraits.ContainsKey(RestTraits.HttpLabel)
        );
    }

    [Fact]
    public void MemberSchemaResolvesMemberTraitsBeforeTargetTraits()
    {
        var traitId = ShapeId.Parse("smithy.api#timestampFormat");
        var schema = Assert.IsAssignableFrom<IStructSchema>(DateInputSchema.Schema);

        var member = schema.GetMember("created")!;

        Assert.Equal("epoch-seconds", member.GetTrait(traitId)!.Value.Value.AsString());
        Assert.Equal("epoch-seconds", member.MemberTraits[traitId].Value.AsString());
        Assert.Equal("date-time", member.Target.GetTrait(traitId)!.Value.Value.AsString());
    }

    [Fact]
    public void OperationSchemaCarriesModeledErrors()
    {
        var operation = GetUserSchema.Schema;

        var error = Assert.IsType<OperationErrorSchema<BadRequest>>(operation.Errors[0]);
        Assert.Equal(ShapeId.Parse("nsmithy.tests.core#BadRequest"), error.Id);
        Assert.Same(BadRequestSchema.Schema, error.Schema.Resolved);
        Assert.Equal(400, error.HttpStatusCode);
    }

    [Fact]
    public void StructProjectionSnapshotsSelectedMembers()
    {
        var schema =
            (StructSchema<VisitorInput, VisitorInputSchema.Builder>)VisitorInputSchema.Schema;
        var selected = new HashSet<string>(StringComparer.Ordinal) { "name" };
        var projection = Schemas.Project(schema, selected);

        selected.Clear();
        selected.Add("age");

        Assert.NotNull(projection.GetMember("name"));
        Assert.Null(projection.GetMember("age"));
        Assert.Equal(
            "{\"name\":\"Ada\"}",
            Encoding.UTF8.GetString(
                JsonCodecFactory
                    .Default.FromProjection(projection)
                    .Serialize(new VisitorInput(Name: "Ada", Age: 36))
            )
        );
    }

    [Fact]
    public void SchemaVisitorsRecoverHiddenBuilderAndErrorTypes()
    {
        var structure = Assert.IsAssignableFrom<IStructSchema<VisitorInput>>(
            VisitorInputSchema.Schema
        );

        Assert.Equal(typeof(VisitorInputSchema.Builder), GetBuilderType(structure));
        Assert.Equal(typeof(BadRequest), GetErrorType(GetUserSchema.Schema.Errors[0]));
    }

    [Fact]
    public void NullableSchemaDistinguishesTypedAndUntypedTargets()
    {
        var nullable = Assert.IsType<NullableSchema<int>>(Schemas.Nullable(Schemas.Integer));

        Assert.Same(Schemas.Integer, nullable.TypedTarget);
        Assert.Same(Schemas.Integer, nullable.Target);
    }

    [Fact]
    public void StringEnumSchemaModelsEnumWireValue()
    {
        var statusSchema = Assert.IsType<StringEnumSchema<Status>>(StatusSchema.Schema);

        Assert.Equal(ShapeId.Parse("nsmithy.tests.core#Status"), statusSchema.Id);
        Assert.Equal(ShapeKind.Enum, statusSchema.Kind);
        Assert.Equal("ACTIVE", ((IStringEnumValue)Status.ACTIVE).Value);
        Assert.Equal(Status.INACTIVE, statusSchema.Create("INACTIVE"));
    }

    private sealed class BuilderTypeVisitor : IStructSchemaVisitor<VisitorInput, Type>
    {
        public Type Visit<TBuilder>(StructSchema<VisitorInput, TBuilder> schema) =>
            typeof(TBuilder);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The erased interface is the behavior under test."
    )]
    private static Type GetBuilderType(IStructSchema<VisitorInput> schema) =>
        schema.Accept(new BuilderTypeVisitor());

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The erased interface is the behavior under test."
    )]
    private static Type GetErrorType(IOperationErrorSchema schema) =>
        schema.Accept(ErrorTypeVisitor.Instance);

    private sealed class ErrorTypeVisitor : IOperationErrorSchemaVisitor<Type>
    {
        public static ErrorTypeVisitor Instance { get; } = new();

        public Type Visit<TError>(OperationErrorSchema<TError> schema)
            where TError : Exception => typeof(TError);
    }
}
