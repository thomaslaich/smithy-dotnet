using System.Net.Http;
using NSmithy.Core;
using NSmithy.Core.Functional;

namespace NSmithy.Protocols.Rest;

public readonly record struct QueryMemberBinding(IFunctionalMemberSchema Member, string QueryName);

public readonly record struct HeaderMemberBinding(
    IFunctionalMemberSchema Member,
    string HeaderName
);

public readonly record struct PrefixHeaderMemberBinding(
    IFunctionalMemberSchema Member,
    string Prefix
);

public sealed class RestOperationBinding<TInput, TOutput>
{
    private RestOperationBinding(
        HttpMethod httpMethod,
        string uriTemplate,
        IFunctionalStructSchema<TInput> inputSchema,
        IReadOnlyList<IFunctionalMemberSchema> labelMembers,
        IReadOnlyList<QueryMemberBinding> queryMembers,
        IFunctionalMemberSchema? queryParamsMember,
        IReadOnlyList<HeaderMemberBinding> requestHeaderMembers,
        PrefixHeaderMemberBinding? requestPrefixHeadersMember,
        IFunctionalMemberSchema<TInput>? inputPayloadMember,
        FunctionalStructProjection<TInput>? inputBodyProjection,
        HashSet<string> boundQueryNames,
        IFunctionalStructSchema<TOutput> outputSchema,
        IFunctionalMemberSchema? responseCodeMember,
        IReadOnlyList<HeaderMemberBinding> responseHeaderMembers,
        PrefixHeaderMemberBinding? responsePrefixHeadersMember,
        IFunctionalMemberSchema<TOutput>? outputPayloadMember,
        FunctionalStructProjection<TOutput>? outputBodyProjection
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
    public IFunctionalStructSchema<TInput> InputSchema { get; }
    public IReadOnlyList<IFunctionalMemberSchema> LabelMembers { get; }
    public IReadOnlyList<QueryMemberBinding> QueryMembers { get; }
    public IFunctionalMemberSchema? QueryParamsMember { get; }
    public IReadOnlyList<HeaderMemberBinding> RequestHeaderMembers { get; }
    public PrefixHeaderMemberBinding? RequestPrefixHeadersMember { get; }
    public IFunctionalMemberSchema<TInput>? InputPayloadMember { get; }
    public FunctionalStructProjection<TInput>? InputBodyProjection { get; }
    public HashSet<string> BoundQueryNames { get; }

    // Output
    public IFunctionalStructSchema<TOutput> OutputSchema { get; }
    public IFunctionalMemberSchema? ResponseCodeMember { get; }
    public IReadOnlyList<HeaderMemberBinding> ResponseHeaderMembers { get; }
    public PrefixHeaderMemberBinding? ResponsePrefixHeadersMember { get; }
    public IFunctionalMemberSchema<TOutput>? OutputPayloadMember { get; }
    public FunctionalStructProjection<TOutput>? OutputBodyProjection { get; }

    internal static RestOperationBinding<TInput, TOutput> CreateFrom(
        FunctionalOperationSchema<TInput, TOutput> operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        var httpTrait =
            operation.GetTrait(FunctionalRestTraits.Http)
            ?? throw new InvalidOperationException(
                $"Operation '{operation.Id}' is missing the @http trait."
            );
        var http = httpTrait.Value.AsObject();
        var httpMethod = ResolveHttpMethod(http["method"].AsString());
        var uriTemplate = http["uri"].AsString();

        var inputSchema =
            operation.Input.Resolved as IFunctionalStructSchema<TInput>
            ?? throw new InvalidOperationException(
                $"Operation '{operation.Id}' input must be a structure schema."
            );
        var outputSchema =
            operation.Output.Resolved as IFunctionalStructSchema<TOutput>
            ?? throw new InvalidOperationException(
                $"Operation '{operation.Id}' output must be a structure schema."
            );

        // Partition input members in a single pass
        var labelMembers = new List<IFunctionalMemberSchema>();
        var queryMembers = new List<QueryMemberBinding>();
        IFunctionalMemberSchema? queryParamsMember = null;
        var requestHeaderMembers = new List<HeaderMemberBinding>();
        PrefixHeaderMemberBinding? requestPrefixHeadersMember = null;
        IFunctionalMemberSchema<TInput>? inputPayloadMember = null;
        var inputBodyMembers = new List<IFunctionalMemberSchema<TInput>>();
        var boundQueryNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in inputSchema.TypedMembers)
        {
            if (member.Traits.ContainsKey(FunctionalRestTraits.HttpLabel))
            {
                labelMembers.Add(member);
            }
            else if (member.Traits.TryGetValue(FunctionalRestTraits.HttpQuery, out var queryTrait))
            {
                var name = queryTrait.Value.AsString();
                queryMembers.Add(new QueryMemberBinding(member, name));
                boundQueryNames.Add(name);
            }
            else if (member.Traits.ContainsKey(FunctionalRestTraits.HttpQueryParams))
            {
                queryParamsMember = member;
            }
            else if (
                member.Traits.TryGetValue(FunctionalRestTraits.HttpHeader, out var headerTrait)
            )
            {
                requestHeaderMembers.Add(
                    new HeaderMemberBinding(member, headerTrait.Value.AsString())
                );
            }
            else if (
                member.Traits.TryGetValue(
                    FunctionalRestTraits.HttpPrefixHeaders,
                    out var prefixTrait
                )
            )
            {
                requestPrefixHeadersMember = new PrefixHeaderMemberBinding(
                    member,
                    prefixTrait.Value.AsString()
                );
            }
            else if (member.Traits.ContainsKey(FunctionalRestTraits.HttpPayload))
            {
                inputPayloadMember = member;
            }
            else
            {
                inputBodyMembers.Add(member);
            }
        }

        FunctionalStructProjection<TInput>? inputBodyProjection = null;
        if (inputPayloadMember is null && inputBodyMembers.Count > 0)
            inputBodyProjection = FunctionalSchemas.Project(inputSchema, inputBodyMembers);

        // Partition output members in a single pass
        IFunctionalMemberSchema? responseCodeMember = null;
        var responseHeaderMembers = new List<HeaderMemberBinding>();
        PrefixHeaderMemberBinding? responsePrefixHeadersMember = null;
        IFunctionalMemberSchema<TOutput>? outputPayloadMember = null;
        var outputBodyMembers = new List<IFunctionalMemberSchema<TOutput>>();

        foreach (var member in outputSchema.TypedMembers)
        {
            if (member.Traits.ContainsKey(FunctionalRestTraits.HttpResponseCode))
            {
                responseCodeMember = member;
            }
            else if (
                member.Traits.TryGetValue(FunctionalRestTraits.HttpHeader, out var headerTrait)
            )
            {
                responseHeaderMembers.Add(
                    new HeaderMemberBinding(member, headerTrait.Value.AsString())
                );
            }
            else if (
                member.Traits.TryGetValue(
                    FunctionalRestTraits.HttpPrefixHeaders,
                    out var prefixTrait
                )
            )
            {
                responsePrefixHeadersMember = new PrefixHeaderMemberBinding(
                    member,
                    prefixTrait.Value.AsString()
                );
            }
            else if (member.Traits.ContainsKey(FunctionalRestTraits.HttpPayload))
            {
                outputPayloadMember = member;
            }
            else
            {
                outputBodyMembers.Add(member);
            }
        }

        FunctionalStructProjection<TOutput>? outputBodyProjection = null;
        if (outputPayloadMember is null && outputBodyMembers.Count > 0)
            outputBodyProjection = FunctionalSchemas.Project(outputSchema, outputBodyMembers);

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
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        object,
        object
    > Cache = new();

    public static RestOperationBinding<TInput, TOutput> From<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation
    )
    {
        if (Cache.TryGetValue(operation, out var cached))
            return (RestOperationBinding<TInput, TOutput>)cached;
        var binding = RestOperationBinding<TInput, TOutput>.CreateFrom(operation);
        Cache.AddOrUpdate(operation, binding);
        return binding;
    }
}
