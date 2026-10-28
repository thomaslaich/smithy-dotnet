package io.github.thomaslaich.nsmithy.codegen.core;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ShapeId;

final class JsonSchemasTest {

  @Test
  void generatesCanonicalDocumentSchema() {
    Model model =
        Model.assembler()
            .addUnparsedModel(
                "weather.smithy",
                """
                $version: "2"
                namespace example.weather

                structure LookupInput {
                    @required
                    @jsonName("place_name")
                    @documentation("Place to look up.")
                    @length(min: 2, max: 80)
                    place: String

                    count: Integer
                    payload: Blob
                    observedAt: Timestamp
                    choice: Choice
                    otherChoice: unrelated#Choice
                }

                union Choice {
                    text: String
                    count: Integer
                }
                """)
            .addUnparsedModel(
                "unrelated.smithy",
                """
                $version: "2"
                namespace unrelated

                structure Choice {
                    ignored: String
                }
                """)
            .assemble()
            .unwrap();

    ObjectNode schema =
        Node.parse(JsonSchemas.generate(model, ShapeId.from("example.weather#LookupInput")))
            .expectObjectNode();

    assertEquals(
        "https://json-schema.org/draft/2020-12/schema",
        schema.expectStringMember("$schema").getValue());
    assertEquals("object", schema.expectStringMember("type").getValue());
    assertFalse(schema.expectBooleanMember("additionalProperties").getValue());

    ObjectNode properties = schema.expectObjectMember("properties");
    ObjectNode place = properties.expectObjectMember("place_name");
    assertEquals("string", place.expectStringMember("type").getValue());
    assertEquals("Place to look up.", place.expectStringMember("description").getValue());
    assertEquals(2, place.expectNumberMember("minLength").getValue().intValue());
    assertEquals(80, place.expectNumberMember("maxLength").getValue().intValue());

    ObjectNode count = properties.expectObjectMember("count");
    assertEquals("integer", count.expectStringMember("type").getValue());
    assertEquals(Integer.MIN_VALUE, count.expectNumberMember("minimum").getValue().intValue());
    assertEquals(Integer.MAX_VALUE, count.expectNumberMember("maximum").getValue().intValue());
    assertEquals(
        "base64",
        properties.expectObjectMember("payload").expectStringMember("contentEncoding").getValue());
    assertEquals(
        "number",
        properties.expectObjectMember("observedAt").expectStringMember("type").getValue());
    assertTrue(schema.containsMember("$defs"));
    assertEquals(
        "place_name",
        schema.expectArrayMember("required").getElements().get(0).expectStringNode().getValue());
  }

  @Test
  void aMemberNamedTypeIsAProperty() {
    Model model =
        Model.assembler()
            .addUnparsedModel(
                "kitchen.smithy",
                """
                $version: "2"
                namespace example.kitchen

                structure Dish {
                    type: String
                    properties: Integer
                }
                """)
            .assemble()
            .unwrap();

    ObjectNode schema =
        Node.parse(JsonSchemas.generate(model, ShapeId.from("example.kitchen#Dish")))
            .expectObjectNode();

    ObjectNode properties = schema.expectObjectMember("properties");
    assertEquals(
        "string", properties.expectObjectMember("type").expectStringMember("type").getValue());
    assertEquals(
        "integer",
        properties.expectObjectMember("properties").expectStringMember("type").getValue());
    assertFalse(properties.containsMember("additionalProperties"));
    assertFalse(schema.expectBooleanMember("additionalProperties").getValue());
  }
}
