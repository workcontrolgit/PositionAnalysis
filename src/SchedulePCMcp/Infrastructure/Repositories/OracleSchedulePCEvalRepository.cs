using System.Text.Json;
using Oracle.ManagedDataAccess.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Domain.Enums;
using SchedulePCMcp.Infrastructure.Config;

namespace SchedulePCMcp.Infrastructure.Repositories;

/// <summary>
/// Oracle implementation of SchedulePCEvalRepository
/// Reads from/writes to SCHEDULE_PC_EVAL table
/// Stores evaluation results as JSON in result_json CLOB column
/// </summary>
public class OracleSchedulePCEvalRepository : ISchedulePCEvalRepository
{
    private readonly OracleSettings _settings;
    private readonly ILogger<OracleSchedulePCEvalRepository> _logger;

    public OracleSchedulePCEvalRepository(
        IOptions<OracleSettings> options,
        ILogger<OracleSchedulePCEvalRepository> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<string> InsertAsync(EvaluationResult result)
    {
        _logger.LogInformation("Inserting evaluation result for PD {PdNbr} in run {RunId}", result.PdNbr, result.RunId);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO schedule_pc_eval 
            (run_id, pd_nbr, occ_series, grade, status, overall_score, rating, is_candidate, 
             justification_summary, raw_llm_response, result_json, evaluated_date, evaluated_by)
            VALUES 
            (:runId, :pdNbr, :series, :grade, :status, :score, :rating, :isCandidate,
             :justification, :rawResponse, :resultJson, SYSDATE, :evaluatedBy)
            RETURNING eval_id INTO :evalId";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };

        cmd.Parameters.Add(":runId", result.RunId);
        cmd.Parameters.Add(":pdNbr", result.PdNbr);
        cmd.Parameters.Add(":series", result.Series.ToString());
        cmd.Parameters.Add(":grade", result.Grade.Value);
        cmd.Parameters.Add(":status", (int)EvaluationStatus.Complete);
        cmd.Parameters.Add(":score", result.OverallScore);
        cmd.Parameters.Add(":rating", result.Rating.ToString());
        cmd.Parameters.Add(":isCandidate", result.IsCandidate ? "Y" : "N");
        cmd.Parameters.Add(":justification", result.JustificationSummary);
        cmd.Parameters.Add(":rawResponse", result.RawLlmResponse);
        
        var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        cmd.Parameters.Add(":resultJson", resultJson);
        cmd.Parameters.Add(":evaluatedBy", result.EvaluatedBy ?? "SYSTEM");

        var evalIdParam = new OracleParameter(":evalId", OracleDbType.Decimal)
        {
            Direction = System.Data.ParameterDirection.Output
        };
        cmd.Parameters.Add(evalIdParam);

        await cmd.ExecuteNonQueryAsync();
        var evalId = evalIdParam.Value?.ToString() ?? "";

        _logger.LogInformation("Inserted evaluation result with ID {EvalId}", evalId);
        return evalId;
    }

    public async Task UpdateAsync(EvaluationResult result)
    {
        _logger.LogInformation("Updating evaluation result for PD {PdNbr} in run {RunId}", result.PdNbr, result.RunId);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE schedule_pc_eval 
            SET status = :status,
                overall_score = :score,
                rating = :rating,
                is_candidate = :isCandidate,
                justification_summary = :justification,
                raw_llm_response = :rawResponse,
                result_json = :resultJson,
                evaluated_date = SYSDATE,
                evaluated_by = :evaluatedBy
            WHERE run_id = :runId AND pd_nbr = :pdNbr";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };

        cmd.Parameters.Add(":status", (int)EvaluationStatus.Complete);
        cmd.Parameters.Add(":score", result.OverallScore);
        cmd.Parameters.Add(":rating", result.Rating.ToString());
        cmd.Parameters.Add(":isCandidate", result.IsCandidate ? "Y" : "N");
        cmd.Parameters.Add(":justification", result.JustificationSummary);
        cmd.Parameters.Add(":rawResponse", result.RawLlmResponse);
        
        var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        cmd.Parameters.Add(":resultJson", resultJson);
        cmd.Parameters.Add(":evaluatedBy", result.EvaluatedBy ?? "SYSTEM");
        cmd.Parameters.Add(":runId", result.RunId);
        cmd.Parameters.Add(":pdNbr", result.PdNbr);

        var rows = await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Updated {RowCount} evaluation result rows", rows);
    }

    public async Task<EvaluationResult?> GetByRunAndPdAsync(string runId, string pdNbr)
    {
        _logger.LogDebug("Fetching evaluation result for run {RunId}, PD {PdNbr}", runId, pdNbr);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT result_json FROM schedule_pc_eval 
            WHERE run_id = :runId AND pd_nbr = :pdNbr";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":runId", runId);
        cmd.Parameters.Add(":pdNbr", pdNbr);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var resultJson = reader.GetString(0);
        var result = JsonSerializer.Deserialize<EvaluationResult>(resultJson);

        _logger.LogDebug("Retrieved evaluation result for {PdNbr}", pdNbr);
        return result;
    }

    public async Task<List<EvaluationResult>> GetByRunAsync(string runId)
    {
        _logger.LogInformation("Fetching all evaluation results for run {RunId}", runId);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT result_json FROM schedule_pc_eval 
            WHERE run_id = :runId
            ORDER BY pd_nbr";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":runId", runId);

        var results = new List<EvaluationResult>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var resultJson = reader.GetString(0);
            var result = JsonSerializer.Deserialize<EvaluationResult>(resultJson);
            if (result != null)
                results.Add(result);
        }

        _logger.LogInformation("Retrieved {Count} evaluation results for run {RunId}", results.Count, runId);
        return results;
    }

    public async Task<List<EvaluationResult>> GetByRunAndSeriesAsync(string runId, OccupationalSeries series)
    {
        _logger.LogInformation("Fetching evaluation results for run {RunId}, series {Series}", runId, series);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT result_json FROM schedule_pc_eval 
            WHERE run_id = :runId AND occ_series = :series
            ORDER BY pd_nbr";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":runId", runId);
        cmd.Parameters.Add(":series", series.ToString());

        var results = new List<EvaluationResult>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var resultJson = reader.GetString(0);
            var result = JsonSerializer.Deserialize<EvaluationResult>(resultJson);
            if (result != null)
                results.Add(result);
        }

        _logger.LogInformation("Retrieved {Count} evaluation results for run {RunId}, series {Series}", 
            results.Count, runId, series);
        return results;
    }

    public async Task<List<EvaluationResult>> GetByStatusAsync(EvaluationStatus status)
    {
        _logger.LogInformation("Fetching evaluation results with status {Status}", status);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT result_json FROM schedule_pc_eval 
            WHERE status = :status
            ORDER BY evaluated_date DESC";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":status", (int)status);

        var results = new List<EvaluationResult>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var resultJson = reader.GetString(0);
            var result = JsonSerializer.Deserialize<EvaluationResult>(resultJson);
            if (result != null)
                results.Add(result);
        }

        _logger.LogInformation("Retrieved {Count} evaluation results with status {Status}", results.Count, status);
        return results;
    }

    public async Task<int> GetCountByStatusAsync(string runId, EvaluationStatus status)
    {
        _logger.LogDebug("Counting evaluation results for run {RunId} with status {Status}", runId, status);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT COUNT(*) FROM schedule_pc_eval 
            WHERE run_id = :runId AND status = :status";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":runId", runId);
        cmd.Parameters.Add(":status", (int)status);

        var result = await cmd.ExecuteScalarAsync();
        var count = result != null ? (int)(decimal)result : 0;
        _logger.LogDebug("Count result: {Count} evaluations with status {Status}", count, status);
        return count;
    }
}
