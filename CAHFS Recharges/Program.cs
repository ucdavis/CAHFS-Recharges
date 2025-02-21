using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime.CredentialManagement;
using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using Joonasw.AspNetCore.SecurityHeaders;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;
using NLog;
using NLog.Web;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using Quartz;
using System.Net;
using System.Security.Claims;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);

string awsCredentialsFilePath = Directory.GetCurrentDirectory() + "\\awscredentials.xml";

// Early init of NLog to allow startup and exception logging, before host is built
var logger = NLog.LogManager.Setup().SetupExtensions(s => s.RegisterLayoutRenderer("currentEnviroment", (logevent) => builder.Environment.EnvironmentName)).LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    //Load config files and AWS parameter store
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
            Region = RegionEndpoint.USWest1
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

    // Add cache options - for example, to cache the logged in user and their permissions
    // Could remove if caching is not needed
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddMemoryCache();

    // Add Session
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(60);
        options.Cookie.Name = ".CAHFSRecharge.Session";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.IsEssential = true;
    });

    // Cross site request forgery security
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-CSRF-TOKEN";
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.Name = "CAHFSRecharge.Antiforgery";
    });

    // Setup CAS authentication cookie
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.Cookie.Name = "CAHFSRecharge.Authentication.UCD";
            options.LoginPath = new PathString("/Login");
            options.AccessDeniedPath = new PathString("/Denied");
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
        });

    // Add CAS settings from appSettings configuration
    builder.Services.Configure<CasSettings>(builder.Configuration.GetSection("Cas"));

    // Define authorization policies
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("CAHFSUser", policy => policy.RequireClaim(ClaimTypes.AuthenticationMethod, "CAS"));
        
        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new AuthorizationPolicyBuilder().RequireClaim(ClaimTypes.AuthenticationMethod, "CAS").Build().Requirements.ToArray())
            .Build();
    });

    // Add services necessary for nonces in CSP, 32-byte nonces
    builder.Services.AddCsp(nonceByteAmount: 32);

    // Add a CAS HttpClient factory with a retry policy where requests are retried up to 3 times with a exponential backoff of 2^n seconds between attempts.
    // Each request has a timeout of 1 second and the overall will timeout after the default 100 seconds
    var retryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .Or<TimeoutRejectedException>()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

    var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(1);

    builder.Services
        .AddHttpClient("CAS")
        .AddPolicyHandler(retryPolicy)
        .AddPolicyHandler(timeoutPolicy);

    // Settings for HTTP Secure Transport Service
    // See https://aka.ms/aspnetcore-hsts
    builder.Services.AddHsts(options =>
    {
        options.Preload = false;
        options.IncludeSubDomains = false;
        options.MaxAge = TimeSpan.FromHours(1); // expand after we are confident
        options.ExcludedHosts.Add("ucdavis.edu");
        options.ExcludedHosts.Add("vetmed.ucdavis.edu");
    });

    // Settings when forcing HTTPS
    builder.Services.AddHttpsRedirection(options =>
    {
        options.RedirectStatusCode = (int)HttpStatusCode.TemporaryRedirect;
        options.HttpsPort = 443;
    });

    builder.Services.AddDbContext<FinancialContext>();

    // Add Data Protection services (i.e. encryption)
    builder.Services.AddDataProtection();

    // Quartz setup
    builder.Services.AddQuartz(q =>
    {
        q.Properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
        q.UseDefaultThreadPool(x => x.MaxConcurrency = 5);
        q.MisfireThreshold = TimeSpan.FromSeconds(5);
        q.UsePersistentStore(storeOptions =>
        {
            storeOptions.UseProperties = true;
            storeOptions.UseSqlServer(builder.Configuration.GetConnectionString("VIPER") ?? "");
            storeOptions.UseSystemTextJsonSerializer();
            storeOptions.PerformSchemaValidation = true;
        });
    });
    builder.Services.AddQuartzHostedService(q =>
    {
        q.AwaitApplicationStarted = true;
        q.WaitForJobsToComplete = true;
    });

    var app = builder.Build();

    // Add Content Security Policy
    app.UseCsp(csp =>
    {
        // Allow JavaScript from:
        csp.AllowScripts
            .FromSelf() // This domain
            .AddNonce() // Inline scripts only with Nonce
            .AllowUnsafeEval(); // allow JS eval command (must also fit within other restrictions)

        // Contained iframes can be sourced from:
        csp.AllowFrames
            .FromNowhere(); // Nowhere, no iframes allowed

        // Allow fonts to be downloaded from:
        csp.AllowFonts
            .FromSelf();// This domain

        // Allow other sites to put this in an iframe?
        csp.AllowFraming
            .FromNowhere(); // Block framing on other sites, equivalent to X-Frame-Options: DENY

        csp.AllowImages
            .FromSelf()// This domain
            .From("data:")// Allow data: images
            .From("https://www.google-analytics.com")
            .From("*.ucdavis.edu")
            .From("*.vetmed.ucdavis.edu")
            .From("http://localhost");//viper1 typically runs on http on developer machines

        csp.AllowPlugins
            .FromNowhere(); // Plugins not allowed

        csp.AllowStyles
            .FromSelf() // This domain
            .AllowUnsafeInline(); // Allows inline CSS
    });

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        // see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
        app.UseHttpsRedirection(); // Force HTTPS
    }
    else
    {
        app.UseDeveloperExceptionPage(); // Development error / exception page
    }

    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseCookiePolicy();
    app.UseSession();
    app.MapRazorPages();

    //Setup our HTTP Helper to get settings
    HttpHelper.Configure(app.Services.GetService<IMemoryCache>(),
        app.Services.GetService<IConfiguration>(),
        app.Environment,
        app.Services.GetService<IHttpContextAccessor>(),
        app.Services.GetService<IAuthorizationService>(),
        app.Services.GetService<IDataProtectionProvider>()
        );

    app.Run();
}
catch (Exception exception)
{
    // NLog: catch setup errors
    logger.Fatal(exception, "Stopped program because of exception");
    throw;
}
finally
{
    // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
    NLog.LogManager.Shutdown();
}


/// <summary>
/// Try and parse the AWS credentials XML file and store it in the encrypted JSON
/// </summary>
void SetAwsCredentials(Logger logger)
{
    XElement xAwsCredentials = XElement.Load(awsCredentialsFilePath, LoadOptions.None);

    if (!String.IsNullOrWhiteSpace(xAwsCredentials?.Element("AccessKeyId")?.Value) && !String.IsNullOrWhiteSpace(xAwsCredentials?.Element("SecretAccessKey")?.Value))
    {
        // grab the credentials ouf of the xml file to stor in the encrypted json file inthe profile
        var options = new CredentialProfileOptions
        {
            AccessKey = xAwsCredentials?.Element("AccessKeyId")?.Value.Trim(),
            SecretKey = xAwsCredentials?.Element("SecretAccessKey")?.Value.Trim()
        };

        var profile = new CredentialProfile("default", options);
        // if a region was specified in the xml then use the specified region else default to USWest1
        if (!string.IsNullOrWhiteSpace(xAwsCredentials?.Element("RegionEndpoint")?.Value) && xAwsCredentials?.Element("RegionEndpoint") != null)
        {
#pragma warning disable CS8604 // Possible null reference argument.
            profile.Region = typeof(Amazon.RegionEndpoint).GetField(xAwsCredentials?.Element("RegionEndpoint")?.Value)?.GetValue(null) as Amazon.RegionEndpoint;
#pragma warning restore CS8604 // Possible null reference argument.
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
            logger.Error($"COULD NOT DELETE THE AWS CREDENTIALS XML FILE (\"{awsCredentialsFilePath}\").  The file will need to be deleted manually.");
        }
    }
    else
    {
        throw new FormatException($"Could not parse AWS Credentials File: \"{awsCredentialsFilePath}\". AccessKeyId and/or SecretAccessKey are blank.");
    }
}