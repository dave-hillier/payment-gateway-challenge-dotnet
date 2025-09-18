# Architectural Decision Log

## Initial approach/steps

1. Make the repo match the assessment specification.
2. Consider how the assessment diverges from the real.
3. Add e2e tests using curl: Success, bank decline, validation failure, duplicate & bank failure.

The challenge calls for "not overengineering" - to me I apply [Beck Design Rules](https://martinfowler.com/bliki/BeckDesignRules.html). However, I also do not want to under-engineer the system and therefore I am considering it a challenge to give "at least once" delivery guarantees.

## Notes

- I notice that it says not to change the simulator. I will re-evaluate if that's a hard requirement later.
- use strings for CardNumberLastFour and CVV to preserve leading zeros and it's common convention including checkout.com's own APIs
