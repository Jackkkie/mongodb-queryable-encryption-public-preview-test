using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoQEDemo.Configuration
{
    public static class EncryptionConfiguration
    {
        public static byte[] GetOrCreateLocalMasterKey()
        {
            const string keyPath = "master-key.txt";

            if (File.Exists(keyPath))
            {
                var keyBase64 = File.ReadAllText(keyPath).Trim();
                return Convert.FromBase64String(keyBase64);
            }

            var key = new byte[96];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
            }

            var keyBase64String = Convert.ToBase64String(key);
            File.WriteAllText(keyPath, keyBase64String);

            Console.WriteLine($"Generated new master key and saved to {keyPath}");
            return key;
        }


        public static BsonDocument GetEncryptedFieldsMap(Dictionary<string, Guid> dataKeys)
        {
            var encryptedFields = new BsonDocument
            {
                {
                    "fields", new BsonArray
                    {
                        // firstName - prefixPreview query (min 3 chars)
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
                                        { "contention", new BsonInt64(0) },
                                        { "strMinQueryLength", new BsonInt64(3) },
                                        { "strMaxQueryLength", new BsonInt64(20) },
                                        { "caseSensitive", false },
                                        { "diacriticSensitive", false }
                                    }
                                }
                            }
                        },
                        // lastName - prefixPreview query (min 3 chars)
                        new BsonDocument
                        {
                            { "keyId", new BsonBinaryData(dataKeys["lastName"], GuidRepresentation.Standard) },
                            { "path", "lastName" },
                            { "bsonType", "string" },
                            { "queries", new BsonArray
                                {
                                    new BsonDocument
                                    {
                                        { "queryType", "prefixPreview" },
                                        { "contention", new BsonInt64(0) },
                                        { "strMinQueryLength", new BsonInt64(3) },
                                        { "strMaxQueryLength", new BsonInt64(20) },
                                        { "caseSensitive", false },
                                        { "diacriticSensitive", false }
                                    }
                                }
                            }
                        },
                        // dateOfBirth - range query
                        new BsonDocument
                        {
                            { "keyId", new BsonBinaryData(dataKeys["dateOfBirth"], GuidRepresentation.Standard) },
                            { "path", "dateOfBirth" },
                            { "bsonType", "date" },
                            { "queries", new BsonArray
                                {
                                    new BsonDocument
                                    {
                                        { "queryType", "range" },
                                        { "sparsity", 1 },
                                        { "min", new BsonDateTime(new DateTime(1920, 1, 1)) },
                                        { "max", new BsonDateTime(new DateTime(3000, 12, 31)) },
                                        { "trimFactor", 4 }
                                    }
                                }
                            }
                        },
                        // zipCode - equality query (exact match)
                        new BsonDocument
                        {
                            { "keyId", new BsonBinaryData(dataKeys["zipCode"], GuidRepresentation.Standard) },
                            { "path", "zipCode" },
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
                        // nationalId - prefixPreview query (min 6 chars)
                        new BsonDocument
                        {
                            { "keyId", new BsonBinaryData(dataKeys["nationalId"], GuidRepresentation.Standard) },
                            { "path", "nationalId" },
                            { "bsonType", "string" },
                            { "queries", new BsonArray
                                {
                                    new BsonDocument
                                    {
                                        { "queryType", "prefixPreview" },
                                        { "contention", new BsonInt64(0) },
                                        { "strMinQueryLength", new BsonInt64(6) },
                                        { "strMaxQueryLength", new BsonInt64(15) },
                                        { "caseSensitive", false },
                                        { "diacriticSensitive", false }
                                    }
                                }
                            }
                        },
                        // phoneNumber - suffixPreview query (min 4 chars)
                        new BsonDocument
                        {
                            { "keyId", new BsonBinaryData(dataKeys["phoneNumber"], GuidRepresentation.Standard) },
                            { "path", "phoneNumber" },
                            { "bsonType", "string" },
                            { "queries", new BsonArray
                                {
                                    new BsonDocument
                                    {
                                        { "queryType", "suffixPreview" },
                                        { "contention", new BsonInt64(0) },
                                        { "strMinQueryLength", new BsonInt64(4) },
                                        { "strMaxQueryLength", new BsonInt64(15) },
                                        { "caseSensitive", false },
                                        { "diacriticSensitive", false }
                                    }
                                }
                            }
                        },
                        // notes - substringPreview query (proper configuration)
                        new BsonDocument
                        {
                            { "keyId", new BsonBinaryData(dataKeys["notes"], GuidRepresentation.Standard) },
                            { "path", "notes" },
                            { "bsonType", "string" },
                            { "queries", new BsonArray
                                {
                                    new BsonDocument
                                    {
                                        { "queryType", "substringPreview" },
                                        { "contention", new BsonInt64(0) },
                                        { "strMinQueryLength", new BsonInt64(3) },
                                        { "strMaxQueryLength", new BsonInt64(10) },
                                        { "strMaxLength", new BsonInt64(60) },
                                        { "caseSensitive", false },
                                        { "diacriticSensitive", false }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            return encryptedFields;
        }
    }
}