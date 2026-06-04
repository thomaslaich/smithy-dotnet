namespace NSmithy.Core.Functional;

public interface IFunctionalCodec<TValue, TPayload>
{
    TPayload Serialize(TValue value);

    TValue Deserialize(TPayload payload);
}

public interface IFunctionalCodecFactory<TPayload>
{
    IFunctionalCodec<object?, TPayload> FromSchema(FunctionalSchema schema);
}
