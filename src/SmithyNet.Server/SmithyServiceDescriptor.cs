namespace SmithyNet.Server;

public sealed class SmithyServiceDescriptor<THandler>
{
    public SmithyServiceDescriptor(
        string shapeId,
        string name,
        IReadOnlyList<SmithyTraitDescriptor>? traits,
        IReadOnlyList<ISmithyOperationDescriptor>? operations
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shapeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ShapeId = shapeId;
        Name = name;
        Traits = traits ?? [];
        Operations = operations ?? [];
    }

    public string ShapeId { get; }

    public string Name { get; }

    public Type HandlerType => typeof(THandler);

    public IReadOnlyList<SmithyTraitDescriptor> Traits { get; }

    public IReadOnlyList<ISmithyOperationDescriptor> Operations { get; }
}
