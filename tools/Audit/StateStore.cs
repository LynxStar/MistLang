using System.Text.Json;
using System.Text.Json.Serialization;
using Audit.Models;

namespace Audit;

public sealed class StateStore
{
    private readonly string statePath;
    private readonly JsonSerializerOptions jsonOptions;

    public StateStore(string repositoryRoot)
    {
        var auditDir = Path.Combine(repositoryRoot, ".audit");
        _ = Directory.CreateDirectory(auditDir);
        statePath = Path.Combine(auditDir, "audit-state.json");
        jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
    }

    /// <summary>
    /// Loads existing state from disk, or returns an empty state if the file does not exist.
    /// Throws InvalidOperationException if the file exists but is corrupt/unparseable.
    /// </summary>
    public AuditState Load()
    {
        if(!File.Exists(statePath))
        {
            return new AuditState([]);
        }

        try
        {
            var json = File.ReadAllText(statePath);
            var state = JsonSerializer.Deserialize<AuditState>(json, jsonOptions);
            return state ?? new AuditState([]);
        }
        catch(JsonException ex)
        {
            throw new InvalidOperationException(
                $"Corrupt audit state file: {statePath}", ex);
        }
    }

    /// <summary>
    /// Persists state atomically: writes to a .tmp file then renames over the target.
    /// Throws IOException if the write fails (does not corrupt the existing file).
    /// </summary>
    public void Save(AuditState state)
    {
        var tempPath = statePath + ".tmp";
        var json = JsonSerializer.Serialize(state, jsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, statePath, overwrite: true);
    }
}