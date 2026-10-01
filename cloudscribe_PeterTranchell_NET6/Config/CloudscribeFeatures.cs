using cloudscribe.UserProperties.Models;
using cloudscribe.UserProperties.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using cloudscribe.QueryTool.Services;
using cloudscribe.QueryTool.EFCore.MSSQL;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class CloudscribeFeatures
    {

        public static IServiceCollection SetupDataStorage(
            this IServiceCollection services,
            IConfiguration config,
            IWebHostEnvironment env
            )
        {
            var connectionString = config.GetConnectionString("EntityFrameworkConnection");
            var queryToolConnectionString = config.GetConnectionString("QueryToolConnectionString");


            services.AddCloudscribeCoreEFStorageMSSQL(connectionString);


            services.AddCloudscribeKvpEFStorageMSSQL(connectionString);
            services.AddCloudscribeLoggingEFStorageMSSQL(connectionString);

            services.AddCloudscribeSimpleContentEFStorageMSSQL(connectionString);


            services.AddFormsStorageMSSQL(connectionString);



            services.AddEmailTemplateStorageMSSQL(connectionString);
            services.AddEmailQueueStorageMSSQL(connectionString);

            services.AddEmailListStorageMSSQL(connectionString);

            services.AddQueryToolEFStorageMSSQL(connectionString: connectionString, maxConnectionRetryCount: 0, maxConnectionRetryDelaySeconds: 30, transientSqlErrorNumbersToAdd: null);



            return services;
        }

        public static IServiceCollection SetupCloudscribeFeatures(
            this IServiceCollection services,
            IConfiguration config
            )
        {

            services.AddCloudscribeLogging(config);

            services.Configure<ProfilePropertySetContainer>(config.GetSection("ProfilePropertySetContainer"));
            services.AddEmailListKvpIntegration(config);
            services.AddCloudscribeKvpUserProperties();


            services.AddScoped<cloudscribe.Web.Navigation.INavigationNodePermissionResolver, cloudscribe.Web.Navigation.NavigationNodePermissionResolver>();
            services.AddScoped<cloudscribe.Web.Navigation.INavigationNodePermissionResolver, cloudscribe.SimpleContent.Web.Services.PagesNavigationNodePermissionResolver>();
            services.AddCloudscribeCoreMvc(config);
            services.AddCloudscribeCoreIntegrationForSimpleContent(config);
            services.AddSimpleContentMvc(config);
            services.AddContentTemplatesForSimpleContent(config);

            services.AddMetaWeblogForSimpleContent(config.GetSection("MetaWeblogApiOptions"));
            services.AddSimpleContentRssSyndiction();
            //services.AddCloudscribeSimpleContactFormCoreIntegration(config);
            //services.AddCloudscribeSimpleContactForm(config);

            services.AddFormsCloudscribeCoreIntegration(config);
            services.AddFormsServices(config);
            services.AddFormSurveyContentTemplatesForSimpleContent(config);
            // these are examples to show you how to implement custom form submission handlers.
            // see /Services/SampleFormSubmissionHandlers.cs
            services.AddScoped<cloudscribe.Forms.Models.IHandleFormSubmission, cloudscribe_PeterTranchell_NET6.Services.FakeFormSubmissionHandler1>();
            services.AddScoped<cloudscribe.Forms.Models.IHandleFormSubmission, cloudscribe_PeterTranchell_NET6.Services.FakeFormSubmissionHandler2>();



            services.AddEmailQueueBackgroundTask(config);
            services.AddEmailQueueWithCloudscribeIntegration(config);
            services.AddEmailRazorTemplating(config);

            services.AddEmailListWithCloudscribeIntegration(config);

            services.AddScoped<IQueryTool, QueryTool>();

            services.Configure<cloudscribe_PeterTranchell_NET6.Services.FolderGalleryOptions>(config.GetSection(cloudscribe_PeterTranchell_NET6.Services.FolderGalleryOptions.SectionName));

            // Scoped so the per-request generation budget resets each request;
            // cross-request concurrency is handled by the store's static
            // per-folder locks.
            services.AddScoped<cloudscribe_PeterTranchell_NET6.Services.IThumbnailStore, cloudscribe_PeterTranchell_NET6.Services.ThumbnailStore>();
            services.AddScoped<cloudscribe_PeterTranchell_NET6.Services.IFolderImageEnumerator, cloudscribe_PeterTranchell_NET6.Services.FolderImageEnumerator>();

            services.AddHttpClient();
            services.AddHttpContextAccessor();
            services.Configure<cloudscribe_PeterTranchell_NET6.Services.Chat.ChatOptions>(config.GetSection("ChatOptions"));
            services.AddSingleton<cloudscribe_PeterTranchell_NET6.Services.Chat.SiteCorpusProvider>();
            services.AddSingleton<cloudscribe_PeterTranchell_NET6.Services.Chat.DatabaseCorpusProvider>();
            services.AddSingleton<cloudscribe_PeterTranchell_NET6.Services.Chat.CorpusProvider>();
            services.AddSingleton<cloudscribe_PeterTranchell_NET6.Services.Chat.EmbeddingClient>();
            services.AddSingleton<cloudscribe_PeterTranchell_NET6.Services.Chat.LlmChatClient>();
            services.AddSingleton<cloudscribe_PeterTranchell_NET6.Services.Chat.ChatIndexService>();
            services.AddSingleton<cloudscribe_PeterTranchell_NET6.Services.Chat.ChatNonceService>();
            services.AddSingleton<cloudscribe_PeterTranchell_NET6.Services.Chat.RecaptchaVerifier>();
            services.AddHostedService<cloudscribe_PeterTranchell_NET6.Services.Chat.ChatWarmupService>();
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    var chatOptions = httpContext.RequestServices
                        .GetRequiredService<Microsoft.Extensions.Options.IOptions<cloudscribe_PeterTranchell_NET6.Services.Chat.ChatOptions>>().Value;
                    if (chatOptions.Enabled && httpContext.Request.Path.StartsWithSegments("/api/chat"))
                    {
                        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
                            new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                            {
                                PermitLimit = Math.Max(1, chatOptions.RateLimitRequestsPerMinute),
                                Window = TimeSpan.FromMinutes(1)
                            });
                    }
                    return System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter<string>("none");
                });
            });



            return services;
        }

    }
}
