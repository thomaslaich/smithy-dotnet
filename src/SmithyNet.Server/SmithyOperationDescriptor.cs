namespace SmithyNet.Server;

public sealed class SmithyOperationDescriptor<THandler, TInput, TOutput>
    : ISmithyOperationDescriptor
{
    private readonly Func<THandler, TInput, CancellationToken, Task<TOutput>> invokeAsync;

    public SmithyOperationDescriptor(
        string shapeId,
        string name,
        IReadOnlyList<SmithyTraitDescriptor>? traits,
        Func<THandler, TInput, CancellationToken, Task<TOutput>> invokeAsync,
        bool hasStreamingInput = false,
        bool hasStreamingOutput = false
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shapeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(invokeAsync);

        ShapeId = shapeId;
        Name = name;
        Traits = traits ?? [];
        this.invokeAsync = invokeAsync;
        HasStreamingInput = hasStreamingInput;
        HasStreamingOutput = hasStreamingOutput;
    }

    public string ShapeId { get; }

    public string Name { get; }

    public Type HandlerType => typeof(THandler);

    public Type InputType => typeof(TInput);

    public Type OutputType => typeof(TOutput);

    public bool HasInput => typeof(TInput) != typeof(SmithyUnit);

    public bool HasOutput => typeof(TOutput) != typeof(SmithyUnit);

    public bool HasStreamingInput { get; }

    public bool HasStreamingOutput { get; }

    public IReadOnlyList<SmithyTraitDescriptor> Traits { get; }

    public Task<TOutput> InvokeAsync(
        THandler handler,
        TInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        return invokeAsync(handler, input, cancellationToken);
    }
}
