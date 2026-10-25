/*
 * A rules engine library function implemented by a static C# method.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.endpoints;

import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * A rules engine library function that generated resolvers call as {@code Owner.Method(args)}.
 *
 * @param id the rules function id, such as {@code parseURL} or {@code aws.partition}.
 * @param owner the static class that implements it.
 * @param method the method name.
 * @param returnType the C# shape of the result.
 * @param nullable whether the method returns null for an empty rules value.
 */
@SmithyUnstableApi
public record EndpointFunction(
    String id, Symbol owner, String method, EndpointValueType returnType, boolean nullable) {}
