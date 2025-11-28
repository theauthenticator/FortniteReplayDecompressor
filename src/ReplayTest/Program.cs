using FortniteReplayReader;
using Unreal.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
var replayDirectory = Environment.GetEnvironmentVariable("REPLAY_DIRECTORY") ?? @"C:\replays";
var epicAccessToken = Environment.GetEnvironmentVariable("EPIC_ACCESS_TOKEN") ?? "dbb29d4e04844d868240b66f5408090a";
var maxParallelParsing = 500;

var enableLogging = true;


var isDevelopment = environment == "Development";

ILogger<ReplayReader> logger;
ILogger<Program> programLogger;

if (enableLogging)
{
    var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });

        builder.SetMinimumLevel(LogLevel.Debug);
    });

    logger = loggerFactory.CreateLogger<ReplayReader>();
    programLogger = loggerFactory.CreateLogger<Program>();
}
else
{
    logger = NullLogger<ReplayReader>.Instance;
    programLogger = NullLogger<Program>.Instance;
}

// CLI MODE - BATCH PROCESSING
if (args.Contains("--cli-batch"))
{
    var batchFileIndex = Array.IndexOf(args, "--cli-batch");
    var batchFile = args[batchFileIndex + 1];
    var outputFileIndex = Array.IndexOf(args, "--output");
    var outputFile = args[outputFileIndex + 1];
    var silentMode = args.Contains("--silent");

    Console.WriteLine($"[CLI] Processing batch file: {batchFile}");
    Console.WriteLine($"[CLI] Output file: {outputFile}");
    Console.WriteLine($"[CLI] Silent mode: {silentMode}");

    await ProcessBatchFileCLI(batchFile, outputFile, silentMode);
    return;
}

// CLI MODE - SINGLE FILE TESTING
if (args.Contains("--cli-single"))
{
    var replayFileIndex = Array.IndexOf(args, "--cli-single");
    var replayFile = args[replayFileIndex + 1];
    var outputFileIndex = Array.IndexOf(args, "--output");
    var outputFile = outputFileIndex >= 0 ? args[outputFileIndex + 1] : null;
    var silentMode = args.Contains("--silent");

    Console.WriteLine($"[CLI] Processing single replay: {replayFile}");
    if (outputFile != null)
    {
        Console.WriteLine($"[CLI] Output file: {outputFile}");
    }
    Console.WriteLine($"[CLI] Silent mode: {silentMode}");

    await ProcessSingleFileCLI(replayFile, outputFile, silentMode);
    return;
}

Console.WriteLine("Usage:");
Console.WriteLine("  Batch mode:  ./ReplayTest --cli-batch <batch-file> --output <output-file> [--silent]");
Console.WriteLine("  Single mode: ./ReplayTest --cli-single <replay-file> [--output <output-file>] [--silent]");
return;

async Task ProcessSingleFileCLI(string replayFile, string? outputFile, bool silentMode)
{
    try
    {
        Console.WriteLine($"[CLI] Checking file exists...");

        if (!File.Exists(replayFile))
        {
            Console.WriteLine($"[CLI] ERROR: File not found: {replayFile}");

            var errorResult = new ParseResult
            {
                Success = false,
                Error = $"File not found: {replayFile}"
            };

            if (outputFile != null)
            {
                await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(errorResult, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = null
                }));
            }
            return;
        }

        var sessionId = Path.GetFileNameWithoutExtension(replayFile);
        Console.WriteLine($"[CLI] Session ID: {sessionId}");
        Console.WriteLine($"[CLI] Parsing replay...");

        TextWriter? originalOut = null;
        TextWriter? originalError = null;

        if (silentMode)
        {
            originalOut = Console.Out;
            originalError = Console.Error;
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }

        ParseResult result;
        try
        {
            var parseRequest = new ParseRequest
            {
                SessionId = sessionId,
                ReplayPath = replayFile
            };

            result = await ParseReplayLocal(parseRequest, programLogger);
        }
        finally
        {
            if (silentMode && originalOut != null)
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError!);
            }
        }

        result.SessionId = sessionId;

        if (result.Success)
        {
            Console.WriteLine($"[CLI] ✅ Successfully parsed replay");

            if (outputFile != null)
            {
                Console.WriteLine($"[CLI] Writing results to: {outputFile}");
                var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = null
                });
                await File.WriteAllTextAsync(outputFile, resultJson);
            }
            else
            {
                Console.WriteLine($"[CLI] Stats preview:");
                var statsJson = JsonSerializer.Serialize(result.Stats, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = null
                });
                Console.WriteLine(statsJson.Length > 500 ? statsJson.Substring(0, 500) + "..." : statsJson);
            }
        }
        else
        {
            Console.WriteLine($"[CLI] ❌ Failed to parse replay");
            Console.WriteLine($"[CLI] Error: {result.Error}");

            if (outputFile != null)
            {
                await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = null
                }));
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CLI] FATAL ERROR: {ex.Message}");
        Console.WriteLine($"[CLI] Stack trace: {ex.StackTrace}");

        var errorResult = new ParseResult
        {
            Success = false,
            Error = $"Exception: {ex.Message}"
        };

        if (outputFile != null)
        {
            await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(errorResult, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null
            }));
        }
    }
}

async Task ProcessBatchFileCLI(string batchFile, string outputFile, bool silentMode)
{
    try
    {
        Console.WriteLine($"[CLI] Reading batch file...");
        var batchJson = await File.ReadAllTextAsync(batchFile);

        // 🔥 Use case-insensitive deserialization
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var batch = JsonSerializer.Deserialize<CLIBatchRequest>(batchJson, options);

        if (batch?.Replays == null || batch.Replays.Count == 0)
        {
            Console.WriteLine($"[CLI] ERROR: No replays in batch file");
            await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(new CLIBatchResponse
            {
                Success = false,
                Error = "No replays in batch file",
                Results = new List<ParseResult>()
            }));
            return;
        }

        Console.WriteLine($"[CLI] Processing {batch.Replays.Count} replays...");

        var results = new ConcurrentBag<ParseResult>();
        var semaphore = new SemaphoreSlim(maxParallelParsing);

        var tasks = batch.Replays.Select(async replay =>
        {
            await semaphore.WaitAsync();
            try
            {
                Console.WriteLine($"[CLI] Parsing: {replay.SessionId}");

                TextWriter? originalOut = null;
                TextWriter? originalError = null;

                if (silentMode)
                {
                    originalOut = Console.Out;
                    originalError = Console.Error;
                    Console.SetOut(TextWriter.Null);
                    Console.SetError(TextWriter.Null);
                }

                ParseResult result;
                try
                {
                    result = await ParseReplayLocal(replay, NullLogger<Program>.Instance);
                }
                finally
                {
                    if (silentMode && originalOut != null)
                    {
                        Console.SetOut(originalOut);
                        Console.SetError(originalError!);
                    }
                }

                result.SessionId = replay.SessionId;
                result.EventId = replay.EventId;
                result.WindowId = replay.WindowId;
                results.Add(result);

                var status = result.Success ? "✅" : "❌";
                Console.WriteLine($"[CLI] {status} {replay.SessionId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLI] ❌ Exception for {replay.SessionId}: {ex.Message}");
                results.Add(new ParseResult
                {
                    SessionId = replay.SessionId,
                    EventId = replay.EventId,
                    WindowId = replay.WindowId,
                    Success = false,
                    Error = $"Exception: {ex.Message}"
                });
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var response = new CLIBatchResponse
        {
            Success = true,
            Results = results.ToList()
        };

        Console.WriteLine($"[CLI] Writing results to: {outputFile}");

        // 🔥 Serialize with exact property names
        var resultJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null
        });
        await File.WriteAllTextAsync(outputFile, resultJson);

        Console.WriteLine($"[CLI] Complete: {results.Count(r => r.Success)}/{results.Count} successful");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CLI] FATAL ERROR: {ex.Message}");

        await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(new CLIBatchResponse
        {
            Success = false,
            Error = ex.Message,
            Results = new List<ParseResult>()
        }));
    }
}

async Task<ParseResult> ParseReplayLocal(ParseRequest request, ILogger<Program> log)
{
    try
    {
        string replayPath;

        if (!string.IsNullOrEmpty(request.ReplayPath))
        {
            replayPath = request.ReplayPath;
        }
        else
        {
            replayPath = Path.Combine(replayDirectory, $"{request.SessionId}.replay");
        }

        if (!File.Exists(replayPath))
        {
            var directory = Path.GetDirectoryName(replayPath);
            var filesInDir = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.replay").Length
                : 0;

            var errorMsg = $"File not found: {replayPath} | Dir exists: {Directory.Exists(directory)} | Files in dir: {filesInDir}";
            return new ParseResult { Success = false, Error = errorMsg };
        }

        var reader = new ReplayReader(logger, ParseMode.Full);
        reader.SetEpicAccessToken(epicAccessToken);

        var replay = reader.ReadReplay(replayPath);
        var statsJson = await reader.ExportPlayerStatsToJsonAsync(request.SessionId);

        // Extract ReplayInfo
        ReplayInfoDto? replayInfoDto = null;
        if (replay?.Info != null)
        {
            replayInfoDto = new ReplayInfoDto
            {
                LengthInMs = replay.Info.LengthInMs,
                NetworkVersion = replay.Info.NetworkVersion,
                Changelist = replay.Info.Changelist,
                FriendlyName = replay.Info.FriendlyName ?? string.Empty,
                Timestamp = replay.Info.Timestamp,
                TotalDataSizeInBytes = replay.Info.TotalDataSizeInBytes,
                IsLive = replay.Info.IsLive,
                IsCompressed = replay.Info.IsCompressed,
                IsEncrypted = replay.Info.IsEncrypted,
                FileVersion = replay.Info.FileVersion.ToString()
            };
        }

        return new ParseResult
        {
            Success = true,
            ReplayInfo = replayInfoDto,
            Stats = statsJson
        };
    }
    catch (Exception ex)
    {
        var error = $"{ex.GetType().Name}: {ex.Message}";
        return new ParseResult { Success = false, Error = error };
    }
}

// Class definitions with JSON attributes
public class CLIBatchRequest
{
    [JsonPropertyName("Replays")]
    public List<ParseRequest> Replays { get; set; } = new();
}

public class CLIBatchResponse
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Error")]
    public string? Error { get; set; }

    [JsonPropertyName("Results")]
    public List<ParseResult> Results { get; set; } = new();
}

public class ParseRequest
{
    [JsonPropertyName("SessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("EventId")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("WindowId")]
    public string WindowId { get; set; } = string.Empty;

    [JsonPropertyName("ReplayPath")]
    public string? ReplayPath { get; set; }
}

public class ParseResult
{
    [JsonPropertyName("SessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("EventId")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("WindowId")]
    public string WindowId { get; set; } = string.Empty;

    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Error")]
    public string? Error { get; set; }

    [JsonPropertyName("ReplayInfo")]
    public ReplayInfoDto? ReplayInfo { get; set; }

    [JsonPropertyName("Stats")]
    public object? Stats { get; set; }
}

public class ReplayInfoDto
{
    [JsonPropertyName("LengthInMs")]
    public uint LengthInMs { get; set; }

    [JsonPropertyName("NetworkVersion")]
    public uint NetworkVersion { get; set; }

    [JsonPropertyName("Changelist")]
    public uint Changelist { get; set; }

    [JsonPropertyName("FriendlyName")]
    public string FriendlyName { get; set; } = string.Empty;

    [JsonPropertyName("Timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("TotalDataSizeInBytes")]
    public long TotalDataSizeInBytes { get; set; }

    [JsonPropertyName("IsLive")]
    public bool IsLive { get; set; }

    [JsonPropertyName("IsCompressed")]
    public bool IsCompressed { get; set; }

    [JsonPropertyName("IsEncrypted")]
    public bool IsEncrypted { get; set; }

    [JsonPropertyName("FileVersion")]
    public string FileVersion { get; set; } = string.Empty;
}