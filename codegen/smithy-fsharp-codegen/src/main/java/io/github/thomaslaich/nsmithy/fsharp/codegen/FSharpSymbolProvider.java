/*
 * Maps Smithy shapes to F# types. A generated type is a symbol in the shape's F# namespace that
 * the writer qualifies from `global`; every other type is a symbol with no namespace whose name is
 * the complete F# type expression, such as `string`, `int64 option list`, or
 * `global.Microsoft.FSharp.Collections.Map<string, int>`.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.BigDecimalShape;
import software.amazon.smithy.model.shapes.BigIntegerShape;
import software.amazon.smithy.model.shapes.BlobShape;
import software.amazon.smithy.model.shapes.BooleanShape;
import software.amazon.smithy.model.shapes.ByteShape;
import software.amazon.smithy.model.shapes.DocumentShape;
import software.amazon.smithy.model.shapes.DoubleShape;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.FloatShape;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.IntegerShape;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.LongShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ResourceShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeVisitor;
import software.amazon.smithy.model.shapes.ShortShape;
import software.amazon.smithy.model.shapes.StringShape;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.shapes.TimestampShape;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class FSharpSymbolProvider implements SymbolProvider, ShapeVisitor<Symbol> {

  /** The child namespace that holds the generated schemas, so they never clash with model types. */
  public static final String SCHEMAS_NAMESPACE = "Schemas";

  private final Model model;
  private final FSharpSettings settings;

  public FSharpSymbolProvider(Model model, FSharpSettings settings) {
    this.model = model;
    this.settings = settings;
  }

  @Override
  public Symbol toSymbol(Shape shape) {
    return shape.accept(this);
  }

  @Override
  public String toMemberName(MemberShape shape) {
    return FSharpNaming.fieldName(shape);
  }

  /** The F# type expression for a symbol, qualified from {@code global} when it is generated. */
  public static String render(Symbol symbol) {
    return symbol.getNamespace().isEmpty()
        ? symbol.getName()
        : "global." + symbol.getNamespace() + "." + symbol.getName();
  }

  /** The type a shape's schema reads and writes, which the generated types and fields use. */
  public String typeOf(Shape shape) {
    return render(toSymbol(shape));
  }

  @Override
  public Symbol blobShape(BlobShape s) {
    return builtin("byte[]");
  }

  @Override
  public Symbol booleanShape(BooleanShape s) {
    return builtin("bool");
  }

  @Override
  public Symbol byteShape(ByteShape s) {
    return builtin("sbyte");
  }

  @Override
  public Symbol shortShape(ShortShape s) {
    return builtin("int16");
  }

  @Override
  public Symbol integerShape(IntegerShape s) {
    return builtin("int");
  }

  @Override
  public Symbol longShape(LongShape s) {
    return builtin("int64");
  }

  @Override
  public Symbol floatShape(FloatShape s) {
    return builtin("float32");
  }

  @Override
  public Symbol doubleShape(DoubleShape s) {
    return builtin("float");
  }

  @Override
  public Symbol bigIntegerShape(BigIntegerShape s) {
    return builtin("global.System.Numerics.BigInteger");
  }

  @Override
  public Symbol bigDecimalShape(BigDecimalShape s) {
    return builtin("decimal");
  }

  /**
   * A string with the deprecated {@code @enum} trait stays a {@code string}: an {@code @enum} entry
   * need not carry the name a case would need. Its values still reach the runtime as traits.
   */
  @Override
  public Symbol stringShape(StringShape s) {
    return builtin("string");
  }

  @Override
  public Symbol timestampShape(TimestampShape s) {
    return builtin("global.System.DateTimeOffset");
  }

  @Override
  public Symbol documentShape(DocumentShape s) {
    return builtin("global.NSmithy.Core.Document");
  }

  @Override
  public Symbol enumShape(EnumShape s) {
    return declared(s);
  }

  @Override
  public Symbol intEnumShape(IntEnumShape s) {
    return declared(s);
  }

  @Override
  public Symbol structureShape(StructureShape s) {
    if (ShapeSupport.isUnit(s.getId())) {
      return builtin("global.NSmithy.Core.SmithyUnit");
    }
    // The server runtime throws this error itself, so a model that declares it binds to the
    // runtime's type.
    if (ShapeSupport.isValidationException(s.getId())) {
      return Symbol.builder()
          .name("ValidationException")
          .namespace("NSmithy.Core.Validation", ".")
          .build();
    }
    return declared(s);
  }

  @Override
  public Symbol unionShape(UnionShape s) {
    return declared(s);
  }

  @Override
  public Symbol listShape(ListShape s) {
    return builtin(element(s.getMember(), ShapeSupport.isSparse(s)) + " list");
  }

  /** A map key is always a string: the key's target shape is kept in the schema only. */
  @Override
  public Symbol mapShape(MapShape s) {
    return builtin(
        "global.Microsoft.FSharp.Collections.Map<string, "
            + element(s.getValue(), ShapeSupport.isSparse(s))
            + ">");
  }

  @Override
  public Symbol memberShape(MemberShape s) {
    return toSymbol(model.expectShape(s.getTarget()));
  }

  @Override
  public Symbol operationShape(OperationShape s) {
    return declared(s);
  }

  @Override
  public Symbol resourceShape(ResourceShape s) {
    return declared(s);
  }

  @Override
  public Symbol serviceShape(ServiceShape s) {
    return declared(s);
  }

  /** The schema class of a shape: {@code Example.Weather.Schemas.CitySchema}. */
  public Symbol schemaSymbol(Shape shape) {
    if (ShapeSupport.isValidationException(shape.getId())) {
      return Symbol.builder()
          .name("ValidationExceptionSchema")
          .namespace("NSmithy.Core.Validation", ".")
          .build();
    }
    return Symbol.builder()
        .name(FSharpNaming.typeName(shape.getId().getName()) + "Schema")
        .namespace(schemaNamespace(shape.getId().getNamespace()), ".")
        .build();
  }

  /** The F# namespace of a Smithy namespace's schemas. */
  public String schemaNamespace(String smithyNamespace) {
    return settings.fsharpNamespace(smithyNamespace) + "." + SCHEMAS_NAMESPACE;
  }

  private String element(MemberShape member, boolean sparse) {
    String type = typeOf(model.expectShape(member.getTarget()));
    return sparse ? type + " option" : type;
  }

  private Symbol declared(Shape shape) {
    String name = FSharpNaming.typeName(shape.getId().getName());
    if (name.equals(SCHEMAS_NAMESPACE)) {
      throw new CodegenException(
          "Shape "
              + shape.getId()
              + " would generate an F# type named Schemas, which clashes with the "
              + settings.fsharpNamespace(shape.getId().getNamespace())
              + ".Schemas namespace that holds generated schemas. Rename the shape, for example"
              + " with the renameShapes build transform.");
    }
    return Symbol.builder()
        .name(name)
        .namespace(settings.fsharpNamespace(shape.getId().getNamespace()), ".")
        .build();
  }

  private static Symbol builtin(String type) {
    return Symbol.builder().name(type).namespace("", ".").build();
  }
}
