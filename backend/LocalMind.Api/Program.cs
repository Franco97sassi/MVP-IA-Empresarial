using LocalMind.Api.Data;
using LocalMind.Api.Middleware;
using LocalMind.Api.Options;
using LocalMind.Api.Services.Ai;
using LocalMind.Api.Services.Auth;
using LocalMind.Api.Services.Chat;
using LocalMind.Api.Services.FineTuning;
using LocalMind.Api.Services.Mcp;
using LocalMind.Api.Services.Metrics;
using LocalMind.Api.Services.Multimodal;
using LocalMind.Api.Services.Observability;
using LocalMind.Api.Services.Prompts;
using LocalMind.Api.Services.Tokens;
using LocalMind.Api.Services.Orchestration;
using LocalMind.Api.Services.Rag;
using LocalMind.Api.Services.Security;
using LocalMind.Api.Services.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LocalMind.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers(options =>
        {
            options.Filters.Add(
                new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(10 * 1024 * 1024)
            );
        });

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "LocalMind AI API",
                Version = "v1",
                Description = "API para autenticación, chat local con Ollama, documentos RAG, tools y métricas."
            });

            options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Name = "Authorization",
                Description = "Pegá solo el token JWT, sin Bearer."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        builder.Services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite(
                builder.Configuration.GetConnectionString("DefaultConnection")
            );
        });

        builder.Services.AddScoped<IJwtService, JwtService>();
        builder.Services.AddMemoryCache(options => options.SizeLimit = 250_000);
        builder.Services.AddScoped<IChatService, ChatService>();
        builder.Services.AddScoped<IToolIntentDetector, ToolIntentDetector>();
        builder.Services.AddScoped<IAiToolService, AiToolService>();
        builder.Services.AddSingleton<IToolDefinitionRegistry, ToolDefinitionRegistry>();
        builder.Services.Configure<RateLimitOptions>(
            builder.Configuration.GetSection("RateLimit")
        );

        builder.Services.Configure<RagOptions>(
            builder.Configuration.GetSection("Rag")
        );

        builder.Services.AddScoped<IRagService, RagService>();
        builder.Services.AddScoped<IDocumentTextExtractor, DocumentTextExtractor>();
        builder.Services.AddScoped<ITextChunker, TextChunker>();
        builder.Services.AddScoped<IEmbeddingSerializer, EmbeddingSerializer>();
        builder.Services.AddScoped<IEmbeddingCacheService, EmbeddingCacheService>();
        builder.Services.AddScoped<LocalVectorStore>();
        builder.Services.AddScoped<IVectorStoreResolver, VectorStoreResolver>();
        builder.Services.AddHttpClient<QdrantVectorStore>(client =>
        {
            client.BaseAddress = new Uri(
                builder.Configuration["Rag:QdrantUrl"]
                ?? "http://localhost:6333");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        builder.Services.AddScoped<IMetricsService, MetricsService>();
        builder.Services.AddSingleton<IPromptTemplateService, PromptTemplateService>();
        builder.Services.AddSingleton<ITokenBudgetService, TokenBudgetService>();
        builder.Services.AddSingleton<IAiTelemetryService, AiTelemetryService>();
        builder.Services.Configure<ChatSecurityOptions>(
            builder.Configuration.GetSection("Security:Chat")
        );

        builder.Services.AddScoped<IInputSafetyService, InputSafetyService>();
        builder.Services.AddScoped<IMcpHostService, McpHostService>();
        builder.Services.AddScoped<IMcpServer, DocumentSearchMcpServer>();
        builder.Services.AddScoped<IMcpServer, UserMetricsMcpServer>();
        builder.Services.AddScoped<IMcpServer, TaskExtractorMcpServer>();
        builder.Services.AddScoped<IMultiAgentOrchestrator, MultiAgentOrchestrator>();
        builder.Services.AddScoped<IMultimodalService, MultimodalService>();
        builder.Services.AddSingleton<IFineTuningService, FineTuningService>();

        builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
        {
            client.BaseAddress = new Uri(
                builder.Configuration["Ollama:BaseUrl"]
                ?? "http://localhost:11434"
            );

            var timeoutSeconds =
                int.TryParse(
                    builder.Configuration["Ollama:RequestTimeoutSeconds"],
                    out var seconds
                )
                ? seconds
                : 300;

            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("LocalMind.Api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddHttpClientInstrumentation()
                .AddConsoleExporter());
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                policy
                    .WithOrigins(
                        "https://mvp-ia-empresarial.vercel.app",
                        "http://localhost:5173",
                        "http://localhost:3000"
                    )
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        var jwtKey =
            builder.Configuration["Jwt:Key"]
            ?? Environment.GetEnvironmentVariable("JWT__KEY")
            ?? string.Empty;

        if (jwtKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key debe tener al menos 32 caracteres."
            );
        }

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(jwtKey)
                        )
                    };
            });

        builder.Services.AddAuthorization();

        var app = builder.Build();

        var applyMigrationsOnStartup =
            !bool.TryParse(
                app.Configuration["Database:ApplyMigrationsOnStartup"],
                out var shouldApplyMigrations
            )
            || shouldApplyMigrations;

        if (applyMigrationsOnStartup)
        {
            using var scope = app.Services.CreateScope();

            var dbContext =
                scope.ServiceProvider.GetRequiredService<AppDbContext>();

            dbContext.Database.Migrate();
        }

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseRouting();

        app.UseCors("Frontend");

        app.UseMiddleware<ErrorHandlingMiddleware>();

        // Importante: auth antes de middlewares
        // que dependen del usuario autenticado.
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseMiddleware<UserRateLimitMiddleware>();
        app.UseMiddleware<AuditMiddleware>();

        app.MapGet("/version", () => "VERSION NUEVA");

        app.MapControllers();

        app.Run();
    }
}