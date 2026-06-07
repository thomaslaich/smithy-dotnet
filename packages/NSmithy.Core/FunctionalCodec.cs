namespace NSmithy.Core.Functional;

public interface IFunctionalCodec<TValue, TPayload>
{
    TPayload Serialize(TValue value);

    TValue Deserialize(TPayload payload);
}

public interface IFunctionalProjectionCodec<TValue, TPayload>
{
    TPayload Serialize(TValue value);

    void ReadInto(TPayload payload, object builder);
}
