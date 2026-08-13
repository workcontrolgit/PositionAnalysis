using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PositionAnalysis.Mcp.Domain.Entities;
using PositionAnalysis.Mcp.Infrastructure.Config;

namespace PositionAnalysis.Mcp.Infrastructure.DocumentGeneration;

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
    //  [3]  Rating (Section 1) [4]  Org Code            [5]  Pay Plan/Series/Grade
    //  [6]  Service Category   [7]  Position Purpose
    //  [8]  CB Policy-Determining Yes   [9]  No   [10] Evidence
    //  [11] CB Policy-Making Yes        [12] No   [13] Evidence
    //  [14] CB Policy-Advocating Yes    [15] No   [16] Evidence
    //  [17] CB Confidential Yes         [18] No   [19] Evidence
    //  [20-21] reserved
    //  [22] CB Final ΓÇö Convert          [23] CB Final ΓÇö Retain
    //  [24] Rating (Section 3)  [25] Justification  [26] Evaluator  [27] Agency Head
    //  [28-31] Appendix A: PD Nbr, Title, Org Name, Org Code
    //  [32-39] Appendix A cont.  [40-41] Appendix B template row
    // -------------------------------------------------------------------------

    private void FillDocument(
        XmlDocument doc, XmlNamespaceManager nsm,
        XmlNode[] allSdts, EvaluationResult result, PositionDescription pd)
    {
        var (ratingBg, ratingFg) = GetRatingColors(result.Rating);
        var triggeredCount = result.CriteriaScores.Count(c => c.Triggered);
        var ratingLabel    = $"{result.Rating} -- {triggeredCount} of 4 criteria met";
        var evalDate       = result.EvaluatedDate.ToString("yyyy-MM-dd");

        var bureauOrgDisplay = FormatBureauOrgDisplay(pd);

        // Section 1
        SetSdtText(allSdts, nsm, 0, result.PdNbr);
        SetSdtText(allSdts, nsm, 1, evalDate);
        SetSdtText(allSdts, nsm, 2, pd.Title);
        SetRatingCell(doc, nsm, allSdts, 3, ratingLabel, ratingBg, ratingFg);
        SetSdtText(allSdts, nsm, 4, bureauOrgDisplay);
        SetSdtText(allSdts, nsm, 5, $"{pd.PayPlan}-{pd.Series}-{pd.Grade}");
        SetSdtText(allSdts, nsm, 6, "Competitive");
        // Fall back to the raw PD intro for results scored before positionPurpose was captured.
        SetSdtText(allSdts, nsm, 7, string.IsNullOrWhiteSpace(result.PositionPurpose) ? pd.IntroText : result.PositionPurpose);


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

        // SDTs 20, 21 reserved ΓÇö skip

        // Section 3
        SetCheckbox  (allSdts, nsm, 22,  result.IsCandidate);
        SetCheckbox  (allSdts, nsm, 23, !result.IsCandidate);
        SetRatingCell(doc, nsm, allSdts, 24, ratingLabel, ratingBg, ratingFg);
        SetSdtText   (allSdts, nsm, 25, result.JustificationSummary);
        // SDT 26 (Evaluator Name & Date) is left as the template placeholder for manual entry.
        SetSdtText   (allSdts, nsm, 27, "(Pending Human Review)");

        // Appendix A
        SetSdtText(allSdts, nsm, 28, result.PdNbr);
        SetSdtText(allSdts, nsm, 29, pd.Title);
        SetSdtText(allSdts, nsm, 30, FormatCodeParenDescription(pd.BureauCode, pd.BureauName));
        SetSdtText(allSdts, nsm, 31, FormatCodeParenDescription(pd.OrganizationCode, pd.OrganizationName));
        SetSdtText(allSdts, nsm, 32, pd.PayPlan);
        SetSdtText(allSdts, nsm, 33, pd.Series.ToString());
        SetSdtText(allSdts, nsm, 34, pd.Grade.ToString());
        SetSdtText(allSdts, nsm, 35, FormatEffectiveDate(pd.EffectiveDate));
        SetSdtText(allSdts, nsm, 36, FormatCodeWithLabel(pd.ManagerLevel, GetManagerLevelLabel(pd.ManagerLevel)));
        SetSdtText(allSdts, nsm, 37, FormatCodeWithLabel(pd.PositionSensitivity, GetSensitivityLabel(pd.PositionSensitivity)));
        SetSdtText(allSdts, nsm, 38, FormatCodeWithLabel(pd.PublicTrust, GetPublicTrustLabel(pd.PublicTrust)));
        SetSdtText(allSdts, nsm, 39, "Competitive");

        // Appendix B
        FillAppendixB(doc, nsm, allSdts, pd, result);
    }

    private static string FormatBureauOrgDisplay(PositionDescription pd)
    {
        // Section 1 "Bureau/Org Code" shows "(code) description / (code) description" e.g. "(130000) EAP / (130200) EAP/EX".
        var bureau = FormatCodeParenDescription(pd.BureauCode, pd.BureauName);
        var org = FormatCodeParenDescription(pd.OrganizationCode, pd.OrganizationName);

        if (string.IsNullOrWhiteSpace(bureau)) return org;
        if (string.IsNullOrWhiteSpace(org)) return bureau;
        return $"{bureau} / {org}";
    }

    private static string FormatCodeParenDescription(string? code, string? description)
    {
        if (string.IsNullOrWhiteSpace(code)) return description ?? string.Empty;
        return string.IsNullOrWhiteSpace(description) ? $"({code})" : $"({code}) {description}";
    }

    private static string FormatCodeWithLabel(string? code, string label)
    {
        if (string.IsNullOrWhiteSpace(code)) return "(not provided)";
        return string.IsNullOrEmpty(label) ? code : $"{code} ({label})";
    }

    private static string FormatEffectiveDate(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate)) return "(not provided)";
        return DateTime.TryParse(rawDate, out var parsed) ? parsed.ToString("yyyy-MM-dd") : rawDate.Trim();
    }

    private static string GetManagerLevelLabel(string? code) => code switch
    {
        "2" => "Supervisor or Manager",
        "4" => "Supervisor (CSRA)",
        "5" => "Management Official (CSRA)",
        "6" => "Leader",
        "7" => "Team Leader",
        "8" => "All Other Positions",
        _ => string.Empty
    };

    private static string GetSensitivityLabel(string? code) => code switch
    {
        "1" => "Non-Sensitive",
        "2" => "Non-Critical Sensitive",
        "3" => "Critical Sensitive",
        "4" => "Special Sensitive",
        _ => string.Empty
    };

    private static string GetPublicTrustLabel(string? code) => code switch
    {
        "9" => "High Risk",
        "10" => "Mod Risk",
        "11" => "Low Risk",
        "99" => "No Risk",
        _ => string.Empty
    };

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

        foreach (var duty in pd.Duties)
        {
            var dutyText  = string.IsNullOrWhiteSpace(duty.Text) ? "(duty text not provided)" : duty.Text;
            var dutyLabel = $"Duty #{duty.SequenceNumber}: {dutyText}";

            // Prefer the LLM's own duty-number attribution; fall back to evidence-text matching for
            // results scored before supportingDutyNumbers was captured.
            var supportingCriteria = result.CriteriaScores
                .Where(c => c.Triggered && (c.SupportingDutyNumbers.Count > 0
                    ? c.SupportingDutyNumbers.Contains(duty.SequenceNumber)
                    : DutySupportsCriterion(dutyText, c.Evidence)))
                .Select(c => c.CriterionName)
                .ToList();
            var metaText  = supportingCriteria.Count > 0
                ? $"Supports: {string.Join(", ", supportingCriteria)}"
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

    private static bool DutySupportsCriterion(string dutyText, string evidence)
    {
        if (string.IsNullOrWhiteSpace(dutyText) || string.IsNullOrWhiteSpace(evidence)) return false;

        var normalizedDuty     = NormalizeForMatch(dutyText);
        var normalizedEvidence = NormalizeForMatch(evidence);
        if (normalizedEvidence.Length == 0) return false;

        if (normalizedDuty.Contains(normalizedEvidence, StringComparison.OrdinalIgnoreCase))
            return true;

        // Evidence quotes may be lightly paraphrased; require most significant words to appear in the duty text.
        var evidenceWords = normalizedEvidence.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (evidenceWords.Length == 0) return false;

        var matchCount = evidenceWords.Count(w => normalizedDuty.Contains(w, StringComparison.OrdinalIgnoreCase));
        return matchCount / (double)evidenceWords.Length >= 0.6;
    }

    private static string NormalizeForMatch(string text) => Regex.Replace(text, @"[^\w\s]", "").Trim();

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
