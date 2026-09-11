using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class ChatWarmupService : BackgroundService
    {
        private readonly ChatIndexService _indexService;
        private readonly ChatOptions _options;
        private readonly ILogger<ChatWarmupService> _logger;

        public ChatWarmupService(
            ChatIndexService indexService,
            IOptions<ChatOptions> optionsAccessor,
            ILogger<ChatWarmupService> logger)
        {
            _indexService = indexService;
            _options = optionsAccessor.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Chat is disabled; warmup service idle");
                return;
            }

            _logger.LogWarning(
                "Chat warmup starting: Configured={Configured}, EmbeddingsConfigured={Embeddings}, BaseUrlSet={BaseUrl}, ApiKeySet={ApiKey}, ChatDeployment='{Chat}', EmbeddingDeployment='{Embedding}', Source='{Source}', Env={Env}",
                _options.IsConfigured(), _options.IsConfigured() && !string.IsNullOrWhiteSpace(_options.EmbeddingDeployment),
                !string.IsNullOrWhiteSpace(_options.BaseUrl), !string.IsNullOrWhiteSpace(_options.ApiKey),
                _options.ChatDeployment, _options.EmbeddingDeployment, _options.Source, Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));

            var vectorRetryAttempts = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _indexService.EnsureBuiltAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chat index warmup failed; will retry");
                }

                if (_indexService.HasBuilt
                    && (_indexService.HasVectors || !_indexService.EmbeddingsConfigured))
                {
                    break;
                }

                if (_indexService.HasBuilt && !_indexService.HasVectors && _indexService.EmbeddingsConfigured)
                {
                    vectorRetryAttempts++;
                    if (vectorRetryAttempts > 3)
                    {
                        _logger.LogError("Chat index still has no vectors after {Attempts} attempts; continuing in keyword-only mode", vectorRetryAttempts - 1);
                        break;
                    }
                    _logger.LogWarning(
                        "Chat index has no vectors; retrying embedding build (attempt {Attempt} of 3) in 60s",
                        vectorRetryAttempts);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (!_indexService.HasBuilt)
                {
                    continue;
                }
                try
                {
                    await _indexService.EnsureBuiltAsync(forceRebuild: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chat index rebuild attempt failed; will retry");
                    vectorRetryAttempts++;
                    if (vectorRetryAttempts > 3)
                    {
                        _logger.LogError("Giving up on vector rebuild after {Attempts} failures", vectorRetryAttempts - 1);
                        break;
                    }
                }
            }

            var refreshInterval = TimeSpan.FromHours(Math.Max(1, _options.RefreshIntervalHours));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(refreshInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                try
                {
                    await _indexService.RefreshIfChangedAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chat index refresh failed");
                }
            }
        }
    }
}
