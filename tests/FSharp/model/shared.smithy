$version: "2"

namespace example.shared

structure Money {
    @required
    amount: BigDecimal

    @default("CHF")
    currency: String
}
