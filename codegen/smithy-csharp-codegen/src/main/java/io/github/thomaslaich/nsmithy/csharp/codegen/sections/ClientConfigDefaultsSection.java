/*
 * Code sections generators expose to integrations' code interceptors.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.sections;

import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.CodeSection;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * The body of the parameterless {@code {Service}ClientConfig} constructor, where statements set the
 * configuration's defaults, such as {@code Interceptors.Add(...)}. Empty unless an integration
 * appends to it.
 *
 * @param service the service the client is generated for
 */
@SmithyUnstableApi
public record ClientConfigDefaultsSection(ServiceShape service) implements CodeSection {}
