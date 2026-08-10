using System.IO.Compression;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchedulePCMcp.Domain.Entities;
using SchedulePCMcp.Infrastructure.Config;

namespace SchedulePCMcp.Infrastructure.DocumentGeneration;

/// <summary>
/// OpenXml-based Word document generation using raw ZIP + XmlDocument.
/// Fills SDT content controls by ordinal position (document order) to match
/// the v2 template SDT map defined in Fill-EvalTemplate-Batch-v2.ps1.
/// </summary>
public class OpenXmlDocumentStrategy : IDocumentGenerationStrategy
{
    private const string WNs   = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string W14Ns = "http://schemas.microsoft.com/office/word/2010/wordml";

    private readonly ILogger<OpenXmlDocumentStrategy> _logger;
    private readonly string _templatePath;

    public OpenXmlDocumentStrategy(
        ILogger<OpenXmlDocumentStrategy> logger,
        DocumentGenerationSettings settings)
    {
        _logger = logger;
        _templatePath = settings.TemplateFile;
    }

    public async Task GenerateAsync(
        EvaluationResult result, PositionDescription pd, string outputPath)
    {
        if (!File.Exists(_templatePath))
            throw new FileNotFoundException($"Template not found: {_templatePath}");

        _logger.LogInformation(
            "Generating Word document for PD {PdNbr} using template {TemplatePath}",
            result.PdNbr, _templatePath);

        var outputDir = Path.GetDirectoryName(outputPath);
        if (outputDir != null) Directory.CreateDirectory(outputDir);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            ZipFile.ExtractToDirectory(_templatePath, tempDir);

            var docXmlPath = Path.Combine(tempDir, "word", "document.xml");
            var xmlDoc = new XmlDocument { PreserveWhitespace = true };
            xmlDoc.Load(docXmlPath);

            var nsm = new XmlNamespaceManager(xmlDoc.NameTable);
            nsm.AddNamespace("w",   WNs);
            nsm.AddNamespace("w14", W14Ns);

            var allSdts = xmlDoc.SelectNodes("//w:sdt", nsm)!
                                .Cast<XmlNode>()
                                .ToArray();

            FillDocument(xmlDoc, nsm, allSdts, result, pd);

            xmlDoc.Save(docXmlPath);

            if (File.Exists(outputPath)) File.Delete(outputPath);
            ZipFile.CreateFromDirectory(tempDir, outputPath);

            _logger.LogInformation("Successfully generated Word document: {OutputPath}", outputPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }

        await Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // SDT ordinal map (v2 template, document order, 0-based):
    //  [0]  PD Number          [1]  Effective Date      [2]  Position Title
    //  [3]  Rating (Section 1) [4]  Agency/OrgCode      [5]  Pay Plan/Series/Grade
    //  [6]  Service Category   [7]  Position Purpose
    //  [8]  CB Policy-Determining Yes   [9]  No   [10] Evidence
    //  [11] CB Policy-Making Yes        [12] No   [13] Evidence
    //  [14] CB Policy-Advocating Yes    [15] No   [16] Evidence
    //  [17] CB Confidential Yes         [18] No   [19] Evidence
    //  [20-21] reserved
    //  [22] CB Final — Convert          [23] CB Final — Retain
    //  [24] Rating (Section 3)  [25] Justification  [26] Evaluator  [27] Agency Head
    //  [28-39] Appendix A       [40-41] Appendix B template row
    // -------------------------------------------------------------------------

    private void FillDocument(
        XmlDocument doc, XmlNamespaceManager nsm,
        XmlNode[] allSdts, EvaluationResult result, PositionDescription pd)
    {
        var (ratingBg, ratingFg) = GetRatingColors(result.Rating);
        var triggeredCount = result.CriteriaScores.Count(c => c.Triggered);
        var ratingLabel    = $"{result.Rating} -- {triggeredCount} of 4 criteria met";
        var evalDate       = result.EvaluatedDate.ToString("yyyy-MM-dd");

        // Section 1
        SetSdtText(allSdts, nsm, 0, result.PdNbr);
        SetSdtText(allSdts, nsm, 1, evalDate);
        SetSdtText(allSdts, nsm, 2, pd.Title);
        SetRatingCell(doc, nsm, allSdts, 3, ratingLabel, ratingBg, ratingFg);
        SetSdtText(allSdts, nsm, 4, pd.OrganizationCode);
        SetSdtText(allSdts, nsm, 5, $"{pd.PayPlan}-{pd.Series}-{pd.Grade}");
        SetSdtText(allSdts, nsm, 6, "Competitive");
        SetSdtText(allSdts, nsm, 7, pd.IntroText);

        // Section 2: 4 criteria in fixed order
        var criteriaOrder = new[] {
            "Policy-Determining", "Policy-Making", "Policy-Advocating", "Confidential"
        };

        for (int i = 0; i < 4; i++)
        {
            var criterion = result.CriteriaScores.FirstOrDefault(c =>
                string.Equals(c.CriterionName, criteriaOrder[i], StringComparison.OrdinalIgnoreCase));

            var triggered = criterion?.Triggered ?? false;
            var evidence  = criterion?.Evidence  ?? "";

            SetCheckbox(allSdts, nsm, i * 3 + 8,   triggered);
            SetCheckbox(allSdts, nsm, i * 3 + 9,  !triggered);
            SetSdtText (allSdts, nsm, i * 3 + 10,  evidence);
        }

        // SDTs 20, 21 reserved — skip

        // Section 3
        SetCheckbox  (allSdts, nsm, 22,  result.IsCandidate);
        SetCheckbox  (allSdts, nsm, 23, !result.IsCandidate);
        SetRatingCell(doc, nsm, allSdts, 24, ratingLabel, ratingBg, ratingFg);
        SetSdtText   (allSdts, nsm, 25, result.JustificationSummary);
        SetSdtText   (allSdts, nsm, 26, "AI Agent");
        SetSdtText   (allSdts, nsm, 27, "(Pending Human Review)");

        // Appendix A
        SetSdtText(allSdts, nsm, 28, result.PdNbr);
        SetSdtText(allSdts, nsm, 29, pd.Title);
        SetSdtText(allSdts, nsm, 30, pd.OrganizationCode);
        SetSdtText(allSdts, nsm, 31, pd.OrganizationCode);
        SetSdtText(allSdts, nsm, 32, "GS");
        SetSdtText(allSdts, nsm, 33, pd.Series.ToString());
        SetSdtText(allSdts, nsm, 34, pd.Grade.ToString());
        SetSdtText(allSdts, nsm, 35, evalDate);
        SetSdtText(allSdts, nsm, 36, "(not provided)");
        SetSdtText(allSdts, nsm, 37, "(not provided)");
        SetSdtText(allSdts, nsm, 38, "(not provided)");
        SetSdtText(allSdts, nsm, 39, "Competitive");

        // Appendix B
        FillAppendixB(doc, nsm, allSdts, pd, result);
    }

    private static void SetSdtText(
        XmlNode[] allSdts, XmlNamespaceManager nsm, int index, string value)
    {
        if (index >= allSdts.Length) return;
        var sdt = allSdts[index];

        var plcHdr = sdt.SelectSingleNode("w:sdtPr/w:showingPlcHdr", nsm);
        plcHdr?.ParentNode?.RemoveChild(plcHdr);

        var tNode = sdt.SelectSingleNode("w:sdtContent//w:t", nsm);
        if (tNode == null) return;

        tNode.InnerText = value;

        var rPr = sdt.SelectSingleNode("w:sdtContent//w:rPr", nsm);
        if (rPr != null)
        {
            rPr.SelectSingleNode("w:i",     nsm)?.ParentNode?.RemoveChild(rPr.SelectSingleNode("w:i",     nsm)!);
            rPr.SelectSingleNode("w:color", nsm)?.ParentNode?.RemoveChild(rPr.SelectSingleNode("w:color", nsm)!);
        }
    }

    private static void SetCheckbox(
        XmlNode[] allSdts, XmlNamespaceManager nsm, int index, bool isChecked)
    {
        if (index >= allSdts.Length) return;
        var sdt = allSdts[index];

        var checkedNode = sdt.SelectSingleNode("w:sdtPr/w14:checkbox/w14:checked", nsm);
        if (checkedNode?.Attributes != null)
        {
            var valAttr = checkedNode.Attributes["val", W14Ns]
                          ?? checkedNode.Attributes["val"];
            if (valAttr != null)
                valAttr.Value = isChecked ? "1" : "0";
        }

        var tNode = sdt.SelectSingleNode("w:sdtContent//w:t", nsm);
        if (tNode != null)
            tNode.InnerText = isChecked ? "\u2612" : "\u2610";
    }

    private static void SetRatingCell(
        XmlDocument doc, XmlNamespaceManager nsm,
        XmlNode[] allSdts, int index,
        string label, string bgColor, string fgColor)
    {
        if (index >= allSdts.Length) return;
        var sdt = allSdts[index];

        // Walk up to parent <w:tc> to set shading fill
        var tc = sdt.ParentNode;
        while (tc != null && tc.LocalName != "tc")
            tc = tc.ParentNode;

        if (tc != null)
        {
            var shd = tc.SelectSingleNode("w:tcPr/w:shd", nsm);
            if (shd?.Attributes != null)
            {
                var fillAttr = shd.Attributes["fill", WNs] ?? shd.Attributes["fill"];
                if (fillAttr != null) fillAttr.Value = bgColor;
            }
        }

        // Set foreground color on the run
        var run = sdt.SelectSingleNode("w:sdtContent//w:r", nsm);
        if (run?.LocalName == "r")
        {
            var rPr = run.SelectSingleNode("w:rPr", nsm);
            if (rPr == null)
            {
                rPr = doc.CreateElement("w", "rPr", WNs);
                run.PrependChild(rPr);
            }

            var colorNode = rPr.SelectSingleNode("w:color", nsm);
            if (colorNode == null)
            {
                colorNode = doc.CreateElement("w", "color", WNs);
                rPr.AppendChild(colorNode);
            }

            var valAttr = colorNode.Attributes?["val", WNs];
            if (valAttr == null)
            {
                valAttr = doc.CreateAttribute("w", "val", WNs);
                colorNode.Attributes!.Append(valAttr);
            }
            valAttr.Value = fgColor;
        }

        var plcHdr = sdt.SelectSingleNode("w:sdtPr/w:showingPlcHdr", nsm);
        plcHdr?.ParentNode?.RemoveChild(plcHdr);

        var tNode = sdt.SelectSingleNode("w:sdtContent//w:t", nsm);
        if (tNode != null) tNode.InnerText = label;
    }

    private void FillAppendixB(
        XmlDocument doc, XmlNamespaceManager nsm,
        XmlNode[] allSdts, PositionDescription pd, EvaluationResult result)
    {
        if (allSdts.Length <= 41) return;

        var dutyTemplateSdt     = allSdts[40];
        var evidenceTemplateSdt = allSdts[41];

        var templateRow = dutyTemplateSdt?.ParentNode;
        while (templateRow != null && templateRow.LocalName != "tr")
            templateRow = templateRow.ParentNode;

        if (templateRow == null || evidenceTemplateSdt == null) return;

        if (pd.Duties.Count == 0)
        {
            SetSdtNodeText(nsm, dutyTemplateSdt!, "No duty rows were provided for this evaluation.");
            SetSdtNodeText(nsm, evidenceTemplateSdt, "(no supporting duty metadata available)");
            return;
        }

        var triggeredCriteriaNames = result.CriteriaScores
            .Where(c => c.Triggered)
            .Select(c => c.CriterionName)
            .ToList();

        foreach (var duty in pd.Duties)
        {
            var dutyText  = string.IsNullOrWhiteSpace(duty.Text) ? "(duty text not provided)" : duty.Text;
            var dutyLabel = $"Duty #{duty.SequenceNumber}: {dutyText}";
            var metaText  = triggeredCriteriaNames.Count > 0
                ? $"Supports: {string.Join(", ", triggeredCriteriaNames)}"
                : "No direct support finding.";

            var newRow  = templateRow.CloneNode(deep: true);
            var rowSdts = newRow.SelectNodes(".//w:sdt", nsm)!
                                .Cast<XmlNode>()
                                .ToArray();

            if (rowSdts.Length > 0) SetSdtNodeText(nsm, rowSdts[0], dutyLabel);
            if (rowSdts.Length > 1) SetSdtNodeText(nsm, rowSdts[1], metaText);

            templateRow.ParentNode!.InsertBefore(newRow, templateRow);
        }

        templateRow.ParentNode!.RemoveChild(templateRow);
    }

    private static void SetSdtNodeText(XmlNamespaceManager nsm, XmlNode sdt, string value)
    {
        var plcHdr = sdt.SelectSingleNode("w:sdtPr/w:showingPlcHdr", nsm);
        plcHdr?.ParentNode?.RemoveChild(plcHdr);

        var tNode = sdt.SelectSingleNode("w:sdtContent//w:t", nsm);
        if (tNode == null) return;

        tNode.InnerText = value;

        var rPr = sdt.SelectSingleNode("w:sdtContent//w:rPr", nsm);
        if (rPr != null)
        {
            rPr.SelectSingleNode("w:i",     nsm)?.ParentNode?.RemoveChild(rPr.SelectSingleNode("w:i",     nsm)!);
            rPr.SelectSingleNode("w:color", nsm)?.ParentNode?.RemoveChild(rPr.SelectSingleNode("w:color", nsm)!);
        }
    }

    private static (string bg, string fg) GetRatingColors(string rating) =>
        rating.ToUpperInvariant() switch
        {
            "HIGH"       => ("E2F0D9", "1E4620"),
            "MEDIUM"     => ("FFF2CC", "5C4300"),
            "BORDERLINE" => ("FCE8B2", "7B4F00"),
            _            => ("FCE4D6", "801414")
        };
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
            _ => throw new InvalidOperationException($"Unknown document generation strategy: {_strategy}")
        };
    }
}
