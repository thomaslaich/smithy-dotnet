namespace NSmithy.Core.Functional;

public interface IFunctionalCodec<TValue, TPayload>
{
    TPayload Serialize(TValue value);

    TValue Deserialize(TPayload payload);
}

public interface IFunctionalObjectCodec<TPayload>
{
    TPayload Serialize(object? value);

    object? Deserialize(TPayload payload);
}

public interface IFunctionalCodecFactory<TPayload>
{
    IFunctionalObjectCodec<TPayload> FromSchema(FunctionalSchema schema);
}
