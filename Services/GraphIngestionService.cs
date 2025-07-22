using Microsoft.Graph;
using Microsoft.Graph.Models.ExternalConnectors;
using Azure.Identity;
using copilotIngestionEngine.Configuration;
using copilotIngestionEngine.Models;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace copilotIngestionEngine.Services;

public interface IGraphIngestionService
{
    Task<(bool Success, string[] Errors)> IngestDocumentsAsync(JsonDocument[] documents, AuthenticationRequest authRequest);
    Task<(bool Success, string[] Errors)> IngestDocumentsBatchAsync(JsonDocument[] documents, AuthenticationRequest authRequest);
}

public class GraphIngestionService : IGraphIngestionService
{
    private readonly ILogger<GraphIngestionService> _logger;
    private const int BatchSize = 20; // Microsoft Graph batch limit

    public GraphIngestionService(ILogger<GraphIngestionService> logger)
    {
        _logger = logger;
    }

    public async Task<(bool Success, string[] Errors)> IngestDocumentsAsync(JsonDocument[] documents, AuthenticationRequest authRequest)
    {
        var errors = new List<string>();
        var successCount = 0;
        var graphServiceClient = CreateGraphServiceClient(authRequest);

        foreach (var (document, index) in documents.Select((doc, i) => (doc, i)))
        {
            try
            {
                var externalItem = CreateExternalItem(document, index);
                
                await graphServiceClient.External.Connections[authRequest.ConnectionId]
                    .Items[externalItem.Id]
                    .PutAsync(externalItem);

                successCount++;
                _logger.LogInformation("Successfully ingested document {Index}", index);
            }
            catch (Exception ex)
            {
                var error = $"Failed to ingest document {index}: {ex.Message}";
                errors.Add(error);
                _logger.LogError(ex, "Failed to ingest document {Index}", index);
            }
        }

        return (successCount == documents.Length, errors.ToArray());
    }

    public async Task<(bool Success, string[] Errors)> IngestDocumentsBatchAsync(JsonDocument[] documents, AuthenticationRequest authRequest)
    {
        var errors = new List<string>();
        var successCount = 0;

        // Process documents in batches
        for (int i = 0; i < documents.Length; i += BatchSize)
        {
            var batch = documents.Skip(i).Take(BatchSize).ToArray();
            var (batchSuccessCount, batchErrors) = await ProcessBatchAsync(batch, i, authRequest);
            
            successCount += batchSuccessCount;
            errors.AddRange(batchErrors);
        }

        return (successCount == documents.Length, errors.ToArray());
    }

    private GraphServiceClient CreateGraphServiceClient(AuthenticationRequest authRequest)
    {
        var credential = new ClientSecretCredential(
            authRequest.TenantId,
            authRequest.ClientId,
            authRequest.ClientSecret);

        return new GraphServiceClient(credential);
    }

    private async Task<(int SuccessCount, List<string> Errors)> ProcessBatchAsync(JsonDocument[] documents, int batchStartIndex, AuthenticationRequest authRequest)
    {
        var errors = new List<string>();
        var successCount = 0;

        try
        {
            var graphServiceClient = CreateGraphServiceClient(authRequest);
            var batchRequestContent = new BatchRequestContentCollection(graphServiceClient);
            var requestIds = new List<string>();

            // Create batch requests
            for (int i = 0; i < documents.Length; i++)
            {
                var document = documents[i];
                var documentIndex = batchStartIndex + i;
                var externalItem = CreateExternalItem(document, documentIndex);
                
                var requestUrl = $"/external/connections/{authRequest.ConnectionId}/items/{externalItem.Id}";
                var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
                {
                    HttpMethod = Microsoft.Kiota.Abstractions.Method.PUT,
                    URI = new Uri("https://graph.microsoft.com/v1.0" + requestUrl),
                };
                requestInfo.Headers.Add("Content-Type", "application/json");
                requestInfo.SetContentFromParsable(graphServiceClient.RequestAdapter, "application/json", externalItem);

                // Convert RequestInformation to HttpRequestMessage
                var httpRequestMessage = await graphServiceClient.RequestAdapter.ConvertToNativeRequestAsync<System.Net.Http.HttpRequestMessage>(requestInfo);

                var batchStepId = Guid.NewGuid().ToString();
                var batchStep = new Microsoft.Graph.BatchRequestStep(
                    batchStepId,
                    httpRequestMessage,
                    null
                );

                batchRequestContent.AddBatchRequestStep(batchStep);

                requestIds.Add(batchStepId);
                _logger.LogDebug("Added document {Index} to batch with request ID {RequestId}", documentIndex, batchStepId);
            }

            // Execute batch request
            _logger.LogInformation("Executing batch request with {Count} documents", documents.Length);
            var batchResponse = await graphServiceClient.Batch.PostAsync(batchRequestContent);

            // Process batch responses
            for (int i = 0; i < requestIds.Count; i++)
            {
                var requestId = requestIds[i];
                var documentIndex = batchStartIndex + i;
                
                var response = await batchResponse.GetResponseByIdAsync(requestId);
                if (response != null)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        successCount++;
                        _logger.LogInformation("Successfully ingested document {Index} via batch", documentIndex);
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        var error = $"Failed to ingest document {documentIndex}: {response.StatusCode} - {errorContent}";
                        errors.Add(error);
                        _logger.LogError("Failed to ingest document {Index} via batch: {StatusCode} - {Error}", 
                            documentIndex, response.StatusCode, errorContent);
                    }
                }
                else
                {
                    var error = $"Failed to get response for document {documentIndex} from batch";
                    errors.Add(error);
                    _logger.LogError("Failed to get response for document {Index} from batch", documentIndex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch starting at index {Index}", batchStartIndex);
            
            // Add error for all documents in this batch
            for (int i = 0; i < documents.Length; i++)
            {
                var documentIndex = batchStartIndex + i;
                errors.Add($"Failed to ingest document {documentIndex}: Batch processing error - {ex.Message}");
            }
        }

        return (successCount, errors);
    }

    private ExternalItem CreateExternalItem(JsonDocument document, int index)
    {
        var properties = new Dictionary<string, object?>();
        
        // Parse the JSON document and extract properties
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                properties[property.Name] = ExtractValue(property.Value);
            }
        }

        // Generate a unique ID for the item
        var itemId = properties.ContainsKey("id") 
            ? properties["id"]?.ToString() ?? $"item_{index}_{Guid.NewGuid()}"
            : $"item_{index}_{Guid.NewGuid()}";

        // Create the external item
        var externalItem = new ExternalItem
        {
            Id = itemId,
            Properties = new Properties
            {
                AdditionalData = properties
            },
            Content = new ExternalItemContent
            {
                Type = ExternalItemContentType.Text,
                Value = document.RootElement.GetRawText()
            },
            Acl = new List<Acl>
            {
                new Acl
                {
                    Type = AclType.Everyone,
                    Value = "everyone",
                    AccessType = AccessType.Grant
                }
            }
        };

        return externalItem;
    }

    private object? ExtractValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ExtractValue).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ExtractValue(p.Value)),
            _ => element.GetRawText()
        };
    }
}