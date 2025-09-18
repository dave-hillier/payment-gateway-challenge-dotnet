#!/bin/bash

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
API_URL="https://localhost:7092"
BANK_URL="http://localhost:8080"
TIMEOUT=30

print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

# Function to wait for service to be ready
wait_for_service() {
    local url=$1
    local service_name=$2
    local max_attempts=30
    local attempt=1

    print_status "Waiting for $service_name to be ready at $url..."

    while [ $attempt -le $max_attempts ]; do
        if curl -s -k --max-time 5 "$url" > /dev/null 2>&1; then
            print_success "$service_name is ready!"
            return 0
        fi

        echo -n "."
        sleep 2
        attempt=$((attempt + 1))
    done

    print_error "$service_name failed to start within $((max_attempts * 2)) seconds"
    return 1
}

# Function to run a curl test
run_test() {
    local test_name=$1
    local method=$2
    local endpoint=$3
    local data=$4
    local expected_status=$5
    local description=$6
    local extra_headers=$7

    print_status "Running test: $test_name - $description"

    local response=$(curl -s -k -w "\n%{http_code}" -X "$method" \
        -H "Content-Type: application/json" \
        ${extra_headers:+-H "$extra_headers"} \
        ${data:+-d "$data"} \
        "$API_URL$endpoint")

    local body=$(echo "$response" | head -n -1)
    local status=$(echo "$response" | tail -n 1)

    if [ "$status" = "$expected_status" ]; then
        print_success "✓ Test passed: $test_name (HTTP $status)"
        echo "Response: $body"
    else
        print_error "✗ Test failed: $test_name"
        print_error "Expected status: $expected_status, Got: $status"
        print_error "Response: $body"
        return 1
    fi

    echo ""
    return 0
}

# Function to run idempotency test
run_idempotency_test() {
    local test_name=$1
    local idempotency_key=$2
    local data=$3
    local description=$4

    print_status "Running idempotency test: $test_name - $description"

    # First request
    local response1=$(curl -s -k -w "\n%{http_code}" -X "POST" \
        -H "Content-Type: application/json" \
        -H "Cko-Idempotency-Key: $idempotency_key" \
        -d "$data" \
        "$API_URL/api/payments")

    local body1=$(echo "$response1" | head -n -1)
    local status1=$(echo "$response1" | tail -n 1)

    # Second request with same idempotency key
    local response2=$(curl -s -k -w "\n%{http_code}" -X "POST" \
        -H "Content-Type: application/json" \
        -H "Cko-Idempotency-Key: $idempotency_key" \
        -d "$data" \
        "$API_URL/api/payments")

    local body2=$(echo "$response2" | head -n -1)
    local status2=$(echo "$response2" | tail -n 1)

    # Check if both requests returned same status and response
    if [ "$status1" = "$status2" ] && [ "$body1" = "$body2" ]; then
        print_success "✓ Idempotency test passed: $test_name"
        echo "First response: $body1"
        echo "Second response: $body2"
    else
        print_error "✗ Idempotency test failed: $test_name"
        print_error "First response (HTTP $status1): $body1"
        print_error "Second response (HTTP $status2): $body2"
        return 1
    fi

    echo ""
    return 0
}

# Cleanup function
cleanup() {
    print_status "Cleaning up processes..."

    # Kill dotnet processes
    pkill -f "dotnet.*PaymentGateway.Api" 2>/dev/null

    # Stop docker compose
    docker-compose down > /dev/null 2>&1

    print_status "Cleanup completed"
}

# Set up trap for cleanup on exit
trap cleanup EXIT

# Main execution
main() {
    print_status "Starting Payment Gateway E2E Tests"
    echo "======================================"

    # Build the solution
    print_status "Building the solution..."
    if ! dotnet build > build.log 2>&1; then
        print_error "Build failed. Check build.log for details."
        return 1
    fi
    print_success "Build completed successfully"

    # Start bank simulator
    print_status "Starting bank simulator..."
    docker-compose up -d > /dev/null 2>&1
    if [ $? -ne 0 ]; then
        print_error "Failed to start bank simulator"
        return 1
    fi

    # Wait for bank simulator to be ready
    if ! wait_for_service "$BANK_URL/payments" "Bank Simulator"; then
        return 1
    fi

    # Start payment gateway API
    print_status "Starting Payment Gateway API..."
    cd "$(dirname "$0")"
    nohup dotnet run --project src/PaymentGateway.Api > api.log 2>&1 &
    API_PID=$!

    # Wait for API to be ready
    if ! wait_for_service "$API_URL/api/payments" "Payment Gateway API"; then
        print_error "Payment Gateway API failed to start"
        return 1
    fi

    print_success "All services are running!"
    echo ""

    # Run E2E Tests
    print_status "Running End-to-End Tests"
    echo "========================="

    local failed_tests=0

    # Test 1: Success scenario (card ending in odd number)
    print_status "Test 1: Success - Authorized payment"
    if ! run_test "SUCCESS" "POST" "/api/payments" \
        '{"cardNumber":"4111111111111111","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":1000,"cvv":"123"}' \
        "200" "Card ending in 1 (odd) should be authorized"; then
        failed_tests=$((failed_tests + 1))
    fi

    # Test 2: Bank decline (card ending in even number)
    print_status "Test 2: Bank Decline - Declined payment"
    if ! run_test "DECLINE" "POST" "/api/payments" \
        '{"cardNumber":"4111111111111112","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":1000,"cvv":"123"}' \
        "200" "Card ending in 2 (even) should be declined"; then
        failed_tests=$((failed_tests + 1))
    fi

    # Test 3: Validation failure - Invalid card number
    print_status "Test 3: Validation Failure - Invalid card number"
    if ! run_test "VALIDATION_FAIL" "POST" "/api/payments" \
        '{"cardNumber":"123","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":1000,"cvv":"123"}' \
        "400" "Invalid card number should be rejected"; then
        failed_tests=$((failed_tests + 1))
    fi

    # Test 4: Validation failure - Invalid expiry
    print_status "Test 4: Validation Failure - Past expiry date"
    if ! run_test "VALIDATION_FAIL_EXPIRY" "POST" "/api/payments" \
        '{"cardNumber":"4111111111111111","expiryMonth":12,"expiryYear":2020,"currency":"USD","amount":1000,"cvv":"123"}' \
        "400" "Past expiry date should be rejected"; then
        failed_tests=$((failed_tests + 1))
    fi

    # Test 5: Bank failure (card ending in 0)
    print_status "Test 5: Bank Failure - Bank service error"
    if ! run_test "BANK_FAILURE" "POST" "/api/payments" \
        '{"cardNumber":"4111111111111110","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":1000,"cvv":"123"}' \
        "502" "Card ending in 0 should cause bank failure"; then
        failed_tests=$((failed_tests + 1))
    fi

    # Test 6: Get payment details (use a successful payment ID from earlier)
    print_status "Test 6: Get Payment - Retrieve payment details"
    # First create a payment to get an ID
    local payment_response=$(curl -s -k -X POST \
        -H "Content-Type: application/json" \
        -d '{"cardNumber":"4111111111111111","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":1500,"cvv":"456"}' \
        "$API_URL/api/payments")

    local payment_id=$(echo "$payment_response" | grep -o '"id":"[^"]*"' | cut -d'"' -f4)

    if [ -n "$payment_id" ]; then
        if ! run_test "GET_PAYMENT" "GET" "/api/payments/$payment_id" "" "200" "Retrieve existing payment details"; then
            failed_tests=$((failed_tests + 1))
        fi
    else
        print_error "Could not extract payment ID for GET test"
        failed_tests=$((failed_tests + 1))
    fi

    # Test 7: Get non-existent payment
    print_status "Test 7: Get Payment - Non-existent payment"
    if ! run_test "GET_NOT_FOUND" "GET" "/api/payments/00000000-0000-0000-0000-000000000000" "" "404" "Non-existent payment should return 404"; then
        failed_tests=$((failed_tests + 1))
    fi

    # Test 8: Idempotency - Same request twice should return same response
    print_status "Test 8: Idempotency - Successful payment with same key"
    # Restart bank simulator for idempotency tests
    print_status "Restarting bank simulator for idempotency tests..."
    docker-compose up -d > /dev/null 2>&1
    wait_for_service "$BANK_URL/payments" "Bank Simulator"

    local idempotency_key_1=$(uuidgen)
    if ! run_idempotency_test "IDEMPOTENCY_SUCCESS" "$idempotency_key_1" \
        '{"cardNumber":"4111111111111111","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":3000,"cvv":"123"}' \
        "Two requests with same idempotency key should return identical responses"; then
        failed_tests=$((failed_tests + 1))
    fi

    # Test 9: Idempotency - Different keys should create different payments
    print_status "Test 9: Idempotency - Different keys create different payments"
    local idempotency_key_2=$(uuidgen)

    # First payment with first key
    local response1=$(curl -s -k -X POST \
        -H "Content-Type: application/json" \
        -H "Cko-Idempotency-Key: $idempotency_key_1" \
        -d '{"cardNumber":"4111111111111111","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":4000,"cvv":"456"}' \
        "$API_URL/api/payments")

    # Second payment with different key
    local response2=$(curl -s -k -X POST \
        -H "Content-Type: application/json" \
        -H "Cko-Idempotency-Key: $idempotency_key_2" \
        -d '{"cardNumber":"4111111111111111","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":4000,"cvv":"456"}' \
        "$API_URL/api/payments")

    local id1=$(echo "$response1" | grep -o '"id":"[^"]*"' | cut -d'"' -f4)
    local id2=$(echo "$response2" | grep -o '"id":"[^"]*"' | cut -d'"' -f4)

    if [ "$id1" != "$id2" ] && [ -n "$id1" ] && [ -n "$id2" ]; then
        print_success "✓ Test passed: Different idempotency keys create different payments"
        echo "Payment 1 ID: $id1"
        echo "Payment 2 ID: $id2"
    else
        print_error "✗ Test failed: Different idempotency keys should create different payments"
        print_error "Payment 1 ID: $id1"
        print_error "Payment 2 ID: $id2"
        failed_tests=$((failed_tests + 1))
    fi
    echo ""

    # Test 10: Idempotency - No header should create new payment each time
    print_status "Test 10: Idempotency - No header creates new payments"

    # First payment without idempotency key
    local response_no_key1=$(curl -s -k -X POST \
        -H "Content-Type: application/json" \
        -d '{"cardNumber":"4111111111111111","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":5000,"cvv":"789"}' \
        "$API_URL/api/payments")

    # Second payment without idempotency key
    local response_no_key2=$(curl -s -k -X POST \
        -H "Content-Type: application/json" \
        -d '{"cardNumber":"4111111111111111","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":5000,"cvv":"789"}' \
        "$API_URL/api/payments")

    local id_no_key1=$(echo "$response_no_key1" | grep -o '"id":"[^"]*"' | cut -d'"' -f4)
    local id_no_key2=$(echo "$response_no_key2" | grep -o '"id":"[^"]*"' | cut -d'"' -f4)

    if [ "$id_no_key1" != "$id_no_key2" ] && [ -n "$id_no_key1" ] && [ -n "$id_no_key2" ]; then
        print_success "✓ Test passed: Requests without idempotency key create different payments"
        echo "Payment 1 ID: $id_no_key1"
        echo "Payment 2 ID: $id_no_key2"
    else
        print_error "✗ Test failed: Requests without idempotency key should create different payments"
        print_error "Payment 1 ID: $id_no_key1"
        print_error "Payment 2 ID: $id_no_key2"
        failed_tests=$((failed_tests + 1))
    fi
    echo ""

    # Test 11: Bank simulator shutdown test
    print_status "Test 11: Bank Simulator Shutdown - Service unavailable"
    print_status "Shutting down bank simulator..."
    docker-compose down > /dev/null 2>&1

    # Wait a moment for the shutdown to complete
    sleep 3

    if ! run_test "BANK_SHUTDOWN" "POST" "/api/payments" \
        '{"cardNumber":"4111111111111111","expiryMonth":12,"expiryYear":2025,"currency":"USD","amount":2000,"cvv":"789"}' \
        "503" "Payment should fail when bank simulator is unavailable"; then
        failed_tests=$((failed_tests + 1))
    fi

    # Summary
    echo ""
    print_status "Test Summary"
    echo "============"
    if [ $failed_tests -eq 0 ]; then
        print_success "All tests passed! ✓"
        return 0
    else
        print_error "$failed_tests test(s) failed ✗"
        return 1
    fi
}

# Run main function
main "$@"