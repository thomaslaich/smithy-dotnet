/*
 * Auth scheme properties a resolved endpoint carries when its rules set no auth schemes.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.endpoints;

import java.util.Map;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * The properties of a modeled auth scheme on endpoints whose rules set no {@code authSchemes}, such
 * as the SigV4 signing name and region.
 *
 * @param schemeId the auth trait shape id, such as {@code aws.auth#sigv4}.
 * @param properties properties with a fixed string value.
 * @param builtInProperties properties whose value is the rule parameter bound to a built-in, keyed
 *     by property name with the built-in id as value. A property whose built-in the rules do not
 *     declare, or whose parameter is not set, is left out.
 */
@SmithyUnstableApi
public record EndpointDefaultAuthScheme(
    String schemeId, Map<String, String> properties, Map<String, String> builtInProperties) {}
