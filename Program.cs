using copilotIngestionEngine.Configuration;
using copilotIngestionEngine.Models;
using copilotIngestionEngine.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure options
builder.Services.Configure<MicrosoftGraphOptions>(
    builder.Configuration.GetSection(MicrosoftGraphOptions.SectionName));

// Register services
builder.Services.AddScoped<IApiKeyValidationService, ApiKeyValidationService>();
builder.Services.AddScoped<IGraphIngestionService, GraphIngestionService>();

// Add logging
builder.Services.AddLogging();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Only use HTTPS redirection in non-development environments
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Minimal API endpoint for document ingestion
app.MapPost("/api/ingest", async (
    [FromBody] IngestionRequest request,
    [FromHeader(Name = "X-API-Key")] string? apiKey,
    IApiKeyValidationService apiKeyService,
    IGraphIngestionService graphService,
    ILogger<Program> logger) =>
{
    // Validate API key
    if (!await apiKeyService.ValidateApiKeyAsync(apiKey))
    {
        logger.LogWarning("Invalid API key provided");
        return Results.Unauthorized();
    }

    // Validate request
    if (request.Documents == null || request.Documents.Length == 0)
    {
        return Results.BadRequest(new IngestionResponse(
            false,
            "No documents provided",
            0,
            new[] { "Request must contain at least one document" }
        ));
    }

    try
    {
        logger.LogInformation("Starting ingestion of {Count} documents", request.Documents.Length);

        //single document processing
        //var (success, errors) = await graphService.IngestDocumentsAsync(request.Documents);

        //batch processing
        var (success, errors) = await graphService.IngestDocumentsBatchAsync(request.Documents);
        
        var response = new IngestionResponse(
            success,
            success ? "All documents ingested successfully" : "Some documents failed to ingest",
            success ? request.Documents.Length : request.Documents.Length - errors.Length,
            errors
        );

        logger.LogInformation("Ingestion completed. Success: {Success}, Processed: {Count}", 
            success, response.ProcessedCount);

        return success ? Results.Ok(response) : Results.BadRequest(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error during document ingestion");
        
        return Results.Problem(
            title: "Internal Server Error",
            detail: "An unexpected error occurred during document ingestion"
        );
    }
})
.WithName("IngestDocuments")
.WithSummary("Ingest JSON documents into Microsoft Graph")
.WithDescription("Accepts an array of JSON documents and ingests them into Microsoft Graph as ExternalItems")
.WithOpenApi();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithOpenApi();

app.Run();