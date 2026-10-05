using System.Diagnostics.CodeAnalysis;

namespace NSmithy.Core.Serde;

/// <summary>
/// The event-stream member of an operation's input or output structure, bound with the structure's
/// builder type and the stream's event type. A protocol reads the events off a value with
/// <see cref="GetEvents"/> and assembles a value around incoming events with <see cref="Build"/>.
/// </summary>
public sealed class EventStreamBinding<TShape, TBuilder, TEvent>
{
    internal EventStreamBinding(
        IStructSchema<TShape, TBuilder> structure,
        IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> member,
        Schema<TEvent> eventSchema,
        bool hasInitialMembers
    )
    {
        Structure = structure;
        Member = member;
        EventSchema = eventSchema;
        HasInitialMembers = hasInitialMembers;
    }

    public IStructSchema<TShape, TBuilder> Structure { get; }

    public IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> Member { get; }

    public Schema<TEvent> EventSchema { get; }

    /// <summary>
    /// Whether the structure has members besides the event stream. Protocols that support them
    /// send them as an initial message ahead of the events.
    /// </summary>
    public bool HasInitialMembers { get; }

    /// <summary>The structure's members other than the event stream.</summary>
    public StructProjection<TShape, TBuilder> InitialMembers =>
        Schemas.Project(Structure, member => !ReferenceEquals(member, Member));

    public IAsyncEnumerable<TEvent> GetEvents(TShape shape) =>
        Member.GetValue(shape)
        ?? throw new InvalidOperationException($"Event stream member '{Member.Name}' was null.");

    /// <summary>
    /// Builds a value carrying <paramref name="events"/>. <paramref name="readInitialMembers"/>
    /// fills the other members first, for protocols that send them ahead of the stream.
    /// </summary>
    public TShape Build(
        IAsyncEnumerable<TEvent> events,
        Action<TBuilder>? readInitialMembers = null
    )
    {
        var builder = Structure.CreateTypedBuilder();
        readInitialMembers?.Invoke(builder);
        Member.SetValue(builder, events);
        return Structure.Build(builder);
    }
}

/// <summary>
/// Receives an <see cref="EventStreamBinding{TShape, TBuilder, TEvent}"/> with its builder and
/// event types in scope.
/// </summary>
public interface IEventStreamBindingVisitor<TShape, out TResult>
{
    TResult Visit<TBuilder, TEvent>(EventStreamBinding<TShape, TBuilder, TEvent> binding);
}

public static class EventStreamBinding
{
    /// <summary>
    /// Binds the event-stream member of <paramref name="schema"/> and hands it to
    /// <paramref name="visitor"/>. Returns false when the schema is not a structure or has no
    /// event-stream member; throws when it has more than one.
    /// </summary>
    public static bool TryBind<TShape, TResult>(
        Schema<TShape> schema,
        IEventStreamBindingVisitor<TShape, TResult> visitor,
        [MaybeNullWhen(false)] out TResult result
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(visitor);

        if (schema.Resolved is not IStructSchema<TShape> structure)
        {
            result = default;
            return false;
        }

        var (found, value) = structure.Accept(new StructBinder<TShape, TResult>(visitor));
        result = value;
        return found;
    }

    private sealed class StructBinder<TShape, TResult>(
        IEventStreamBindingVisitor<TShape, TResult> visitor
    ) : IStructSchemaVisitor<TShape, (bool Found, TResult? Result)>
    {
        public (bool Found, TResult? Result) Visit<TBuilder>(
            IStructSchema<TShape, TBuilder> structure
        )
        {
            var finder = new MemberFinder<TShape, TBuilder>();
            structure.VisitMembers(finder);
            return finder.Stream is { } stream
                ? (true, stream.Bind(structure, finder.MemberCount > 1, visitor))
                : (false, default);
        }
    }

    private sealed class MemberFinder<TShape, TBuilder> : IMemberVisitor<TShape, TBuilder>
    {
        public IUnboundStream<TShape, TBuilder>? Stream { get; private set; }

        public int MemberCount { get; private set; }

        public void Visit<TValue>(IMemberSchema<TShape, TBuilder, TValue> member)
        {
            MemberCount++;
            if (member.Target.Resolved is not IEventStreamSchema)
            {
                return;
            }

            if (Stream is not null)
            {
                throw new InvalidOperationException(
                    $"Shape '{member.Id.Namespace}#{member.Id.Name}' has more than one event stream member."
                );
            }

            Stream = member.TypedTarget.Resolved.Accept(
                new StreamMemberCompiler<TShape, TBuilder, TValue>(member)
            );
        }
    }

    // The event type only comes into scope by visiting the member's target.
    private sealed class StreamMemberCompiler<TShape, TBuilder, TValue>(
        IMemberSchema<TShape, TBuilder, TValue> member
    ) : PartialSchemaVisitor<IUnboundStream<TShape, TBuilder>>
    {
        public override IUnboundStream<TShape, TBuilder> VisitEventStream<TEvent>(
            EventStreamSchema<TEvent> schema
        ) =>
            new UnboundStream<TShape, TBuilder, TEvent>(
                (IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>>)(object)member,
                schema.TypedEventSchema
            );
    }

    private interface IUnboundStream<TShape, TBuilder>
    {
        TResult Bind<TResult>(
            IStructSchema<TShape, TBuilder> structure,
            bool hasInitialMembers,
            IEventStreamBindingVisitor<TShape, TResult> visitor
        );
    }

    private sealed class UnboundStream<TShape, TBuilder, TEvent>(
        IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> member,
        Schema<TEvent> eventSchema
    ) : IUnboundStream<TShape, TBuilder>
    {
        public TResult Bind<TResult>(
            IStructSchema<TShape, TBuilder> structure,
            bool hasInitialMembers,
            IEventStreamBindingVisitor<TShape, TResult> visitor
        ) =>
            visitor.Visit(
                new EventStreamBinding<TShape, TBuilder, TEvent>(
                    structure,
                    member,
                    eventSchema,
                    hasInitialMembers
                )
            );
    }
}
