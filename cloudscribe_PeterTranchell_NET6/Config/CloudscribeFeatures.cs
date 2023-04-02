using cloudscribe.UserProperties.Models;
using cloudscribe.UserProperties.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
            services.AddCloudscribeSimpleContactFormCoreIntegration(config);
            services.AddCloudscribeSimpleContactForm(config);

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



            return services;
        }

    }
}
