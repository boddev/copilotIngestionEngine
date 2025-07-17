using System.Text.Json;

namespace copilotIngestionEngine.Models;

public record IngestionRequest(JsonDocument[] Documents);

public record IngestionResponse(
    bool Success,
    string Message,
    int ProcessedCount,
    string[] Errors
);