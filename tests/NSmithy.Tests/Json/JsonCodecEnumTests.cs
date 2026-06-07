using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Json;

public sealed class JsonCodecEnumTests
{
    public readonly record struct Status(string Value) : IStringEnumValue<Status>
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
    public void JsonCodecRoundTripsStringEnumMember()
    {
        var input = new Deployment("deploy-api", Status.Active);
        var expectedJson = "{\"name\":\"deploy-api\",\"status\":\"ACTIVE\"}";

        var statusSchema = Schemas.StringEnum<Status>(new ShapeId("example", "Status"));
        var deploymentSchema = Schemas
            .Structure<Deployment, DeploymentBuilder>(new ShapeId("example", "Deployment"))
            .Required(
                "name",
                static deployment => deployment.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
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
        var codec = JsonCodec.FromSchema(deploymentSchema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

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
    public void JsonCodecRoundTripsIntEnumMember()
    {
        var input = new WorkItem("rollback", Priority.High);
        var expectedJson = "{\"title\":\"rollback\",\"priority\":2}";

        var prioritySchema = Schemas.IntEnum<Priority>(new ShapeId("example", "Priority"));
        var workItemSchema = Schemas
            .Structure<WorkItem, WorkItemBuilder>(new ShapeId("example", "WorkItem"))
            .Required(
                "title",
                static workItem => workItem.Title,
                static (builder, value) => builder.Title = value,
                Schemas.String
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
        var codec = JsonCodec.FromSchema(workItemSchema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
    }
}
