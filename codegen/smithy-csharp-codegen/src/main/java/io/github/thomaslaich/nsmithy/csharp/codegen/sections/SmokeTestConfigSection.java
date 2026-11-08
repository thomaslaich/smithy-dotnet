/*
 * Code sections generators expose to integrations' code interceptors.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.sections;

import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.smoketests.traits.SmokeTestCase;
import software.amazon.smithy.utils.CodeSection;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * The statements that apply a smoke test's settings to {@code configured}, a copy of the {@code
 * {Service}ClientConfig} the test runs with, such as {@code configured.Region = "us-west-2";}.
 * Empty unless an integration appends to it, typically by translating the test case's vendor
 * params.
 *
 * @param service the service the client is generated for
 * @param operation the operation the test calls
 * @param testCase the smoke test case
 */
@SmithyUnstableApi
public record SmokeTestConfigSection(
    ServiceShape service, OperationShape operation, SmokeTestCase testCase)
    implements CodeSection {}
