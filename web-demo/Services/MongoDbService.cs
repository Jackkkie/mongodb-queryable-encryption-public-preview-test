using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Encryption;
using MongoQEDemo.Configuration;
using MongoQEDemo.Models;
using MongoQEDemo.Services;

namespace MongoQEDemo.Services
{
    public class MongoDbService
    {
        private readonly IMongoClient _encryptedClient;
        private readonly IMongoDatabase _encryptedDatabase;
        private readonly IMongoCollection<Patient> _patientsCollection;
        private readonly string _connectionString;
        private readonly string _databaseName = "medical";
        private readonly string _collectionName = "patients";
        private readonly Dictionary<string, Guid> _dataKeys;
        private readonly ClientEncryption _clientEncryption;
        private readonly ILogger<MongoDbService> _logger;
        private volatile bool _collectionEnsured = false;
        private readonly SemaphoreSlim _collectionEnsureSemaphore = new(1, 1);

        public MongoDbService(string connectionString, ILogger<MongoDbService> logger)
        {
            _connectionString = connectionString;
            _logger = logger;

            // Initialize data keys dictionary
            _dataKeys = new Dictionary<string, Guid>();

            // Set up MongoDB client with encryption (tutorial approach)
            try
            {
                var localMasterKey = EncryptionConfiguration.GetOrCreateLocalMasterKey();
                var kmsProviders = new Dictionary<string, IReadOnlyDictionary<string, object>>
                {
                    { "local", new Dictionary<string, object> { { "key", localMasterKey } } }
                };

                var keyVaultNamespace = new CollectionNamespace("encryption", "__keyVault");

                // First create data keys to get their IDs for encryptedFieldsMap
                var clientEncryptionForKeyCreation = new ClientEncryption(new ClientEncryptionOptions(
                    keyVaultClient: new MongoClient(_connectionString),
                    keyVaultNamespace: keyVaultNamespace,
                    kmsProviders: kmsProviders
                ));

                var fieldNames = new[] { "firstName", "lastName", "dateOfBirth", "zipCode", "nationalId", "phoneNumber", "notes" };
                var keyVaultCollection = new MongoClient(_connectionString).GetDatabase("encryption").GetCollection<BsonDocument>("__keyVault");

                foreach (var fieldName in fieldNames)
                {
                    // Try to find existing key first
                    var filter = Builders<BsonDocument>.Filter.AnyEq("keyAltNames", fieldName);
                    var existingKey = keyVaultCollection.Find(filter).FirstOrDefault();

                    if (existingKey != null)
                    {
                        var keyId = existingKey["_id"].AsGuid;
                        _dataKeys[fieldName] = keyId;
                        Console.WriteLine($"Using existing data key for {fieldName}: {keyId}");
                    }
                    else
                    {
                        var dataKeyId = clientEncryptionForKeyCreation.CreateDataKey(
                            kmsProvider: "local",
                            dataKeyOptions: new DataKeyOptions(alternateKeyNames: new[] { fieldName })
                        );

                        _dataKeys[fieldName] = dataKeyId;
                        Console.WriteLine($"Created new data key for {fieldName}: {dataKeyId}");
                    }
                }

                var extraOptions = new Dictionary<string, object>();

                var autoEncryptionOptions = new AutoEncryptionOptions(
                    keyVaultNamespace: keyVaultNamespace,
                    kmsProviders: kmsProviders,
                    bypassQueryAnalysis: true,
                    extraOptions: extraOptions
                );

                MongoClientSettings.Extensions.AddAutoEncryption();

                var settings = MongoClientSettings.FromConnectionString(_connectionString);
                settings.AutoEncryptionOptions = autoEncryptionOptions;

                settings.MaxConnectionPoolSize = 200;
                settings.MinConnectionPoolSize = 32;
                settings.WaitQueueSize = 1000;
                settings.WaitQueueTimeout = TimeSpan.FromMinutes(5);
                settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(60);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(60);
                settings.ConnectTimeout = TimeSpan.FromSeconds(60);
                settings.SocketTimeout = TimeSpan.FromMinutes(10);

                _encryptedClient = new MongoClient(settings);
                _encryptedDatabase = _encryptedClient.GetDatabase(_databaseName);

                var clientEncryptionOptions = new ClientEncryptionOptions(
                    keyVaultClient: new MongoClient(_connectionString),
                    keyVaultNamespace: keyVaultNamespace,
                    kmsProviders: kmsProviders
                );
                _clientEncryption = new ClientEncryption(clientEncryptionOptions);

                Console.WriteLine("MongoDB client initialized with Queryable Encryption.");

                try
                {
                    var plainClient = new MongoClient(_connectionString);
                    var plainDatabase = plainClient.GetDatabase(_databaseName);
                    var collections = plainDatabase.ListCollectionNames().ToList();

                    if (!collections.Contains(_collectionName))
                    {
                        var createOptions = new CreateCollectionOptions
                        {
                            EncryptedFields = EncryptionConfiguration.GetEncryptedFieldsMap(_dataKeys)
                        };

                        plainDatabase.CreateCollection(_collectionName, createOptions);
                        Console.WriteLine($"Created encrypted collection '{_collectionName}' with preview query types using plain client.");
                    }
                    else
                    {
                        Console.WriteLine($"Using existing collection '{_collectionName}' with encryption configuration.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating encrypted collection: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting up encryption: {ex.Message}");
                throw;
            }

            _patientsCollection = _encryptedDatabase.GetCollection<Patient>(_collectionName);
        }

        private async Task EnsureEncryptedCollectionExistsAsync()
        {
            if (_collectionEnsured)
                return;

            await _collectionEnsureSemaphore.WaitAsync();
            try
            {
                if (_collectionEnsured)
                    return;

                var collections = await _encryptedDatabase.ListCollectionNamesAsync();
                var collectionList = await collections.ToListAsync();

                if (!collectionList.Contains(_collectionName))
                {
                    var createOptions = new CreateCollectionOptions
                    {
                        EncryptedFields = EncryptionConfiguration.GetEncryptedFieldsMap(_dataKeys)
                    };

                    var plainClient = new MongoClient(_connectionString);
                    var plainDatabase = plainClient.GetDatabase(_databaseName);
                    await plainDatabase.CreateCollectionAsync(_collectionName, createOptions);
                    Console.WriteLine($"Created encrypted collection '{_collectionName}' with preview query types using plain client during runtime.");
                }

                _collectionEnsured = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ensuring encrypted collection exists: {ex.Message}");
                throw;
            }
            finally
            {
                _collectionEnsureSemaphore.Release();
            }
        }

        public async Task<Patient> InsertPatientAsync(Patient patient)
        {
            await EnsureEncryptedCollectionExistsAsync();

            var encryptedDoc = await CreateEncryptedPatientDocumentAsync(patient);

            var collection = _encryptedDatabase.GetCollection<BsonDocument>(_collectionName);
            await collection.InsertOneAsync(encryptedDoc);
            return patient;
        }

        public async Task<List<Patient>> InsertManyPatientsAsync(List<Patient> patients)
        {
            await EnsureEncryptedCollectionExistsAsync();

            var encryptedDocs = new List<BsonDocument>();
            foreach (var patient in patients)
            {
                var encryptedDoc = await CreateEncryptedPatientDocumentAsync(patient);
                encryptedDocs.Add(encryptedDoc);
            }

            var collection = _encryptedDatabase.GetCollection<BsonDocument>(_collectionName);
            await collection.InsertManyAsync(encryptedDocs);
            return patients;
        }

        public async Task<List<Patient>> SearchPatientsAsync(
            string? firstName = null,
            string? lastName = null,
            DateTime? dobFrom = null,
            DateTime? dobTo = null,
            string? zipCode = null,
            string? nationalIdPrefix = null,
            string? phoneNumber = null,
            string? notesKeyword = null)
        {
            var filterBuilder = Builders<Patient>.Filter;
            var filters = new List<FilterDefinition<Patient>>();

            if (!string.IsNullOrWhiteSpace(zipCode))
            {
                filters.Add(filterBuilder.Eq(p => p.ZipCode, zipCode));
            }

            if (dobFrom.HasValue && dobTo.HasValue)
            {
                try
                {
                    var rangeExpression = new BsonDocument
                    {
                        {
                            "$and", new BsonArray
                            {
                                new BsonDocument { { "dateOfBirth", new BsonDocument { { "$gte", dobFrom.Value } } } },
                                new BsonDocument { { "dateOfBirth", new BsonDocument { { "$lte", dobTo.Value } } } }
                            }
                        }
                    };

                    var encryptOptions = new EncryptOptions(
                        algorithm: EncryptionAlgorithm.Range,
                        keyId: _dataKeys["dateOfBirth"],
                        rangeOptions: new RangeOptions(
                            min: new BsonDateTime(new DateTime(1900, 1, 1)),
                            max: new BsonDateTime(new DateTime(2100, 12, 31)),
                            sparsity: 1,
                            trimFactor: 4
                        ),
                        queryType: "range",
                        contentionFactor: 0
                    );

                    var encryptedRangeFilter = await _clientEncryption.EncryptExpressionAsync(rangeExpression, encryptOptions);
                    filters.Add(new BsonDocumentFilterDefinition<Patient>(encryptedRangeFilter));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to create encrypted date range query: {ex.Message}. Skipping date filter.");
                }
            }

            if (!string.IsNullOrWhiteSpace(firstName))
            {
                try
                {
                    var encryptedFirstName = await _clientEncryption.EncryptAsync(
                        firstName,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.TextPreview,
                            keyId: _dataKeys["firstName"],
                            queryType: "prefixPreview",
                            textOptions: new TextOptions(
                                caseSensitive: true,
                                diacriticSensitive: true,
                                new PrefixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                            ),
                            contentionFactor: 0
                        )
                    );
                    var prefixFilter = new BsonDocument("$expr", new BsonDocument("$encStrStartsWith",
                        new BsonDocument
                        {
                            { "input", "$firstName" },
                            { "prefix", encryptedFirstName }
                        }));
                    filters.Add(new BsonDocumentFilterDefinition<Patient>(prefixFilter));
                }
                catch (Exception)
                {
                    var encryptedFirstName = await _clientEncryption.EncryptAsync(
                        firstName,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.Indexed,
                            keyId: _dataKeys["firstName"],
                            queryType: "equality",
                            contentionFactor: 0
                        )
                    );
                    filters.Add(filterBuilder.Eq("firstName", encryptedFirstName));
                }
            }

            if (!string.IsNullOrWhiteSpace(lastName))
            {
                try
                {
                    var encryptedLastName = await _clientEncryption.EncryptAsync(
                        lastName,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.TextPreview,
                            keyId: _dataKeys["lastName"],
                            queryType: "prefixPreview",
                            textOptions: new TextOptions(
                                caseSensitive: true,
                                diacriticSensitive: true,
                                new PrefixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                            ),
                            contentionFactor: 0
                        )
                    );
                    var prefixFilter = new BsonDocument("$expr", new BsonDocument("$encStrStartsWith",
                        new BsonDocument
                        {
                            { "input", "$lastName" },
                            { "prefix", encryptedLastName }
                        }));
                    filters.Add(new BsonDocumentFilterDefinition<Patient>(prefixFilter));
                }
                catch (Exception)
                {
                    var encryptedLastName = await _clientEncryption.EncryptAsync(
                        lastName,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.Indexed,
                            keyId: _dataKeys["lastName"],
                            queryType: "equality",
                            contentionFactor: 0
                        )
                    );
                    filters.Add(filterBuilder.Eq("lastName", encryptedLastName));
                }
            }

            if (!string.IsNullOrWhiteSpace(nationalIdPrefix))
            {
                try
                {
                    var encryptedNationalId = await _clientEncryption.EncryptAsync(
                        nationalIdPrefix,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.TextPreview,
                            keyId: _dataKeys["nationalId"],
                            queryType: "prefixPreview",
                            textOptions: new TextOptions(
                                caseSensitive: true,
                                diacriticSensitive: true,
                                new PrefixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                            ),
                            contentionFactor: 0
                        )
                    );
                    var prefixFilter = new BsonDocument("$expr", new BsonDocument("$encStrStartsWith",
                        new BsonDocument
                        {
                            { "input", "$nationalId" },
                            { "prefix", encryptedNationalId }
                        }));
                    filters.Add(new BsonDocumentFilterDefinition<Patient>(prefixFilter));
                }
                catch (Exception)
                {
                    var encryptedNationalId = await _clientEncryption.EncryptAsync(
                        nationalIdPrefix,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.Indexed,
                            keyId: _dataKeys["nationalId"],
                            queryType: "equality",
                            contentionFactor: 0
                        )
                    );
                    filters.Add(filterBuilder.Eq("nationalId", encryptedNationalId));
                }
            }

            if (!string.IsNullOrWhiteSpace(phoneNumber))
            {
                try
                {
                    var encryptedPhoneNumber = await _clientEncryption.EncryptAsync(
                        phoneNumber,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.TextPreview,
                            keyId: _dataKeys["phoneNumber"],
                            queryType: "suffixPreview",
                            textOptions: new TextOptions(
                                caseSensitive: true,
                                diacriticSensitive: true,
                                suffixOptions: new SuffixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                            ),
                            contentionFactor: 0
                        )
                    );
                    var suffixFilter = new BsonDocument("$expr", new BsonDocument("$encStrEndsWith",
                        new BsonDocument
                        {
                            { "input", "$phoneNumber" },
                            { "suffix", encryptedPhoneNumber }
                        }));
                    filters.Add(new BsonDocumentFilterDefinition<Patient>(suffixFilter));
                }
                catch (Exception)
                {
                    var encryptedPhoneNumber = await _clientEncryption.EncryptAsync(
                        phoneNumber,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.Indexed,
                            keyId: _dataKeys["phoneNumber"],
                            queryType: "equality",
                            contentionFactor: 0
                        )
                    );
                    filters.Add(filterBuilder.Eq("phoneNumber", encryptedPhoneNumber));
                }
            }

            if (!string.IsNullOrWhiteSpace(notesKeyword))
            {
                try
                {
                    var encryptedNotes = await _clientEncryption.EncryptAsync(
                        notesKeyword,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.TextPreview,
                            keyId: _dataKeys["notes"],
                            queryType: "substringPreview",
                            textOptions: new TextOptions(
                                caseSensitive: true,
                                diacriticSensitive: true,
                                substringOptions: new SubstringOptions(
                                    strMaxLength: 60,
                                    strMaxQueryLength: 10,
                                    strMinQueryLength: 3
                                )
                            ),
                            contentionFactor: 0
                        )
                    );
                    var substringFilter = new BsonDocument("$expr", new BsonDocument("$encStrContains",
                        new BsonDocument
                        {
                            { "input", "$notes" },
                            { "substring", encryptedNotes }
                        }));
                    filters.Add(new BsonDocumentFilterDefinition<Patient>(substringFilter));
                }
                catch (Exception)
                {
                    var encryptedNotes = await _clientEncryption.EncryptAsync(
                        notesKeyword,
                        new EncryptOptions(
                            algorithm: EncryptionAlgorithm.Indexed,
                            keyId: _dataKeys["notes"],
                            queryType: "equality",
                            contentionFactor: 0
                        )
                    );
                    filters.Add(filterBuilder.Eq("notes", encryptedNotes));
                }
            }

            var combinedFilter = filters.Any() ? filterBuilder.And(filters) : filterBuilder.Empty;

            var results = await _patientsCollection.Find(combinedFilter).ToListAsync();

            return results;
        }

        public async Task<(List<Patient> results, object explainResult)> SearchPatientsWithExplainAsync(
            string? firstName = null,
            string? lastName = null,
            DateTime? dobFrom = null,
            DateTime? dobTo = null,
            string? zipCode = null,
            string? nationalIdPrefix = null,
            string? phoneNumber = null,
            string? notesKeyword = null)
        {
            _logger.LogInformation("=== EXPLAIN QUERY START ===");
            _logger.LogInformation($"Search Parameters: firstName='{firstName}', lastName='{lastName}', zipCode='{zipCode}', nationalIdPrefix='{nationalIdPrefix}', phoneNumber='{phoneNumber}', notesKeyword='{notesKeyword}', dobFrom='{dobFrom}', dobTo='{dobTo}'");

            var filterBuilder = Builders<Patient>.Filter;
            var filters = new List<FilterDefinition<Patient>>();

            if (!string.IsNullOrWhiteSpace(zipCode))
            {
                filters.Add(filterBuilder.Eq(p => p.ZipCode, zipCode));
                _logger.LogInformation($"Added ZipCode filter: {zipCode}");
            }

            if (dobFrom.HasValue && dobTo.HasValue)
            {
                try
                {
                    var rangeExpression = new BsonDocument
                    {
                        {
                            "$and", new BsonArray
                            {
                                new BsonDocument { { "dateOfBirth", new BsonDocument { { "$gte", dobFrom.Value } } } },
                                new BsonDocument { { "dateOfBirth", new BsonDocument { { "$lte", dobTo.Value } } } }
                            }
                        }
                    };

                    var encryptOptions = new EncryptOptions(
                        algorithm: EncryptionAlgorithm.Range,
                        keyId: _dataKeys["dateOfBirth"],
                        rangeOptions: new RangeOptions(
                            min: new BsonDateTime(new DateTime(1900, 1, 1)),
                            max: new BsonDateTime(new DateTime(2100, 12, 31)),
                            sparsity: 1,
                            trimFactor: 4
                        ),
                        queryType: "range",
                        contentionFactor: 0
                    );

                    var encryptedRangeFilter = await _clientEncryption.EncryptExpressionAsync(rangeExpression, encryptOptions);
                    filters.Add(new BsonDocumentFilterDefinition<Patient>(encryptedRangeFilter));
                    _logger.LogInformation($"Added encrypted DateOfBirth range filter: {dobFrom} to {dobTo}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to create encrypted date range query: {ex.Message}. Skipping date filter.");
                }
            }

            if (!string.IsNullOrWhiteSpace(firstName))
            {
                var prefixFilter = new BsonDocument("$expr", new BsonDocument("$encStrStartsWith",
                    new BsonDocument
                    {
                        { "input", "$firstName" },
                        { "prefix", firstName }
                    }));
                filters.Add(new BsonDocumentFilterDefinition<Patient>(prefixFilter));
                _logger.LogInformation($"Added firstName prefix filter: {firstName}");
            }

            if (!string.IsNullOrWhiteSpace(lastName))
            {
                var prefixFilter = new BsonDocument("$expr", new BsonDocument("$encStrStartsWith",
                    new BsonDocument
                    {
                        { "input", "$lastName" },
                        { "prefix", lastName }
                    }));
                filters.Add(new BsonDocumentFilterDefinition<Patient>(prefixFilter));
                _logger.LogInformation($"Added lastName prefix filter: {lastName}");
            }

            if (!string.IsNullOrWhiteSpace(nationalIdPrefix))
            {
                var prefixFilter = new BsonDocument("$expr", new BsonDocument("$encStrStartsWith",
                    new BsonDocument
                    {
                        { "input", "$nationalId" },
                        { "prefix", nationalIdPrefix }
                    }));
                filters.Add(new BsonDocumentFilterDefinition<Patient>(prefixFilter));
                _logger.LogInformation($"Added nationalId prefix filter: {nationalIdPrefix}");
            }

            if (!string.IsNullOrWhiteSpace(phoneNumber))
            {
                var suffixFilter = new BsonDocument("$expr", new BsonDocument("$encStrEndsWith",
                    new BsonDocument
                    {
                        { "input", "$phoneNumber" },
                        { "suffix", phoneNumber }
                    }));
                filters.Add(new BsonDocumentFilterDefinition<Patient>(suffixFilter));
                _logger.LogInformation($"Added phoneNumber suffix filter: {phoneNumber}");
            }

            if (!string.IsNullOrWhiteSpace(notesKeyword))
            {
                var substringFilter = new BsonDocument("$expr", new BsonDocument("$encStrContains",
                    new BsonDocument
                    {
                        { "input", "$notes" },
                        { "substring", notesKeyword }
                    }));
                filters.Add(new BsonDocumentFilterDefinition<Patient>(substringFilter));
                _logger.LogInformation($"Added notes substring filter: {notesKeyword}");
            }

            var combinedFilter = filters.Any() ? filterBuilder.And(filters) : filterBuilder.Empty;
            _logger.LogInformation($"Combined filter count: {filters.Count}");

            var findCommand = new BsonDocument
            {
                { "find", _collectionName },
                { "filter", combinedFilter.ToBsonDocument() }
            };

            var explainCommand = new BsonDocument
            {
                { "explain", findCommand },
                { "verbosity", "executionStats" }
            };

            _logger.LogInformation("Executing explain command against database");

            BsonDocument explainResult = await _encryptedDatabase.RunCommandAsync<BsonDocument>(explainCommand);
            _logger.LogInformation($"Explain result received, size: {explainResult.ToJson().Length} characters");

            var explainJson = explainResult.ToJson();
            _logger.LogInformation($"Full Explain Result: {explainJson}");

            var results = await SearchPatientsAsync(firstName, lastName, dobFrom, dobTo,
                zipCode, nationalIdPrefix, phoneNumber, notesKeyword);
            _logger.LogInformation($"Search returned {results.Count} patients");

            _logger.LogInformation("=== EXPLAIN QUERY END ===");
            return (results, explainJson);
        }


        public async Task<long> GetPatientCountAsync()
        {
            return await _patientsCollection.EstimatedDocumentCountAsync();
        }

        public async Task DeleteAllPatientsAsync()
        {
            await _encryptedDatabase.DropCollectionAsync(_collectionName);

            _collectionEnsured = false;

            Console.WriteLine($"Dropped collection '{_collectionName}' for fast deletion. It will be recreated with encryption on next insert.");
        }



        public async Task<object> GetCollectionSchemaAsync()
        {
            try
            {
                // Get collection information using listCollections command
                var command = new BsonDocument
                {
                    { "listCollections", 1 },
                    { "filter", new BsonDocument { { "name", _collectionName } } }
                };

                var result = await _encryptedDatabase.RunCommandAsync<BsonDocument>(command);

                if (result.Contains("cursor"))
                {
                    var cursor = result["cursor"].AsBsonDocument;
                    if (cursor.Contains("firstBatch"))
                    {
                        var firstBatch = cursor["firstBatch"].AsBsonArray;
                        if (firstBatch.Count > 0)
                        {
                            var collectionInfo = firstBatch[0].AsBsonDocument;

                            // Extract and return the collection schema
                            var schema = new
                            {
                                Name = collectionInfo.GetValue("name", "").AsString,
                                Type = collectionInfo.GetValue("type", "").AsString,
                                Options = collectionInfo.Contains("options") ? collectionInfo["options"].ToJson() : "{}",
                                Info = collectionInfo.Contains("info") ? collectionInfo["info"].ToJson() : "{}",
                                IdIndex = collectionInfo.Contains("idIndex") ? collectionInfo["idIndex"].ToJson() : "{}"
                            };

                            return schema;
                        }
                    }
                }

                return new { message = "Collection not found or no schema information available" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting collection schema");
                throw new Exception($"Failed to get collection schema: {ex.Message}");
            }
        }


        private async Task<BsonDocument> CreateEncryptedPatientDocumentAsync(Patient patient)
        {
            var doc = new BsonDocument();

            if (!string.IsNullOrEmpty(patient.FirstName))
            {
                var encryptedFirstName = await _clientEncryption.EncryptAsync(
                    patient.FirstName,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.TextPreview,
                        keyId: _dataKeys["firstName"],
                        textOptions: new TextOptions(
                            caseSensitive: true,
                            diacriticSensitive: true,
                            new PrefixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                        ),
                        contentionFactor: 0
                    )
                );
                doc["firstName"] = encryptedFirstName;
            }

            if (!string.IsNullOrEmpty(patient.LastName))
            {
                var encryptedLastName = await _clientEncryption.EncryptAsync(
                    patient.LastName,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.TextPreview,
                        keyId: _dataKeys["lastName"],
                        textOptions: new TextOptions(
                            caseSensitive: true,
                            diacriticSensitive: true,
                            new PrefixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                        ),
                        contentionFactor: 0
                    )
                );
                doc["lastName"] = encryptedLastName;
            }

            if (patient.DateOfBirth != default)
            {
                var encryptedDateOfBirth = await _clientEncryption.EncryptAsync(
                    patient.DateOfBirth,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.Range,
                        keyId: _dataKeys["dateOfBirth"],
                        rangeOptions: new RangeOptions(
                            min: new BsonDateTime(new DateTime(1900, 1, 1)),
                            max: new BsonDateTime(new DateTime(2100, 12, 31)),
                            sparsity: 1,
                            trimFactor: 4
                        ),
                        contentionFactor: 0
                    )
                );
                doc["dateOfBirth"] = encryptedDateOfBirth;
            }

            if (!string.IsNullOrEmpty(patient.ZipCode))
            {
                var encryptedZipCode = await _clientEncryption.EncryptAsync(
                    patient.ZipCode,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.Indexed,
                        keyId: _dataKeys["zipCode"],
                        contentionFactor: 0
                    )
                );
                doc["zipCode"] = encryptedZipCode;
            }

            if (!string.IsNullOrEmpty(patient.NationalId))
            {
                var encryptedNationalId = await _clientEncryption.EncryptAsync(
                    patient.NationalId,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.TextPreview,
                        keyId: _dataKeys["nationalId"],
                        textOptions: new TextOptions(
                            caseSensitive: true,
                            diacriticSensitive: true,
                            new PrefixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                        ),
                        contentionFactor: 0
                    )
                );
                doc["nationalId"] = encryptedNationalId;
            }

            if (!string.IsNullOrEmpty(patient.PhoneNumber))
            {
                var encryptedPhoneNumber = await _clientEncryption.EncryptAsync(
                    patient.PhoneNumber,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.TextPreview,
                        keyId: _dataKeys["phoneNumber"],
                        textOptions: new TextOptions(
                            caseSensitive: true,
                            diacriticSensitive: true,
                            suffixOptions: new SuffixOptions(strMaxQueryLength: 10, strMinQueryLength: 2)
                        ),
                        contentionFactor: 0
                    )
                );
                doc["phoneNumber"] = encryptedPhoneNumber;
            }

            if (!string.IsNullOrEmpty(patient.Notes))
            {
                var encryptedNotes = await _clientEncryption.EncryptAsync(
                    patient.Notes,
                    new EncryptOptions(
                        algorithm: EncryptionAlgorithm.TextPreview,
                        keyId: _dataKeys["notes"],
                        textOptions: new TextOptions(
                            caseSensitive: true,
                            diacriticSensitive: true,
                            substringOptions: new SubstringOptions(
                                strMaxLength: 60,
                                strMaxQueryLength: 10,
                                strMinQueryLength: 3
                            )
                        ),
                        contentionFactor: 0
                    )
                );
                doc["notes"] = encryptedNotes;
            }

            return doc;
        }
    }
}