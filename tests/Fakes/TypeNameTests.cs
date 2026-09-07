using Example.Names;
using NSmithy.Core.Serde;

namespace NSmithy.Fakes.Tests;

public class TypeNameTests
{
    [Fact]
    public void GeneratedSchemasKeepTheIntendedTypesWhenNamesCollide()
    {
        Assert.IsAssignableFrom<Schema<Builder>>(BuilderSchema.Schema);
        Assert.IsAssignableFrom<Schema<ValueSerializer>>(ValueSerializerSchema.Schema);
        Assert.IsAssignableFrom<Schema<RoundTripInput>>(RoundTripInputSchema.Schema);
        var local = new Widget("local");
        var foreign = new global::Example.Other.Widget(42);
        var payload = new RoundTripInput(LocalWidget: local, ForeignWidget: foreign);
        Assert.Same(local, payload.LocalWidget);
        Assert.Same(foreign, payload.ForeignWidget);
        Assert.Same(local, new Choice.Widget(local).Value);
        Assert.IsType<T>(new Choice.Generic(new T()).Value);
        Assert.IsType<Unknown>(new Choice.UnknownValue(new Unknown()).Value);
    }
}
