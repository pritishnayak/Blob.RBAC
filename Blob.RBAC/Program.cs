using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Core.Serialization;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string cosmosAccountEndpoint = "https://audax-db.documents.azure.com:443/";
var cosmosClientOptions = new CosmosClientOptions
{
    ApplicationName = "Audex.Api",
    Serializer = new CosmosSystemTextJsonSerializer()
};
CosmosClient cosmosClient = new CosmosClient(cosmosAccountEndpoint, new DefaultAzureCredential(), cosmosClientOptions);
Database database = await cosmosClient.CreateDatabaseIfNotExistsAsync("RadarSensorData");
Container cosmosContainer = await database.CreateContainerIfNotExistsAsync("SensorData", "/partitionKey");
builder.Services.AddSingleton<Container>(cosmosContainer);

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}
app.UseHttpsRedirection();

app.MapGet("/test", () =>
    {
        Uri storageAccountUri = new("https://eurotrip.blob.core.windows.net");
        const string containerName = "zeropoint";
        BlobServiceClient blobServiceClient = new BlobServiceClient(storageAccountUri, new DefaultAzureCredential());
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        List<string> blobs = containerClient.GetBlobs().Select(x => x.Name).ToList();
        //BlobClient blobClient = containerClient.GetBlobClient("blobName.csv");
        //if (await blobClient.ExistsAsync())
        //{
        //    var response = await blobClient.DownloadAsync();
        //    using (var streamReader= new StreamReader(response.Value.Content))
        //    {
        //        while (!streamReader.EndOfStream)
        //        {
        //            var line = await streamReader.ReadLineAsync();
        //            Console.WriteLine(line);
        //        }
        //    }
        //}

        return Results.Ok(blobs);
    })
    .WithName("test")
    .WithOpenApi();

app.MapPost("/data", async ([FromBody] dynamic data, [FromService] Container container) =>
    {
        
        var cData = new SensorData
        {
            Data = data
        };
        var res = await container.CreateItemAsync(cData, new PartitionKey(cData.PartitionKey));

        return res.StatusCode == HttpStatusCode.Created ? Results.Ok() : Results.BadRequest(res); 
    })
    .WithName("SubmitData")
    .WithOpenApi();

app.MapGet("ping", () => Results.Ok("1.0.0.0"));

app.Run();

public class SensorData
{
    [JsonPropertyName("id")] 
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("partitionKey")] 
    public string PartitionKey { get; set; } = "Default";
    public dynamic Data { get; set; }  
}

public class CosmosSystemTextJsonSerializer : CosmosSerializer
{
    private readonly JsonObjectSerializer _systemTextJsonSerializer = 
        new(new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (stream is { CanSeek: true, Length: 0 })
            {
                return default;
            }

            if (typeof(Stream).IsAssignableFrom(typeof(T)))
            {
                return (T)(object)stream;
            }

            return (T)_systemTextJsonSerializer.Deserialize(stream, typeof(T), default);
        }
    }

    public override Stream ToStream<T>(T input)
    {
        MemoryStream streamPayload = new MemoryStream();
        _systemTextJsonSerializer.Serialize(streamPayload, input, input.GetType(), default);
        streamPayload.Position = 0;
        return streamPayload;
    }
}

