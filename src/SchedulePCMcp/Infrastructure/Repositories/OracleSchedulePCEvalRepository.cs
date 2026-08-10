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
        _logger.LogInformation("Inserting evaluation result for PD {PdNbr}", result.PdNbr);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO schedule_pc_eval
            (pd_seq_num, pd_nbr, series, grade, status, rating, is_candidate,
             justification_summary, scored_at, result_json, error_msg)
            VALUES
            (:pdSeqNum, :pdNbr, :series, :grade, :status, :rating, :isCandidate,
             :justification, SYSTIMESTAMP, :resultJson, NULL)";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };

        cmd.Parameters.Add(":pdSeqNum", result.PdSeqNum);
        cmd.Parameters.Add(":pdNbr", result.PdNbr);
        cmd.Parameters.Add(":series", result.Series.ToString());
        cmd.Parameters.Add(":grade", result.Grade.Value.ToString("D2"));
        cmd.Parameters.Add(":status", "pending");
        cmd.Parameters.Add(":rating", result.Rating.ToString());
        cmd.Parameters.Add(":isCandidate", result.IsCandidate ? "Y" : "N");
        cmd.Parameters.Add(":justification", result.JustificationSummary);

        var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        cmd.Parameters.Add(":resultJson", resultJson);

        await cmd.ExecuteNonQueryAsync();
        var newPdSeqNum = result.PdSeqNum.ToString();

        _logger.LogInformation("Inserted staged evaluation row with PD_SEQ_NUM {PdSeqNum}", newPdSeqNum);
        return newPdSeqNum;
    }

    public async Task UpdateAsync(EvaluationResult result)
    {
        _logger.LogInformation("Updating evaluation result for PD {PdNbr}", result.PdNbr);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE schedule_pc_eval
            SET status = :status,
                rating = :rating,
                is_candidate = :isCandidate,
                justification_summary = :justification,
                result_json = :resultJson,
                scored_at = SYSTIMESTAMP,
                error_msg = :errorMsg
            WHERE pd_nbr = :pdNbr AND series = :series";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };

        cmd.Parameters.Add(":status", MapStatusFromResult(result));
        cmd.Parameters.Add(":rating", result.Rating.ToString());
        cmd.Parameters.Add(":isCandidate", result.IsCandidate ? "Y" : "N");
        cmd.Parameters.Add(":justification", result.JustificationSummary);

        var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        cmd.Parameters.Add(":resultJson", resultJson);
        cmd.Parameters.Add(":errorMsg", result.Rating.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ? result.JustificationSummary : null);
        cmd.Parameters.Add(":pdNbr", result.PdNbr);
        cmd.Parameters.Add(":series", result.Series.ToString());

        var rows = await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Updated {RowCount} evaluation result rows", rows);
    }

    public async Task<int> DeleteAllAsync()
    {
        _logger.LogWarning("Deleting all records from schedule_pc_eval");

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = "DELETE FROM schedule_pc_eval";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };

        var deletedRows = await cmd.ExecuteNonQueryAsync();
        _logger.LogWarning("Deleted {RowCount} records from schedule_pc_eval", deletedRows);
        return deletedRows;
    }

    public async Task<int> StageFromMaxPdAsync(StagingFilter filter)
    {
        _logger.LogInformation("Calling Oracle bulk staging procedure with filter: {Filter}", filter);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        using var cmd = new OracleCommand("stage_schedule_pc_eval", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = _settings.CommandTimeout,
            BindByName = true
        };

        cmd.Parameters.Add("p_series", OracleDbType.Varchar2, filter.Series?.ToString(), System.Data.ParameterDirection.Input);
        cmd.Parameters.Add("p_org_code", OracleDbType.Varchar2, filter.OrganizationCode, System.Data.ParameterDirection.Input);
        var stagedCountParameter = cmd.Parameters.Add("p_staged_count", OracleDbType.Int32);
        stagedCountParameter.Direction = System.Data.ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();

        var stagedCount = Convert.ToInt32(stagedCountParameter.Value);
        _logger.LogInformation("Oracle bulk staging procedure inserted {StagedCount} rows", stagedCount);
        return stagedCount;
    }

    public async Task<List<SeriesCounts>> GetSeriesCountsAsync()
    {
        _logger.LogInformation("Fetching aggregate series counts from schedule_pc_eval");

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        var seriesColumn = await ResolveColumnNameAsync(connection, "SCHEDULE_PC_EVAL", "OCC_SERIES", "SERIES");
        var statusColumn = await ResolveColumnNameAsync(connection, "SCHEDULE_PC_EVAL", "STATUS");

        if (seriesColumn is null)
            throw new InvalidOperationException("SCHEDULE_PC_EVAL is missing both OCC_SERIES and SERIES columns.");

        if (statusColumn is null)
            throw new InvalidOperationException("SCHEDULE_PC_EVAL is missing STATUS column.");

        var sql = $@"
            SELECT {seriesColumn},
                   SUM(CASE WHEN UPPER(NVL({statusColumn}, 'PENDING')) = 'PENDING' THEN 1 ELSE 0 END) AS staged_count,
                   SUM(CASE WHEN UPPER(NVL({statusColumn}, '')) = 'IN_PROGRESS' THEN 1 ELSE 0 END) AS in_progress_count,
                   SUM(CASE WHEN UPPER(NVL({statusColumn}, '')) IN ('FAILED', 'GENERATION_FAILED') THEN 1 ELSE 0 END) AS failed_count,
                   SUM(CASE WHEN UPPER(NVL({statusColumn}, 'PENDING')) IN ('DONE', 'COMPLETE', 'COMPLETED') THEN 1 ELSE 0 END) AS complete_count
            FROM schedule_pc_eval
            GROUP BY {seriesColumn}
            ORDER BY {seriesColumn}";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        var output = new List<SeriesCounts>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var series = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(series) || series.Length != 5)
                continue;

            var staged = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            var inProgress = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
            var failed = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
            var complete = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));

            output.Add(new SeriesCounts(series, staged, inProgress, complete, failed));
        }

        _logger.LogInformation("Retrieved aggregate status counts for {Count} series", output.Count);
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

    public async Task<EvaluationResult?> GetByPdAsync(string pdNbr)
    {
        _logger.LogDebug("Fetching latest evaluation result for PD {PdNbr}", pdNbr);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT pd_nbr,
                   series AS occ_series,
                   TO_NUMBER(grade) AS grade,
                   CAST(0 AS NUMBER) AS overall_score,
                   rating,
                   is_candidate,
                   justification_summary,
                   result_json,
                   NVL(CAST(scored_at AS DATE), SYSDATE) AS evaluated_date
            FROM schedule_pc_eval
            WHERE pd_nbr = :pdNbr
            ORDER BY scored_at DESC
            FETCH FIRST 1 ROWS ONLY";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":pdNbr", pdNbr);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var result = MapEvaluationResult(reader);

        _logger.LogDebug("Retrieved evaluation result for {PdNbr}", pdNbr);
        return result;
    }

    public async Task<List<EvaluationResult>> GetAllAsync()
    {
        _logger.LogInformation("Fetching all evaluation results");

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT pd_nbr,
                   series AS occ_series,
                   TO_NUMBER(grade) AS grade,
                   CAST(0 AS NUMBER) AS overall_score,
                   rating,
                   is_candidate,
                   justification_summary,
                   result_json,
                   NVL(CAST(scored_at AS DATE), SYSDATE) AS evaluated_date
            FROM schedule_pc_eval
            ORDER BY pd_nbr";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        var results = new List<EvaluationResult>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var result = MapEvaluationResult(reader);
            if (result is not null)
                results.Add(result);
        }

        _logger.LogInformation("Retrieved {Count} evaluation results", results.Count);
        return results;
    }

    public async Task<List<EvaluationResult>> GetBySeriesAsync(OccupationalSeries series)
    {
        _logger.LogInformation("Fetching evaluation results for series {Series}", series);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT pd_nbr,
                   series AS occ_series,
                   TO_NUMBER(grade) AS grade,
                   CAST(0 AS NUMBER) AS overall_score,
                   rating,
                   is_candidate,
                   justification_summary,
                   result_json,
                   NVL(CAST(scored_at AS DATE), SYSDATE) AS evaluated_date
            FROM schedule_pc_eval
            WHERE series = :series
            ORDER BY pd_nbr";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":series", series.ToString());

        var results = new List<EvaluationResult>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var result = MapEvaluationResult(reader);
            if (result is not null)
                results.Add(result);
        }

        _logger.LogInformation("Retrieved {Count} evaluation results for series {Series}", results.Count, series);
        return results;
    }

    public async Task<List<EvaluationResult>> GetByStatusAsync(EvaluationStatus status)
    {
        _logger.LogInformation("Fetching evaluation results with status {Status}", status);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT pd_nbr,
                   series AS occ_series,
                   TO_NUMBER(grade) AS grade,
                   CAST(0 AS NUMBER) AS overall_score,
                   rating,
                   is_candidate,
                   justification_summary,
                   result_json,
                   NVL(CAST(scored_at AS DATE), SYSDATE) AS evaluated_date
            FROM schedule_pc_eval
            WHERE status = :status
            ORDER BY scored_at DESC";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":status", MapStatus(status));

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

    public async Task<int> GetCountByStatusAsync(EvaluationStatus status)
    {
        _logger.LogDebug("Counting evaluation results with status {Status}", status);

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT COUNT(*) FROM schedule_pc_eval
            WHERE status = :status";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        cmd.Parameters.Add(":status", MapStatus(status));

        var result = await cmd.ExecuteScalarAsync();
        var count = result != null ? (int)(decimal)result : 0;
        _logger.LogDebug("Count result: {Count} evaluations with status {Status}", count, status);
        return count;
    }

    public async Task<int> ResetFailedAsync()
    {
        _logger.LogInformation("Resetting failed evaluation rows back to PENDING for retry");

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE schedule_pc_eval
            SET rating = 'PENDING',
                status = 'Staged',
                scored_at = NULL,
                justification_summary = NULL,
                result_json = NULL,
                error_msg = NULL
            WHERE rating = 'FAILED'";

        using var cmd = new OracleCommand(sql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };

        var affected = await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Reset {Count} failed evaluation rows to PENDING", affected);
        return affected;
    }

    private static string MapStatus(EvaluationStatus status)
    {
        return status switch
        {
            EvaluationStatus.Staged => "PENDING",
            EvaluationStatus.InProgress => "IN_PROGRESS",
            EvaluationStatus.Complete => "COMPLETE",
            EvaluationStatus.Failed => "FAILED",
            _ => "PENDING"
        };
    }

    private static string MapStatusFromResult(EvaluationResult result)
    {
        if (result.Rating.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
            return "FAILED";

        if (result.Rating.Equals("PENDING", StringComparison.OrdinalIgnoreCase))
            return "PENDING";

        return "COMPLETE";
    }

    private EvaluationResult? MapEvaluationResult(OracleDataReader reader)
    {
        // col 0=pd_nbr, 1=occ_series, 2=grade, 3=overall_score, 4=rating,
        // col 5=is_candidate, 6=justification_summary, 7=result_json, 8=evaluated_date

        // Preferred path: deserialize authoritative JSON payload when present.
        if (!reader.IsDBNull(7))
        {
            var resultJson = reader.GetValue(7).ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(resultJson))
            {
                var parsed = JsonSerializer.Deserialize<EvaluationResult>(resultJson);
                if (parsed is not null)
                    return parsed;
            }
        }

        // Fallback path: hydrate from relational columns for older/partially populated rows.
        var pdNbr = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var seriesCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);

        if (string.IsNullOrWhiteSpace(seriesCode) || seriesCode.Length != 5)
        {
            _logger.LogWarning(
                "Skipping malformed evaluation row for PD {PdNbr}: invalid series '{SeriesCode}'",
                pdNbr,
                seriesCode);
            return null;
        }

        var rawGrade = reader.IsDBNull(2) ? 1 : Convert.ToInt32(reader.GetValue(2));
        var grade = Math.Clamp(rawGrade, 1, 15);

        return new EvaluationResult
        {
            PdNbr = pdNbr,
            Series = new OccupationalSeries(seriesCode),
            Grade = new Grade(grade),
            OverallScore = reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3)),
            Rating = reader.IsDBNull(4) ? "PENDING" : reader.GetValue(4).ToString() ?? "PENDING",
            IsCandidate = !reader.IsDBNull(5) && string.Equals(reader.GetValue(5).ToString(), "Y", StringComparison.OrdinalIgnoreCase),
            JustificationSummary = reader.IsDBNull(6) ? string.Empty : reader.GetValue(6).ToString() ?? string.Empty,
            RawLlmResponse = string.Empty,
            EvaluatedDate = reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8),
            EvaluatedBy = "SYSTEM"
        };
    }
}
