namespace SmithyNet.Server;

public readonly record struct SmithyTraitDescriptor(string Id, string? Value = null)
{
    public SmithyTraitDescriptor()
        : this(string.Empty) { }
}
