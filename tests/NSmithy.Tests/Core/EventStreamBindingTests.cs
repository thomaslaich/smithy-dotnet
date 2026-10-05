using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Core;

public sealed class EventStreamBindingTests
{
    private sealed class Envelope
    {
        public string? Name { get; set; }

        public IAsyncEnumerable<string>? Events { get; set; }

        public IAsyncEnumerable<string>? MoreEvents { get; set; }
    }

    private static readonly Schema<string> EventSchema = Schemas.String;

    private static Schema<Envelope> EnvelopeSchema(bool withName, bool withSecondStream = false)
    {
        var builder = Schemas.Structure<Envelope, Envelope>(ShapeId.Parse("example#Envelope"));
        if (withName)
        {
            builder = builder.Optional(
                "name",
                x => x.Name,
                (b, v) => b.Name = v,
                Schemas.NullableReference(Schemas.String)
            );
        }

        builder = builder.Required(
            "events",
            x => x.Events!,
            (b, v) => b.Events = v,
            Schemas.EventStream(EventSchema)
        );
        if (withSecondStream)
        {
            builder = builder.Required(
                "moreEvents",
                x => x.MoreEvents!,
                (b, v) => b.MoreEvents = v,
                Schemas.EventStream(EventSchema)
            );
        }

        return builder.Build(() => new Envelope(), b => b);
    }

    private sealed class Probe : IEventStreamBindingVisitor<Envelope, IProbeResult>
    {
        public IProbeResult Visit<TBuilder, TEvent>(
            EventStreamBinding<Envelope, TBuilder, TEvent> binding
        ) => new ProbeResult<TBuilder, TEvent>(binding);
    }

    private interface IProbeResult
    {
        string MemberName { get; }

        bool HasInitialMembers { get; }

        Envelope Build(IAsyncEnumerable<string> events);

        IAsyncEnumerable<string> GetEvents(Envelope envelope);
    }

    private sealed class ProbeResult<TBuilder, TEvent>(
        EventStreamBinding<Envelope, TBuilder, TEvent> binding
    ) : IProbeResult
    {
        public string MemberName => binding.Member.Name;

        public bool HasInitialMembers => binding.HasInitialMembers;

        public Envelope Build(IAsyncEnumerable<string> events) =>
            binding.Build((IAsyncEnumerable<TEvent>)events);

        public IAsyncEnumerable<string> GetEvents(Envelope envelope) =>
            (IAsyncEnumerable<string>)binding.GetEvents(envelope);
    }

    [Fact]
    public void BindsTheEventStreamMember()
    {
        Assert.True(
            EventStreamBinding.TryBind(EnvelopeSchema(withName: false), new Probe(), out var result)
        );

        Assert.Equal("events", result.MemberName);
        Assert.False(result.HasInitialMembers);
    }

    [Fact]
    public void ReportsInitialMembers()
    {
        Assert.True(
            EventStreamBinding.TryBind(EnvelopeSchema(withName: true), new Probe(), out var result)
        );

        Assert.True(result.HasInitialMembers);
    }

    [Fact]
    public void BuildsAndReadsTheEvents()
    {
        EventStreamBinding.TryBind(EnvelopeSchema(withName: false), new Probe(), out var result);
        var events = AsyncEnumerable.Empty<string>();

        var envelope = result!.Build(events);

        Assert.Same(events, envelope.Events);
        Assert.Same(events, result.GetEvents(envelope));
    }

    [Fact]
    public void ReadingANullStreamThrows()
    {
        EventStreamBinding.TryBind(EnvelopeSchema(withName: false), new Probe(), out var result);

        Assert.Throws<InvalidOperationException>(() => result!.GetEvents(new Envelope()));
    }

    [Fact]
    public void DoesNotBindAShapeWithoutAnEventStream()
    {
        var schema = Schemas
            .Structure<Envelope, Envelope>(ShapeId.Parse("example#Plain"))
            .Optional(
                "name",
                x => x.Name,
                (b, v) => b.Name = v,
                Schemas.NullableReference(Schemas.String)
            )
            .Build(() => new Envelope(), b => b);

        Assert.False(EventStreamBinding.TryBind(schema, new Probe(), out _));
    }

    [Fact]
    public void RejectsMoreThanOneEventStream()
    {
        var schema = EnvelopeSchema(withName: false, withSecondStream: true);

        Assert.Throws<InvalidOperationException>(() =>
            EventStreamBinding.TryBind(schema, new Probe(), out _)
        );
    }
}
