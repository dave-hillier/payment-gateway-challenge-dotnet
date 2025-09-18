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

    # Wait for Redis to be ready
    print_status "Waiting for Redis to be ready..."
    if ! timeout 30 bash -c 'until echo "PING" | nc -q 1 localhost 6379 | grep -q PONG; do sleep 1; done'; then
        print_error "Redis failed to start"
        return 1
    fi
    print_success "Redis is ready!"

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

    # Run E2E Tests using .NET test framework
    print_status "Running End-to-End Tests with .NET Framework"
    echo "=============================================="

    # Run the E2E tests
    print_status "Executing E2E test suite..."
    cd "$(dirname "$0")"

    if dotnet test test/PaymentGateway.Api.Tests --filter "Category=E2E" --logger "console;verbosity=detailed"; then
        print_success "All E2E tests passed! ✓"
        return 0
    else
        print_error "Some E2E tests failed ✗"
        return 1
    fi
}

# Run main function
main "$@"