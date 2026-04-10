using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime.CredentialManagement;
using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Data;
using CAHFS_Recharges.Middleware;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Hangfire;
using Hangfire.SqlServer;
using Joonasw.AspNetCore.SecurityHeaders;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NLog;
using NLog.Web;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using Quartz;
using System.Security.Claims;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);

string awsCredentialsFilePath = Directory.GetCurrentDirectory() + "\\awscredentials.xml";

// Early init of NLog to allow startup and exception logging, before host is built
var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    // Load config files and AWS parameter store
    builder.Configuration.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings." + builder.Environment.EnvironmentName + ".json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    if (File.Exists(awsCredentialsFilePath))
    {
        SetAwsCredentials(logger);
    }

    try
    {
        // AWS Configurations
        AWSOptions awsOptions = new()
        {
            Region = RegionEndpoint.USWest1,
            Profile = "cahfs"
        };

        builder.Configuration
            .AddSystemsManager("/" + builder.Environment.EnvironmentName, awsOptions)
            .AddSystemsManager("/Shared", awsOptions);
    }
    catch (Exception ex)
    {
        logger.Fatal("Failed to get secrets from AWS. Error: " + ex.InnerException);
    }

    // Add services to the container.
    builder.Services.AddRazorPages(options =>
    {
        options.Conventions.AuthorizeFolder("/");
        options.Conventions.AllowAnonymousToPage("/Login");
        options.Conventions.AllowAnonymousToPage("/CasLogin");
        options.Conventions.AllowAnonymousToPage("/Error");
        options.Conventions.AllowAnonymousToPage("/Denied");
    });

    builder.Host.UseNLog();

    // Cache
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddMemoryCache();

    // Session
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(60);
        options.Cookie.Name = ".CAHFSRecharge.Session";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.IsEssential = true;
    });

    // CSRF
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-CSRF-TOKEN";
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.Name = "CAHFSRecharge.Antiforgery";
    });

    // CAS cookie auth
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.Cookie.Name = "CAHFSRecharge.Authentication.UCD";
            options.LoginPath = new PathString("/Login");
            options.AccessDeniedPath = new PathString("/Denied");
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
        });

    // CAS settings
    builder.Services.Configure<CasSettings>(builder.Configuration.GetSection("Cas"));

    // Authorization policies
    builder.Services.AddAuthorization(options =>
    {
        // Legacy policy (kept for backwards compatibility)
        options.AddPolicy("CAHFSUser", policy => policy.RequireClaim(ClaimTypes.AuthenticationMethod, "CAS"));

        // CAEI role-based policies
        options.AddPolicy(CaeiPolicies.ViewerPolicy, policy =>
            policy.RequireAuthenticatedUser()
                  .AddRequirements(new RoleRequirement(CaeiRoles.Viewer)));

        options.AddPolicy(CaeiPolicies.OperatorPolicy, policy =>
            policy.RequireAuthenticatedUser()
                  .AddRequirements(new RoleRequirement(CaeiRoles.Operator)));

        options.AddPolicy(CaeiPolicies.AdminPolicy, policy =>
            policy.RequireAuthenticatedUser()
                  .AddRequirements(new RoleRequirement(CaeiRoles.Admin)));

        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new AuthorizationPolicyBuilder()
                .RequireClaim(ClaimTypes.AuthenticationMethod, "CAS")
                .Build()
                .Requirements
                .ToArray())
            .Build();
    });

    // Role-based authorization handler
    builder.Services.AddSingleton<IAuthorizationHandler, ConfigBasedRoleHandler>();

    // CSP nonces
    builder.Services.AddCsp(nonceByteAmount: 32);

    // CAS HttpClient retry + timeout
    var retryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .Or<TimeoutRejectedException>()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

    var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(1);

    builder.Services
        .AddHttpClient("CAS")
        .AddPolicyHandler(retryPolicy)
        .AddPolicyHandler(timeoutPolicy);

    // HSTS
    builder.Services.AddHsts(options =>
    {
        options.Preload = false;
        options.IncludeSubDomains = false;
        options.MaxAge = TimeSpan.FromHours(1);
        options.ExcludedHosts.Add("ucdavis.edu");
        options.ExcludedHosts.Add("vetmed.ucdavis.edu");
    });

    // DbContexts
    builder.Services.AddDbContext<FinancialContext>();
    builder.Services.AddDbContext<StarLIMSContext>();
    builder.Services.AddDbContext<EquineFinancialContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("EquineFinancialDb")));

    // Data Protection
    builder.Services.AddDataProtection();

    // Quartz setup (kept as-is)
    builder.Services.AddQuartz(q =>
    {
        // optional quartz settings
    });
    builder.Services.AddQuartzHostedService(q =>
    {
        q.AwaitApplicationStarted = true;
        q.WaitForJobsToComplete = true;
    });

    
    // AE HTTP Trace Store 
    builder.Services.AddSingleton<IAeHttpTraceStore, AeHttpTraceStore>();

    // Hangfire setup
    var hangfireEnabled = builder.Configuration.GetValue<bool?>("Hangfire:Enabled") ?? true;
    TimeZoneInfo? hangfireTz = null;

    if (hangfireEnabled)
    {
        // Prefer standard ConnectionStrings lookup; fallback to direct key lookup for Parameter Store mappings
        var hangfireConn =
            builder.Configuration.GetConnectionString("HangfireDb")
            ?? builder.Configuration["ConnectionStrings:HangfireDb"];

        if (string.IsNullOrWhiteSpace(hangfireConn))
        {
            throw new Exception("Hangfire is enabled but ConnectionStrings:HangfireDb is missing/empty (check AWS Parameter Store mapping).");
        }

        var prepareSchema = builder.Configuration.GetValue<bool?>("Hangfire:PrepareSchemaIfNecessary") ?? false;

        builder.Services.AddHangfire(hf =>
            hf.SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
              .UseSimpleAssemblyNameTypeSerializer()
              .UseRecommendedSerializerSettings()
              .UseSqlServerStorage(hangfireConn, new SqlServerStorageOptions
              {
                  PrepareSchemaIfNecessary = prepareSchema,
                  CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                  SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                  QueuePollInterval = TimeSpan.FromSeconds(15),
                  UseRecommendedIsolationLevel = true,
                  DisableGlobalLocks = true
              })
        );

        builder.Services.AddHangfireServer();
        builder.Services.AddScoped<HangfireJobs>();

        // Timezone for recurring jobs
        var tzId = builder.Configuration.GetValue<string>("Hangfire:TimeZone") ?? "America/Los_Angeles";
        hangfireTz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
    }

    // Aggie Enterprise client + services
    builder.Services.AddSingleton<ITokenService, TokenService>();
    builder.Services.AddTransient<AuthorizationMessageHandler>();

    builder.Services
        .AddAggieEnterpriseClient()
        .ConfigureHttpClient((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var baseUrl = config.GetSection("AggieEnterprise").GetValue<string>("BaseUrl") ?? "";
            client.BaseAddress = new Uri(baseUrl);

            // Optional
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        },
        clientBuilder =>
        {
            // IMPORTANT: this is IHttpClientBuilder here, so AddHttpMessageHandler works
            clientBuilder.AddHttpMessageHandler<AuthorizationMessageHandler>();
        });


    // CAHFS Services

    // Integration Context
    builder.Services.AddScoped<IIntegrationContextService, IntegrationContextService>();

    // Integration DbContext Resolver (for unified services)
    builder.Services.AddScoped<IIntegrationDbResolver, IntegrationDbResolver>();

    // COA Validation
    builder.Services.AddScoped<StagingCoaValidationService>();

    // Ready to Send Gatekeeper (unified for CAHFS + EQUINE via IIntegrationDbResolver)
    builder.Services.AddScoped<AggieEnterpriseSendGatekeeper>();

    // Journal creation and Upload service (unified for CAHFS + EQUINE via IIntegrationDbResolver)
    builder.Services.AddScoped<AggieEnterpriseJournalUploadService>();

    var app = builder.Build();

    // Configure integration link helper (route-based vs legacy pages)
    // Set "CAEI:UseRouteBased" in appsettings.json to false to use legacy pages
    IntegrationLinkHelper.Configure(builder.Configuration);

    // CSP
    app.UseCsp(csp =>
    {
        csp.AllowScripts
            .FromSelf()
            .AddNonce()
            .AllowUnsafeEval();

        csp.AllowFrames.FromNowhere();
        csp.AllowFonts.FromSelf();
        csp.AllowFraming.FromNowhere();

        csp.AllowImages
            .FromSelf()
            .From("data:")
            .From("https://www.google-analytics.com")
            .From("*.ucdavis.edu")
            .From("*.vetmed.ucdavis.edu");

        csp.AllowPlugins.FromNowhere();

        csp.AllowStyles
            .FromSelf()
            .AllowUnsafeInline();
    });

    // PathBase: when hosted under a virtual directory (e.g. IIS /caei-test), set in appsettings.{Environment}.json
    var pathBase = builder.Configuration.GetValue<string>("PathBase");
    if (!string.IsNullOrWhiteSpace(pathBase))
    {
        pathBase = pathBase.TrimEnd('/');
        if (pathBase.Length > 0)
            app.UsePathBase(pathBase);
    }

    // Pipeline
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
        // app.UseHttpsRedirection();
    }
    else
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseStaticFiles();
    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseCookiePolicy();
    app.UseSession();

    // Redirect legacy URLs to route-based so old bookmarks do not 404 (Phase 2)
    app.UseMiddleware<LegacyIntegrationRedirectMiddleware>();

    app.MapRazorPages();

    // Setup HTTP Helper
    HttpHelper.Configure(app.Services.GetService<IMemoryCache>(),
        app.Services.GetService<IConfiguration>(),
        app.Environment,
        app.Services.GetService<IHttpContextAccessor>(),
        app.Services.GetService<IAuthorizationService>(),
        app.Services.GetService<IDataProtectionProvider>());

    // Hangfire dashboard + recurring jobs
    if (hangfireEnabled)
    {
        // Dashboard (middleware). Auth is enforced by your HangfireAuthorizationFilter + global auth middleware.
        app.UseHangfireDashboard("/hangfire", new Hangfire.DashboardOptions
        {
            AppPath = pathBase + "/Home", // "Back to site" link target
            Authorization = new Hangfire.Dashboard.IDashboardAuthorizationFilter[]
            {
                new CAHFS_Recharges.Services.HangfireAuthorizationFilter()
            }
        });

        var hangCronDaily = builder.Configuration.GetValue<string>("Hangfire:CronDaily") ?? "0 2 * * *";
        var hangCronWed = builder.Configuration.GetValue<string>("Hangfire:CronWednesday") ?? "0 3 * * 3";
        var tz = hangfireTz ?? TimeZoneInfo.Local;

        RecurringJob.AddOrUpdate<HangfireJobs>(
            "DailyCoaValidation",
            job => job.ValidatePendingCoasJob(),
            hangCronDaily,
            new RecurringJobOptions { TimeZone = tz });

        RecurringJob.AddOrUpdate<HangfireJobs>(
            "WednesdaySendLastWeek",
            job => job.SendLastWeekBatchesJob(),
            hangCronWed,
            new RecurringJobOptions { TimeZone = tz });
    }

    app.Run();
}
catch (Exception exception)
{
    logger.Fatal(exception, "Stopped program because of exception");
    throw;
}
finally
{
    NLog.LogManager.Shutdown();
}

/// Try and parse the AWS credentials XML file and store it in the encrypted JSON
void SetAwsCredentials(Logger logger)
{
    XElement xAwsCredentials = XElement.Load(awsCredentialsFilePath, LoadOptions.None);

    if (!string.IsNullOrWhiteSpace(xAwsCredentials?.Element("AccessKeyId")?.Value) &&
        !string.IsNullOrWhiteSpace(xAwsCredentials?.Element("SecretAccessKey")?.Value))
    {
        var options = new CredentialProfileOptions
        {
            AccessKey = xAwsCredentials?.Element("AccessKeyId")?.Value.Trim(),
            SecretKey = xAwsCredentials?.Element("SecretAccessKey")?.Value.Trim()
        };

        var profile = new CredentialProfile("cahfs", options);

        if (!string.IsNullOrWhiteSpace(xAwsCredentials?.Element("RegionEndpoint")?.Value) &&
            xAwsCredentials?.Element("RegionEndpoint") != null)
        {
#pragma warning disable CS8604
            profile.Region = typeof(Amazon.RegionEndpoint)
                .GetField(xAwsCredentials?.Element("RegionEndpoint")?.Value)?
                .GetValue(null) as Amazon.RegionEndpoint;
#pragma warning restore CS8604
        }
        else
        {
            profile.Region = Amazon.RegionEndpoint.USWest1;
        }
        var netSDKFile = new NetSDKCredentialsFile();
        netSDKFile.RegisterProfile(profile);

        try
        {
            File.Delete(awsCredentialsFilePath);
        }
        catch
        {
            logger.Error($"COULD NOT DELETE THE AWS CREDENTIALS XML FILE (\"{awsCredentialsFilePath}\"). The file will need to be deleted manually.");
        }
    }
    else
    {
        throw new FormatException($"Could not parse AWS Credentials File: \"{awsCredentialsFilePath}\". AccessKeyId and/or SecretAccessKey are blank.");
    }
}
