using System.Data;
using System.Text;
using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Lockbox;
using CAHFS_Recharges.Models.Options;
using CAHFS_Recharges.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Services.Lockbox
{
    /// Loads C_LB_Raw_Line from vault and runs C_LB_Process_File (parse + staging).
    public sealed class LockboxFileProcessService
    {
        private readonly LockboxOptions _options;
        private readonly IIntegrationDbResolver _dbResolver;
        private readonly FinancialContext _cahfsContext;
        private readonly EquineFinancialContext _equineContext;
        private readonly ILogger<LockboxFileProcessService> _logger;

        public LockboxFileProcessService(
            IOptions<LockboxOptions> options,
            IIntegrationDbResolver dbResolver,
            FinancialContext cahfsContext,
            EquineFinancialContext equineContext,
            ILogger<LockboxFileProcessService> logger)
        {
            _options = options.Value;
            _dbResolver = dbResolver;
            _cahfsContext = cahfsContext;
            _equineContext = equineContext;
            _logger = logger;
        }

        public async Task ProcessPendingFilesAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
                return;

            foreach (var integration in new[] { IntegrationType.CAHFS, IntegrationType.EQUINE })
            {
                var lab = _options.GetLab(integration);
                var files = await _dbResolver.GetLockboxFiles(integration)
                    .Where(f => f.LockboxId == lab.LockboxId &&
                                (f.ParseStatus == LockboxParseStatus.Pending ||
                                 f.ParseStatus == LockboxParseStatus.Error))
                    .OrderBy(f => f.FileBusinessDate)
                    .ToListAsync(cancellationToken);

                foreach (var file in files)
                {
                    try
                    {
                        await ProcessFileAsync(integration, file.FileId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lockbox process failed FileId={FileId}", file.FileId);
                    }
                }
            }
        }

        public async Task ProcessFileAsync(
            IntegrationType integration,
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            var files = _dbResolver.GetLockboxFiles(integration);
            var file = await files.FirstOrDefaultAsync(f => f.FileId == fileId, cancellationToken)
                ?? throw new InvalidOperationException($"Lockbox file {fileId} not found.");

            file.ParseStatus = LockboxParseStatus.Parsing;
            file.ParseError = null;
            await _dbResolver.SaveChangesAsync(integration, cancellationToken);

            try
            {
                var lineCount = await LoadRawLinesAsync(integration, file, cancellationToken);
                _logger.LogInformation(
                    "Lockbox loaded {LineCount} raw lines for {FileName} ({Integration})",
                    lineCount, file.RemoteFileName, integration);

                await _dbResolver.ExecuteSqlAsync(
                    integration,
                    $"EXEC dbo.C_LB_Process_File {file.RemoteFileName}",
                    cancellationToken);

                file.ParseStatus = LockboxParseStatus.Parsed;
                file.ParsedAtUtc = DateTime.UtcNow;
                file.ParseError = null;
            }
            catch (Exception ex)
            {
                file.ParseStatus = LockboxParseStatus.Error;
                file.ParseError = Truncate(ex.Message, 500);
                throw;
            }
            finally
            {
                await _dbResolver.SaveChangesAsync(integration, cancellationToken);
            }
        }

        private async Task<int> LoadRawLinesAsync(
            IntegrationType integration,
            LockboxFile file,
            CancellationToken cancellationToken)
        {
            await _dbResolver.ExecuteSqlAsync(
                integration,
                $"EXEC dbo.C_LB_Clear_File {file.RemoteFileName}",
                cancellationToken);

            var lines = SplitRawLines(file.RawContent);
            if (lines.Count == 0)
                return 0;

            var context = GetContext(integration);
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            try
            {
                using var bulk = new SqlBulkCopy((SqlConnection)connection)
                {
                    DestinationTableName = "dbo.C_LB_Raw_Line",
                    BatchSize = 5000
                };
                bulk.ColumnMappings.Add("FileName", "FileName");
                bulk.ColumnMappings.Add("LockboxFileId", "LockboxFileId");
                bulk.ColumnMappings.Add("RawLine", "RawLine");

                var table = new DataTable();
                table.Columns.Add("FileName", typeof(string));
                table.Columns.Add("LockboxFileId", typeof(Guid));
                table.Columns.Add("RawLine", typeof(string));

                foreach (var line in lines)
                {
                    table.Rows.Add(file.RemoteFileName, file.FileId, line);
                }

                await bulk.WriteToServerAsync(table, cancellationToken);
            }
            finally
            {
                await connection.CloseAsync();
            }

            return lines.Count;
        }

        internal static List<string> SplitRawLines(byte[] rawContent)
        {
            var text = Encoding.Latin1.GetString(rawContent);
            return text.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
        }

        private DbContext GetContext(IntegrationType integration) =>
            integration == IntegrationType.EQUINE ? _equineContext : _cahfsContext;

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];
    }
}
