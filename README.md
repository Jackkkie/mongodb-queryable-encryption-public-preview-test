# MongoDB Queryable Encryption with .NET and Local KMS

This repository contains demonstration projects for MongoDB's Queryable Encryption feature using .NET with Local Key Management Service (KMS).

## Prerequisites

- .NET 9.0 SDK
- MongoDB Atlas cluster (or MongoDB server with encryption support)
- NuGet packages:
  - MongoDB.Driver 3.0.0+
  - MongoDB.Driver.Encryption 3.0.0+

## Projects

### console-demo
Console application demonstrating basic queryable encryption operations including:
- Local KMS setup and key generation
- Document insertion with encrypted fields
- Encrypted queries with explain output

### web-demo
ASP.NET Core web application with full UI for:
- Patient data management with encrypted fields
- Real-time search across encrypted data
- Batch patient generation for performance testing
- Query performance analysis

## Environment Setup

1. Copy `.env.example` to `.env` in each project directory
2. Configure your MongoDB connection string:
   ```
   MONGODB_CONNECTION_STRING=mongodb+srv://username:password@cluster.mongodb.net/
   ```
3. Adjust performance settings as needed:
   ```
   MAX_BATCH_CONCURRENCY=64
   ```

## Running the Projects

### Console Demo
```bash
cd console-demo
dotnet run
```

### Web Demo
```bash
cd web-demo
dotnet run
```

Access the web interface at `http://localhost:9999`

## Key Features

- **Local KMS**: Uses local key management instead of cloud KMS for development
- **Preview Query Types**: Supports prefix, suffix, and substring encrypted queries
- **Performance Optimized**: Configured for high-throughput encrypted operations
- **Real-time Search**: Auto-search functionality with debounced queries
- **Batch Processing**: Parallel batch insertion with detailed logging