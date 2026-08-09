using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Infrastructure.Config;

namespace SchedulePCMcp.Infrastructure.DocumentGeneration;

/// <summary>
/// OpenXml-based document generation for Word templates
/// Fills bookmarks and content controls with evaluation data
/// </summary>
public class OpenXmlDocumentStrategy : IDocumentGenerationStrategy
{
    private readonly ILogger<OpenXmlDocumentStrategy> _logger;
    private readonly string _defaultTemplatePath;

    public OpenXmlDocumentStrategy(ILogger<OpenXmlDocumentStrategy> logger, DocumentGenerationSettings settings)
    {
        _logger = logger;
        _defaultTemplatePath = settings.TemplateFile;
    }

    public async Task GenerateAsync(EvaluationResult result, string templatePath, string outputPath)
    {
        // Use provided templatePath if not empty, otherwise use configured default
        var effectiveTemplatePath = string.IsNullOrWhiteSpace(templatePath) ? _defaultTemplatePath : templatePath;
        
        if (!File.Exists(effectiveTemplatePath))
            throw new FileNotFoundException($"Template not found: {effectiveTemplatePath}");

        _logger.LogInformation("Generating Word document for PD {PdNbr} using template {TemplatePath}", 
            result.PdNbr, effectiveTemplatePath);

        try
        {
            // Create output directory if it doesn't exist
            var outputDir = Path.GetDirectoryName(outputPath);
            if (outputDir != null)
                Directory.CreateDirectory(outputDir);

            // Copy template to output location
            File.Copy(effectiveTemplatePath, outputPath, overwrite: true);

            // Open the Word document
            using var wordDoc = WordprocessingDocument.Open(outputPath, isEditable: true);
            var mainPart = wordDoc.MainDocumentPart;
            
            if (mainPart == null)
                throw new InvalidOperationException("Unable to access main document part");

            // Fill document with evaluation data
            FillDocumentWithEvaluationData(mainPart, result);

            _logger.LogInformation("Successfully generated Word document: {OutputPath}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Word document for PD {PdNbr}", result.PdNbr);
            throw;
        }

        await Task.CompletedTask;
    }

    private void FillDocumentWithEvaluationData(MainDocumentPart mainPart, EvaluationResult result)
    {
        var document = mainPart.Document;
        
        if (document.Body == null)
            throw new InvalidOperationException("Document body not found");

        // Define the data to fill
        var data = new Dictionary<string, string>
        {
            { "PD_NBR", result.PdNbr },
            { "SERIES", result.Series.ToString() },
            { "GRADE", result.Grade.ToString() },
            { "OVERALL_SCORE", result.OverallScore.ToString("F2") },
            { "RATING", result.Rating.ToString() },
            { "IS_CANDIDATE", result.IsCandidate ? "Yes" : "No" },
            { "JUSTIFICATION", result.JustificationSummary },
            { "EVALUATED_DATE", result.EvaluatedDate.ToString("yyyy-MM-dd") },
            { "EVALUATED_BY", result.EvaluatedBy ?? "SYSTEM" }
        };

        // Add criterion scores to data
        for (int i = 0; i < result.CriteriaScores.Count; i++)
        {
            var criterion = result.CriteriaScores[i];
            data[$"CRITERION_{i + 1}_NAME"] = criterion.CriterionName;
            data[$"CRITERION_{i + 1}_SCORE"] = criterion.Score.ToString("F2");
            data[$"CRITERION_{i + 1}_JUSTIFICATION"] = criterion.Justification;
        }

        // Replace bookmarks
        ReplaceBookmarks(document, data);

        // Replace content control tags
        ReplaceContentControls(document, data);

        document.Save();
    }

    private void ReplaceBookmarks(Document document, Dictionary<string, string> data)
    {
        var bookmarks = document.Descendants<BookmarkStart>().ToList();

        foreach (var bookmark in bookmarks)
        {
            var bookmarkName = bookmark.Name?.Value ?? "";
            if (data.TryGetValue(bookmarkName, out var value))
            {
                // Find the bookmark end
                var bookmarkEnd = bookmark.Parent?.Descendants<BookmarkEnd>()
                    .FirstOrDefault(be => be.Id == bookmark.Id);

                if (bookmarkEnd != null)
                {
                    // Clear existing content between start and end
                    var nodesToRemove = new List<OpenXmlElement>();
                    var current = bookmark.NextSibling();

                    while (current != null && current != bookmarkEnd)
                    {
                        nodesToRemove.Add(current);
                        current = current.NextSibling();
                    }

                    foreach (var node in nodesToRemove)
                        node.Remove();

                    // Insert new text run with value
                    var textRun = new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve });
                    bookmark.Parent?.InsertAfter(textRun, bookmark);

                    _logger.LogDebug("Replaced bookmark {BookmarkName} with value", bookmarkName);
                }
            }
        }
    }

    private void ReplaceContentControls(Document document, Dictionary<string, string> data)
    {
        var controls = document.Descendants<SdtBlock>().ToList();

        foreach (var control in controls)
        {
            var properties = control.SdtProperties;
            if (properties == null)
                continue;

            // Try to find tag in the properties - iterate through all child elements
            var tag = "";
            foreach (var child in properties.ChildElements)
            {
                if (child is Tag tagElement && tagElement.Val != null)
                {
                    tag = tagElement.Val.Value;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(tag) && data.TryGetValue(tag, out var value))
            {
                // Clear and replace content in all paragraphs within the control
                var paragraphs = control.Descendants<Paragraph>().ToList();
                if (paragraphs.Any())
                {
                    // Keep the first paragraph and clear its content, remove others
                    var firstPara = paragraphs.First();
                    firstPara.RemoveAllChildren<Run>();
                    firstPara.Append(new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));

                    for (int i = 1; i < paragraphs.Count; i++)
                        paragraphs[i].Remove();

                    _logger.LogDebug("Filled content control {Tag} with value", tag);
                }
            }
        }
    }
}

/// <summary>
/// Factory for document generation strategy selection
/// </summary>
public class DocumentGenerationStrategyFactory : IDocumentGenerationStrategyFactory
{
    private readonly string _strategy;
    private readonly DocumentGenerationSettings _settings;
    private readonly ILogger<DocumentGenerationStrategyFactory> _logger;
    private readonly IServiceProvider _serviceProvider;

    public DocumentGenerationStrategyFactory(
        Microsoft.Extensions.Options.IOptions<DocumentGenerationSettings> options,
        ILogger<DocumentGenerationStrategyFactory> logger,
        IServiceProvider serviceProvider)
    {
        _settings = options.Value;
        _strategy = options.Value.Strategy.ToLowerInvariant();
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public IDocumentGenerationStrategy CreateStrategy()
    {
        _logger.LogInformation("Creating document generation strategy: {Strategy}", _strategy);

        return _strategy switch
        {
            "openxml" => new OpenXmlDocumentStrategy(
                _serviceProvider.GetRequiredService<ILogger<OpenXmlDocumentStrategy>>(),
                _settings),
            // "powershell" => new PowerShellDocumentStrategy(...), // Future implementation
            _ => throw new InvalidOperationException($"Unknown document generation strategy: {_strategy}")
        };
    }
}
