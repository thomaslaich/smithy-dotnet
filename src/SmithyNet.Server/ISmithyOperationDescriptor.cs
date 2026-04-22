namespace SmithyNet.Server;

public interface ISmithyOperationDescriptor
{
    string ShapeId { get; }
    string Name { get; }
    Type HandlerType { get; }
    Type InputType { get; }
    Type OutputType { get; }
    bool HasInput { get; }
    bool HasOutput { get; }
    bool HasStreamingInput { get; }
    bool HasStreamingOutput { get; }
    IReadOnlyList<SmithyTraitDescriptor> Traits { get; }
}
