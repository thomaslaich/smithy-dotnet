using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Functional;

namespace NSmithy.Tests.Json;

public sealed class FunctionalJsonCodecEnumTests
{
    public readonly record struct Status(string Value) : IFunctionalStringEnumValue<Status>
    {
        public static readonly Status Active = new("ACTIVE");

        public static Status FromValue(string value) => new(value);
    }

    public sealed record Deployment(string Name, Status Status);

    public sealed class DeploymentBuilder
    {
        public string? Name { get; set; }

        public Status Status { get; set; }
    }

    [Fact]
    public void FunctionalJsonCodecRoundTripsStringEnumMember()
    {
        var input = new Deployment("deploy-api", Status.Active);
        var expectedJson = "{\"name\":\"deploy-api\",\"status\":\"ACTIVE\"}";

        var statusSchema = FunctionalSchemas.StringEnum<Status>(new ShapeId("example", "Status"));
        var deploymentSchema = FunctionalSchemas
            .Structure<Deployment, DeploymentBuilder>(new ShapeId("example", "Deployment"))
            .Required(
                "name",
                static deployment => deployment.Name,
                static (builder, value) => builder.Name = value,
                FunctionalSchemas.String
            )
            .Required(
                "status",
                static deployment => deployment.Status,
                static (builder, value) => builder.Status = value,
                statusSchema
            )
            .Build(
                static () => new DeploymentBuilder(),
                static builder => new Deployment(builder.Name!, builder.Status)
            );
        var codec = FunctionalJsonCodec.FromSchema(deploymentSchema);

        var json = codec.Serialize(input);
        var decoded = codec.Deserialize(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
    }

    public enum Priority
    {
        Low = 1,
        High = 2,
    }

    public sealed record WorkItem(string Title, Priority Priority);

    public sealed class WorkItemBuilder
    {
        public string? Title { get; set; }

        public Priority Priority { get; set; }
    }

    [Fact]
    public void FunctionalJsonCodecRoundTripsIntEnumMember()
    {
        var input = new WorkItem("rollback", Priority.High);
        var expectedJson = "{\"title\":\"rollback\",\"priority\":2}";

        var prioritySchema = FunctionalSchemas.IntEnum<Priority>(
            new ShapeId("example", "Priority")
        );
        var workItemSchema = FunctionalSchemas
            .Structure<WorkItem, WorkItemBuilder>(new ShapeId("example", "WorkItem"))
            .Required(
                "title",
                static workItem => workItem.Title,
                static (builder, value) => builder.Title = value,
                FunctionalSchemas.String
            )
            .Required(
                "priority",
                static workItem => workItem.Priority,
                static (builder, value) => builder.Priority = value,
                prioritySchema
            )
            .Build(
                static () => new WorkItemBuilder(),
                static builder => new WorkItem(builder.Title!, builder.Priority)
            );
        var codec = FunctionalJsonCodec.FromSchema(workItemSchema);

        var json = codec.Serialize(input);
        var decoded = codec.Deserialize(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
    }
}
