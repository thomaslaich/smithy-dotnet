namespace Smithy.NET.Core.Runtime;

public interface ICodec<T>
{
    ValueTask SerializeAsync(
        T value,
        Stream output,
        CodecContext context,
        CancellationToken cancellationToken = default
    );

    ValueTask<T> DeserializeAsync(
        Stream input,
        CodecContext context,
        CancellationToken cancellationToken = default
    );
}
