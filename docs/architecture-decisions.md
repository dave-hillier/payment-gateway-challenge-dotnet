# Architectural Decision Log

## Implementation Status

### Completed Features

- [x] Make the repo match the assessment specification
- [x] Consider how the assessment diverges from the real
- [x] Add comprehensive e2e tests: Success, bank decline, validation failure, duplicate & bank failure scenarios
- [x] Added swagger friendly decorators
- [x] Implement idempotency key header (`Cko-Idempotency-Key`) with middleware
- [x] Comprehensive card validation with Luhn algorithm (AI provided)
- [x] Bank integration with Authorized, declined, rejected & bad gateway responses
- [x] Status updated by bank response (Authorized/Declined based on card number ending)
- [x] Exception handling middleware with "throw high catch low" approach
- [x] Complete API endpoints: POST /api/payments and GET /api/payments/{id}
- [x] Comprehensive test suite with unit tests, integration tests, and E2E tests
- [x] HTTP client service for acquiring bank integration with proper error handling
- [x] In-memory repository for payment storage with card number masking
- [x] Request/response models aligned with assessment specification
- [x] Currency validation for USD, GBP, EUR
- [x] Amount validation in minor currency units
- [x] CVV and expiry date validation
- [x] Proper HTTP status codes and error responses

The challenge calls for "not overengineering" - to me I apply [Beck Design Rules](https://martinfowler.com/bliki/BeckDesignRules.html) rather than Controller -> Service -> Repository approach.

However, I also do not want to under-engineer the system and therefore I am considering it a challenge to give "at least once" delivery guarantees as an extension.

## Key Architectural Decisions

### 1. Idempotency Implementation

**Decision**: Custom middleware with `Cko-Idempotency-Key` header as per real Checkout.com API
**Rationale**:

- Follows Checkout.com's real API patterns
- Prevents duplicate payments from network retries
- Middleware approach keeps controllers clean
- In-memory storage sufficient for assessment scope

### 2. Error Handling Architecture

**Decision**: "Throw high, catch low" with centralized exception middleware
**Rationale**:

- Services throw domain-specific exceptions
- Middleware translates to appropriate HTTP responses
- Consistent error response format across API
- Proper logging and observability

### 3. Testing Strategy

**Decision**: Three-tier testing with separated E2E tests
**Rationale**:

- Unit tests: Fast feedback for business logic
- Integration tests: Service interaction validation
- E2E tests: Full flow validation (marked with Category="E2E")
- Follows classic TDD over mockist approach

### 4. Bank Integration Design

**Decision**: Dedicated HTTP client service with proper error handling
**Rationale**:

- Encapsulates external dependency
- Handles HTTP-specific concerns (timeouts, serialization)
- Proper logging for debugging
- Abstracted behind interface for testability

## Other Implementation Notes

- I notice that it says not to change the simulator. I will re-evaluate if that's a hard requirement later.
- use strings for CardNumberLastFour and CVV to preserve leading zeros and it's common convention including checkout.com's own APIs
- Implemented proper HTTP status codes: 200 (success), 400 (validation errors), 422 (business rule violations), 500 (server errors)

## Additional Links

- [API reference](https://api-reference.checkout.com/#tag/Payments)
- [Payment](https://www.checkout.com/docs/payments/accept-payments/accept-a-payment-using-the-payments-api)
- [Idempotency](https://www.checkout.com/docs/developer-resources/api/idempotency)