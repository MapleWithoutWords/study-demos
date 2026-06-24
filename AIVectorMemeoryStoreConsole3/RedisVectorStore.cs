using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;

namespace AIVectorMemeoryStoreConsole3;

internal class RedisVectorStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly string _indexName;
    private readonly string _keyPrefix;

    public RedisVectorStore(IConnectionMultiplexer redis,string collectionName)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _db = _redis.GetDatabase();
        _indexName = $"idx:{collectionName}";
        _keyPrefix = $"{collectionName}:";
    }

    public async Task EnsureCollectionExistsAsync(int vectorSize, bool allowRecreation, CancellationToken ct = default)
    {
        var ft = _db.FT();

        // Use _LIST command to check if index exists
        RedisResult[] indexes = ft._List();
        bool indexExists = indexes.Any(e => _indexName.Equals((string)e!));

        if (indexExists)
        {
            if (allowRecreation)
            {
                ft.DropIndex(_indexName);
                await CreateIndexAsync(ft, vectorSize);
            }
            return;
        }

        // Index does not exist, create it
        await CreateIndexAsync(ft, vectorSize);
    }

    private Task CreateIndexAsync(ISearchCommands ft, int vectorSize)
    {
        var schema = new Schema()
            .AddTextField("id")
            .AddTextField("data")
            .AddTextField("user_id")
            .AddTextField("hash")
            .AddTextField("metadata")
            .AddNumericField("created_at")
            .AddNumericField("updated_at")
            .AddVectorField("embedding",
                Schema.VectorField.VectorAlgo.HNSW,
                new Dictionary<string, object>
                {
                    ["TYPE"] = "FLOAT32",
                    ["DIM"] = vectorSize,
                    ["DISTANCE_METRIC"] = "COSINE"
                });

        bool success = ft.Create(_indexName,
            new FTCreateParams()
                .On(IndexDataType.HASH)
                .Prefix(_keyPrefix),
            schema);
        if (!success)
        {
            throw new Exception("Failed to create Redis vector index.");
        }
        return Task.CompletedTask;
    }

    public async Task InsertAsync(List<VectorModel> vectors, CancellationToken ct = default)
    {
        foreach (var vector in vectors)
        {
            var key = $"{_keyPrefix}{vector.Id}";
            var hashEntries = new HashEntry[]
            {
                new("id", vector.Id),
                new("data", vector.Data),
                new("user_id", vector.UserId ?? string.Empty),
                new("hash", vector.Hash ?? string.Empty),
                new("metadata", System.Text.Json.JsonSerializer.Serialize(vector.Metadata ?? new Dictionary<string, object>())),
                new("created_at", vector.CreatedAt.Ticks),
                new("updated_at", vector.UpdatedAt?.Ticks),
                new("embedding", SerializeVector(vector.Embedding))
            };

            await _db.HashSetAsync(key, hashEntries);
        }
    }

    public async Task UpdateAsync(List<VectorModel> vectors, CancellationToken ct = default)
    {
        foreach (var vector in vectors)
        {
            var key = $"{_keyPrefix}{vector.Id}";

            // Check if exists
            if (await _db.KeyExistsAsync(key))
            {
                var hashEntries = new HashEntry[]
                {
                    new("data", vector.Data),
                    new("hash", vector.Hash ?? string.Empty),
                    new("metadata", System.Text.Json.JsonSerializer.Serialize(vector.Metadata ?? new Dictionary<string, object>())),
                    new("updated_at", vector.UpdatedAt?.Ticks ?? DateTime.UtcNow.Ticks),
                    new("embedding", SerializeVector(vector.Embedding))
                };

                await _db.HashSetAsync(key, hashEntries);
            }
        }
    }

    public Task<List<QueryVectorItemDto>> SearchAsync(float[] queryVector, string? userId = null, int limit = 100, CancellationToken ct = default)
    {
        var ft = _db.FT();

        // Build query
        var queryStr = userId != null ? $"@user_id:{EscapeRedisQuery(userId)}" : "*";

        var query = new Query(queryStr)
            .SetSortBy("__embedding_score")
            .Limit(0, limit)
            .ReturnFields("id", "data", "user_id", "hash", "metadata", "created_at", "updated_at", "embedding", "__embedding_score")
            .Dialect(2);

        // Add vector similarity search parameter
        var vectorBytes = SerializeVector(queryVector);
        query.AddParam("query_vector", vectorBytes);
        query.AddParam("BLOB", vectorBytes);

        // Perform KNN search
        var searchQuery = $"{queryStr}=>[KNN {limit} @embedding $query_vector AS __embedding_score]";
        var fullQuery = new Query(searchQuery)
            .SetSortBy("__embedding_score")
            .Limit(0, limit)
            .ReturnFields("id", "data", "user_id", "hash", "metadata", "created_at", "updated_at", "__embedding_score")
            .Dialect(2);

        fullQuery.AddParam("query_vector", vectorBytes);

        var results = ft.Search(_indexName, fullQuery);

        var searchResults = new List<QueryVectorItemDto>();

        foreach (var doc in results.Documents)
        {
            var vectorItem = ParseVectorModel(doc);
            var scoreValue = doc["__embedding_score"];
            var score = !scoreValue.IsNull && float.TryParse(scoreValue.ToString(), out var s)
                ? 1.0f - s // Convert distance to similarity
                : 0.0f;

            searchResults.Add(new QueryVectorItemDto
            {
                Vector = vectorItem,
                Score = score
            });
        }

        return Task.FromResult(searchResults);
    }

    public Task<List<VectorModel>> ListAsync(string? userId = null, int limit = 100, CancellationToken ct = default)
    {
        var ft = _db.FT();

        var queryStr = userId != null ? $"@user_id:{EscapeRedisQuery(userId)}" : "*";
        var query = new Query(queryStr)
            .SetSortBy("created_at", false)
            .Limit(0, limit)
            .ReturnFields("id", "data", "user_id", "hash", "metadata", "created_at", "updated_at", "embedding")
            .Dialect(2);

        var results = ft.Search(_indexName, query);

        return Task.FromResult(results.Documents.Select(ParseVectorModel).ToList());
    }

    public async Task<VectorModel?> GetAsync(string vectorId, CancellationToken ct = default)
    {
        var key = $"{_keyPrefix}{vectorId}";

        if (!await _db.KeyExistsAsync(key))
        {
            return null;
        }

        var hash = await _db.HashGetAllAsync(key);

        if (hash.Length == 0)
        {
            return null;
        }

        return ParseVectorModel(hash);
    }

    public async Task DeleteAsync(string vectorId, CancellationToken ct = default)
    {
        var key = $"{_keyPrefix}{vectorId}";
        await _db.KeyDeleteAsync(key);
    }

    public async Task DeleteByUserAsync(string userId, CancellationToken ct = default)
    {
        var vectors = await ListAsync(userId, limit: 10000, ct);

        foreach (var vector in vectors)
        {
            await DeleteAsync(vector.Id, ct);
        }
    }

    private VectorModel ParseVectorModel(Document doc)
    {
        var dict = new Dictionary<string, RedisValue>();
        foreach (var key in new[] { "id", "data", "user_id", "hash", "metadata", "created_at", "updated_at", "embedding" })
        {
            var value = doc[key];
            if (!value.IsNull)
            {
                dict[key] = value;
            }
        }
        return ParseVectorModelFromDict(dict);
    }

    private VectorModel ParseVectorModel(HashEntry[] hash)
    {
        var dict = hash.ToDictionary(h => h.Name.ToString(), h => h.Value);
        return ParseVectorModelFromDict(dict);
    }

    private VectorModel ParseVectorModelFromDict(Dictionary<string, RedisValue> dict)
    {
        var metadata = dict.ContainsKey("metadata") && !string.IsNullOrEmpty(dict["metadata"])
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(dict["metadata"])
            : new Dictionary<string, object>();

        var embedding = dict.ContainsKey("embedding") && !string.IsNullOrEmpty(dict["embedding"])
            ? DeserializeVector((byte[])dict["embedding"])
            : Array.Empty<float>();

        var createdAtTicks = dict.ContainsKey("created_at") && long.TryParse(dict["created_at"], out var ct)
            ? ct
            : DateTime.UtcNow.Ticks;

        var updatedAtTicks = dict.ContainsKey("updated_at") && long.TryParse(dict["updated_at"], out var ut) && ut > 0
            ? (DateTime?)new DateTime(ut)
            : null;

        return new VectorModel
        {
            Id = dict.ContainsKey("id") ? dict["id"] : string.Empty,
            Data = dict.ContainsKey("data") ? dict["data"] : string.Empty,
            UserId = dict.ContainsKey("user_id") && !string.IsNullOrEmpty(dict["user_id"]) ? dict["user_id"].ToString() : null,
            Hash = dict.ContainsKey("hash") ? dict["hash"].ToString() : null,
            Metadata = metadata,
            CreatedAt = new DateTime(createdAtTicks),
            UpdatedAt = updatedAtTicks,
            Embedding = embedding
        };
    }

    private byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private float[] DeserializeVector(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private string EscapeRedisQuery(string value)
    {
        return value.Replace("-", "\\-")
                   .Replace(":", "\\:")
                   .Replace("@", "\\@");
    }
}
