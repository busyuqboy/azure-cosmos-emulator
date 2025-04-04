#!/bin/bash
#set -e

echo "$ASPNETCORE_ENVIRONMENT environment..."

if [ "$ASPNETCORE_ENVIRONMENT" == "Development" ]; then

    cosmosHost=azurecosmosemulator
    cosmosPort=8081

    # Wait for CosmosDB to be available, a health check from the container that is connecting to CosmosDB
    echo "Waiting for local dev CosmosDB emulator at $cosmosHost:$cosmosPort..."
    until [ "$(curl -k -s --connect-timeout 5 -o /dev/null -w "%{http_code}" https://$cosmosHost:${cosmosPort}/_explorer/emulator.pem)" == "200" ]; do
        sleep 5;
        echo "Waiting for CosmosDB at $cosmosHost:$cosmosPort..."
    done;
    echo "CosmosDB is available."

    # Download the CosmosDB Cert and add it to the Trusted Certs
    echo "Downloading CosmosDB Cert..."
    curl -k https://$cosmosHost:${cosmosPort}/_explorer/emulator.pem > emulatorcert.crt

    echo "Adding CosmosDB Cert to Trusted Certs..."
    cp emulatorcert.crt /usr/local/share/ca-certificates/
    update-ca-certificates
fi

echo "Running Azure Cosmos Emulator console app using .NET SDK.."
dotnet AzureCosmosEmulator.dll