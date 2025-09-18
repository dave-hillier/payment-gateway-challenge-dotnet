# Architectural Decision Log

## TODOs

- [x] Make the repo match the assessment specification.
- [x] Consider how the assessment diverges from the real.
- [x] Add e2e tests using curl: Success, bank decline, validation failure, duplicate & bank failure.
- [x] Implement idempotency key header
- [x] Card validation
- [ ] Bank integration with Authorized, declined, rejected & bad gateway
- [ ] Status updated by bank response
- [ ] Retries and circuit breakers/outbox?


The challenge calls for "not overengineering" - to me I apply [Beck Design Rules](https://martinfowler.com/bliki/BeckDesignRules.html). However, I also do not want to under-engineer the system and therefore I am considering it a challenge to give "at least once" delivery guarantees.

## Notes

- I notice that it says not to change the simulator. I will re-evaluate if that's a hard requirement later.
- use strings for CardNumberLastFour and CVV to preserve leading zeros and it's common convention including checkout.com's own APIs
- I prefer a classic approach to TDD over mockist. Socialable tests are fine.

## Links

- Checkout.com [API reference](https://api-reference.checkout.com/#tag/Payments)
- Checkout.com [Payment](https://www.checkout.com/docs/payments/accept-payments/accept-a-payment-using-the-payments-api)
- [Idempotency](https://www.checkout.com/docs/developer-resources/api/idempotency)

## Comparison to real API

- Accepts tokens
- 3DS flow has pending
- Uses idempotency key
- Capture, refund and void
- Loads more fields
