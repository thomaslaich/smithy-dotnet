namespace NSmithy.Core;

/// <summary>
/// Singleton schemas for the Smithy prelude shapes (<c>smithy.api#*</c>). Codecs and
/// generated code reuse these instances rather than constructing new schemas each call.
/// </summary>
public static class PreludeSchemas
{
    private const string Ns = "smithy.api";

    public static Schema Boolean { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Boolean"), ShapeKind.Boolean);
    public static Schema Byte { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Byte"), ShapeKind.Byte);
    public static Schema Short { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Short"), ShapeKind.Short);
    public static Schema Integer { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Integer"), ShapeKind.Integer);
    public static Schema Long { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Long"), ShapeKind.Long);
    public static Schema Float { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Float"), ShapeKind.Float);
    public static Schema Double { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Double"), ShapeKind.Double);
    public static Schema BigInteger { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "BigInteger"), ShapeKind.BigInteger);
    public static Schema BigDecimal { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "BigDecimal"), ShapeKind.BigDecimal);
    public static Schema String { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "String"), ShapeKind.String);
    public static Schema Blob { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Blob"), ShapeKind.Blob);
    public static Schema Timestamp { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Timestamp"), ShapeKind.Timestamp);
    public static Schema Document { get; } =
        Schema.CreateSimple(new ShapeId(Ns, "Document"), ShapeKind.Document);
    public static Schema Unit { get; } =
        Schema.CreateStructure(new ShapeId(Ns, "Unit"), members: []);
}
