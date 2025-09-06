#!/bin/bash

# ===== BurbujaEngine - Enhanced Development Setup Script =====
# Comprehensive setup script for BurbujaEngine development
# Supports both development and production modes

set -euo pipefail  # Exit on error, undefined vars, pipe failures

# ===== PATH CONFIGURATION =====
# Script directory and project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"

# Change to project root so relative paths work
cd "$PROJECT_ROOT"

# ===== COLORS =====
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly CYAN='\033[0;36m'
readonly MAGENTA='\033[0;35m'
readonly NC='\033[0m'

# ===== CONFIGURATION =====
readonly PROJECT_NAME="burbuja-engine"
readonly DOCKER_COMPOSE_FILE="docker-compose.yml"
readonly DOCKERFILE="Dockerfile"
readonly MAX_WAIT_TIME=60
readonly HEALTH_CHECK_INTERVAL=2
readonly STARTUP_WAIT_TIME=10

# Local Development Configuration
readonly LOCAL_DATA_DIR="local-data"
readonly ENVIRONMENT="local"

# Flags (default values)
REBUILD=false        # --rebuild
PRUNE_CACHE=false    # --prune-cache
FORCE_CLEAN=false    # --clean
SKIP_BUILD=false     # --skip-build
PULL_IMAGES=false    # --pull
ONLY_DATABASE=false  # --only-database (run only MongoDB for local C# dev)
DEV_MODE=false       # --dev (run dotnet locally with hot reload)
PROD_MODE=false      # --prod (TODO: full production deployment)

# Docker Compose command (will be detected)
DOCKER_COMPOSE=""

# ===== LOGGING FUNCTIONS =====
log_debug() { echo -e "${MAGENTA}[DEBUG]${NC} $1"; }
log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[✓]${NC} $1"; }
log_warning() { echo -e "${YELLOW}[!]${NC} $1"; }
log_error() { echo -e "${RED}[✗]${NC} $1"; }
log_step() { echo -e "${CYAN}[STEP]${NC} $1"; }

# ===== BANNER =====
show_banner() {
    if [[ "$DEV_MODE" == "true" ]]; then
        echo -e "${CYAN}"
        echo "╔════════════════════════════════════════════════════════════╗"
        echo "║            BurbujaEngine - Development Mode               ║"
        echo "║          Dotnet Hot Reload + MongoDB Container            ║"
        echo "║              C# Local Development                         ║"
        echo "╚════════════════════════════════════════════════════════════╝"
        echo -e "${NC}"
        echo -e "Mode: ${YELLOW}Development${NC} (dotnet with hot reload)"
        echo -e "API: ${YELLOW}Local dotnet server${NC}"
        echo -e "Database: ${YELLOW}MongoDB Container${NC}"
    elif [[ "$ONLY_DATABASE" == "true" ]]; then
        echo -e "${GREEN}"
        echo "╔════════════════════════════════════════════════════════════╗"
        echo "║            BurbujaEngine - Database Only Mode             ║"
        echo "║              For Local C# Development                     ║"
        echo "║              Database: MongoDB Container                  ║"
        echo "╚════════════════════════════════════════════════════════════╝"
        echo -e "${NC}"
        echo -e "Mode: ${YELLOW}Database Only${NC} (for local C# development)"
        echo -e "Service: ${YELLOW}MongoDB Container${NC}"
    elif [[ "$PROD_MODE" == "true" ]]; then
        echo -e "${RED}"
        echo "╔════════════════════════════════════════════════════════════╗"
        echo "║            BurbujaEngine - Production Mode                ║"
        echo "║                   TODO: Implementation                    ║"
        echo "╚════════════════════════════════════════════════════════════╝"
        echo -e "${NC}"
        echo -e "Mode: ${YELLOW}Production${NC} (containerized deployment)"
    else
        echo -e "${MAGENTA}"
        echo "╔════════════════════════════════════════════════════════════╗"
        echo "║                    BURBUJA ENGINE                          ║"
        echo "║                Enhanced Development Setup                  ║"
        echo "║                                                            ║"
        echo "║    C# ASP.NET Core + MongoDB + Docker Setup               ║"
        echo "╚════════════════════════════════════════════════════════════╝"
        echo -e "${NC}"
        echo -e "Environment: ${YELLOW}${ENVIRONMENT}${NC}"
    fi
    echo -e "Working Directory: ${YELLOW}${PROJECT_ROOT}${NC}"
    echo -e "Data Directory: ${YELLOW}${LOCAL_DATA_DIR}${NC}"
    echo -e "Docker Configuration: ${YELLOW}${DOCKER_COMPOSE_FILE}${NC}"
}

# ===== USAGE FUNCTION =====
print_usage() {
    cat <<EOF
Usage: ./start.sh [options]

Description:
  Enhanced startup script for BurbujaEngine development environment.
  Provides comprehensive setup with improved error handling and debugging.

Options:
  --rebuild         Build without cache (docker compose build --no-cache)
  --prune-cache     Clean only build cache (buildx/builder)
  --clean           Deep clean: stop, delete project images and prune
  --skip-build      Skip build step; only docker compose up -d
  --pull            Force download of base layers during build (--pull)
  --dev             Run dotnet locally with hot reload (database only in Docker)
  --only-database   Run ONLY MongoDB container for local C# development
  --prod            Production mode (TODO: full containerized deployment)
  -h, --help        Show this help

Development Modes:

Database Only Mode (--only-database):
  Perfect for running your C# ASP.NET Core app locally while using containerized MongoDB.
  This mode:
  - Starts ONLY the MongoDB container (no C# app container)
  - Exposes MongoDB on localhost:27017
  - Creates all necessary data directories
  - Sets up proper MongoDB configuration
  - Allows you to run 'dotnet run' locally with access to the database

Development Mode (--dev):
  Use --dev to run the C# ASP.NET Core app locally with dotnet hot reload while using MongoDB in Docker.
  This mode:
  - Starts ONLY the MongoDB container
  - Runs dotnet locally with hot reload enabled
  - Automatically restarts on code changes
  - Uses local .NET environment

Production Mode (--prod):
  TODO: Full containerized deployment with both API and database containers.

Local Data Structure:
  - MongoDB: ./local-data/mongodb/
  - Logs: ./local-data/logs/

Examples:
  ./start.sh                    # normal start (development services)
  ./start.sh --rebuild          # build without cache + up
  ./start.sh --skip-build       # use existing image
  ./start.sh --clean            # complete cleanup + start
  ./start.sh --only-database    # MongoDB only for local C# dev
  ./start.sh --dev              # run dotnet locally with hot reload
  ./start.sh --prod             # TODO: production deployment
EOF
}

# ===== ARGUMENT PARSING =====
parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --rebuild|-r)
                REBUILD=true
                ;;
            --prune-cache)
                PRUNE_CACHE=true
                ;;
            --clean)
                FORCE_CLEAN=true
                ;;
            --skip-build)
                SKIP_BUILD=true
                ;;
            --pull)
                PULL_IMAGES=true
                ;;
            --dev)
                DEV_MODE=true
                ONLY_DATABASE=true    # We only need MongoDB container
                SKIP_BUILD=true       # No need to build app container
                log_info "🚀 Development mode: Running dotnet locally with hot reload"
                ;;
            --only-database)
                ONLY_DATABASE=true
                SKIP_BUILD=true    # No need to build app container
                log_info "🗄️ Database-only mode: Starting MongoDB container for local C# development"
                ;;
            --prod)
                PROD_MODE=true
                log_warning "🚧 Production mode is TODO - not yet implemented"
                ;;
            -h|--help)
                print_usage
                exit 0
                ;;
            *)
                log_warning "Unknown option: $1"
                print_usage
                exit 1
                ;;
        esac
        shift
    done
}

# ===== DOCKER COMPOSE DETECTION =====
detect_docker_compose() {
    if command -v docker-compose &> /dev/null; then
        DOCKER_COMPOSE="docker-compose"
    elif docker compose version &> /dev/null; then
        DOCKER_COMPOSE="docker compose"
    else
        log_error "Docker Compose is not available"
        log_info "Please install Docker Desktop or docker-compose"
        exit 1
    fi
    log_success "Docker Compose detected: $DOCKER_COMPOSE"
}

# Build compose command 
get_compose_command() {
    local cmd="$DOCKER_COMPOSE -f $DOCKER_COMPOSE_FILE"
    echo "$cmd"
}

# ===== ENHANCED DEPENDENCY CHECKS =====
check_command() {
    local cmd="$1"
    local name="$2"
    local install_hint="$3"
    
    if command -v "$cmd" >/dev/null 2>&1; then
        local version
        case "$cmd" in
            "docker")
                version=$(docker --version | cut -d' ' -f3 | cut -d',' -f1)
                ;;
            "docker-compose")
                version=$(docker-compose --version | cut -d' ' -f3 | cut -d',' -f1)
                ;;
            "dotnet")
                version=$(dotnet --version 2>/dev/null || echo "unknown")
                ;;
            "curl")
                version=$(curl --version | head -n1 | grep -o '[0-9]\+\.[0-9]\+\.[0-9]\+' | head -n1 || echo "unknown")
                ;;
            *)
                version=$($cmd --version 2>/dev/null | head -n1 | grep -o '[0-9]\+\.[0-9]\+\.[0-9]\+' | head -n1 || echo "unknown")
                ;;
        esac
        log_success "$name is installed (version: $version)"
        return 0
    else
        log_error "$name is not installed"
        log_info "Install hint: $install_hint"
        return 1
    fi
}

check_docker_running() {
    if docker info >/dev/null 2>&1; then
        log_success "Docker daemon is running"
        return 0
    else
        log_error "Docker daemon is not running"
        log_info "Please start Docker Desktop and try again"
        return 1
    fi
}

check_dotnet_version() {
    if command -v dotnet >/dev/null 2>&1; then
        local version
        version=$(dotnet --version 2>/dev/null)
        if [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]; then
            local major_version=$(echo "$version" | cut -d'.' -f1)
            
            if [[ "$major_version" -ge "8" ]]; then
                log_success ".NET $version is installed and compatible"
                return 0
            else
                log_error ".NET version $version is not supported. .NET 8+ is required"
                return 1
            fi
        else
            log_error "Could not determine .NET version"
            return 1
        fi
    else
        log_error ".NET is not installed"
        return 1
    fi
}

# ===== PROJECT STRUCTURE VERIFICATION =====
check_project_structure() {
    log_info "Checking project structure..."
    
    local required_files=(
        "$DOCKER_COMPOSE_FILE"
        "$DOCKERFILE"
        "burbuja-engine/burbuja-engine.csproj"
        "burbuja-engine/Program.cs"
    )
    
    for file in "${required_files[@]}"; do
        if [[ ! -f "$file" ]]; then
            log_error "Required file not found: $file"
            exit 1
        fi
    done
    
    log_success "Project structure verified"
}

# ===== DEPENDENCY VERIFICATION =====
verify_dependencies() {
    log_step "Verifying system dependencies..."
    
    local all_deps_ok=true
    
    # Detect Docker Compose first
    detect_docker_compose
    
    # Check essential tools
    check_command "docker" "Docker" "Install Docker Desktop from https://www.docker.com/products/docker-desktop" || all_deps_ok=false
    check_command "dotnet" ".NET" "Install .NET SDK from https://dotnet.microsoft.com/download" || all_deps_ok=false
    check_command "curl" "curl" "Install via package manager or Homebrew: brew install curl" || all_deps_ok=false
    
    # Check if Docker is running
    check_docker_running || all_deps_ok=false
    
    # Check .NET version
    check_dotnet_version || all_deps_ok=false
    
    # Check project structure
    check_project_structure || all_deps_ok=false
    
    if [[ "$all_deps_ok" == "false" ]]; then
        log_error "Some essential dependencies are missing. Please install them and run this script again."
        exit 1
    fi
    
    log_success "All essential dependencies are available!"
}

# ===== ENVIRONMENT SETUP =====
setup_local_environment() {
    log_info "Setting up local environment..."
    
    # Docker environment variables (detect platform automatically)
    if [[ "$(uname -m)" == "arm64" ]]; then
        export DOCKER_DEFAULT_PLATFORM=linux/arm64
    else
        export DOCKER_DEFAULT_PLATFORM=linux/amd64
    fi
    export DOCKER_BUILDKIT=1
    export COMPOSE_DOCKER_CLI_BUILD=1
    
    # Environment configuration for local development
    export MONGODB_HOST="localhost"
    export MONGODB_PORT="27017"
    export MONGODB_DATABASE="burbuja_engine"
    export APP_HOST="0.0.0.0"
    export APP_PORT="8000"
    export ASPNETCORE_ENVIRONMENT="Development"
    export ASPNETCORE_URLS="http://+:8000"
    
    log_success "Local environment configured"
}

# ===== LOCAL DATA DIRECTORIES =====
check_volume_directories() {
    log_info "Setting up local data directories..."
    
    local storage_dirs=(
        "${LOCAL_DATA_DIR}"
        "${LOCAL_DATA_DIR}/mongodb"
        "${LOCAL_DATA_DIR}/mongodb/logs"
        "${LOCAL_DATA_DIR}/logs"
    )
    
    # Create directories
    for dir in "${storage_dirs[@]}"; do
        if [[ ! -d "$dir" ]]; then
            log_info "Creating directory: $dir"
            mkdir -p "$dir"
        fi
    done
    
    # Verify permissions (especially for MongoDB)
    local mongodb_dir="${LOCAL_DATA_DIR}/mongodb"
    if [[ ! -w "$mongodb_dir" ]]; then
        log_warning "Adjusting permissions for MongoDB..."
        chmod 755 "$mongodb_dir"
    fi
    
    log_success "Local data directories verified"
}

# ===== CLEANUP FUNCTIONS =====
force_clean() {
    log_warning "Executing deep cleanup..."
    
    # Stop services
    log_info "Stopping services..."
    $DOCKER_COMPOSE down -v 2>/dev/null || true
    
    # Remove project images
    log_info "Removing project images..."
    docker images | grep "$PROJECT_NAME" | awk '{print $3}' | xargs -r docker rmi -f 2>/dev/null || true
    
    # Clean unused images
    log_info "Cleaning unused images..."
    docker image prune -f
    
    # Clean build cache if requested
    if [[ "$PRUNE_CACHE" == "true" ]]; then
        log_info "Cleaning build cache..."
        docker builder prune -f 2>/dev/null || true
        docker buildx prune -f 2>/dev/null || true
    fi
    
    log_success "Cleanup completed"
}

# ===== CONTAINER DEBUGGING =====
debug_container_failure() {
    local container_name="$1"
    
    log_debug "=== CONTAINER DEBUG REPORT ==="
    log_debug "Container: $container_name"
    log_debug "Timestamp: $(date)"
    
    # Container inspection
    log_debug "Container status:"
    $DOCKER_COMPOSE ps "$container_name" || true
    
    # Recent logs with error highlighting
    log_debug "Recent logs (last 50 lines):"
    local logs=$($DOCKER_COMPOSE logs --tail=50 "$container_name" 2>&1)
    
    # Highlight errors
    echo "$logs" | while IFS= read -r line; do
        if echo "$line" | grep -i "error\|exception\|traceback\|failed\|fatal" &>/dev/null; then
            echo -e "${RED}$line${NC}"
        elif echo "$line" | grep -i "warning\|warn" &>/dev/null; then
            echo -e "${YELLOW}$line${NC}"
        else
            echo "$line"
        fi
    done
    
    # Check for specific .NET/C# issues
    if echo "$logs" | grep -q "System\.\|Microsoft\.\|dotnet"; then
        log_debug "🐛 DETECTED: .NET/C# application issues"
        log_debug "   Check if all required packages are restored"
    fi
    
    if echo "$logs" | grep -q "ConnectionError\|MongoDB"; then
        log_debug "🐛 DETECTED: MongoDB connection issues"
        log_debug "   Check if MongoDB container is running and accessible"
    fi
    
    log_debug "=== END DEBUG REPORT ==="
}

# ===== BUILD SERVICES =====
build_services() {
    log_step "Building services..."
    
    local build_args=""
    local compose_cmd="$(get_compose_command)"
    
    if [[ "$REBUILD" == "true" ]]; then
        build_args="--no-cache"
        log_info "Build without cache enabled"
    fi
    
    if [[ "$PULL_IMAGES" == "true" ]]; then
        build_args="$build_args --pull"
        log_info "Pull base images enabled"
    fi
    
    # Build with compose
    if $compose_cmd build $build_args; then
        log_success "Services built successfully"
    else
        log_error "Error building services"
        return 1
    fi
}

# ===== START SERVICES =====
start_services() {
    log_step "Starting services..."
    
    local compose_cmd="$(get_compose_command)"
    
    if [[ "$ONLY_DATABASE" == "true" ]]; then
        log_info "🗄️ Starting MongoDB container only for local C# development"
        if $compose_cmd up -d mongodb; then
            log_success "MongoDB container started successfully"
        else
            log_error "Error starting MongoDB"
            return 1
        fi
    elif [[ "$PROD_MODE" == "true" ]]; then
        log_error "🚧 Production mode is not yet implemented"
        log_info "TODO: Implement full production deployment with both API and database containers"
        return 1
    else
        if $compose_cmd up -d; then
            log_success "Services started"
        else
            log_error "Error starting services"
            return 1
        fi
    fi
}

# ===== WAIT FOR SERVICES =====
wait_for_services() {
    log_info "Waiting for services to be ready..."
    
    local elapsed=0
    local db_ready=false
    local app_ready=false
    local app_port=8000
    local dot_count=0
    local error_logged=false
    
    # In database-only mode, we only check the database
    if [[ "$ONLY_DATABASE" == "true" ]]; then
        log_info "🗄️ Database-only mode: Checking MongoDB container..."
        
        while [[ $elapsed -lt $MAX_WAIT_TIME ]]; do
            # Check Database (MongoDB)
            if ! $db_ready; then
                if $DOCKER_COMPOSE exec -T mongodb mongosh --eval "db.adminCommand('ping')" &>/dev/null; then
                    echo # New line after dots
                    log_success "MongoDB is ready and responsive"
                    db_ready=true
                    return 0
                fi
            fi
            
            # Print progress dots
            if [[ $dot_count -lt 30 ]]; then
                echo -n "."
                ((dot_count++))
            else
                echo # New line every 30 dots
                log_info "Still waiting for MongoDB... (${elapsed}s/${MAX_WAIT_TIME}s)"
                dot_count=0
            fi
            
            sleep $HEALTH_CHECK_INTERVAL
            elapsed=$((elapsed + HEALTH_CHECK_INTERVAL))
        done
        
        echo # New line after dots
        log_warning "MongoDB did not respond within expected time (${MAX_WAIT_TIME}s)"
        return 1
    fi
    
    # TODO: Add production service wait logic here
    log_info "Production service waiting not yet implemented"
    return 0
}

# ===== HEALTH CHECKS =====
verify_services_health() {
    log_step "Verifying service health..."
    
    local all_healthy=true
    local app_port=8000
    
    # Database health check (MongoDB)
    if $DOCKER_COMPOSE exec -T mongodb mongosh --eval "db.adminCommand('ping')" &>/dev/null; then
        log_success "MongoDB: Healthy"
    else
        log_error "MongoDB: Unhealthy"
        all_healthy=false
    fi
    
    # In database-only mode, skip app health check
    if [[ "$ONLY_DATABASE" == "true" ]]; then
        log_info "Database-only mode: Skipping application health check"
        if $all_healthy; then
            log_success "Database service is healthy"
            return 0
        else
            log_warning "Database service is not responding correctly"
            return 1
        fi
    fi
    
    # TODO: Add production app health check here
    if [[ "$PROD_MODE" == "true" ]]; then
        log_info "Production app health check not yet implemented"
    fi
    
    if $all_healthy; then
        log_success "All services are healthy"
        return 0
    else
        log_warning "Some services are not responding correctly"
        return 1
    fi
}

test_database_connection() {
    log_info "Testing database connection..."
    
    if $DOCKER_COMPOSE exec -T mongodb mongosh --eval "
        use burbuja_engine;
        db.getCollectionNames();
        print('BurbujaEngine MongoDB: Connection successful');
    " &>/dev/null; then
        log_success "MongoDB connection: Successful"
        log_info "Database configured for: burbuja_engine"
    else
        log_warning "Could not connect to MongoDB"
    fi
}

run_health_checks() {
    log_step "Running comprehensive health checks..."
    
    # Check API health (only in production mode)
    if [[ "$PROD_MODE" == "true" ]]; then
        log_info "TODO: Implement production API health checks"
    elif [[ "$ONLY_DATABASE" != "true" && "$DEV_MODE" != "true" ]]; then
        log_info "Checking API health..."
        local health_response
        health_response=$(curl -s http://localhost:8000/health)
        if echo "$health_response" | grep -q '"IsHealthy":true'; then
            log_success "API is healthy"
        else
            log_warning "API health check failed"
            echo "$health_response"
        fi
    fi
}

# ===== INFORMATION DISPLAY =====
show_project_info() {
    local app_port=8000
    
    if [[ "$DEV_MODE" == "true" ]]; then
        # Development mode information
        echo
        echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
        echo -e "${GREEN}      Development Mode: Dotnet + MongoDB Container     ${NC}"
        echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
        echo
        echo -e "${CYAN}BurbujaEngine API:${NC}"
        echo -e "  Status:          ${YELLOW}Running locally with hot reload${NC}"
        echo -e "  URL:             ${YELLOW}http://localhost:${app_port}${NC}"
        echo -e "  API Docs:        ${YELLOW}http://localhost:${app_port}/swagger${NC}"
        echo -e "  Health Check:    ${YELLOW}http://localhost:${app_port}/health${NC}"
        echo
        echo -e "${CYAN}Database Connection:${NC}"
        echo -e "  MongoDB Host:    ${YELLOW}localhost${NC}"
        echo -e "  MongoDB Port:    ${YELLOW}27017${NC}"
        echo -e "  Database Name:   ${YELLOW}burbuja_engine${NC}"
        echo -e "  Connection URI:  ${YELLOW}mongodb://localhost:27017/burbuja_engine${NC}"
        echo
        echo -e "${CYAN}Hot Reload:${NC}"
        echo -e "  🔄 Code changes will automatically restart the server${NC}"
        echo -e "  🛑 Press Ctrl+C to stop the development server${NC}"
        echo
        echo -e "${CYAN}Data Directory:${NC}"
        echo -e "  MongoDB Data:    ${YELLOW}${PROJECT_ROOT}/${LOCAL_DATA_DIR}/mongodb/${NC}"
        echo -e "  Logs:            ${YELLOW}${PROJECT_ROOT}/${LOCAL_DATA_DIR}/logs/${NC}"
        echo
        echo -e "${CYAN}Useful Commands:${NC}"
        echo -e "  MongoDB Shell:   ${YELLOW}$DOCKER_COMPOSE exec mongodb mongosh burbuja_engine${NC}"
        echo -e "  Check DB Logs:   ${YELLOW}$DOCKER_COMPOSE logs -f mongodb${NC}"
        echo -e "  Stop Database:   ${YELLOW}$DOCKER_COMPOSE down${NC}"
        echo
        echo -e "${CYAN}Development Mode Active! 🚀${NC}"
        echo
        return
    fi
    
    if [[ "$ONLY_DATABASE" == "true" ]]; then
        # Database-only mode information
        echo
        echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
        echo -e "${GREEN}          MongoDB Container Ready for Local Dev        ${NC}"
        echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
        echo
        echo -e "${CYAN}Database Connection:${NC}"
        echo -e "  MongoDB Host:    ${YELLOW}localhost${NC}"
        echo -e "  MongoDB Port:    ${YELLOW}27017${NC}"
        echo -e "  Database Name:   ${YELLOW}burbuja_engine${NC}"
        echo -e "  Connection URI:  ${YELLOW}mongodb://localhost:27017/burbuja_engine${NC}"
        echo
        echo -e "${CYAN}Local C# Development:${NC}"
        echo -e "  1. Run your BurbujaEngine app locally:${NC}"
        echo -e "     ${YELLOW}dotnet run${NC}"
        echo
        echo -e "  2. Your app will connect to MongoDB container on localhost:27017${NC}"
        echo
        echo -e "${CYAN}Data Directory:${NC}"
        echo -e "  MongoDB Data:    ${YELLOW}${PROJECT_ROOT}/${LOCAL_DATA_DIR}/mongodb/${NC}"
        echo -e "  Logs:            ${YELLOW}${PROJECT_ROOT}/${LOCAL_DATA_DIR}/logs/${NC}"
        echo
        echo -e "${CYAN}Useful Commands:${NC}"
        echo -e "  MongoDB Shell:   ${YELLOW}$DOCKER_COMPOSE exec mongodb mongosh burbuja_engine${NC}"
        echo -e "  Check Logs:      ${YELLOW}$DOCKER_COMPOSE logs -f mongodb${NC}"
        echo -e "  Stop Database:   ${YELLOW}$DOCKER_COMPOSE down${NC}"
        echo -e "  Restart DB:      ${YELLOW}$DOCKER_COMPOSE restart mongodb${NC}"
        echo
        echo -e "${CYAN}Ready for Local Development! 🚀${NC}"
        echo -e "Start your BurbujaEngine app with: ${YELLOW}dotnet run${NC}"
        echo
        return
    fi
    
    if [[ "$PROD_MODE" == "true" ]]; then
        echo
        echo -e "${RED}╔════════════════════════════════════════════════════════════╗${NC}"
        echo -e "${RED}║                 PRODUCTION MODE - TODO                    ║${NC}"
        echo -e "${RED}╚════════════════════════════════════════════════════════════╝${NC}"
        echo
        echo -e "${YELLOW}Production mode is not yet implemented.${NC}"
        echo -e "${YELLOW}TODO: Implement full containerized deployment.${NC}"
        echo
        return
    fi
    
    # Default information
    echo
    echo -e "${GREEN}╔════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║                    PROJECT READY!                         ║${NC}"
    echo -e "${GREEN}╚════════════════════════════════════════════════════════════╝${NC}"
    echo
    
    echo -e "${CYAN}Development Commands:${NC}"
    echo -e "   • Local dev mode:  ${YELLOW}./start.sh --dev${NC}"
    echo -e "   • Database only:   ${YELLOW}./start.sh --only-database${NC}"
    echo -e "   • Production:      ${YELLOW}./start.sh --prod${NC} (TODO)"
    echo -e "   • Clean rebuild:   ${YELLOW}./start.sh --clean --rebuild${NC}"
    echo
}

# ===== MONITOR APP STARTUP =====
monitor_app_startup() {
    if [[ "$ONLY_DATABASE" == "true" ]]; then
        log_info "🗄️ Monitoring MongoDB container startup..."
        
        # Give initial time for MongoDB to initialize
        sleep 5
        
        # Check that MongoDB container is running
        local mongodb_status=$($DOCKER_COMPOSE ps mongodb --format "table {{.Status}}" 2>/dev/null | tail -n +2)
        
        if [[ -z "$mongodb_status" ]]; then
            log_error "MongoDB container not found"
            return 1
        fi
        
        if [[ "$mongodb_status" == *"Up"* ]]; then
            log_success "MongoDB container is running"
            
            # Wait for MongoDB to be ready to accept connections
            if wait_for_services; then
                log_success "MongoDB is ready for connections"
                return 0
            else
                log_warning "MongoDB container is running but not responding to connections"
                return 1
            fi
        else
            log_error "MongoDB container is not running: $mongodb_status"
            return 1
        fi
    fi
    
    # TODO: Add production monitoring
    log_info "Production monitoring not yet implemented"
    return 0
}

# ===== RUN DOTNET LOCALLY =====
run_dotnet_dev() {
    log_step "Starting dotnet development server with hot reload..."
    
    # Check if dotnet is available
    if ! command -v dotnet >/dev/null 2>&1; then
        log_error ".NET is not available"
        return 1
    fi
    
    # Check if project file exists
    if [[ ! -f "burbuja-engine/burbuja-engine.csproj" ]]; then
        log_error "burbuja-engine.csproj not found in burbuja-engine/ directory"
        return 1
    fi
    
    # Change to the project directory
    cd burbuja-engine || {
        log_error "Could not change to burbuja-engine directory"
        return 1
    }
    
    # Restore packages
    log_info "Restoring .NET packages..."
    dotnet restore || {
        log_warning "Failed to restore packages. Some dependencies might be missing."
    }
    
    # Set environment variables for local development
    export MONGODB_HOST="localhost"
    export MONGODB_PORT="27017"
    export MONGODB_DATABASE="burbuja_engine"
    export APP_HOST="0.0.0.0"
    export APP_PORT="8000"
    export ASPNETCORE_ENVIRONMENT="Development"
    export ASPNETCORE_URLS="http://+:8000"
    
    log_info "🚀 Starting BurbujaEngine application with hot reload..."
    log_info "📍 Application will be available at: http://localhost:8000"
    log_info "📚 API documentation at: http://localhost:8000/swagger"
    log_info "🔄 Hot reload enabled - code changes will restart the server"
    log_info "🛑 Press Ctrl+C to stop the server"
    echo
    
    # Run dotnet with hot reload
    dotnet run --urls "http://0.0.0.0:8000"
}

# ===== ERROR HANDLING =====
handle_error() {
    local exit_code=$1
    local error_context=$2
    
    log_error "Error during: $error_context"
    
    echo
    log_info "Quick diagnostics:"
    $DOCKER_COMPOSE ps || true
    
    echo
    log_info "Recent logs:"
    $DOCKER_COMPOSE logs --tail=20 || true
    
    echo
    log_error "Startup failed. Check the logs for more details."
    log_info "Try: ./start.sh --clean --rebuild"
    
    exit $exit_code
}

# ===== MAIN EXECUTION =====
main() {
    parse_args "$@"
    show_banner
    
    # Check if production mode is requested
    if [[ "$PROD_MODE" == "true" ]]; then
        log_error "🚧 Production mode is not yet implemented"
        log_info "TODO: Implement full containerized deployment with both API and database containers"
        show_project_info
        exit 1
    fi
    
    # Check prerequisites
    verify_dependencies
    setup_local_environment
    check_volume_directories
    
    # Clean if requested
    if [[ "$FORCE_CLEAN" == "true" ]]; then
        force_clean
    fi
    
    # Development mode: MongoDB + local dotnet
    if [[ "$DEV_MODE" == "true" ]]; then
        log_info "🚀 Starting in development mode: MongoDB container + local dotnet with hot reload"
        
        # Start only MongoDB
        if ! start_services; then
            handle_error 1 "database startup"
        fi
        
        # Monitor MongoDB startup
        if ! monitor_app_startup; then
            log_warning "MongoDB container setup completed with warnings"
        fi
        
        # Quick health check for MongoDB
        set +e  # Disable error trap for health checks
        log_step "Performing MongoDB health check..."
        sleep 3  # Give MongoDB a moment
        
        if verify_services_health; then
            test_database_connection
            log_success "✅ MongoDB container is ready!"
        else
            log_info "🗄️ MongoDB container is starting up..."
            log_info "💡 Continuing with dotnet startup..."
        fi
        
        set -e  # Re-enable error handling
        
        # Run dotnet locally with hot reload
        run_dotnet_dev
        return 0
    fi
    
    # Database-only mode: simplified startup
    if [[ "$ONLY_DATABASE" == "true" ]]; then
        log_info "🗄️ Starting in database-only mode for local C# development"
        
        # Start only MongoDB
        if ! start_services; then
            handle_error 1 "database startup"
        fi
        
        # Monitor MongoDB startup
        if ! monitor_app_startup; then
            log_warning "MongoDB container setup completed with warnings"
        fi
        
        # Quick health check
        set +e  # Disable error trap for health checks
        log_step "Performing MongoDB health check..."
        sleep 3  # Give MongoDB a moment
        
        if verify_services_health; then
            test_database_connection
            show_project_info
            log_success "✅ MongoDB container is ready for local C# development!"
            log_info "💡 You can now run 'dotnet run' to start your BurbujaEngine app locally"
        else
            log_info "🗄️ MongoDB container is starting up..."
            show_project_info
            log_info "💡 Try connecting in a few seconds with your BurbujaEngine app"
        fi
        
        set -e  # Re-enable error handling
        return 0
    fi
    
    # Default mode - show available options
    show_project_info
    log_info "Select a mode: --dev, --only-database, or --prod (TODO)"
}

# ===== CLEANUP FUNCTION =====
cleanup() {
    log_info "Cleaning up..."
    # Any cleanup tasks if needed
}

# ===== ERROR HANDLING =====
trap 'handle_error $LINENO "script execution"' ERR
trap cleanup EXIT

# ===== SCRIPT OPTIONS =====
case "${1:-}" in
    "--help"|"-h")
        print_usage
        exit 0
        ;;
    "--cleanup")
        log_info "Cleaning up Docker containers and volumes..."
        $DOCKER_COMPOSE down -v 2>/dev/null || true
        docker system prune -f
        log_success "Cleanup completed"
        exit 0
        ;;
esac

# Run main function
main "$@"
