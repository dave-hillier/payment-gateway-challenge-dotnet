# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Assessment Overview

This is a Checkout.com Payment Gateway technical assessment. The goal is to build a payment gateway API that:
1. Processes card payments through a simulated acquiring bank
2. Retrieves previously made payment details
3. Returns appropriate responses (Authorized, Declined, or Rejected)

## Essential Commands

### Build and Run
```bash
# Build the solution
dotnet build

# Run the API
dotnet run --project src/PaymentGateway.Api

# Run tests
dotnet test

# Run a specific test
dotnet test --filter "FullyQualifiedName~PaymentGateway.Api.Tests.TestClassName.TestMethodName"

# Start bank simulator (required for integration)
docker-compose up
```

### Bank Simulator
- Runs on http://localhost:8080/payments
- Card ending in odd number (1,3,5,7,9): Returns authorized
- Card ending in even number (2,4,6,8): Returns declined
- Card ending in 0: Returns 503 error
- Request format: `{"card_number": "2222405343248877", "expiry_date": "04/2025", "currency": "GBP", "amount": 100, "cvv": "123"}`

## Architecture Overview

### Project Structure
- **PaymentGateway.Api**: ASP.NET Core 8.0 Web API
  - Controllers: REST endpoints for payment operations
  - Models: Request/Response DTOs and domain models
  - Services: Business logic and repository layer
  - Enums: PaymentStatus (Authorized, Declined, Rejected)

- **PaymentGateway.Api.Tests**: xUnit test project with AspNetCore.Mvc.Testing for integration tests

### Key Implementation Requirements

#### Payment Processing Flow
1. Validate incoming payment request (card number, expiry, CVV, currency, amount)
2. If invalid, return Rejected status without calling bank
3. If valid, call bank simulator API
4. Store payment details with masked card number (last 4 digits only)
5. Return payment response with status and payment ID

#### Validation Rules
- **Card Number**: 14-19 digits, numeric only
- **Expiry**: Month 1-12, Year must be future (combined expiry must be future)
- **CVV**: 3-4 digits, numeric only
- **Currency**: 3 character ISO code (implement for at least USD, GBP, EUR)
- **Amount**: Integer representing minor currency units (e.g., 100 = $1.00)

#### API Endpoints
- `POST /api/payments`: Process a new payment
- `GET /api/payments/{id}`: Retrieve payment details by ID

### Current Implementation Status
- Basic project structure exists with skeleton controller
- PaymentsRepository in-memory storage implemented
- Models defined but missing full card number field in PostPaymentRequest
- PaymentStatus enum includes all required statuses
- No validation or bank integration yet implemented

## Important Notes
- The PaymentsRepository is an in-memory test double - no real database required
- Never return full card numbers in responses - only last 4 digits
- The namespace for PaymentStatus enum is incorrect (should be PaymentGateway.Api.Enums)
- Current PostPaymentRequest model has CardNumberLastFour but needs full CardNumber for processing