using LockboxCashReceiptPoster.Abstractions;
using LockboxCashReceiptPoster.Models;
using LockboxCashReceiptPoster.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace LockboxCashReceiptPoster.Destinations
{
    /// GP 18 eConnect cash receipt poster (IM Cash Receipts mapping).
    /// Builds XML explicitly and loads eConnect at runtime so the project
    /// compiles on developer machines without the GP SDK installed.
    public sealed class GpEConnectCashReceiptPoster : ICashReceiptPoster
    {
        private readonly string _connectionString;
        private readonly string _checkbookId;
        private readonly CashReceiptDateSource _dateSource;
        private readonly string _eConnectDllPath;
        private readonly ILogger<GpEConnectCashReceiptPoster> _logger;

        private static readonly object LoadGate = new object();
        private static Assembly? _eConnectAssembly;
        private static Type? _eConnectMethodsType;
        private static string? _loadedFrom;

        public GpEConnectCashReceiptPoster(
            string connectionString,
            CashReceiptPosterOptions options,
            ILogger<GpEConnectCashReceiptPoster> logger)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));

            // eConnect requires Integrated Security; CAEI SSM strings often use SQL auth.
            _connectionString = ConnectionStringResolver.ToEConnectIntegratedSecurity(connectionString);
            _checkbookId = options?.CheckbookId ?? "CASH RECEIPTS";
            _dateSource = options?.DateSource ?? CashReceiptDateSource.RunDate;
            _eConnectDllPath = string.IsNullOrWhiteSpace(options?.EConnectDllPath)
                ? @"C:\Program Files (x86)\Microsoft Dynamics\eConnect 18.0\API\Microsoft.Dynamics.GP.eConnect.dll"
                : options!.EConnectDllPath!;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                var b = new System.Data.SqlClient.SqlConnectionStringBuilder(_connectionString);
                _logger.LogInformation(
                    "eConnect will use Integrated Security for Data Source={DataSource} Initial Catalog={Catalog}",
                    b.DataSource, b.InitialCatalog);
            }
            catch
            {
                // ignore parse logging failures
            }
        }

        public Task<PostResult> PostAsync(CashReceiptRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Cheap guard before reserving GP's next payment number (avoids burning DOCNUMBRs).
                EnsureCustomerExists(request.CustomerId);

                var batchId = BuildBatchId(request.DepositDate, request.BatchNumber);
                var docNumber = GetNextRmPaymentNumber();
                var docDate = ResolveDocumentDate(request);
                var xml = BuildCashReceiptXml(request, batchId, docNumber, docDate);

                _logger.LogDebug(
                    "eConnect post StagingId={StagingId} Customer={Customer} Amount={Amount} Batch={Batch} Document={Document} Date={Date}",
                    request.StagingId, request.CustomerId, request.Amount, batchId, docNumber, docDate);

                InvokeCreateTransactionEntity(_connectionString, xml);
                return Task.FromResult(PostResult.Ok(docNumber));
            }
            catch (Exception ex)
            {
                var root = ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException
                    : ex;

                _logger.LogError(root, "eConnect failed StagingId={StagingId}", request.StagingId);
                return Task.FromResult(PostResult.Fail(root.Message, root.GetType().Name));
            }
        }

        private object InvokeCreateTransactionEntity(string connectionString, string xml)
        {
            var methodsType = EnsureEConnectMethodsType();
            var instance = Activator.CreateInstance(methodsType)
                ?? throw new InvalidOperationException("Could not create eConnectMethods instance.");

            var method = methodsType.GetMethod(
                "CreateTransactionEntity",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(string) },
                null);

            if (method == null)
                throw new MissingMethodException(methodsType.FullName, "CreateTransactionEntity(string, string)");

            return method.Invoke(instance, new object[] { connectionString, xml })!;
        }

        /// Confirms CUSTNMBR exists in RM00101 before GetNextRMNumber so failed
        /// unknown-customer posts do not advance GP's payment-number sequence.
        private void EnsureCustomerExists(string customerId)
        {
            var cust = (customerId ?? "").Trim();
            if (cust.Length == 0)
                throw new InvalidOperationException("CustomerId (BillingId) is required.");

            const string sql = @"
SELECT TOP (1) 1
FROM dbo.RM00101
WHERE RTRIM(CUSTNMBR) = @cust;";

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@cust", cust);
                connection.Open();
                var found = command.ExecuteScalar();
                if (found == null || found == DBNull.Value)
                {
                    throw new InvalidOperationException(
                        "Customer Number (CUSTNMBR) does not exist in the Customer Master Table - RM00101: " + cust);
                }
            }
        }

        private string GetNextRmPaymentNumber()
        {
            var assembly = EnsureEConnectAssembly();
            var type = assembly.GetType(
                "Microsoft.Dynamics.GP.eConnect.GetNextDocNumbers",
                throwOnError: true)!;
            var method = Array.Find(
                type.GetMethods(BindingFlags.Instance | BindingFlags.Public),
                candidate =>
                {
                    var parameters = candidate.GetParameters();
                    return candidate.Name == "GetNextRMNumber"
                        && parameters.Length == 3
                        && parameters[2].ParameterType == typeof(string);
                });

            if (method == null)
                throw new MissingMethodException(type.FullName, "GetNextRMNumber");

            var parameters = method.GetParameters();
            var increment = Enum.Parse(parameters[0].ParameterType, "Increment", ignoreCase: true);
            var payments = Enum.Parse(parameters[1].ParameterType, "RMPayments", ignoreCase: true);
            var instance = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Could not create GetNextDocNumbers instance.");
            var value = method.Invoke(instance, new[] { increment, payments, _connectionString }) as string;

            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("eConnect did not return the next RM payment number.");

            var documentNumber = value!.Trim();
            _logger.LogInformation("Reserved next GP RM payment number {DocumentNumber}", documentNumber);
            return documentNumber;
        }

        private Assembly EnsureEConnectAssembly()
        {
            EnsureEConnectMethodsType();
            return _eConnectAssembly!;
        }

        private Type EnsureEConnectMethodsType()
        {
            if (_eConnectMethodsType != null)
                return _eConnectMethodsType;

            lock (LoadGate)
            {
                if (_eConnectMethodsType != null)
                    return _eConnectMethodsType;

                if (!File.Exists(_eConnectDllPath))
                {
                    throw new FileNotFoundException(
                        "eConnect DLL not found. Install eConnect 18.0 or set CashReceiptPoster:EConnectDllPath.",
                        _eConnectDllPath);
                }

                _eConnectAssembly = Assembly.LoadFrom(_eConnectDllPath);
                var type = _eConnectAssembly.GetType(
                    "Microsoft.Dynamics.GP.eConnect.eConnectMethods",
                    throwOnError: true);
                _eConnectMethodsType = type!;
                _loadedFrom = _eConnectDllPath;
                _logger.LogInformation("Loaded eConnect from {Path}", _loadedFrom);
                return _eConnectMethodsType;
            }
        }

        private string BuildCashReceiptXml(
            CashReceiptRequest request,
            string batchId,
            string docNumber,
            DateTime documentDate)
        {
            // eConnect requires fields that IM's Cash Receipts destination supplied from GP defaults.
            var docDate = FormatGpDate(documentDate);
            var amount = request.Amount.ToString("0.00", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
            {
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(false),
                Indent = false
            }))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("eConnect");
                writer.WriteStartElement("RMCashReceiptType");
                writer.WriteStartElement("taRMCashReceiptInsert");

                WriteElement(writer, "CUSTNMBR", request.CustomerId);
                WriteElement(writer, "DOCNUMBR", docNumber);
                WriteElement(writer, "DOCDATE", docDate);
                WriteElement(writer, "ORTRXAMT", amount);
                WriteElement(writer, "GLPOSTDT", docDate);
                WriteElement(writer, "BACHNUMB", batchId);
                WriteElement(writer, "CSHRCTYP", "0"); // 0 = Check
                WriteElement(writer, "CHEKBKID", _checkbookId);
                WriteElement(writer, "CHEKNMBR", request.CheckNumber ?? "");
                WriteElement(writer, "CREATEDIST", "1"); //Flag to automatically create distributions: // 0=Manual;// 1=Automatic

                writer.WriteEndElement(); // taRMCashReceiptInsert
                writer.WriteEndElement(); // RMCashReceiptType
                writer.WriteEndElement(); // eConnect
                writer.WriteEndDocument();
            }

            return sb.ToString();
        }

        private DateTime ResolveDocumentDate(CashReceiptRequest request)
        {
            if (_dateSource == CashReceiptDateSource.RunDate)
                return DateTime.Today;

            return request.DepositDate > System.Data.SqlTypes.SqlDateTime.MinValue.Value
                ? request.DepositDate.Date
                : DateTime.Today;
        }

        private static string FormatGpDate(DateTime date) =>
            date.Date.ToString("yyyy-MM-ddT00:00:00", CultureInfo.InvariantCulture);

        private static void WriteElement(XmlWriter writer, string name, string value)
        {
            writer.WriteElementString(name, value ?? "");
        }

        /// GP BACHNUMB max length is 15. Format: lb-yyyyMMdd-{BatchNumber}
        /// (deposit date + staging batch), e.g. lb-20260630-001.
        internal static string BuildBatchId(DateTime depositDate, string batchNumber)
        {
            var datePart = depositDate > System.Data.SqlTypes.SqlDateTime.MinValue.Value
                ? depositDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            var batch = (batchNumber ?? "").Trim();
            if (batch.Length == 0)
                batch = "0";

            // "lb-" (3) + yyyyMMdd (8) + "-" (1) = 12; leave up to 3 for batch number.
            if (batch.Length > 3)
                batch = batch.Substring(0, 3);

            return "lb-" + datePart + "-" + batch;
        }

    }
}
