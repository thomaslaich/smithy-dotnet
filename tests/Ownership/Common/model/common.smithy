$version: "2"

namespace example.shop

/// An amount of money in a currency.
structure Money {
    @required
    @range(min: 1)
    amount: Integer

    @required
    @length(min: 3, max: 3)
    currency: String
}

/// No order has the given id.
@error("client")
@httpError(404)
structure OrderNotFound {
    @required
    message: String
}
