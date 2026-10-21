using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace CAHFS_Recharges.Hiwu
{
    public static class HiwuResultsReportBuilder
    {
        public static bool TryBuild(
            string json,
            IReadOnlyList<HiwuCoflSampleRow> samples,
            string? resultsRef,
            out string? reportXml,
            out string? error)
        {
            reportXml = null;
            error = null;
            var bySpecimen = samples
                .Where(sample => sample.SpecimenId != null && !string.IsNullOrWhiteSpace(sample.SampleKey))
                .GroupBy(sample => sample.SpecimenId!.Value)
                .ToDictionary(group => group.Key, group => group.First().SampleKey);
            if (bySpecimen.Count == 0)
            {
                error = "No imported specimen is saved for this file.";
                return false;
            }

            var buckets = bySpecimen.ToDictionary(pair => pair.Key, _ => new Bucket());
            try
            {
                using var document = JsonDocument.Parse(json);
                Walk(document.RootElement, null, buckets);
            }
            catch (JsonException)
            {
                error = "Published results were not valid JSON.";
                return false;
            }

            var notes = new List<string>();
            var results = new List<XElement>();
            foreach (var pair in bySpecimen)
            {
                var bucket = buckets[pair.Key];
                var overall = MapOverall(bucket.Overall);
                if (overall == null)
                {
                    if (!string.IsNullOrWhiteSpace(bucket.Overall))
                        notes.Add(pair.Value + " result \"" + bucket.Overall + "\" is not a HIWU overallResult.");
                    continue;
                }

                if (bucket.Received == null || bucket.Reported == null)
                {
                    notes.Add(pair.Value + " is missing a received or reported date.");
                    continue;
                }

                if (overall == "EXTENSION" && string.IsNullOrWhiteSpace(bucket.Comment))
                {
                    notes.Add(pair.Value + " is EXTENSION and needs a comment.");
                    continue;
                }

                var sample = new XElement("sampleResult",
                    new XElement("sampleId", pair.Value),
                    new XElement("receivedDate", bucket.Received.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XElement("reportedDate", bucket.Reported.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XElement("overallResult", overall));
                if (!string.IsNullOrWhiteSpace(bucket.Comment))
                    sample.Add(new XElement("optionalComment", bucket.Comment.Trim()));
                if (bucket.Tco2 != null)
                    sample.Add(new XElement("tco2Value", bucket.Tco2.Value.ToString(CultureInfo.InvariantCulture)));
                if (overall == "ADVERSE FINDING" && bucket.Findings.Count > 0)
                {
                    sample.Add(new XElement("adverseFindings", bucket.Findings.Select(finding =>
                    {
                        var node = new XElement("adverseFinding",
                            new XElement("substance", finding.Substance),
                            new XElement("concentrationLevel", finding.Concentration));
                        if (!string.IsNullOrWhiteSpace(finding.Comment))
                            node.Add(new XElement("labComments", finding.Comment));
                        return node;
                    })));
                }

                results.Add(sample);
            }

            if (results.Count == 0)
            {
                error = notes.Count == 0
                    ? "No published result matched a saved specimen."
                    : Trim(string.Join(" ", notes));
                return false;
            }

            var report = new XElement("resultsReport", new XElement("sampleResults", results));
            if (!string.IsNullOrWhiteSpace(resultsRef))
                report.AddFirst(new XElement("resultsRef", resultsRef.Trim()));
            reportXml = new XDocument(new XDeclaration("1.0", "utf-8", null), report).ToString();
            error = notes.Count == 0 ? null : Trim(string.Join(" ", notes));
            return true;
        }

        private static void Walk(JsonElement element, long? specimenId, Dictionary<long, Bucket> buckets)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    Walk(item, specimenId, buckets);
                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            var current = specimenId;
            if (IsSpecimen(element) && TryId(element, out var id) && buckets.ContainsKey(id))
                current = id;

            if (current != null && buckets.TryGetValue(current.Value, out var bucket))
                Read(element, bucket);

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    Walk(property.Value, current, buckets);
            }
        }

        private static void Read(JsonElement element, Bucket bucket)
        {
            bucket.Received ??= ReadDate(element, "received_at", "received_date", "date_received", "collection_date");
            bucket.Reported ??= ReadDate(element, "published_at", "reported_at", "reported_date", "finalised_at", "finalized_at");
            var overall = ReadString(element, "overall_result", "result", "interpretation");
            if (!string.IsNullOrWhiteSpace(overall))
                bucket.Overall = overall;
            var comment = ReadString(element, "optional_comment", "comment", "lab_comments");
            if (!string.IsNullOrWhiteSpace(comment))
                bucket.Comment = comment;
            if (IsTco2(element) && ReadDecimal(element, "value", "result_value", "numeric_result") is decimal tco2)
                bucket.Tco2 = tco2;

            var substance = ReadString(element, "substance", "analyte");
            var concentration = ReadString(element, "concentration", "concentration_level")
                ?? ReadDecimal(element, "value", "result_value")?.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(substance) && !string.IsNullOrWhiteSpace(concentration))
            {
                bucket.Findings.Add(new Finding(substance.Trim(), concentration.Trim(), ReadString(element, "lab_comments")));
            }
        }

        private static bool IsSpecimen(JsonElement element) =>
            element.TryGetProperty("specimen_type_id", out _)
            || element.TryGetProperty("specimen_type", out _);

        private static bool IsTco2(JsonElement element)
        {
            var name = ReadString(element, "test_name", "short_name", "name", "long_name") ?? "";
            return name.Contains("TCO2", StringComparison.OrdinalIgnoreCase)
                || name.Contains("carbon dioxide", StringComparison.OrdinalIgnoreCase)
                || name.Contains("E508", StringComparison.OrdinalIgnoreCase);
        }

        private static string? MapOverall(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            return text.Trim().ToUpperInvariant() switch
            {
                "NEGATIVE" => "NEGATIVE",
                "NOT CONFIRMED" => "NOT CONFIRMED",
                "ADVERSE FINDING" => "ADVERSE FINDING",
                "ATYPICAL RESULT" => "ATYPICAL RESULT",
                "NOT ANALYSED" or "NOT ANALYZED" => "NOT ANALYSED",
                "PENDING" => "PENDING",
                "EXTENSION" => "EXTENSION",
                _ => null
            };
        }

        private static DateOnly? ReadDate(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                var text = ReadString(element, name);
                if (text != null && text.Length >= 10 && DateOnly.TryParse(text[..10], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return date;
            }

            return null;
        }

        private static decimal? ReadDecimal(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (!element.TryGetProperty(name, out var property))
                    continue;
                if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
                    return number;
                if (property.ValueKind == JsonValueKind.String
                    && decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }

            return null;
        }

        private static string? ReadString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    var text = property.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
            }

            return null;
        }

        private static bool TryId(JsonElement element, out long id)
        {
            id = 0;
            if (!element.TryGetProperty("id", out var property))
                return false;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out id))
                return true;
            return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out id);
        }

        private static string Trim(string value) => value.Length <= 500 ? value : value[..500];

        private sealed class Bucket
        {
            public string? Overall { get; set; }
            public string? Comment { get; set; }
            public decimal? Tco2 { get; set; }
            public DateOnly? Received { get; set; }
            public DateOnly? Reported { get; set; }
            public List<Finding> Findings { get; } = [];
        }

        private sealed record Finding(string Substance, string Concentration, string? Comment);
    }
}
