using LockboxCashReceiptPoster.Abstractions;
using LockboxCashReceiptPoster.Data;
using LockboxCashReceiptPoster.Destinations;
using LockboxCashReceiptPoster.Notify;
using LockboxCashReceiptPoster.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace LockboxCashReceiptPoster
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var runArgs = ParseArgs(args);
                if (runArgs == null)
                {
                    Console.Error.WriteLine(
                        "Usage: LockboxCashReceiptPoster.exe --company CAHFS|EQUINE [--dry-run] [--max N]");
                    return 2;
                }

                var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    ?? "Production";

                Console.WriteLine($"LockboxCashReceiptPoster starting. Environment={env} Company={runArgs.Company}");

                var configuration = AwsConfigurationBootstrap.Build(env, Console.WriteLine);
                var posterOptions = configuration.GetSection(CashReceiptPosterOptions.SectionName)
                    .Get<CashReceiptPosterOptions>() ?? new CashReceiptPosterOptions();

                if (!posterOptions.Enabled)
                {
                    Console.WriteLine("CashReceiptPoster:Enabled=false — exiting.");
                    return 0;
                }

                if (!posterOptions.Companies.TryGetValue(runArgs.Company, out var companyOptions))
                {
                    Console.Error.WriteLine($"Unknown company '{runArgs.Company}'. Configure CashReceiptPoster:Companies.");
                    return 2;
                }

                var connectionString = ConnectionStringResolver.Resolve(
                    configuration,
                    companyOptions.ConnectionStringName);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    Console.Error.WriteLine(
                        $"Missing connection string '{companyOptions.ConnectionStringName}' " +
                        $"(also tried CAEI FinancialDB / EquineFinancialDB spellings) for company {runArgs.Company}.");
                    return 2;
                }

                var sqlConnectionString = connectionString!;

                if (!runArgs.MaxRows.HasValue && posterOptions.MaxRows > 0)
                    runArgs.MaxRows = posterOptions.MaxRows;

                Directory.CreateDirectory(posterOptions.LogDirectory);

                var services = new ServiceCollection();
                services.AddLogging(builder =>
                {
                    builder.AddConfiguration(configuration.GetSection("Logging"));
                    builder.AddConsole();
                    builder.AddProvider(new SimpleFileLoggerProvider(
                        Path.Combine(
                            posterOptions.LogDirectory,
                            $"cash-receipt-{runArgs.Company}-{DateTime.Now:yyyyMMdd}.log")));
                });
                services.AddSingleton(posterOptions);
                services.AddSingleton(runArgs);
                services.AddSingleton<IPendingReceiptSource>(_ => new StagingPendingSource(sqlConnectionString));
                services.AddSingleton<IPostStatusStore>(_ => new StagingPostStatusStore(sqlConnectionString));
                services.AddSingleton<ICashReceiptPoster>(sp =>
                    new GpEConnectCashReceiptPoster(
                        sqlConnectionString,
                        posterOptions,
                        sp.GetRequiredService<ILogger<GpEConnectCashReceiptPoster>>()));

                if (posterOptions.Email.Enabled)
                    services.AddSingleton<IFailureNotifier, SmtpFailureNotifier>();
                else
                    services.AddSingleton<IFailureNotifier, NoOpFailureNotifier>();

                services.AddSingleton(sp => new CashReceiptRunService(
                    sp.GetRequiredService<RunArgs>(),
                    sp.GetRequiredService<IPendingReceiptSource>(),
                    sp.GetRequiredService<ICashReceiptPoster>(),
                    sp.GetRequiredService<IPostStatusStore>(),
                    sp.GetRequiredService<IFailureNotifier>(),
                    sp.GetRequiredService<ILogger<CashReceiptRunService>>()));

                using (var provider = services.BuildServiceProvider())
                {
                    var runner = provider.GetRequiredService<CashReceiptRunService>();
                    return runner.RunAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static RunArgs? ParseArgs(string[] args)
        {
            string? company = null;
            var dryRun = false;
            int? maxRows = null;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, "--company", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    company = args[++i].Trim().ToUpperInvariant();
                }
                else if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    dryRun = true;
                }
                else if (string.Equals(arg, "--max", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (!int.TryParse(args[++i], out var n) || n <= 0)
                    {
                        Console.Error.WriteLine("--max must be a positive integer.");
                        return null;
                    }

                    maxRows = n;
                }
                else if (!arg.StartsWith("-", StringComparison.Ordinal) && company == null && args.Length == 1)
                {
                    company = arg.Trim().ToUpperInvariant();
                }
                else if (arg.StartsWith("-", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Unknown argument: {arg}");
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(company))
                return null;

            return new RunArgs
            {
                Company = company!,
                DryRun = dryRun,
                MaxRows = maxRows
            };
        }
    }

    /// Minimal file logger — keeps Framework app free of NLog dependency.
    internal sealed class SimpleFileLoggerProvider : ILoggerProvider
    {
        private readonly string _path;
        private readonly object _gate = new object();

        public SimpleFileLoggerProvider(string path)
        {
            _path = path;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(categoryName, _path, _gate);

        public void Dispose() { }

        private sealed class SimpleFileLogger : ILogger
        {
            private readonly string _category;
            private readonly string _path;
            private readonly object _gate;

            public SimpleFileLogger(string category, string path, object gate)
            {
                _category = category;
                _path = path;
                _gate = gate;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_category}: {formatter(state, exception)}";
                if (exception != null)
                    line += Environment.NewLine + exception;

                lock (_gate)
                {
                    File.AppendAllText(_path, line + Environment.NewLine);
                }
            }
        }
    }
}
