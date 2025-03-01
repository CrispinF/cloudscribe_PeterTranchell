﻿using Microsoft.AspNetCore.Authorization;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class Authorization
    {
        public static AuthorizationOptions SetupAuthorizationPolicies(this AuthorizationOptions options)
        {
            //https://docs.asp.net/en/latest/security/authorization/policies.html


            options.AddCloudscribeLoggingDefaultPolicy();

            options.AddCloudscribeCoreDefaultPolicies();

            options.AddCloudscribeCoreSimpleContentIntegrationDefaultPolicies();


            options.AddPolicy(
                "FileManagerPolicy",
                authBuilder =>
                {
                    authBuilder.RequireRole("Administrators", "Content Administrators");
                });

            options.AddPolicy(
                "FileUploadPolicy",
                authBuilder =>
                {
                    authBuilder.RequireRole("Administrators", "Content Administrators");
                });

            options.AddPolicy(
                "FileManagerDeletePolicy",
                authBuilder =>
                {
                    authBuilder.RequireRole("Administrators", "Content Administrators");
                });







            options.AddPolicy(
                "FormsAdminPolicy",
                authBuilder =>
                {
                    authBuilder.RequireRole("Administrators", "Content Administrators");
                });

            options.AddPolicy(
                "FormsChooserPolicy",
                authBuilder =>
                {
                    authBuilder.RequireRole("Administrators", "Content Administrators");
                });



            options.AddPolicy(
                "EmailListAdminPolicy",
                authBuilder =>
                {
                    authBuilder.RequireRole("Administrators");
                });



            // add other policies here 

            options.AddPolicy(
                "QueryToolApiPolicy",
                authBuilder =>
                {
                    authBuilder.RequireRole("Administrators");
                });
            options.AddPolicy(
                "QueryToolAdminPolicy",
                authBuilder =>
                {
                    authBuilder.RequireRole("Administrators");
                });

            return options;
                }

            }
}
