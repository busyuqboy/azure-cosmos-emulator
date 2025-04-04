# Azure Cosmos Emulator Integration

This project demonstrates how to integrate and interact with the Azure Cosmos DB Emulator within a Dockerized .NET Core application environment.

## Overview

The solution comprises two primary services:

1. **Azure Cosmos DB Emulator**: A Docker container running the Azure Cosmos DB Emulator, facilitating local development and testing without needing an actual Azure Cosmos DB instance.

2. **.NET Core Application**: A sample application that connects to the emulator to perform database operations, showcasing best practices for SSL certificate handling and network configurations in a containerized setup.

## Prerequisites

- [Docker](https://www.docker.com/get-started) installed on your development machine.
- [.NET SDK](https://dotnet.microsoft.com/download) installed for building and running the application.
- [Git](https://git-scm.com/downloads) installed for version control.

## Getting Started

### 1. Clone the Repository

```bash
git clone https://github.com/busyuqboy/azure-cosmos-emulator.git
cd azure-cosmos-emulator
```

### 2. (optional) Set up the Bridged network in any other container/application that you would like to access the emulator
In your docker-compose.yml file of docker-compose.yml file, add the following:

- Add the bridge network that is external to the app
```bash
networks:
  cosmos-network:
    external: true
```

- add the network to your image
```bash
services:
  my.app.container.name:
    ....
    networks:
      - cosmos-network
```

Note: SSL certificate issues when accessing Cosmos? See further for an example of how to copy the generated certificate during your client connection to Cosmos.

### 3. Start the Services
Use Docker Compose to build and run both the Cosmos DB Emulator and the .NET Core application:

```bash
docker-compose up --build
```
This command performs the following actions:
- Builds and starts the Azure Cosmos DB Emulator: Listens on https://azurecosmosemulator:8081/ with the IP address 172.16.238.246.
- Builds and starts the .NET Core application: Configured to connect to the emulator using the provided connection string.

The container will 

### 4. Run the application
Once the services are up and the certificate created and trusted, the .NET Core application can interact with the Cosmos DB Emulator seamlessly.


## Configuration Details

### Docker Network

The `cosmos-network` bridge network is configured with the subnet `172.16.238.0/24` to enable inter-container communication.

---

### Cosmos DB Emulator

- **Image**: `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest`  
- **Hostname**: `azurecosmosemulator`  
- **IP Address**: `172.16.238.246`  

**Ports Exposed**:
- `8081`: Data Explorer
- `8900-8902`, `10250-10256`, `10350`: Emulator services

**Environment Variables**:
- `AZURE_COSMOS_EMULATOR_PARTITION_COUNT=11`
- `AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE=true`
- `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE=172.16.238.246`

---

### .NET Core Application

**Environment Variables**:
- `ASPNETCORE_ENVIRONMENT=Development`
- `COSMOS_CONN=AccountEndpoint=https://azurecosmosemulator:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;`

**SSL Certificate Handling**:  
The application includes logic to programmatically retrieve and trust the emulator's self-signed SSL certificate during startup, ensuring secure communication between containers.

---

## Troubleshooting

### SSL Handling in your external container app
- Try setting the connection mode to `Gateway` connection mode.  
- In your initilization of your cosmos client, `detect the emulator url` and `extract the certificate` for SSL handshkes

1. **Detect the Emulator**:  
   Checks for `Development` environment and if the `url` contains `azurecosmosemulator`.

2. **Retrieve a copy of the SSL Certificate**:  
   
   In `CosmosClientOptions`, inject a custom `HttpClientFactory` that will copy the trusted certificate:

   ```csharp
   HttpClientFactory = () =>
   {
       if (url.Contains("azurecosmosemulator"))
       {
           var uri = new Uri(url);
           var cert = CosmosDBExtensions.GetServerCertificate(uri);
           var handler = new HttpClientHandler
           {
               ServerCertificateCustomValidationCallback = (request, serverCert, chain, errors) =>
               {
                   return serverCert?.GetCertHashString() == cert.GetCertHashString();
               }
           };
           return new HttpClient(handler);
       }

       return new HttpClient(); // Fallback for production
   };
   ```

   ```csharp
   private static X509Certificate2 GetServerCertificate(this Uri uri)
   {
       using var client = new TcpClient(uri.Host, uri.Port);
       using var sslStream = new SslStream(client.GetStream(), false, (sender, cert, chain, errors) => true);
       sslStream.AuthenticateAsClient(uri.Host);
       return new X509Certificate2(sslStream.RemoteCertificate);
   }
   ```

---
