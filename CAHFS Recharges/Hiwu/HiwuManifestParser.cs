using System.Xml;
using System.Xml.Schema;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuManifestParse
    {
        public string Status { get; init; } = HiwuParseStatus.ParseError;

        public bool IsAmendment { get; init; }

        public string? ParseError { get; init; }

        public string? OriginalNamePrefix { get; init; }
    }

    public sealed class HiwuManifestParser
    {
        private const string SchemaResourceName = "CAHFS_Recharges.Hiwu.samples-v1.8.xsd";
        private readonly XmlSchemaSet _schema = LoadSchema();
        private readonly object _gate = new();

        public HiwuManifestParse Parse(string remoteFileName, string? xml)
        {
            var name = HiwuFileName.Read(remoteFileName ?? "");
            if (string.IsNullOrWhiteSpace(xml))
                return Invalid(name, "File is empty.");

            var errors = new List<string>();
            try
            {
                lock (_gate)
                {
                    var settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        ValidationType = ValidationType.Schema,
                        Schemas = _schema
                    };
                    settings.ValidationEventHandler += (_, args) =>
                    {
                        if (errors.Count < 5)
                            errors.Add(args.Message);
                    };

                    using var reader = XmlReader.Create(new StringReader(xml.TrimStart('\uFEFF')), settings);
                    while (reader.Read())
                    {
                    }
                }
            }
            catch (XmlException ex)
            {
                return Invalid(name, ex.Message);
            }

            if (errors.Count > 0)
                return Invalid(name, string.Join(" ", errors));

            return new HiwuManifestParse
            {
                Status = name.IsAmendment ? HiwuParseStatus.Amended : HiwuParseStatus.Received,
                IsAmendment = name.IsAmendment,
                OriginalNamePrefix = name.OriginalNamePrefix
            };
        }

        private static HiwuManifestParse Invalid(HiwuFileNameInfo name, string error)
        {
            return new HiwuManifestParse
            {
                Status = HiwuParseStatus.ParseError,
                IsAmendment = name.IsAmendment,
                ParseError = error,
                OriginalNamePrefix = name.OriginalNamePrefix
            };
        }

        private static XmlSchemaSet LoadSchema()
        {
            var assembly = typeof(HiwuManifestParser).Assembly;
            using var stream = assembly.GetManifestResourceStream(SchemaResourceName)
                ?? throw new InvalidOperationException("HIWU samples schema is not embedded.");

            var set = new XmlSchemaSet();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            set.Add(null, reader);
            set.Compile();
            return set;
        }
    }
}
