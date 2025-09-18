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

    print_status "Running test: $test_name - $description"

    local response=$(curl -s -k -w "\n%{http_code}" -X "$method" \
        -H "Content-Type: application/json" \
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

    # Test 8: Bank simulator shutdown test
    print_status "Test 8: Bank Simulator Shutdown - Service unavailable"
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