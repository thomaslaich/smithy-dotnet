/*
 * A rules engine built-in bound to a generated client configuration property.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.endpoints;

import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * A rules engine built-in (such as {@code AWS::Region}) and the generated client configuration
 * property that supplies it.
 *
 * @param id the built-in id.
 * @param property the C# property name on the generated client configuration.
 * @param documentation the property's documentation.
 */
@SmithyUnstableApi
public record EndpointBuiltIn(String id, String property, String documentation) {}
