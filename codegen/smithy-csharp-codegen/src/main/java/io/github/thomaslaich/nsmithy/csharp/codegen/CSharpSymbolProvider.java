/*
 * Maps Smithy shapes to C# Symbols. Each generated shape lives in a per-shape
 * namespace = settings.csharpNamespace(shape.id.namespace). Primitives use an
 * empty namespace so the writer renders them as bare keywords (string, int,
 * etc.) without emitting noisy `using` directives.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import java.util.List;
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
public final class CSharpSymbolProvider implements SymbolProvider, ShapeVisitor<Symbol> {

  private final Model model;
  private final CSharpSettings settings;

  public CSharpSymbolProvider(Model model, CSharpSettings settings) {
    this.model = model;
    this.settings = settings;
  }

  @Override
  public Symbol toSymbol(Shape shape) {
    return shape.accept(this);
  }

  @Override
  public String toMemberName(MemberShape shape) {
    return CSharpNaming.propertyName(shape.getMemberName());
  }

  @Override
  public Symbol blobShape(BlobShape s) {
    return primitive("", "byte[]", false);
  }

  @Override
  public Symbol booleanShape(BooleanShape s) {
    return primitive("", "bool", true);
  }

  @Override
  public Symbol byteShape(ByteShape s) {
    return primitive("", "sbyte", true);
  }

  @Override
  public Symbol shortShape(ShortShape s) {
    return primitive("", "short", true);
  }

  @Override
  public Symbol integerShape(IntegerShape s) {
    return primitive("", "int", true);
  }

  @Override
  public Symbol longShape(LongShape s) {
    return primitive("", "long", true);
  }

  @Override
  public Symbol floatShape(FloatShape s) {
    return primitive("", "float", true);
  }

  @Override
  public Symbol doubleShape(DoubleShape s) {
    return primitive("", "double", true);
  }

  @Override
  public Symbol bigIntegerShape(BigIntegerShape s) {
    return RuntimeTypes.BIG_INTEGER.toBuilder()
        .putProperty(SymbolProperties.IS_VALUE_TYPE, true)
        .build();
  }

  @Override
  public Symbol bigDecimalShape(BigDecimalShape s) {
    return primitive("", "decimal", true);
  }

  @Override
  public Symbol stringShape(StringShape s) {
    return primitive("", "string", false);
  }

  @Override
  public Symbol timestampShape(TimestampShape s) {
    return RuntimeTypes.DATE_TIME_OFFSET.toBuilder()
        .putProperty(SymbolProperties.IS_VALUE_TYPE, true)
        .build();
  }

  @Override
  public Symbol documentShape(DocumentShape s) {
    return RuntimeTypes.DOCUMENT.toBuilder()
        .putProperty(SymbolProperties.IS_VALUE_TYPE, false)
        .build();
  }

  @Override
  public Symbol enumShape(EnumShape s) {
    return generated(s, CSharpNaming.typeName(s.getId().getName()));
  }

  @Override
  public Symbol intEnumShape(IntEnumShape s) {
    return generated(s, CSharpNaming.typeName(s.getId().getName()));
  }

  @Override
  public Symbol structureShape(StructureShape s) {
    // smithy.api#Unit maps to the runtime SmithyUnit value type, not a generated record,
    // so union members and other references agree with Schemas.Unit
    // (Schema<SmithyUnit>).
    if (ShapeSupport.isUnit(s.getId())) {
      return RuntimeTypes.SMITHY_UNIT.toBuilder()
          .putProperty(SymbolProperties.IS_VALUE_TYPE, true)
          .build();
    }
    // The server runtime throws this one itself, so a model that declares it must bind to the
    // runtime's type — a generated copy would be a different type the runtime never throws.
    if (ShapeSupport.isValidationException(s.getId())) {
      return RuntimeTypes.VALIDATION_EXCEPTION.toBuilder()
          .putProperty(SymbolProperties.IS_VALUE_TYPE, false)
          .build();
    }
    return generated(s, CSharpNaming.typeName(s.getId().getName()));
  }

  @Override
  public Symbol unionShape(UnionShape s) {
    return generated(s, CSharpNaming.typeName(s.getId().getName()));
  }

  @Override
  public Symbol serviceShape(ServiceShape s) {
    // The ".cs" file generated for a service contains both the I*Client interface
    // and the *Client/IServiceHandler types; we file it under the type-name.
    return generated(s, CSharpNaming.typeName(s.getId().getName()));
  }

  @Override
  public Symbol listShape(ListShape s) {
    Symbol element = typeArgument(s.getMember(), ShapeSupport.isSparse(s));
    return constructed(RuntimeTypes.I_READ_ONLY_LIST, element);
  }

  /**
   * A map key is always a {@code string}, even when it targets an enum shape: a map key is a JSON
   * object name, which has no other form. The schema still carries the key's target shape.
   */
  @Override
  public Symbol mapShape(MapShape s) {
    Symbol value = typeArgument(s.getValue(), ShapeSupport.isSparse(s));
    return constructed(RuntimeTypes.I_READ_ONLY_DICTIONARY, primitive("", "string", false), value);
  }

  @Override
  public Symbol memberShape(MemberShape s) {
    return toSymbol(model.expectShape(s.getTarget()));
  }

  @Override
  public Symbol operationShape(OperationShape s) {
    return generated(s, CSharpNaming.typeName(s.getId().getName()));
  }

  @Override
  public Symbol resourceShape(ResourceShape s) {
    return generated(s, CSharpNaming.typeName(s.getId().getName()));
  }

  private Symbol primitive(String namespace, String name, boolean isValueType) {
    return Symbol.builder()
        .name(name)
        .namespace(namespace, ".")
        .putProperty(SymbolProperties.IS_VALUE_TYPE, isValueType)
        .build();
  }

  /**
   * The symbol a shape declares in its own namespace and file, even when its C# type is not a
   * generated declaration: a list's or map's schema class is still named and filed after the shape.
   */
  public static Symbol declaredSymbol(CSharpSettings settings, Shape shape) {
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    String csNamespace = settings.csharpNamespace(shape.getId().getNamespace());
    String file = csNamespace.replace('.', '/') + "/" + typeName + ".g.cs";
    return Symbol.builder()
        .name(typeName)
        .namespace(csNamespace, ".")
        .definitionFile(file)
        .putProperty(SymbolProperties.IS_VALUE_TYPE, false)
        .build();
  }

  private Symbol typeArgument(MemberShape member, boolean nullable) {
    Symbol target = toSymbol(model.expectShape(member.getTarget()));
    return nullable
        ? target.toBuilder().putProperty(SymbolProperties.IS_NULLABLE, true).build()
        : target;
  }

  private static Symbol constructed(Symbol generic, Symbol... arguments) {
    return generic.toBuilder()
        .putProperty(SymbolProperties.TYPE_ARGUMENTS, List.of(arguments))
        .putProperty(SymbolProperties.IS_VALUE_TYPE, false)
        .build();
  }

  /** Generated user-defined shape: lives in the per-shape C# namespace, has its own .cs file. */
  private Symbol generated(Shape shape, String typeName) {
    return declaredSymbol(settings, shape).toBuilder().name(typeName).build();
  }
}
