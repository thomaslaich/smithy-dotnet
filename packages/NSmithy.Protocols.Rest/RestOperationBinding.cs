using System.Net.Http;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

public readonly record struct QueryMemberBinding(IMemberSchema Member, string QueryName);

public readonly record struct HeaderMemberBinding(IMemberSchema Member, string HeaderName);

public readonly record struct PrefixHeaderMemberBinding(IMemberSchema Member, string Prefix);

public sealed class RestOperationBinding<TInput, TOutput>
{
    private RestOperationBinding(
        HttpMethod httpMethod,
        string uriTemplate,
        IStructSchema<TInput> inputSchema,
        IReadOnlyList<IMemberSchema> labelMembers,
        IReadOnlyList<QueryMemberBinding> queryMembers,
        IMemberSchema? queryParamsMember,
        IReadOnlyList<HeaderMemberBinding> requestHeaderMembers,
        PrefixHeaderMemberBinding? requestPrefixHeadersMember,
        IMemberSchema<TInput>? inputPayloadMember,
        StructProjection<TInput>? inputBodyProjection,
        HashSet<string> boundQueryNames,
        IStructSchema<TOutput> outputSchema,
        IMemberSchema? responseCodeMember,
        IReadOnlyList<HeaderMemberBinding> responseHeaderMembers,
        PrefixHeaderMemberBinding? responsePrefixHeadersMember,
        IMemberSchema<TOutput>? outputPayloadMember,
        StructProjection<TOutput>? outputBodyProjection
    )
    {
        HttpMethod = httpMethod;
        UriTemplate = uriTemplate;
        InputSchema = inputSchema;
        LabelMembers = labelMembers;
        QueryMembers = queryMembers;
        QueryParamsMember = queryParamsMember;
        RequestHeaderMembers = requestHeaderMembers;
        RequestPrefixHeadersMember = requestPrefixHeadersMember;
        InputPayloadMember = inputPayloadMember;
        InputBodyProjection = inputBodyProjection;
        BoundQueryNames = boundQueryNames;
        OutputSchema = outputSchema;
        ResponseCodeMember = responseCodeMember;
        ResponseHeaderMembers = responseHeaderMembers;
        ResponsePrefixHeadersMember = responsePrefixHeadersMember;
        OutputPayloadMember = outputPayloadMember;
        OutputBodyProjection = outputBodyProjection;
    }

    public HttpMethod HttpMethod { get; }
    public string UriTemplate { get; }

    // Input
    public IStructSchema<TInput> InputSchema { get; }
    public IReadOnlyList<IMemberSchema> LabelMembers { get; }
    public IReadOnlyList<QueryMemberBinding> QueryMembers { get; }
    public IMemberSchema? QueryParamsMember { get; }
    public IReadOnlyList<HeaderMemberBinding> RequestHeaderMembers { get; }
    public PrefixHeaderMemberBinding? RequestPrefixHeadersMember { get; }
    public IMemberSchema<TInput>? InputPayloadMember { get; }
    public StructProjection<TInput>? InputBodyProjection { get; }
    public HashSet<string> BoundQueryNames { get; }

    // Output
    public IStructSchema<TOutput> OutputSchema { get; }
    public IMemberSchema? ResponseCodeMember { get; }
    public IReadOnlyList<HeaderMemberBinding> ResponseHeaderMembers { get; }
    public PrefixHeaderMemberBinding? ResponsePrefixHeadersMember { get; }
    public IMemberSchema<TOutput>? OutputPayloadMember { get; }
    public StructProjection<TOutput>? OutputBodyProjection { get; }

    internal static RestOperationBinding<TInput, TOutput> CreateFrom(
        OperationSchema<TInput, TOutput> operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        var httpTrait =
            operation.GetTrait(RestTraits.Http)
            ?? throw new InvalidOperationException(
                $"Operation '{operation.Id}' is missing the @http trait."
            );
        var http = httpTrait.Value.AsObject();
        var httpMethod = ResolveHttpMethod(http["method"].AsString());
        var uriTemplate = http["uri"].AsString();

        var inputSchema =
            operation.Input.Resolved as IStructSchema<TInput>
            ?? throw new InvalidOperationException(
                $"Operation '{operation.Id}' input must be a structure schema."
            );
        var outputSchema =
            operation.Output.Resolved as IStructSchema<TOutput>
            ?? throw new InvalidOperationException(
                $"Operation '{operation.Id}' output must be a structure schema."
            );

        // Partition input members in a single pass
        var labelMembers = new List<IMemberSchema>();
        var queryMembers = new List<QueryMemberBinding>();
        IMemberSchema? queryParamsMember = null;
        var requestHeaderMembers = new List<HeaderMemberBinding>();
        PrefixHeaderMemberBinding? requestPrefixHeadersMember = null;
        IMemberSchema<TInput>? inputPayloadMember = null;
        var inputBodyMembers = new List<IMemberSchema<TInput>>();
        var boundQueryNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in inputSchema.TypedMembers)
        {
            if (member.Traits.ContainsKey(RestTraits.HttpLabel))
            {
                labelMembers.Add(member);
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpQuery, out var queryTrait))
            {
                var name = queryTrait.Value.AsString();
                queryMembers.Add(new QueryMemberBinding(member, name));
                boundQueryNames.Add(name);
            }
            else if (member.Traits.ContainsKey(RestTraits.HttpQueryParams))
            {
                queryParamsMember = member;
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                requestHeaderMembers.Add(
                    new HeaderMemberBinding(member, headerTrait.Value.AsString())
                );
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait))
            {
                requestPrefixHeadersMember = new PrefixHeaderMemberBinding(
                    member,
                    prefixTrait.Value.AsString()
                );
            }
            else if (member.Traits.ContainsKey(RestTraits.HttpPayload))
            {
                inputPayloadMember = member;
            }
            else
            {
                inputBodyMembers.Add(member);
            }
        }

        StructProjection<TInput>? inputBodyProjection = null;
        if (inputPayloadMember is null && inputBodyMembers.Count > 0)
            inputBodyProjection = Schemas.Project(inputSchema, inputBodyMembers);

        // Partition output members in a single pass
        IMemberSchema? responseCodeMember = null;
        var responseHeaderMembers = new List<HeaderMemberBinding>();
        PrefixHeaderMemberBinding? responsePrefixHeadersMember = null;
        IMemberSchema<TOutput>? outputPayloadMember = null;
        var outputBodyMembers = new List<IMemberSchema<TOutput>>();

        foreach (var member in outputSchema.TypedMembers)
        {
            if (member.Traits.ContainsKey(RestTraits.HttpResponseCode))
            {
                responseCodeMember = member;
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                responseHeaderMembers.Add(
                    new HeaderMemberBinding(member, headerTrait.Value.AsString())
                );
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait))
            {
                responsePrefixHeadersMember = new PrefixHeaderMemberBinding(
                    member,
                    prefixTrait.Value.AsString()
                );
            }
            else if (member.Traits.ContainsKey(RestTraits.HttpPayload))
            {
                outputPayloadMember = member;
            }
            else
            {
                outputBodyMembers.Add(member);
            }
        }

        StructProjection<TOutput>? outputBodyProjection = null;
        if (outputPayloadMember is null && outputBodyMembers.Count > 0)
            outputBodyProjection = Schemas.Project(outputSchema, outputBodyMembers);

        return new RestOperationBinding<TInput, TOutput>(
            httpMethod,
            uriTemplate,
            inputSchema,
            labelMembers,
            queryMembers,
            queryParamsMember,
            requestHeaderMembers,
            requestPrefixHeadersMember,
            inputPayloadMember,
            inputBodyProjection,
            boundQueryNames,
            outputSchema,
            responseCodeMember,
            responseHeaderMembers,
            responsePrefixHeadersMember,
            outputPayloadMember,
            outputBodyProjection
        );
    }

    private static HttpMethod ResolveHttpMethod(string method) =>
        method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            "HEAD" => HttpMethod.Head,
            "OPTIONS" => HttpMethod.Options,
            _ => new HttpMethod(method),
        };
}

public static class RestOperationBinding
{
    /// <summary>
    /// Builds the binding for an operation. Callers are expected to build this once per operation
    /// (the generated protocols hold one per operation in a static field), so this no longer
    /// memoizes — the caching now lives in the operation-bound protocol instance.
    /// </summary>
    public static RestOperationBinding<TInput, TOutput> From<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    ) => RestOperationBinding<TInput, TOutput>.CreateFrom(operation);
}
