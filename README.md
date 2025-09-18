# Instructions for candidates

This is the .NET version of the Payment Gateway challenge. If you haven't already read this [README.md](https://github.com/cko-recruitment/) on the details of this exercise, please do so now. 

## Template structure
```
src/
    PaymentGateway.Api - a skeleton ASP.NET Core Web API
test/
    PaymentGateway.Api.Tests - an empty xUnit test project
imposters/ - contains the bank simulator configuration. Don't change this

.editorconfig - don't change this. It ensures a consistent set of rules for submissions when reformatting code
docker-compose.yml - configures the bank simulator
PaymentGateway.sln
```

Feel free to change the structure of the solution, use a different test library etc.

## Implementation Overview

This repository contains three different architectural approaches to implementing the Checkout.com Payment Gateway challenge:

### Main Branch - Challenge Implementation

This branch fulfills all requirements specified in the [Checkout.com assessment](docs/assessment.md) with a focus on honoring the "don't over-engineer" guidance while being cautious not to under-engineer. The implementation follows the approach laid out in the provided template repository while correcting any divergences from the specification. I followed the approach laid out for respository structure and tests.

**Key Design Decisions:**

- **Engineering Balance**: Avoided over-engineering per brief, but  real-world payment gateways always have an idempotency key. I took the same header `Cko-Idempotency-Key`. To not include this I would consider it under-engineering.
- **Testing Strategy**: Developed comprehensive test suite to leverage in future iterations - unit tests focus on details that may change/be discarded, while higher-level tests provide stability

**Implementation Evolution/Commit Summary:**
Built incrementally through focused commits:

- POST endpoint with initial tests
- E2E test script
- idempotency middleware
- card validation with unit tests
- HTTP client for bank integration
- enhanced integration testing
- comprehensive E2E test framework
- additional unit test coverage

### EF Core Persistence Implementation

[**View Branch**](https://github.com/dave-hillier/payment-gateway-challenge-dotnet/tree/ef-core-persistence)

For a payment gateway, reliable payment delivery is key. To achieve this, I decided to implement the [outbox pattern](https://en.wikipedia.org/wiki/Inbox_and_outbox_pattern). The simulator does not have idempotency, therefore I'm assuming that reconcilliation is necessary later on to eliminate any duplicates that may happen.

This branch leverages Redis to signal processing completion of the outbox. It could somewhat be considered over-engineering but offers more real-time completion of payments than polling the DB, which in my experience can have an impact on completion rates.

For transactions, I used SQLite in memory rather than just the vanilla in memory as it does not support them.

### Orleans Actor Model Implementation

[**View Branch**](https://github.com/dave-hillier/payment-gateway-challenge-dotnet/tree/orleans-migration)

The EF Core Approach had become quite complex. I decided to experiement with an alternative approach using Orlean's actor framework. It resulted in ~1000 fewer lines of code than the EF approach whilst maintaining functional parity with the other approaches.

Idempotency comes naturally with the use of grains and adding multiple acquirers becomes trivial.