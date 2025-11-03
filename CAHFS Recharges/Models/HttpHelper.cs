using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NLog;

namespace CAHFS_Recharges.Models
{
    public class HttpHelper
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static IHttpContextAccessor? httpContextAccessor;
        private static IAuthorizationService? authorizationService;
        private static IDataProtectionProvider? dataProtectionProvider;

        // Settings from appsettings.json or AWS Parameter Store
        public static IConfiguration? Settings { get; private set; }
        // The current environment (Development, Test, Production)
        public static IWebHostEnvironment? Environment { get; private set; }
        // Get the current HttpContext
        public static HttpContext? HttpContext
        {
            get
            {
                if (httpContextAccessor != null)
                {
                    return httpContextAccessor.HttpContext;
                }
                else
                {
                    return null;
                }
            }
        }
        // Memory Cache, if needed
        public static IMemoryCache? Cache { get; private set; }

        // <summary>
        /// Helper functions constructor (gets injected with the memeory cache object)
        /// </summary>
        /// <param name="memoryCache"></param>
        public static void Configure(IMemoryCache? memoryCache, IConfiguration? configurationSettings, IWebHostEnvironment env, IHttpContextAccessor? httpContextAccessor, IAuthorizationService? authorizationService, IDataProtectionProvider? dataProtectionProvider)
        {
            Cache = memoryCache;
            Settings = configurationSettings;
            Environment = env;
            HttpHelper.httpContextAccessor = httpContextAccessor;
            HttpHelper.authorizationService = authorizationService;
            HttpHelper.dataProtectionProvider = dataProtectionProvider;
        }

        /// <summary>
        /// Helper function to return a setting, including a null check for Settings
        /// </summary>
        /// <typeparam name="T">Type of setting to be returned, e.g. string</typeparam>
        /// <param name="section">Section for Settings.GetSection()</param>
        /// <param name="setting">Setting for section.GetValue<T>()</param>
        /// <returns></returns>
        public static T? GetSetting<T>(string section, string setting)
        {
            var val = Settings == null
                ? default
                : Settings.GetSection(section).GetValue<T>(setting);
            logger.Warn("section " + section + " " + val == null ? "null" : val.ToString().Length);
            return Settings == null
                ? default
                : Settings.GetSection(section).GetValue<T>(setting);
        }

        /// <summary>
        /// Gets the root URL including protocol and port for the app
        /// </summary>
        public static string GetRootURL()
        {
            string rootURL = string.Empty;

            HttpRequest? thisRequest = httpContextAccessor?.HttpContext?.Request;

            if (thisRequest != null)
            {
                Uri url = new(thisRequest.GetDisplayUrl());
                rootURL = url.GetLeftPart(UriPartial.Authority);

                if(Environment != null && Environment.IsEnvironment("Test"))
                {
                    rootURL += "/caei-test";
                }
                else if (Environment != null && Environment.IsEnvironment("Production"))
                {
                    rootURL += "/caei";
                }
            }

            return rootURL ?? string.Empty;
        }
    }
}

