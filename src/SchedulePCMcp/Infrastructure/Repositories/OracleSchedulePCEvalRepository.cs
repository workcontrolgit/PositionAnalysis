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

    public async Task<string?> GetLatestRunIdAsync()
    {
        _logger.LogDebug("Fetching latest run id from schedule_pc_eval");

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT run_id
            FROM (
                SELECT run_id
                FROM schedule_pc_eval
                GROUP BY run_id
                ORDER BY run_id DESC
            )
            WHERE ROWNUM = 1";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };

        var result = await cmd.ExecuteScalarAsync();
        var runId = result?.ToString();
        _logger.LogDebug("Latest run id resolved to {RunId}", runId ?? "<none>");
        return runId;
    }

    public async Task<List<SeriesCounts>> GetSeriesCountsAsync(string runId)
    {
        _logger.LogInformation("Fetching aggregate series counts for run {RunId}", runId);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        var seriesColumn = await ResolveColumnNameAsync(connection, "SCHEDULE_PC_EVAL", "OCC_SERIES", "SERIES");
        var statusColumn = await ResolveColumnNameAsync(connection, "SCHEDULE_PC_EVAL", "STATUS", "RATING");

        if (seriesColumn is null)
            throw new InvalidOperationException("SCHEDULE_PC_EVAL is missing both OCC_SERIES and SERIES columns.");

        if (statusColumn is null)
            throw new InvalidOperationException("SCHEDULE_PC_EVAL is missing both STATUS and RATING columns.");

        var sql = $@"
            SELECT {seriesColumn},
                   SUM(CASE WHEN UPPER(NVL({statusColumn}, 'PENDING')) = 'PENDING' THEN 1 ELSE 0 END) AS staged_count,
                   SUM(CASE WHEN UPPER(NVL({statusColumn}, '')) = 'IN_PROGRESS' THEN 1 ELSE 0 END) AS in_progress_count,
                   SUM(CASE WHEN UPPER(NVL({statusColumn}, '')) IN ('FAILED', 'GENERATION_FAILED') THEN 1 ELSE 0 END) AS failed_count,
                   SUM(CASE WHEN UPPER(NVL({statusColumn}, 'PENDING')) IN ('DONE', 'COMPLETE', 'COMPLETED') THEN 1 ELSE 0 END) AS complete_count
            FROM schedule_pc_eval
            WHERE run_id = :runId
            GROUP BY {seriesColumn}
            ORDER BY {seriesColumn}";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":runId", runId);

        var output = new List<SeriesCounts>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var series = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(series) || series.Length != 4)
                continue;

            var staged = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            var inProgress = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
            var failed = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
            var complete = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));

            output.Add(new SeriesCounts(series, staged, inProgress, complete, failed));
        }

        _logger.LogInformation("Retrieved aggregate status counts for {Count} series in run {RunId}", output.Count, runId);
        return output;
    }

    private async Task<string?> ResolveColumnNameAsync(OracleConnection connection, string tableName, params string[] preferredColumns)
    {
        if (preferredColumns is null || preferredColumns.Length == 0)
            return null;

        const string sql = @"
            SELECT column_name
            FROM user_tab_columns
            WHERE table_name = :tableName
              AND column_name = :columnName";

        foreach (var column in preferredColumns)
        {
            using var cmd = new OracleCommand(sql, connection)
            {
                CommandTimeout = _settings.CommandTimeout
            };

            cmd.Parameters.Add(":tableName", tableName.ToUpperInvariant());
            cmd.Parameters.Add(":columnName", column.ToUpperInvariant());

            var exists = await cmd.ExecuteScalarAsync();
            if (exists is not null)
                return column.ToUpperInvariant();
        }

        return null;
    }

    public async Task<EvaluationResult?> GetByRunAndPdAsync(string runId, string pdNbr)
    {
        _logger.LogDebug("Fetching evaluation result for run {RunId}, PD {PdNbr}", runId, pdNbr);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
                 SELECT run_id, pd_nbr, occ_series, grade, overall_score, rating, is_candidate,
                     justification_summary, raw_llm_response, result_json, evaluated_date
            FROM schedule_pc_eval 
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

        var result = MapEvaluationResult(reader);

        _logger.LogDebug("Retrieved evaluation result for {PdNbr}", pdNbr);
        return result;
    }

    public async Task<List<EvaluationResult>> GetByRunAsync(string runId)
    {
        _logger.LogInformation("Fetching all evaluation results for run {RunId}", runId);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
                 SELECT run_id, pd_nbr, occ_series, grade, overall_score, rating, is_candidate,
                     justification_summary, raw_llm_response, result_json, evaluated_date
            FROM schedule_pc_eval 
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
            var result = MapEvaluationResult(reader);
            if (result is not null)
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
                 SELECT run_id, pd_nbr, occ_series, grade, overall_score, rating, is_candidate,
                     justification_summary, raw_llm_response, result_json, evaluated_date
            FROM schedule_pc_eval 
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
            var result = MapEvaluationResult(reader);
            if (result is not null)
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
                 SELECT run_id, pd_nbr, occ_series, grade, overall_score, rating, is_candidate,
                     justification_summary, raw_llm_response, result_json, evaluated_date
            FROM schedule_pc_eval 
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
            var result = MapEvaluationResult(reader);
            if (result is not null)
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

    private EvaluationResult? MapEvaluationResult(OracleDataReader reader)
    {
        // Preferred path: deserialize authoritative JSON payload when present.
        if (!reader.IsDBNull(9))
        {
            var resultJson = reader.GetString(9);
            if (!string.IsNullOrWhiteSpace(resultJson))
            {
                var parsed = JsonSerializer.Deserialize<EvaluationResult>(resultJson);
                if (parsed is not null)
                    return parsed;
            }
        }

        // Fallback path: hydrate from relational columns for older/partially populated rows.
        var runId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var pdNbr = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var seriesCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

        if (string.IsNullOrWhiteSpace(seriesCode) || seriesCode.Length != 4)
        {
            _logger.LogWarning(
                "Skipping malformed evaluation row for run {RunId}, PD {PdNbr}: invalid series '{SeriesCode}'",
                runId,
                pdNbr,
                seriesCode);
            return null;
        }

        var rawGrade = reader.IsDBNull(3) ? 1 : Convert.ToInt32(reader.GetValue(3));
        var grade = Math.Clamp(rawGrade, 1, 15);

        return new EvaluationResult
        {
            RunId = runId,
            PdNbr = pdNbr,
            Series = new OccupationalSeries(seriesCode),
            Grade = new Grade(grade),
            OverallScore = reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4)),
            Rating = reader.IsDBNull(5) ? "PENDING" : reader.GetString(5),
            IsCandidate = !reader.IsDBNull(6) && string.Equals(reader.GetString(6), "Y", StringComparison.OrdinalIgnoreCase),
            JustificationSummary = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            RawLlmResponse = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            EvaluatedDate = reader.IsDBNull(10) ? DateTime.UtcNow : reader.GetDateTime(10),
            EvaluatedBy = "SYSTEM"
        };
    }
}
