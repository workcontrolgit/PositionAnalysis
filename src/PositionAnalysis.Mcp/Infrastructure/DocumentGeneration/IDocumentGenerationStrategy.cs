using PositionAnalysis.Mcp.Domain.Entities;

namespace PositionAnalysis.Mcp.Infrastructure.DocumentGeneration;

/// <summary>
/// Strategy interface for generating evaluation documents
/// Supports multiple implementations: OpenXml (Word), PowerShell, etc.
/// </summary>
public interface IDocumentGenerationStrategy
{
    Task GenerateAsync(EvaluationResult result, PositionDescription pd, string outputPath);
}

/// <summary>
/// Factory for creating document generation strategy instances
/// </summary>
public interface IDocumentGenerationStrategyFactory
{
    IDocumentGenerationStrategy CreateStrategy();
}
