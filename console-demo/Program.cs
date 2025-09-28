using MongoDB.Driver;
using MongoDB.Driver.Encryption;
using MongoDB.Bson;
using System.Security.Cryptography;
using DotNetEnv;

namespace Program;

public class Program
{
    static readonly string ConnectionString;
    static readonly string DatabaseName;
    static readonly string CollectionName;
    static readonly string KeyVaultDatabaseName;
    static readonly string KeyVaultCollectionName;

    static Program()
    {
        Env.Load();
        ConnectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") ?? throw new InvalidOperationException("CONNECTION_STRING not found in environment");
        DatabaseName = Environment.GetEnvironmentVariable("DATABASE_NAME") ?? "qe-test";
        CollectionName = Environment.GetEnvironmentVariable("TEST_COLLECTION_NAME") ?? "employees";
        KeyVaultDatabaseName = Environment.GetEnvironmentVariable("KEY_VAULT_DATABASE_NAME") ?? "encryption-test";
        KeyVaultCollectionName = Environment.GetEnvironmentVariable("KEY_VAULT_COLLECTION_NAME") ?? "__keyVault";
    }

    static async Task Main(string[] args)
    {
        MongoClientSettings.Extensions.AddAutoEncryption();

        Console.WriteLine("=== MongoDB 8.2 Preview Query Types Test ===");
        Console.WriteLine("Testing prefixPreview, suffixPreview, substringPreview, range, and equality query types");

        try
        {
            // Step 1: Clean up existing data
            await TestDatabaseCleanup();

            // Step 2: Create encrypted collection with preview query types
            await CreateSimpleEncryptedCollection();

            // Step 3: Test document insertion
            await TestInsertDocuments();

            // Step 4: Test queries
            await TestQueries();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Test failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    static async Task TestDatabaseCleanup()
    {
        Console.WriteLine("\n=== Step 1: Database Cleanup ===");

        var plainClient = new MongoClient(ConnectionString);
        var plainDatabase = plainClient.GetDatabase(DatabaseName);

        Console.WriteLine($"Dropping existing collection '{CollectionName}'...");
        await plainDatabase.DropCollectionAsync(CollectionName);
        Console.WriteLine($"✓ Collection '{CollectionName}' dropped successfully");
    }

    static async Task CreateSimpleEncryptedCollection()
    {
        Console.WriteLine("\n=== Step 2: Creating Encrypted Collection ===");

        var (clientEncryption, dataKeys, plainClient, _) = await SetupEncryption();
        var plainDatabase = plainClient.GetDatabase(DatabaseName);

        var encryptedFields = new BsonDocument
        {
            {
                "fields", new BsonArray
                {
                    new BsonDocument
                    {
                        { "keyId", new BsonBinaryData(dataKeys["firstName"], GuidRepresentation.Standard) },
                        { "path", "firstName" },
                        { "bsonType", "string" },
                        { "queries", new BsonArray
                            {
                                new BsonDocument
                                {
                                    { "queryType", "prefixPreview" },
                                    { "strMinQueryLength", 2 },
                                    { "strMaxQueryLength", 10 },
                                    { "caseSensitive", true },
                                    { "diacriticSensitive", true }
                                }
                            }
                        }
                    },
                    new BsonDocument
                    {
                        { "keyId", new BsonBinaryData(dataKeys["lastName"], GuidRepresentation.Standard) },
                        { "path", "lastName" },
                        { "bsonType", "string" },
                        { "queries", new BsonArray
                            {
                                new BsonDocument
                                {
                                    { "queryType", "suffixPreview" },
                                    { "strMinQueryLength", 2 },
                                    { "strMaxQueryLength", 10 },
                                    { "caseSensitive", true },
                                    { "diacriticSensitive", true }
                                }
                            }
                        }
                    },
                    new BsonDocument
                    {
                        { "keyId", new BsonBinaryData(dataKeys["description"], GuidRepresentation.Standard) },
                        { "path", "description" },
                        { "bsonType", "string" },
                        { "queries", new BsonArray
                            {
                                new BsonDocument
                                {
                                    { "queryType", "substringPreview" },
                                    { "strMaxLength", 25 },
                                    { "strMinQueryLength", 2 },
                                    { "strMaxQueryLength", 10 },
                                    { "caseSensitive", true },
                                    { "diacriticSensitive", true }
                                }
                            }
                        }
                    },
                    new BsonDocument
                    {
                        { "keyId", new BsonBinaryData(dataKeys["department"], GuidRepresentation.Standard) },
                        { "path", "department" },
                        { "bsonType", "string" },
                        { "queries", new BsonArray
                            {
                                new BsonDocument
                                {
                                    { "queryType", "equality" }
                                }
                            }
                        }
                    },
                    new BsonDocument
                    {
                        { "keyId", new BsonBinaryData(dataKeys["salary"], GuidRepresentation.Standard) },
                        { "path", "salary" },
                        { "bsonType", "int" },
                        { "queries", new BsonArray
                            {
                                new BsonDocument
                                {
                                    { "queryType", "range" },
                                    { "sparsity", 1 },
                                    { "min", 30000 },
                                    { "max", 200000 },
                                    { "trimFactor", 4 }
                                }
                            }
                        }
                    }
                }
            }
        };

        var createCollectionOptions = new CreateCollectionOptions
        {
            EncryptedFields = encryptedFields
        };

        Console.WriteLine("Creating encrypted collection with preview query types...");
        try
        {
            await plainDatabase.CreateCollectionAsync(CollectionName, createCollectionOptions);

            Console.WriteLine($"✓ Successfully created encrypted collection '{CollectionName}'");
            Console.WriteLine($"✓ Encrypted fields schema applied for preview query types");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error creating encrypted collection: {ex.Message}");
            throw;
        }
    }

    static async Task TestInsertDocuments()
    {
        Console.WriteLine("\n=== Step 3: Testing Document Insertion with Explicit Encryption ===");

        var (clientEncryption, dataKeys, plainClient, kmsProviders) = await SetupEncryption();
        var encryptedCollection = GetEncryptedCollection(kmsProviders);

        // Prepare test data - 10 documents
        var testData = new[]
        {
            new { firstName = "John", lastName = "Smith", description = "senior engineer", department = "Engineering", salary = 85000 },
            new { firstName = "Jane", lastName = "Johnson", description = "data scientist", department = "Analytics", salary = 95000 },
            new { firstName = "Michael", lastName = "Williams", description = "product manager", department = "Product", salary = 110000 },
            new { firstName = "Sarah", lastName = "Brown", description = "frontend developer", department = "Engineering", salary = 120000 },
            new { firstName = "David", lastName = "Jones", description = "software architect", department = "Engineering", salary = 130000 },
            new { firstName = "Emily", lastName = "Davis", description = "business analyst", department = "Analytics", salary = 105000 },
            new { firstName = "James", lastName = "Miller", description = "backend engineer", department = "Operations", salary = 100000 },
            new { firstName = "Jennifer", lastName = "Wilson", description = "fullstack developer", department = "Engineering", salary = 90000 },
            new { firstName = "Robert", lastName = "Moore", description = "devops engineer", department = "Engineering", salary = 95000 },
            new { firstName = "Jessica", lastName = "Taylor", description = "mobile developer", department = "Engineering", salary = 115000 }
            // new { firstName = "Alexander", lastName = "Anderson", description = "senior software development engineer", department = "Engineering", salary = 135000 }
        };

        try
        {
            Console.WriteLine("\nInserting documents with correct encryption algorithms per field...");

            foreach (var data in testData)
            {
                // firstName with prefixPreview support
                var encryptedFirstName = clientEncryption.Encrypt(
                    data.firstName,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.TextPreview,
                        keyId: dataKeys["firstName"],
                        textOptions: new TextOptions(
                            caseSensitive: true,
                            diacriticSensitive: true,
                            prefixOptions: new PrefixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                        ),
                        contentionFactor: 0
                    )
                );

                // lastName with suffixPreview support
                var encryptedLastName = clientEncryption.Encrypt(
                    data.lastName,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.TextPreview,
                        keyId: dataKeys["lastName"],
                        textOptions: new TextOptions(
                            caseSensitive: true,
                            diacriticSensitive: true,
                            suffixOptions: new SuffixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                        ),
                        contentionFactor: 0
                    )
                );

                // description with substringPreview support
                var encryptedDescription = clientEncryption.Encrypt(
                    data.description,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.TextPreview,
                        keyId: dataKeys["description"],
                        textOptions: new TextOptions(
                            caseSensitive: true,
                            diacriticSensitive: true,
                            substringOptions: new SubstringOptions(strMaxLength: 25, strMaxQueryLength: 10, strMinQueryLength: 2)
                        ),
                        contentionFactor: 0
                    )
                );

                // department with equality support
                var encryptedDepartment = clientEncryption.Encrypt(
                    data.department,
                    new EncryptOptions(algorithm: EncryptionAlgorithm.Indexed, keyId: dataKeys["department"], contentionFactor: 0)
                );

                // salary with range support
                var encryptedSalary = clientEncryption.Encrypt(
                    data.salary,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.Range,
                        keyId: dataKeys["salary"],
                        rangeOptions: new RangeOptions(
                            min: new BsonInt32(30000),
                            max: new BsonInt32(200000),
                            sparsity: 1,
                            trimFactor: 4
                        ),
                        contentionFactor: 0
                    )
                );

                var document = new BsonDocument
                {
                    { "firstName", encryptedFirstName },
                    { "lastName", encryptedLastName },
                    { "description", encryptedDescription },
                    { "department", encryptedDepartment },
                    { "salary", encryptedSalary }
                };

                await encryptedCollection.InsertOneAsync(document);
            }

            Console.WriteLine($"✓ Successfully inserted {testData.Length} documents with all fields encrypted using correct algorithms!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error during insertion: {ex.Message}");
            throw;
        }
    }

    static async Task TestQueries()
    {
        Console.WriteLine("\n=== Step 4: Testing Preview Query Types ===");

        // Get shared encryption setup
        var (clientEncryption, dataKeys, _, kmsProviders) = await SetupEncryption();
        var encryptedCollection = GetEncryptedCollection(kmsProviders);

        try
        {
            // Test 1: Prefix query
            Console.WriteLine("\n--- Testing prefixPreview on firstName ---");
            Console.WriteLine("Searching for firstName starting with 'Joh'...");

            var encryptedQueryPrefix = clientEncryption.Encrypt(
                "Joh",
                new EncryptOptions(
                    algorithm: EncryptionAlgorithm.TextPreview,
                    keyId: dataKeys["firstName"],
                    queryType: "prefixPreview",
                    textOptions: new TextOptions(
                        caseSensitive: true,
                        diacriticSensitive: true,
                        prefixOptions: new PrefixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                    ),
                    contentionFactor: 0
                )
            );

            var prefixFilter = new BsonDocument
            {
                {
                    "$expr", new BsonDocument
                    {
                        {
                            "$encStrStartsWith", new BsonDocument
                            {
                                { "input", "$firstName" },
                                { "prefix", encryptedQueryPrefix }
                            }
                        }
                    }
                }
            };

            var prefixResults = await encryptedCollection.Find(prefixFilter).ToListAsync();

            Console.WriteLine($"Found {prefixResults.Count} documents with firstName starting with 'Joh'");

            // Test 2: Suffix query
            Console.WriteLine("\n--- Testing suffixPreview on lastName ---");
            Console.WriteLine("Searching for lastName ending with 'son'...");

            var encryptedQuerySuffix = clientEncryption.Encrypt(
                "son",
                new EncryptOptions(
                    algorithm: EncryptionAlgorithm.TextPreview,
                    keyId: dataKeys["lastName"],
                    queryType: "suffixPreview",
                    textOptions: new TextOptions(
                        caseSensitive: true,
                        diacriticSensitive: true,
                        suffixOptions: new SuffixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                    ),
                    contentionFactor: 0
                )
            );

            var suffixFilter = new BsonDocument
            {
                {
                    "$expr", new BsonDocument
                    {
                        {
                            "$encStrEndsWith", new BsonDocument
                            {
                                { "input", "$lastName" },
                                { "suffix", encryptedQuerySuffix }
                            }
                        }
                    }
                }
            };

            var suffixResults = await encryptedCollection.Find(suffixFilter).ToListAsync();

            Console.WriteLine($"Found {suffixResults.Count} documents with lastName ending with 'son'");

            // Test 3: Substring query
            Console.WriteLine("\n--- Testing substringPreview on description ---");
            Console.WriteLine("Searching for description containing 'dev'...");

            var encryptedQuerySubstring = clientEncryption.Encrypt(
                "dev",
                new EncryptOptions(
                    algorithm: EncryptionAlgorithm.TextPreview,
                    keyId: dataKeys["description"],
                    queryType: "substringPreview",
                    textOptions: new TextOptions(
                        caseSensitive: true,
                        diacriticSensitive: true,
                        substringOptions: new SubstringOptions(strMaxLength: 25, strMaxQueryLength: 10, strMinQueryLength: 2)
                    ),
                    contentionFactor: 0
                )
            );

            var substringFilter = new BsonDocument
            {
                {
                    "$expr", new BsonDocument
                    {
                        {
                            "$encStrContains", new BsonDocument
                            {
                                { "input", "$description" },
                                { "substring", encryptedQuerySubstring }
                            }
                        }
                    }
                }
            };

            var substringResults = await encryptedCollection.Find(substringFilter).ToListAsync();

            Console.WriteLine($"Found {substringResults.Count} documents with description containing 'dev'");

            // Test 4: Equality query
            Console.WriteLine("\n--- Testing equality query on department ---");
            Console.WriteLine("Searching for department = 'Engineering'...");

            var findPayload = clientEncryption.Encrypt(
                "Engineering",
                new EncryptOptions(algorithm: EncryptionAlgorithm.Indexed, keyId: dataKeys["department"], queryType: "equality", contentionFactor: 0)
            );

            var engineeringDocs = await encryptedCollection.Find(new BsonDocument { { "department", findPayload } }).ToListAsync();

            Console.WriteLine($"Found {engineeringDocs.Count} employees in Engineering department");

            // Test 5: Range query
            Console.WriteLine("\n--- Testing range query on salary ---");
            Console.WriteLine("Searching for salary between $90,000 and $110,000...");

            var rangeExpression = new BsonDocument
            {
                {
                    "$and", new BsonArray
                    {
                        new BsonDocument { { "salary", new BsonDocument { { "$gte", 90000 } } } },
                        new BsonDocument { { "salary", new BsonDocument { { "$lte", 110000 } } } }
                    }
                }
            };

            var encryptOptions = new EncryptOptions(
                algorithm: EncryptionAlgorithm.Range,
                keyId: dataKeys["salary"],
                rangeOptions: new RangeOptions(
                    min: new BsonInt32(30000),
                    max: new BsonInt32(200000),
                    sparsity: 1,
                    trimFactor: 4
                ),
                queryType: "range",
                contentionFactor: 0
            );

            var encryptedRangeFilter = await clientEncryption.EncryptExpressionAsync(rangeExpression, encryptOptions);
            var rangeResults = await encryptedCollection.Find(encryptedRangeFilter).ToListAsync();

            Console.WriteLine($"Found {rangeResults.Count} documents with salary $90K-$110K");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error during query: {ex.Message}");
            throw;
        }
    }

    private static IMongoCollection<BsonDocument> GetEncryptedCollection(Dictionary<string, IReadOnlyDictionary<string, object>> kmsProviders)
    {
        var keyVaultNamespace = new CollectionNamespace(KeyVaultDatabaseName, KeyVaultCollectionName);

        var autoEncryptionOptions = new AutoEncryptionOptions(
            keyVaultNamespace: keyVaultNamespace,
            kmsProviders: kmsProviders,
            bypassQueryAnalysis: true
        );

        var encryptedClientSettings = MongoClientSettings.FromConnectionString(ConnectionString);
        encryptedClientSettings.AutoEncryptionOptions = autoEncryptionOptions;
        var encryptedClient = new MongoClient(encryptedClientSettings);
        var encryptedDatabase = encryptedClient.GetDatabase(DatabaseName);
        return encryptedDatabase.GetCollection<BsonDocument>(CollectionName);
    }

    private static async Task<(ClientEncryption clientEncryption, Dictionary<string, Guid> dataKeys, MongoClient plainClient, Dictionary<string, IReadOnlyDictionary<string, object>> kmsProviders)> SetupEncryption()
    {
        var localMasterKey = GetLocalMasterKey();
        var kmsProviders = new Dictionary<string, IReadOnlyDictionary<string, object>>
        {
            { "local", new Dictionary<string, object> { { "key", localMasterKey } } }
        };

        var keyVaultNamespace = new CollectionNamespace(KeyVaultDatabaseName, KeyVaultCollectionName);
        var plainClient = new MongoClient(ConnectionString);

        var clientEncryptionOptions = new ClientEncryptionOptions(
            keyVaultClient: plainClient,
            keyVaultNamespace: keyVaultNamespace,
            kmsProviders: kmsProviders
        );
        var clientEncryption = new ClientEncryption(clientEncryptionOptions);

        // Create or reuse data keys for each field
        var dataKeys = new Dictionary<string, Guid>();
        var fieldNames = new[] { "firstName", "lastName", "description", "department", "salary" };
        var keyVaultCollection = plainClient.GetDatabase(KeyVaultDatabaseName).GetCollection<BsonDocument>(KeyVaultCollectionName);

        foreach (var fieldName in fieldNames)
        {
            var keyFilter = Builders<BsonDocument>.Filter.Eq("keyAltNames", fieldName);
            var existingKey = await keyVaultCollection.Find(keyFilter).FirstOrDefaultAsync();

            if (existingKey != null)
            {
                dataKeys[fieldName] = existingKey["_id"].AsGuid;
                Console.WriteLine($"Using existing data key for {fieldName}: {dataKeys[fieldName]}");
            }
            else
            {
                var dataKeyId = await clientEncryption.CreateDataKeyAsync("local",
                    new DataKeyOptions(alternateKeyNames: new[] { fieldName }),
                    CancellationToken.None);
                dataKeys[fieldName] = dataKeyId;
                Console.WriteLine($"Created new data key for {fieldName}: {dataKeys[fieldName]}");
            }
        }

        return (clientEncryption, dataKeys, plainClient, kmsProviders);
    }

    private static byte[] GetLocalMasterKey()
    {
        const string keyFileName = "customer-master-key.txt";

        if (!File.Exists(keyFileName))
        {
            Console.WriteLine($"Generating new local master key...");
            using var randomNumberGenerator = RandomNumberGenerator.Create();
            var bytes = new byte[96];
            randomNumberGenerator.GetBytes(bytes);
            var localCustomerMasterKeyBase64 = Convert.ToBase64String(bytes);
            File.WriteAllText(keyFileName, localCustomerMasterKeyBase64);
            Console.WriteLine($"✓ Generated and saved new local master key");
            return bytes;
        }
        else
        {
            Console.WriteLine($"Loading existing local master key...");
            var localCustomerMasterKeyBase64 = File.ReadAllText(keyFileName);
            var localCustomerMasterKeyBytes = Convert.FromBase64String(localCustomerMasterKeyBase64);

            if (localCustomerMasterKeyBytes.Length != 96)
            {
                throw new Exception("Expected the customer master key file to be 96 bytes.");
            }

            Console.WriteLine($"✓ Loaded existing local master key");
            return localCustomerMasterKeyBytes;
        }
    }
}