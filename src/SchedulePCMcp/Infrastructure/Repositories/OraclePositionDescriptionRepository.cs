using Oracle.ManagedDataAccess.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Infrastructure.Config;

namespace SchedulePCMcp.Infrastructure.Repositories;

/// <summary>
/// Oracle implementation of PositionDescriptionRepository
/// Queries MAX_PD_VW and PD_DUTIES tables from HR schema
/// </summary>
public class OraclePositionDescriptionRepository : IPositionDescriptionRepository
{
    private readonly OracleSettings _settings;
    private readonly ILogger<OraclePositionDescriptionRepository> _logger;

    public OraclePositionDescriptionRepository(
        IOptions<OracleSettings> options,
        ILogger<OraclePositionDescriptionRepository> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<List<PositionDescription>> GetAllAsync()
    {
        _logger.LogInformation("Fetching all position descriptions from MAX_PD_VW");
        
        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        var results = new List<PositionDescription>();
        var pdNbrs = await GetAllPdNbrsAsync(connection);

        foreach (var pdNbr in pdNbrs)
        {
            var pd = await GetByPdNbrInternalAsync(connection, pdNbr);
            if (pd != null)
                results.Add(pd);
        }

        _logger.LogInformation("Retrieved {Count} position descriptions", results.Count);
        return results;
    }

    public async Task<PositionDescription?> GetByPdNbrAsync(string pdNbr)
    {
        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();
        return await GetByPdNbrInternalAsync(connection, pdNbr);
    }

    public async Task<List<PositionDescription>> GetBySeriesAsync(OccupationalSeries series)
    {
        _logger.LogInformation("Fetching position descriptions for series {Series}", series);
        
        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT pd_nbr FROM max_pd_vw 
            WHERE gvt_occ_series = :series
            ORDER BY pd_nbr";

        using var cmd = new OracleCommand(sql, connection);
        cmd.CommandTimeout = _settings.CommandTimeout;
        cmd.Parameters.Add(":series", series.ToString());

        var pdNbrs = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pdNbrs.Add(reader.GetString(0));

        var results = new List<PositionDescription>();
        foreach (var pdNbr in pdNbrs)
        {
            var pd = await GetByPdNbrAsync(pdNbr);
            if (pd != null)
                results.Add(pd);
        }

        _logger.LogInformation("Retrieved {Count} position descriptions for series {Series}", results.Count, series);
        return results;
    }

    public async Task<List<PositionDescription>> GetByGradeRangeAsync(Grade minGrade, Grade maxGrade)
    {
        if (minGrade.Value > maxGrade.Value)
            throw new ArgumentException($"minGrade ({minGrade}) cannot be greater than maxGrade ({maxGrade})");

        _logger.LogInformation("Fetching position descriptions for grades {MinGrade}-{MaxGrade}", minGrade, maxGrade);
        
        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT pd_nbr FROM max_pd_vw 
            WHERE TO_NUMBER(grd_code) BETWEEN :minGrade AND :maxGrade
            ORDER BY pd_nbr";

        using var cmd = new OracleCommand(sql, connection);
        cmd.CommandTimeout = _settings.CommandTimeout;
        cmd.Parameters.Add(":minGrade", minGrade.Value);
        cmd.Parameters.Add(":maxGrade", maxGrade.Value);

        var pdNbrs = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pdNbrs.Add(reader.GetString(0));

        var results = new List<PositionDescription>();
        foreach (var pdNbr in pdNbrs)
        {
            var pd = await GetByPdNbrAsync(pdNbr);
            if (pd != null)
                results.Add(pd);
        }

        _logger.LogInformation("Retrieved {Count} position descriptions for grade range {MinGrade}-{MaxGrade}", 
            results.Count, minGrade, maxGrade);
        return results;
    }

    public async Task<List<PositionDescription>> GetByFilterAsync(StagingFilter filter)
    {
        _logger.LogInformation("Fetching position descriptions with filter: {Filter}", filter);

        if (filter.IsEmpty)
            return await GetAllAsync();

        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        var sql = new System.Text.StringBuilder(
            "SELECT pd_nbr FROM max_pd_vw WHERE 1=1");
        var cmd = new OracleCommand { Connection = connection, CommandTimeout = _settings.CommandTimeout };

        if (filter.GradeMin != null || filter.GradeMax != null)
        {
            if (filter.GradeMin != null && filter.GradeMax != null)
            {
                sql.Append(" AND TO_NUMBER(grd_code) BETWEEN :minGrade AND :maxGrade");
                cmd.Parameters.Add(":minGrade", filter.GradeMin.Value);
                cmd.Parameters.Add(":maxGrade", filter.GradeMax.Value);
            }
            else if (filter.GradeMin != null)
            {
                sql.Append(" AND TO_NUMBER(grd_code) >= :minGrade");
                cmd.Parameters.Add(":minGrade", filter.GradeMin.Value);
            }
            else if (filter.GradeMax != null)
            {
                sql.Append(" AND TO_NUMBER(grd_code) <= :maxGrade");
                cmd.Parameters.Add(":maxGrade", filter.GradeMax.Value);
            }
        }

        if (filter.Series != null)
        {
            sql.Append(" AND gvt_occ_series = :series");
            cmd.Parameters.Add(":series", filter.Series.ToString());
        }

        if (!string.IsNullOrWhiteSpace(filter.OrganizationCode))
        {
            sql.Append(" AND pd_origin_org_code = :orgCode");
            cmd.Parameters.Add(":orgCode", filter.OrganizationCode);
        }

        sql.Append(" ORDER BY pd_nbr");
        cmd.CommandText = sql.ToString();

        var pdNbrs = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pdNbrs.Add(reader.GetString(0));

        var results = new List<PositionDescription>();
        foreach (var pdNbr in pdNbrs)
        {
            var pd = await GetByPdNbrAsync(pdNbr);
            if (pd != null)
                results.Add(pd);
        }

        _logger.LogInformation("Retrieved {Count} position descriptions matching filter", results.Count);
        return results;
    }

    private async Task<List<string>> GetAllPdNbrsAsync(OracleConnection connection)
    {
        const string sql = "SELECT DISTINCT pd_nbr FROM max_pd_vw ORDER BY pd_nbr";
        using var cmd = new OracleCommand(sql, connection) { CommandTimeout = _settings.CommandTimeout };

        var pdNbrs = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pdNbrs.Add(reader.GetString(0));

        return pdNbrs;
    }

    private async Task<PositionDescription?> GetByPdNbrInternalAsync(OracleConnection connection, string pdNbr)
    {
        const string pdSql = @"
            SELECT pd_seq_num, pd_nbr, pd_position_title_text, gvt_occ_series, grd_code, pd_origin_org_code, pd_org_title_text, pd_intro
            FROM max_pd_vw 
            WHERE pd_nbr = :pdNbr";

        using var pdCmd = new OracleCommand(pdSql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        pdCmd.Parameters.Add(":pdNbr", pdNbr);

        using var pdReader = await pdCmd.ExecuteReaderAsync();
        if (!await pdReader.ReadAsync())
            return null;

        var pdSeqNum = pdReader.GetInt32(0);
        var series = new OccupationalSeries(pdReader.GetString(3));

        var gradeText = pdReader.IsDBNull(4) ? "0" : pdReader.GetString(4);
        if (!int.TryParse(gradeText, out var gradeValue))
            gradeValue = 1;
        var grade = new Grade(gradeValue);

        var introText = pdReader.IsDBNull(7)
            ? string.Empty
            : pdReader.GetValue(7).ToString() ?? string.Empty;

        var pd = new PositionDescription
        {
            PdNbr = pdReader.GetString(1),
            Title = pdReader.IsDBNull(2) ? string.Empty : pdReader.GetString(2),
            Series = series,
            Grade = grade,
            OrganizationCode = pdReader.IsDBNull(5) ? string.Empty : pdReader.GetString(5),
            OrganizationName = pdReader.IsDBNull(6) ? string.Empty : pdReader.GetString(6),
            IntroText = introText,
            Duties = new List<MajorDuty>(),
            CreatedDate = DateTime.UtcNow
        };

        // Fetch major duties
        const string dutiesSql = @"
            SELECT pdd_seq_num, pdd_major_duties_text, pdd_percent_time_spent, pdd_critical_duty_ind
            FROM pd_duties
            WHERE pd_seq_num = :pdSeqNum
            ORDER BY pdd_seq_num";

        using var dutiesCmd = new OracleCommand(dutiesSql, connection)
        {
            CommandTimeout = _settings.CommandTimeout
        };
        dutiesCmd.Parameters.Add(":pdSeqNum", pdSeqNum);

        using var dutiesReader = await dutiesCmd.ExecuteReaderAsync();
        while (await dutiesReader.ReadAsync())
        {
            var duty = new MajorDuty
            {
                SequenceNumber = dutiesReader.GetInt32(0),
                Text = dutiesReader.IsDBNull(1) ? string.Empty : dutiesReader.GetValue(1).ToString() ?? string.Empty,
                PercentTimeAllotted = dutiesReader.GetDecimal(2),
                IsCritical = dutiesReader.GetString(3) == "Y"
            };
            pd.Duties.Add(duty);
        }

        _logger.LogDebug("Retrieved position description {PdNbr} with {DutyCount} duties", pdNbr, pd.Duties.Count);
        return pd;
    }
}
