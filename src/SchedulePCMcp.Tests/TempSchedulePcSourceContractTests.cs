using Xunit;

namespace SchedulePCMcp.Tests;

public class TempSchedulePcSourceContractTests
{
    [Fact]
    public void DefinesTheTempSchedulePcSourceMapping()
    {
        Assert.Equal("TEMP_PD_SCHED_PC", TempSchedulePcSourceContract.HeaderTable);
        Assert.Equal("TEMP_PD_SCHED_PC_DUTIES", TempSchedulePcSourceContract.DutyTable);
        Assert.Equal("POSITION_OCCUPIED_CODE", TempSchedulePcSourceContract.ServiceCategoryColumn);
        Assert.False(TempSchedulePcSourceContract.HasCriticalDutyIndicator);
    }

    [Fact]
    public void RepositoryUsesOnlyEligibleTempSchedulePcSources()
    {
        var repositoryPath = FindRepositorySourceFile();
        var source = File.ReadAllText(repositoryPath);

        Assert.Contains("temp_pd_sched_pc", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("temp_pd_sched_pc_duties", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS (SELECT 1 FROM temp_pd_sched_pc_duties duty", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duty.pd_seq_num = header.pd_seq_num", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duty.pdd_major_duties_text IS NOT NULL", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsCritical = false", source, StringComparison.Ordinal);
        Assert.Contains("dutiesReader.IsDBNull(2) ? 0m : dutiesReader.GetDecimal(2)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MAX_PD_VW", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PD_POSITION_DATA", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PD_DUTIES", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PDD_CRITICAL_DUTY_IND", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetHumanSchedulePcPdNumbersAsync_UsesHumanFlaggedTempHeadersInPdNumberOrder()
    {
        var source = File.ReadAllText(FindRepositorySourceFile());

        Assert.Contains("GetHumanSchedulePcPdNumbersAsync", source);
        Assert.Contains("FROM temp_pd_sched_pc header", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("header.schedule_pc_ind = 'Y'", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY header.pd_nbr", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BulkStagingProcedureUsesFilteredTempHeadersAndReportsDutylessExclusions()
    {
        var procedurePath = FindBulkStagingProcedureFile();
        var source = File.ReadAllText(procedurePath);

        Assert.Contains("FROM temp_pd_sched_pc pd", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS (SELECT 1 FROM temp_pd_sched_pc_duties duty", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duty.pd_seq_num = pd.pd_seq_num", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duty.pdd_major_duties_text IS NOT NULL", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p_excluded_count OUT PLS_INTEGER", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM temp_pd_sched_pc_duties duty", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FROM max_pd_vw", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositorySourceFile()
    {
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var repositoryPath = FindRepositorySourceFile(root);
            if (repositoryPath != null)
                return repositoryPath;
        }

        throw new FileNotFoundException("Could not find OraclePositionDescriptionRepository.cs.");
    }

    private static string? FindRepositorySourceFile(string root)
    {
        var directory = new DirectoryInfo(root);

        while (directory != null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "SchedulePCMcp",
                "Infrastructure",
                "Repositories",
                "OraclePositionDescriptionRepository.cs");

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    private static string FindBulkStagingProcedureFile()
    {
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(root);

            while (directory != null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "SchedulePCMcp",
                    "Database",
                    "STAGE_SCHEDULE_PC_EVAL.sql");

                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Could not find STAGE_SCHEDULE_PC_EVAL.sql.");
    }
}

internal static class TempSchedulePcSourceContract
{
    internal const string HeaderTable = "TEMP_PD_SCHED_PC";
    internal const string DutyTable = "TEMP_PD_SCHED_PC_DUTIES";
    internal const string ServiceCategoryColumn = "POSITION_OCCUPIED_CODE";
    internal const bool HasCriticalDutyIndicator = false;
}