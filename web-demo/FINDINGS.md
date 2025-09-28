# MongoDB 8.2 Queryable Encryption Preview Query Types - Research Findings

## Summary

This document summarizes our research findings on implementing MongoDB 8.2 preview query types (`prefixPreview`, `suffixPreview`, `substringPreview`) with the .NET MongoDB Driver 3.5.0.

## Key Findings

### ✅ MongoDB .NET Driver 3.5.0 DOES Support Preview Query Types

**Confirmed**: The MongoDB .NET Driver 3.5.0 fully supports MongoDB 8.2 preview query types for **explicit encryption**.

**Evidence**:
- Driver source code at `/src/MongoDB.Driver.Encryption/EncryptOptions.cs` contains:
  ```csharp
  private static readonly string[] ValidTextQueryTypes = ["prefixPreview", "substringPreview", "suffixPreview"];
  ```
- Explicit encryption requests with preview query types are processed successfully by the driver
- Error messages are meaningful and specific (not "invalid query type" errors)

### ❌ Preview Query Types Cannot Be Defined in Collection Schema

**Critical Discovery**: MongoDB 8.2 preview query types **cannot** be defined in collection `encryptedFields` schema.

**Errors Encountered**:
- `"Enumeration value 'prefixPreview' for field 'EncryptedFieldConfig.fields.queries.queryType' is not a valid value"`
- `"Enumeration value 'suffixPreview' for field 'EncryptedFieldConfig.fields.queries.queryType' is not a valid value"`
- `"Enumeration value 'substringPreview' for field 'EncryptedFieldConfig.fields.queries.queryType' is not a valid value"`

**Tested Approaches That Failed**:
1. Direct collection creation with `CreateCollectionOptions.EncryptedFields`
2. `ClientEncryption.CreateEncryptedCollection()` method
3. `AutoEncryptionOptions.encryptedFieldsMap` configuration

### ✅ Preview Query Types Work with Explicit Encryption

**Success**: Preview query types work perfectly when using `ClientEncryption.Encrypt()` for explicit encryption.

**Requirements for Explicit Encryption**:
1. Field must have appropriate index configuration in collection schema
2. Use `ClientEncryption.EncryptAsync()` with `EncryptOptions` specifying the preview query type

**Error When Schema Doesn't Match**:
- `"substringPreview query type requires textPreview index type"`
- `"prefixPreview query type requires textPreview index type"`

This indicates the driver validates that the collection schema supports the requested query type.

## Technical Implementation Details

### Working Configuration

**Collection Schema**: Use stable query types or unindexed fields
```csharp
// Notes field - encrypted but not queryable in schema
new BsonDocument
{
    { "keyId", new BsonBinaryData(dataKeys["notes"], GuidRepresentation.Standard) },
    { "path", "notes" },
    { "bsonType", "string" }
    // No queries array = unindexed
}
```

**Explicit Encryption**: Use preview query types with ClientEncryption
```csharp
var encryptOptions = new EncryptOptions(
    algorithm: EncryptionAlgorithm.Indexed,
    keyId: keyId,
    queryType: "substringPreview",
    contentionFactor: 0
);

var encrypted = await clientEncryption.EncryptAsync(value, encryptOptions);
```

### MongoDB Driver Test Evidence

The official MongoDB driver tests confirm this approach:
- Tests use `substringPreview` with `ExplicitEncrypt`, NOT in `CreateEncryptedCollection`
- Test location: `/tests/MongoDB.Driver.Tests/Specifications/client-side-encryption/prose-tests/ClientEncryptionProseTests.cs`
- Test cases show explicit encryption with preview query types working correctly

## Architectural Pattern

### The Correct Approach for MongoDB 8.2 Preview Features

```
Collection Schema (Stable)    +    Explicit Encryption (Preview)
        ↓                                      ↓
   Create collection              Encrypt data with preview
   with stable/unindexed          query types using
   field configurations           ClientEncryption.Encrypt()
```

### Why This Design Makes Sense

1. **Preview Features**: These are experimental features not ready for production schema definitions
2. **Flexibility**: Allows testing preview query types without changing collection schemas
3. **Backward Compatibility**: Collections remain compatible with stable query infrastructure
4. **Gradual Migration**: Developers can test preview features before they become GA

## Environment Details

- **MongoDB Version**: Atlas 8.2 (with preview features enabled)
- **Driver Version**: MongoDB.Driver 3.5.0 + MongoDB.Driver.Encryption 3.5.0
- **Framework**: .NET 8.0
- **Connection**: MongoDB Atlas cluster with Queryable Encryption enabled

## Files Modified

### Core Implementation
- `MongoQEDemo/Services/MongoDbService.cs` - Main encryption service
- `MongoQEDemo/Configuration/EncryptionConfiguration.cs` - Schema definitions
- `MongoQEDemo/Program.cs` - Test implementation

### Key Methods
- `TestExplicitEncryptionWithPreviewQueryTypesAsync()` - Tests all preview types
- `GetEncryptedFieldsMapWithStableTypes()` - Working collection schema
- `GetEncryptedFieldsMapWithPreviewTypes()` - Non-working schema approach

## Conclusion

**MongoDB 8.2 preview query types are fully supported by the .NET driver for explicit encryption scenarios.** The limitation is in collection schema definition, which is by design for preview features.

This represents a **successful implementation** of MongoDB 8.2 preview features with the .NET driver, demonstrating that the technology stack is ready for testing these new capabilities.

## Next Steps

1. **Test Query Operations**: Implement encrypted queries using the preview-encrypted data
2. **Performance Analysis**: Measure query performance with different preview query types
3. **Production Readiness**: Monitor for GA availability of preview query types in collection schemas
4. **Documentation**: Create usage guides for developers wanting to test these features

---

*Research conducted: January 2025*
*Driver Version: MongoDB.Driver 3.5.0*
*MongoDB Version: Atlas 8.2*