using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AzureCosmosEmulator
{
    public class CosmosDB
    {
        public CosmosClient Client { get; private set; }
        
        public string DatabaseId => GetDatabaseName();
        private static string CosmosdbUrl { get { return "https://azurecosmosemulator:8081/"; } }
        private static string CosmosdbAuthKey { get { return "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="; } }
        
        private static readonly Lazy<Task<CosmosDB>> lazyInstance =
                new Lazy<Task<CosmosDB>>(PrivateInitAsync);

        public static Task<CosmosDB> GetInstanceAsync() => lazyInstance.Value;

        private static string GetDatabaseName()
        {
            return "towbook-dev";
        }


        private static async Task<CosmosDB> InitAsync()
        {
            var client = new CosmosDB();

            string userAgent = "AzureCosmosEmulatorApp";
            bool isBulk = false;

            var environment = CosmosDBExtensions.GetAppEnvironment();

            var connectionMode = ConnectionMode.Direct;
            if (environment.Equals("Development", StringComparison.OrdinalIgnoreCase) &&
                CosmosDBExtensions.IsEmulator(CosmosdbUrl))
                connectionMode = ConnectionMode.Gateway;

            var options = new CosmosClientOptions()
            {
                ConnectionMode = connectionMode,
                RequestTimeout = TimeSpan.FromSeconds(10),
                MaxRetryAttemptsOnRateLimitedRequests = 10,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
                Serializer = new CosmosJsonTowbookSerializer(),
                ApplicationName = userAgent,
                AllowBulkExecution = isBulk,

                // Apply custom handler only for development environment
                HttpClientFactory = () =>
                {
                    // Check if we're in Development and using the emulator
                    if (environment.Equals("Development", StringComparison.OrdinalIgnoreCase) && CosmosDBExtensions.IsEmulator(CosmosdbUrl))
                    {
                        var uri = new Uri(CosmosdbUrl);
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

                    // Return default HttpClient if not in Development or not the emulator
                    return new HttpClient();
                }
            };

            if (GetDatabaseName() == "braintree")
                options.SerializerOptions.PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase;

            if (CosmosDBExtensions.IsEmulator(CosmosdbUrl))
                options.LimitToEndpoint = true; // prevent region fallback

            client.Client = new CosmosClient(CosmosdbUrl, CosmosdbAuthKey, options);

            // Ensure the database exists before assigning it

            var resp = await client.Client.CreateDatabaseIfNotExistsAsync(GetDatabaseName());

            if (resp.StatusCode == System.Net.HttpStatusCode.Created)
                Console.WriteLine($"✔ Database {GetDatabaseName()} created and is ready to use.");
            else if (resp.StatusCode == System.Net.HttpStatusCode.OK)
                Console.WriteLine($"✔ Database {GetDatabaseName()} found and is ready to use.");
            else
                throw new Exception($"❌ Failed to create or find database {GetDatabaseName()}: {resp.StatusCode}");

            // Now get the database reference
            client.database = client.Client.GetDatabase(GetDatabaseName());

            Console.WriteLine($"CosmosDB database {GetDatabaseName()} initialized with {client.Client.Endpoint}-{options.ApplicationRegion}");

            return client;
        }

        /// <summary>
        /// Gets a thread-safe platform-wide cosmosDB object to work with.
        /// </summary>
        /// <returns></returns>
        private static async Task<CosmosDB> PrivateInitAsync()
        {
            var temp = await InitAsync();

            temp.database = temp.Client.GetDatabase(GetDatabaseName());

            return temp;
        }

        public static async Task<CosmosDB> GetAsync() => await GetInstanceAsync();

        private static readonly string[] validContainers = {
            "calls",
            "quotes",
            "call-requests",
            "gps-history",
            "event-notifications",
            "notifications",
            "braintree",
            "report-history",
        };

        public static void IsValidContainerName(string name)
        {
            if (!validContainers.Contains(name))
                throw new Exception("Unexpected Container Name, if this is valid update the IsValidContainerName list: " + name);
        }

        private Database database;
        public ItemResponse<T> InsertItem<T>(string container, T item)
        {
            IsValidContainerName(container);

            //var sw = Stopwatch.StartNew();
            try
            {
                return database.GetContainer(container).CreateItemAsync(item).Result;
            }
            finally
            {
                //System.Diagnostics.Debug.WriteLine("Upsert took " + sw.ElapsedMilliseconds + "ms");
            }
        }

        public async Task<T> InsertItemAsync<T>(string container, T item)
        {
            IsValidContainerName(container);
            return await database.GetContainer(container).CreateItemAsync(item);
        }

        public async Task UpsertItem<T>(string container, T item)
        {
            IsValidContainerName(container);

            await database.GetContainer(container).UpsertItemAsync(item,
                requestOptions: new ItemRequestOptions() { EnableContentResponseOnWrite = false });
        }

        public async Task UpsertItem<T>(string container, T item, PartitionKey pk) => await UpsertItem(container, item, pk, true);
        public async Task UpsertItem<T>(string container, T item, PartitionKey pk, bool allowRetry)
        {
            IsValidContainerName(container);

            try
            {
                await database.GetContainer(container).UpsertItemAsync(item,
                    partitionKey: pk,
                    requestOptions: new ItemRequestOptions() { EnableContentResponseOnWrite = false });
            }
            catch (CosmosException ce)
            {
                if (ce.SubStatusCode == 3200 && allowRetry)
                {
                    await Task.Delay(500);
                    await UpsertItem(container, item, pk, false);
                    return;
                }

                throw;
            }
        }

        public Task UpsertItemBulk<T>(string container, T item, PartitionKey pk)
        {
            IsValidContainerName(container);
            Console.WriteLine("** BULK FLOW ** " + container + pk.ToString());
            var s = Stopwatch.StartNew();
            try
            {
                return database.GetContainer(container).UpsertItemAsync(item,
                    partitionKey: pk,
                    requestOptions: new ItemRequestOptions() { EnableContentResponseOnWrite = false });
            }
            finally
            {
                Console.WriteLine(s.ElapsedMilliseconds + "ms");
            }
        }

        public async Task DeleteItem<T>(string container, string id, int key)
        {
            await DeleteItem<T>(container, id, new PartitionKey(key));
        }

        public async Task DeleteItem<T>(string container, string id, PartitionKey key)
        {
            IsValidContainerName(container);

            try
            {
                await database.GetContainer(container).DeleteItemAsync<T>(id, key,
                    new ItemRequestOptions() { EnableContentResponseOnWrite = false });
            }
            catch (Exception y)
            {
                Console.WriteLine(y.Message);
            }
        }

        public ItemResponse<T> GetItem<T>(string container, string id, PartitionKey key)
        {
            IsValidContainerName(container);

            try
            {
                return database.GetContainer(container).ReadItemAsync<T>(id, key).Result;
            }
            catch (Exception y)
            {
                Debug.WriteLine(y.Message);
                return null;
            }
        }

        public async Task<ItemResponse<T>> GetItemAsync<T>(string container, string id, PartitionKey key)
        {
            IsValidContainerName(container);

            try
            {
                return await database.GetContainer(container).ReadItemAsync<T>(id, key);
            }
            catch (Exception y)
            {
                 Debug.WriteLine(y.Message);
                return null;
            }
        }

        public Collection<T> QueryItems<T>(string container, QueryDefinition sqlQuery)
        {
            IsValidContainerName(container);
            var result = new Collection<T>();
            string continuationToken = null;
            do
            {

                using (var feedIterator = database.GetContainer(container).GetItemQueryIterator<T>(sqlQuery,
                    continuationToken))
                {

                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<T> feedResponse = feedIterator.ReadNextAsync().Result;
                        continuationToken = feedResponse.ContinuationToken;
                        foreach (T item in feedResponse)
                        {
                            result.Add(item);
                        }
                    }
                }
            } while (continuationToken != null);

            return result;
        }

        public async Task<Collection<T>> QueryItemsAsync<T>(string container,
            QueryDefinition sqlQuery,
            string partitionKey = null)
        {
            IsValidContainerName(container);
            var result = new Collection<T>();

            QueryRequestOptions ro = null;

            if (partitionKey != null)
                ro = new QueryRequestOptions() { PartitionKey = new PartitionKey(Convert.ToDouble(partitionKey)) };

            using (var feedIterator = database.GetContainer(container).GetItemQueryIterator<T>(sqlQuery, null, ro))
            {
                while (feedIterator.HasMoreResults)
                {
                    var feedResponse = await feedIterator.ReadNextAsync();

                    foreach (T item in feedResponse)
                    {
                        result.Add(item);
                    }
                }
            }

            return result;
        }

        public async Task<T> QueryScalarAsync<T>(string container,
           QueryDefinition sqlQuery,
           string partitionKey = null)
        {
            IsValidContainerName(container);

            return await QueryScalarAsync<T>(database.GetContainer(container), sqlQuery, partitionKey);
        }

        public async Task<T> QueryScalarAsync<T>(Container container,
           QueryDefinition sqlQuery,
           string partitionKey = null)
        {
            var result = new Collection<T>();

            QueryRequestOptions ro = null;

            if (partitionKey != null)
                ro = new QueryRequestOptions() { PartitionKey = new PartitionKey(Convert.ToDouble(partitionKey)) };

            using (var feedIterator = container.GetItemQueryIterator<T>(sqlQuery, null, ro))
            {
                while (feedIterator.HasMoreResults)
                {
                    var feedResponse = await feedIterator.ReadNextAsync();

                    return feedResponse.SingleOrDefault();
                }
            }

            return default(T);
        }
    }

    public static class CosmosDBExtensions
    {
        /// <summary>
        /// Retrieves the SSL certificate from the specified Uri host and port.
        /// </summary>
        /// <param name="uri">The Uri to connect to (must include host and port).</param>
        /// <returns>The remote X509Certificate2 presented by the server.</returns>
        public static X509Certificate2 GetServerCertificate(this Uri uri)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));

            using var client = new TcpClient(uri.Host, uri.Port);
            using var sslStream = new SslStream(client.GetStream(), false, (sender, cert, chain, errors) => true);
            sslStream.AuthenticateAsClient(uri.Host);
            return new X509Certificate2(sslStream.RemoteCertificate);
        }

        public static string GetAppEnvironment()
        {
            return System.Configuration.ConfigurationManager.AppSettings["WebAppEnvironment"]
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? "Production";
        }

        public static bool IsEmulator(string url) => url.Contains("localhost") || url.Contains("azurecosmosemulator");
    }

    internal sealed class CosmosJsonTowbookSerializer : CosmosSerializer
    {
        private static readonly Encoding DefaultEncoding = new UTF8Encoding(false, true);
        private readonly JsonSerializerSettings SerializerSettings;

        /// <summary>
        /// Create a serializer that uses the JSON.net serializer
        /// </summary>
        /// <remarks>
        /// This is internal to reduce exposure of JSON.net types so
        /// it is easier to convert to System.Text.Json
        /// </remarks>
        internal CosmosJsonTowbookSerializer()
        {
            var jsonSerializerSettings = new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented,
                ContractResolver = new ShouldSerializeContractResolver()
            };

            this.SerializerSettings = jsonSerializerSettings;
        }

        /// <summary>
        /// Convert a Stream to the passed in type.
        /// </summary>
        /// <typeparam name="T">The type of object that should be deserialized</typeparam>
        /// <param name="stream">An open stream that is readable that contains JSON</param>
        /// <returns>The object representing the deserialized stream</returns>
        public override T FromStream<T>(Stream stream)
        {
            using (stream)
            {
                if (typeof(Stream).IsAssignableFrom(typeof(T)))
                {
                    return (T)(object)stream;
                }

                using (StreamReader sr = new StreamReader(stream))
                {
                    using (JsonTextReader jsonTextReader = new JsonTextReader(sr))
                    {
                        JsonSerializer jsonSerializer = this.GetSerializer();
                        return jsonSerializer.Deserialize<T>(jsonTextReader);
                    }
                }
            }
        }

        /// <summary>
        /// Converts an object to a open readable stream
        /// </summary>
        /// <typeparam name="T">The type of object being serialized</typeparam>
        /// <param name="input">The object to be serialized</param>
        /// <returns>An open readable stream containing the JSON of the serialized object</returns>
        public override Stream ToStream<T>(T input)
        {
            MemoryStream streamPayload = new MemoryStream();
            using (StreamWriter streamWriter = new StreamWriter(streamPayload,
                encoding: CosmosJsonTowbookSerializer.DefaultEncoding, bufferSize: 1024, leaveOpen: true))
            {
                using (JsonWriter writer = new JsonTextWriter(streamWriter))
                {
                    writer.Formatting = Newtonsoft.Json.Formatting.None;
                    JsonSerializer jsonSerializer = this.GetSerializer();
                    jsonSerializer.Serialize(writer, input);
                    writer.Flush();
                    streamWriter.Flush();
                }
            }

            streamPayload.Position = 0;
            return streamPayload;
        }

        /// <summary>
        /// JsonSerializer has hit a race conditions with custom settings that cause null reference exception.
        /// To avoid the race condition a new JsonSerializer is created for each call
        /// </summary>
        private JsonSerializer GetSerializer()
        {
            return JsonSerializer.Create(this.SerializerSettings);
        }
    }

    public class ShouldSerializeContractResolver : CamelCasePropertyNamesContractResolver
    {
        public static readonly ShouldSerializeContractResolver Instance = new ShouldSerializeContractResolver();

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            if (property.DeclaringType.Name == "CallModel")
            {
                switch (property.PropertyName)
                {
                    case "id":
                        {
                            if (property.PropertyType == typeof(int))
                            {

                                property.PropertyType = typeof(string);
                                property.Converter = new KeysJsonConverter(typeof(int));
                                property.Ignored = false;
                                property.ShouldSerialize =
                                    instance =>
                                    {
                                        return true;
                                    };
                            }
                        }
                        break;
                    case "availableActions":
                    case "channels":
                    case "chatChannelSid":
                    case "lastModifiedTimestamp":
                    case "statuses":
                    case "_ts":
                        property.Ignored = true;
                        break;
                }
            }
            if (property.DeclaringType.Name == "CallContactModel")
            {
                if (property.PropertyType == typeof(string))
                {
                    property.ShouldSerialize =
                        instance => !string.IsNullOrWhiteSpace(instance?.GetType().GetProperty(property.UnderlyingName)?.GetValue(instance) as string);
                }

                switch (property.PropertyName)
                {
                    case "isProblemCustomer":
                    case "callId":
                        property.Ignored = true;
                        break;
                }


            }
            else if (property.DeclaringType.Name == "ActivityLogItem")
            {
                switch (property.PropertyName)
                {
                    case "id":
                        {
                            if (property.PropertyType == typeof(long))
                            {
                                property.PropertyType = typeof(string);
                                property.Converter = new KeysJsonConverter(typeof(long));
                                property.Ignored = false;
                                property.ShouldSerialize =
                                    instance =>
                                    {
                                        return true;
                                    };
                            }
                        }
                        break;
                }
            }
            else if (property.DeclaringType.Name == "NotificationMessage")
            {
                switch (property.PropertyName)
                {
                    case "id":
                        {
                            if (property.PropertyType == typeof(int))
                            {
                                property.PropertyType = typeof(string);
                                property.Converter = new KeysJsonConverter(typeof(int));
                                property.Ignored = false;
                                property.ShouldSerialize =
                                    instance =>
                                    {
                                        return true;
                                    };
                            }
                        }
                        break;
                    case "json":
                        {
                            if (property.PropertyType == typeof(string))
                            {
                                property.PropertyType = typeof(JObject);
                                property.Converter = new JsonPropertyJsonConverter();
                                property.Ignored = false;
                                property.ShouldSerialize =
                                    instance =>
                                    {
                                        return true;
                                    };
                            }
                        }
                        break;
                }
            }
            else if (property.DeclaringType.Name == "QuoteModel")
            {
                switch (property.PropertyName)
                {
                    case "availableActions":
                    case "url":
                        property.Ignored = true;
                        break;
                }
            }

            return property;
        }
    }

    public class JsonPropertyJsonConverter : JsonConverter
    {
        private readonly Type[] _types;

        public JsonPropertyJsonConverter(params Type[] types)
        {
            _types = types;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // turn the string into a json object.
            JToken t = JToken.Parse(value.ToString());
            t.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // turn the json object into a string.
            var jo = JObject.Load(reader);

            return jo.ToString();
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanConvert(Type objectType)
        {
            return true;
        }
    }

    public class KeysJsonConverter : JsonConverter
    {
        private readonly Type[] _types;

        public KeysJsonConverter(params Type[] types)
        {
            _types = types;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            JToken t = JToken.FromObject(value.ToString());
            t.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return Convert.ToInt32(reader.Value);
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanConvert(Type objectType)
        {
            return true;
        }
    }


}
