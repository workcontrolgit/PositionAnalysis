using SchedulePCMcp.Domain.Entities;

namespace SchedulePCMcp.Infrastructure.DocumentGeneration;

/// <summary>
/// Strategy interface for generating evaluation documents
/// Supports multiple implementations: OpenXml (Word), PowerShell, etc.
/// </summary>
public interface IDocumentGenerationStrategy
{
    Task GenerateAsync(EvaluationResult result, string templatePath, string outputPath);
}

/// <summary>
/// Factory for creating document generation strategy instances
/// </summary>
public interface IDocumentGenerationStrategyFactory
{
    IDocumentGenerationStrategy CreateStrategy();
}
