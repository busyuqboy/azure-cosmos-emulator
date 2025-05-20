using AzureCosmosEmulator;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Cosmos emulator loaded and is initialized and ready.");

        var builder = new ConfigurationBuilder()
            .AddUserSecrets<Program>() // uses <UserSecretsId> from .csproj
            .Build();

        var cosmosDb = await CosmosDB.GetAsync();
        var client = cosmosDb.Client;

        if (client == null)
        {
            throw new Exception("CosmosClient is not initialized.");
        }

        var database = client.GetDatabase(cosmosDb.DatabaseId);

        Console.WriteLine("Checking for containers...");

        var containers = new Dictionary<string, string>
        {
            { "calls", "/companyId" },
            { "quotes", "/companyId" },
            { "quote-photos", "/quoteId" },
            { "call-requests", "/companyId" },
            { "event-notifications", "/companyId" },
            { "report-history", "/companyId" },
            { "activity-log", "/partitionKey" }
        };

        foreach (var kvp in containers)
        {
            try
            {
                await database.CreateContainerIfNotExistsAsync(new ContainerProperties
                {
                    Id = kvp.Key,
                    PartitionKeyPath = kvp.Value
                });

                Console.WriteLine($"✔ Container '{kvp.Key}' is ready.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to create/check container '{kvp.Key}': {ex.Message}");
            }
        }



    }
}
