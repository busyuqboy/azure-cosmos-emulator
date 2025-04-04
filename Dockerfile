# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
LABEL author="Anthony Carroll"
USER app

# This stage is used to build the service project
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /azure_cosmos_emulator
COPY ["AzureCosmosEmulator.csproj", "."]
RUN dotnet restore "./AzureCosmosEmulator.csproj"
COPY . .
WORKDIR "/azure_cosmos_emulator/."
RUN dotnet build "./AzureCosmosEmulator.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Debug
RUN dotnet publish "./AzureCosmosEmulator.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ARG environment=Production
ENV ASPNETCORE_ENVIRONMENT=$environment

# Install curl to check for CosmosDB Emulator in Development
USER root
RUN if [ "$ASPNETCORE_ENVIRONMENT" = "Development" ]; then \
    apt-get update && apt-get install -y curl; \
    fi

COPY ["entrypoint.sh", "entrypoint.sh"]

RUN chmod +x entrypoint.sh

USER root
ENTRYPOINT ["./entrypoint.sh" ]