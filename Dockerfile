# BurbujaEngine Dockerfile
# Multi-stage build for .NET 9.0 application

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy project file
COPY burbuja-engine/*.csproj ./
RUN dotnet restore

# Copy source code
COPY burbuja-engine/ ./
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r burbujaengine && useradd -r -g burbujaengine burbujaengine

# Copy published application
COPY --from=build /app/out .

# Set ownership
RUN chown -R burbujaengine:burbujaengine /app

# Switch to non-root user
USER burbujaengine

# Expose port
EXPOSE 8000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8000/health || exit 1

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8000

# Start the application
ENTRYPOINT ["dotnet", "burbuja-engine.dll"]
