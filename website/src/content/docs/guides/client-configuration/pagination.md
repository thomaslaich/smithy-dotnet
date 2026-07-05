---
title: Pagination
description: Generated IAsyncEnumerable paginators for @paginated operations.
---

Operations modeled with [`@paginated`](https://smithy.io/2.0/spec/behavior-traits.html#paginated-trait)
generate paginator helpers alongside the normal operation method.

Given a paginated operation:

```smithy
@paginated(inputToken: "nextToken", outputToken: "nextToken", pageSize: "pageSize")
service Weather { ... }

@readonly
@paginated(items: "items")
@http(method: "GET", uri: "/cities")
operation ListCities { ... }
```

the client gains a **pages** paginator that repeats the call while the response
carries a continuation token:

```csharp
await foreach (var page in client.ListCitiesPagesAsync(new ListCitiesInput(PageSize: 3)))
{
    foreach (var city in page.Items.Values)
        Console.WriteLine(city.Name);
}
```

and — because the trait names an `items` list member — an **items** paginator
that flattens the pages:

```csharp
await foreach (var city in client.ListCitiesItemsAsync(new ListCitiesInput(PageSize: 3)))
    Console.WriteLine(city.Name);
```

Every page goes through the normal client lifecycle, so auth, retries, endpoint
resolution, and telemetry behave exactly as they do for a single call — a
paginated listing that hits a transient error retries that page and continues.

Notes:

- The token, page-size, and items members come from the resolved `@paginated`
  trait (service-level defaults merged with the operation's trait).
- Enumeration is lazy: stop iterating and no further requests are sent.
- Cancellation flows through `WithCancellation(token)` or the method's
  `CancellationToken` parameter.
- Operations whose `items` member is a map only generate the pages paginator.
