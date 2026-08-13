using Oracle.ManagedDataAccess.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Domain.ValueObjects;
using SchedulePCMcp.Infrastructure.Config;

namespace SchedulePCMcp.Infrastructure.Repositories;

/// <summary>
/// Oracle implementation of PositionDescriptionRepository
/// Queries TEMP Schedule PC header and duty tables from HR schema.
/// </summary>
public class OraclePositionDescriptionRepository : IPositionDescriptionRepository
{
    private const string EligibleDutiesExistsPredicate =
        "EXISTS (SELECT 1 FROM temp_pd_sched_pc_duties duty WHERE duty.pd_seq_num = header.pd_seq_num AND duty.pdd_major_duties_text IS NOT NULL)";

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
        _logger.LogInformation("Fetching all position descriptions from TEMP Schedule PC sources");
        
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

        var sql = $@"
                        SELECT header.pd_nbr
                        FROM temp_pd_sched_pc header
                        WHERE header.gvt_occ_series = :series
                            AND {EligibleDutiesExistsPredicate}
                        ORDER BY header.pd_nbr";

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

        var sql = $@"
                        SELECT header.pd_nbr
                        FROM temp_pd_sched_pc header
                        WHERE CASE WHEN REGEXP_LIKE(TRIM(header.grd_code), '^[[:digit:]]+$') THEN TO_NUMBER(TRIM(header.grd_code)) END BETWEEN :minGrade AND :maxGrade
                            AND {EligibleDutiesExistsPredicate}
                        ORDER BY header.pd_nbr";

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
            $"SELECT header.pd_nbr FROM temp_pd_sched_pc header WHERE {EligibleDutiesExistsPredicate}");
        var cmd = new OracleCommand { Connection = connection, CommandTimeout = _settings.CommandTimeout };

        if (filter.GradeMin != null || filter.GradeMax != null)
        {
            if (filter.GradeMin != null && filter.GradeMax != null)
            {
                sql.Append(" AND CASE WHEN REGEXP_LIKE(TRIM(header.grd_code), '^[[:digit:]]+$') THEN TO_NUMBER(TRIM(header.grd_code)) END BETWEEN :minGrade AND :maxGrade");
                cmd.Parameters.Add(":minGrade", filter.GradeMin.Value);
                cmd.Parameters.Add(":maxGrade", filter.GradeMax.Value);
            }
            else if (filter.GradeMin != null)
            {
                sql.Append(" AND CASE WHEN REGEXP_LIKE(TRIM(header.grd_code), '^[[:digit:]]+$') THEN TO_NUMBER(TRIM(header.grd_code)) END >= :minGrade");
                cmd.Parameters.Add(":minGrade", filter.GradeMin.Value);
            }
            else if (filter.GradeMax != null)
            {
                sql.Append(" AND CASE WHEN REGEXP_LIKE(TRIM(header.grd_code), '^[[:digit:]]+$') THEN TO_NUMBER(TRIM(header.grd_code)) END <= :maxGrade");
                cmd.Parameters.Add(":maxGrade", filter.GradeMax.Value);
            }
        }

        if (filter.Series != null)
        {
            sql.Append(" AND header.gvt_occ_series = :series");
            cmd.Parameters.Add(":series", filter.Series.ToString());
        }

        if (!string.IsNullOrWhiteSpace(filter.OrganizationCode))
        {
            sql.Append(" AND header.pd_origin_org_code = :orgCode");
            cmd.Parameters.Add(":orgCode", filter.OrganizationCode);
        }

        sql.Append(" ORDER BY header.pd_nbr");
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

    public async Task<List<string>> GetHumanSchedulePcPdNumbersAsync()
    {
        using var connection = new OracleConnection(_settings.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT header.pd_nbr
            FROM temp_pd_sched_pc header
            WHERE header.schedule_pc_ind = 'Y'
            ORDER BY header.pd_nbr";

        using var cmd = new OracleCommand(sql, connection) { CommandTimeout = _settings.CommandTimeout };

        var pdNumbers = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pdNumbers.Add(reader.GetString(0));

        return pdNumbers;
    }

    private async Task<List<string>> GetAllPdNbrsAsync(OracleConnection connection)
    {
        var sql = $"SELECT DISTINCT header.pd_nbr FROM temp_pd_sched_pc header WHERE {EligibleDutiesExistsPredicate} ORDER BY header.pd_nbr";
        using var cmd = new OracleCommand(sql, connection) { CommandTimeout = _settings.CommandTimeout };

        var pdNbrs = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pdNbrs.Add(reader.GetString(0));

        return pdNbrs;
    }

    private async Task<PositionDescription?> GetByPdNbrInternalAsync(OracleConnection connection, string pdNbr)
    {
        var pdSql = $@"
             SELECT header.pd_seq_num, header.pd_nbr, header.pd_position_title_text, header.gvt_occ_series,
                 header.grd_code, header.pd_origin_org_code, header.org_desc, header.pd_intro,
                 header.gvt_pay_plan, header.pd_manager_level, header.position_sensitivity,
                 header.gm_public_trust, header.position_occupied_code, header.bureau_code, header.bureau_desc,
                 header.pd_effective_date
             FROM temp_pd_sched_pc header
             WHERE header.pd_nbr = :pdNbr
            AND {EligibleDutiesExistsPredicate}";

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

        var gradeText = pdReader.IsDBNull(4) ? "1" : pdReader.GetString(4);
        if (!int.TryParse(gradeText, out var gradeValue))
        {
            _logger.LogWarning(
                "Position description {PdNbr} has nonnumeric grade {GradeText}; falling back to grade 1",
                pdNbr,
                gradeText);
            gradeValue = 1;
        }
        var grade = new Grade(gradeValue);

        var introText = pdReader.IsDBNull(7)
            ? string.Empty
            : pdReader.GetValue(7).ToString() ?? string.Empty;

        var pd = new PositionDescription
        {
            PdSeqNum = pdSeqNum,
            PdNbr = pdReader.GetString(1),
            Title = pdReader.IsDBNull(2) ? string.Empty : pdReader.GetString(2),
            Series = series,
            Grade = grade,
            OrganizationCode = pdReader.IsDBNull(5) ? string.Empty : pdReader.GetString(5),
            OrganizationName = pdReader.IsDBNull(6) ? string.Empty : pdReader.GetString(6),
            BureauCode = pdReader.IsDBNull(13) ? string.Empty : pdReader.GetString(13),
            BureauName = pdReader.IsDBNull(14) ? string.Empty : pdReader.GetString(14),
            PayPlan = pdReader.IsDBNull(8) ? string.Empty : pdReader.GetValue(8).ToString() ?? string.Empty,
            ManagerLevel = pdReader.IsDBNull(9) ? string.Empty : pdReader.GetValue(9).ToString() ?? string.Empty,
            PositionSensitivity = pdReader.IsDBNull(10) ? string.Empty : pdReader.GetValue(10).ToString() ?? string.Empty,
            PublicTrust = pdReader.IsDBNull(11) ? string.Empty : pdReader.GetValue(11).ToString() ?? string.Empty,
            ServiceCategory = pdReader.IsDBNull(12) ? string.Empty : pdReader.GetValue(12).ToString() ?? string.Empty,
            EffectiveDate = pdReader.IsDBNull(15) ? string.Empty : pdReader.GetValue(15).ToString() ?? string.Empty,
            IntroText = introText,
            Duties = new List<MajorDuty>(),
            CreatedDate = DateTime.UtcNow
        };

        // Fetch major duties
        const string dutiesSql = @"
                        SELECT pdd_seq_num, pdd_major_duties_text, pdd_percent_time_spent
                        FROM temp_pd_sched_pc_duties
                        WHERE pd_seq_num = :pdSeqNum
                            AND pdd_major_duties_text IS NOT NULL
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
                PercentTimeAllotted = dutiesReader.IsDBNull(2) ? 0m : dutiesReader.GetDecimal(2),
                IsCritical = false
            };
            pd.Duties.Add(duty);
        }

        _logger.LogDebug("Retrieved position description {PdNbr} with {DutyCount} duties", pdNbr, pd.Duties.Count);
        return pd;
    }
}
