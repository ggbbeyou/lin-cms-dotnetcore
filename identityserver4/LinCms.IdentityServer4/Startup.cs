using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using AutoMapper;
using FreeSql;
using FreeSql.Internal;
using LinCms.Application;
using LinCms.Application.AutoMapper.Cms;
using LinCms.Application.Cms.Users;
using LinCms.Core.Aop;
using LinCms.Core.Data;
using LinCms.Core.Data.Enums;
using LinCms.Core.Dependency;
using LinCms.Core.Entities;
using LinCms.Core.Extensions;
using LinCms.Core.Middleware;
using LinCms.Core.Security;
using LinCms.IdentityServer4.IdentityServer4;
using LinCms.Infrastructure.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LinCms.IdentityServer4
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public IFreeSql Fsql { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            IConfigurationSection configurationSection = Configuration.GetSection("ConnectionStrings:Default");

            Fsql = new FreeSqlBuilder()
                .UseConnectionString(DataType.MySql, configurationSection.Value)
                .UseEntityPropertyNameConvert(StringConvertType.PascalCaseToUnderscoreWithLower)
                .UseAutoSyncStructure(true)
                .UseMonitorCommand(cmd =>
                    {
                        Trace.WriteLine(cmd.CommandText);
                    }
                )
                .UseSyncStructureToLower(true) // תСдͬ���ṹ
                .Build();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            InMemoryConfiguration.Configuration = this.Configuration;

            services.AddSingleton(Fsql);
            services.AddScoped<IUnitOfWork>(sp => sp.GetService<IFreeSql>().CreateUnitOfWork());

            services.AddFreeRepository(filter =>
            {
                filter.Apply<IDeleteAduitEntity>("IsDeleted", a => a.IsDeleted == false);
            }, GetType().Assembly, typeof(AuditBaseRepository<>).Assembly);

            services.AddIdentityServer()
#if DEBUG
                .AddDeveloperSigningCredential()
#endif
#if !DEBUG
                .AddSigningCredential(new X509Certificate2(
                    Path.Combine(AppContext.BaseDirectory, Configuration["Certificates:Path"]),
                    Configuration["Certificates:Password"])
                )
#endif
                .AddInMemoryIdentityResources(InMemoryConfiguration.GetIdentityResources())
                .AddInMemoryApiResources(InMemoryConfiguration.GetApis())
                .AddInMemoryClients(InMemoryConfiguration.GetClients())
                .AddProfileService<LinCmsProfileService>()
                .AddResourceOwnerValidator<LinCmsResourceOwnerPasswordValidator>();

            #region Swagger
            //Swagger��дPascalCase���ĳ�SnakeCaseģʽ
            services.TryAddEnumerable(ServiceDescriptor
                .Transient<IApiDescriptionProvider, SnakeCaseQueryParametersApiDescriptionProvider>());

            //Register the Swagger generator, defining 1 or more Swagger documents
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo() { Title = "LinCms.IdentityServer4", Version = "v1" });
                var security = new OpenApiSecurityRequirement()
                {
                    { new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference()
                        {
                            Id = "Bearer",
                            Type = ReferenceType.SecurityScheme
                        }
                    }, Array.Empty<string>() }
                };
                options.AddSecurityRequirement(security);//����һ�������ȫ�ְ�ȫ��Ϣ����AddSecurityDefinition����ָ���ķ�������Ҫһ�£�������Bearer��
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT��Ȩ(���ݽ�������ͷ�н��д���) �����ṹ: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",//jwtĬ�ϵĲ�������
                    In = ParameterLocation.Header,//jwtĬ�ϴ��Authorization��Ϣ��λ��(����ͷ��)
                    Type = SecuritySchemeType.ApiKey
                });

                string xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Startup).Assembly.GetName().Name}.xml");
                options.IncludeXmlComments(xmlPath);

            });
            #endregion

            services.AddTransient<IUserIdentityService, UserIdentityService>();
            services.AddTransient<IUserService, UserService>();
            services.AddTransient<ICurrentUser,CurrentUser>();

            services.AddCors();
            services.AddAutoMapper(typeof(UserProfile).Assembly);

            services.AddControllers(options =>
                {
                    options.Filters.Add<LinCmsExceptionFilter>();
                })
                .AddNewtonsoftJson(opt =>
                {
                    //opt.SerializerSettings.DateFormatString = "yyyy-MM-dd HH:MM:ss";
                    //����ʱ�����ʽ
                    opt.SerializerSettings.Converters = new List<JsonConverter>()
                    {
                        new LinCmsTimeConverter()
                    };
                    // �����»��߷�ʽ������ĸ��Сд
                    opt.SerializerSettings.ContractResolver = new DefaultContractResolver()
                    {
                        NamingStrategy = new SnakeCaseNamingStrategy()
                        {
                            ProcessDictionaryKeys = true
                        }
                    };
                })
                .ConfigureApiBehaviorOptions(options =>
                {
                    options.SuppressConsumesConstraintForFormFileParameters = true;
                    //�Զ��� BadRequest ��Ӧ
                    options.InvalidModelStateResponseFactory = context =>
                    {
                        var problemDetails = new ValidationProblemDetails(context.ModelState);

                        var resultDto = new ResultDto(ErrorCode.ParameterError, problemDetails.Errors, context.HttpContext);

                        return new BadRequestObjectResult(resultDto)
                        {
                            ContentTypes = { "application/json" }
                        };
                    };
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseCors(builder =>
            {
                string[] withOrigins = Configuration.GetSection("WithOrigins").Get<string[]>();

                builder.AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithOrigins(withOrigins);
            });
            app.UseIdentityServer();

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            //// Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            //// specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "LinCms");

                //c.RoutePrefix = string.Empty;
                //c.OAuthClientId("demo_api_swagger");//�ͷ�������
                //c.OAuthAppName("Demo API - Swagger-��ʾ"); // ����
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}