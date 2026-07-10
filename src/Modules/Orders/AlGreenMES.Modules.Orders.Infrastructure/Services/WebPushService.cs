using System.Net;
using System.Text.Json;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlGreenMES.Modules.Orders.Infrastructure.Services;

public class WebPushService : IWebPushService
{
    private readonly PushServiceClient _client;
    private readonly WebPushSettings _settings;
    private readonly IPushSubscriptionRepository _subscriptionRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly ILogger<WebPushService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WebPushService(
        IOptions<WebPushSettings> settings,
        IPushSubscriptionRepository subscriptionRepository,
        IOrdersUnitOfWork unitOfWork,
        ILogger<WebPushService> logger)
    {
        _settings = settings.Value;
        _subscriptionRepository = subscriptionRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;

        _client = new PushServiceClient();
        _logger.LogInformation("[WebPush] Initializing — Enabled={Enabled}, HasPublicKey={HasKey}, Subject={Subject}",
            _settings.Enabled, !string.IsNullOrEmpty(_settings.VapidPublicKey), _settings.VapidSubject);

        if (_settings.Enabled && !string.IsNullOrEmpty(_settings.VapidPublicKey))
        {
            _client.DefaultAuthentication = new VapidAuthentication(
                _settings.VapidPublicKey,
                _settings.VapidPrivateKey)
            {
                Subject = _settings.VapidSubject
            };
            _logger.LogInformation("[WebPush] VAPID authentication configured");
        }
        else
        {
            _logger.LogWarning("[WebPush] VAPID authentication NOT configured (Enabled={Enabled})", _settings.Enabled);
        }
    }

    public string GetVapidPublicKey() => _settings.VapidPublicKey;

    public async Task SubscribeAsync(Guid tenantId, Guid userId, string endpoint, string p256dhKey, string authKey, CancellationToken cancellationToken = default)
    {
        var existing = await _subscriptionRepository.FindByEndpointActiveAsync(endpoint, cancellationToken);
        if (existing != null)
        {
            existing.UpdateKeys(p256dhKey, authKey);
        }
        else
        {
            var subscription = Domain.Entities.PushSubscription.Create(tenantId, userId, endpoint, p256dhKey, authKey);
            await _subscriptionRepository.AddAsync(subscription, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UnsubscribeAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.FindByEndpointActiveAsync(endpoint, cancellationToken);
        // Only the OWNER may deactivate a subscription — otherwise a caller who
        // learns another user's endpoint could silently kill their push.
        if (subscription != null && subscription.UserId == userId)
        {
            subscription.Deactivate();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SendToUserAsync(Guid userId, string title, string body, object? data = null, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogWarning("[WebPush] Push is DISABLED, skipping SendToUserAsync");
            return;
        }

        var subscriptions = await _subscriptionRepository.GetByUserIdActiveAsync(userId, cancellationToken);
        _logger.LogInformation("[WebPush] SendToUserAsync for user {UserId}, found {Count} subscriptions", userId, subscriptions.Count);
        await SendToSubscriptionsAsync(subscriptions, title, body, data, cancellationToken);
    }

    public async Task SendToUsersAsync(IEnumerable<Guid> userIds, string title, string body, object? data = null, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogWarning("[WebPush] Push is DISABLED, skipping SendToUsersAsync");
            return;
        }

        var subscriptions = await _subscriptionRepository.GetByUserIdsActiveAsync(userIds, cancellationToken);
        _logger.LogInformation("[WebPush] SendToUsersAsync, found {Count} subscriptions", subscriptions.Count);
        await SendToSubscriptionsAsync(subscriptions, title, body, data, cancellationToken);
    }

    public async Task SendToTenantAsync(Guid tenantId, string title, string body, object? data = null, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogWarning("[WebPush] Push is DISABLED, skipping SendToTenantAsync for tenant {TenantId}", tenantId);
            return;
        }

        _logger.LogInformation("[WebPush] SendToTenantAsync for tenant {TenantId}, title: {Title}", tenantId, title);
        var subscriptions = await _subscriptionRepository.GetByTenantIdActiveAsync(tenantId, cancellationToken);
        _logger.LogInformation("[WebPush] Found {Count} active subscriptions for tenant {TenantId}", subscriptions.Count, tenantId);
        await SendToSubscriptionsAsync(subscriptions, title, body, data, cancellationToken);
    }

    private async Task SendToSubscriptionsAsync(IReadOnlyList<Domain.Entities.PushSubscription> subscriptions, string title, string body, object? data, CancellationToken cancellationToken)
    {
        if (subscriptions.Count == 0)
        {
            _logger.LogWarning("[WebPush] No active subscriptions found, nothing to send");
            return;
        }

        var payload = JsonSerializer.Serialize(new { title, body, data }, _jsonOptions);
        _logger.LogInformation("[WebPush] Sending to {Count} subscriptions, payload: {Payload}", subscriptions.Count, payload);
        var needsSave = false;

        foreach (var sub in subscriptions)
        {
            try
            {
                var pushSubscription = new Lib.Net.Http.WebPush.PushSubscription
                {
                    Endpoint = sub.Endpoint,
                    Keys = new Dictionary<string, string>
                    {
                        ["p256dh"] = sub.P256dhKey,
                        ["auth"] = sub.AuthKey
                    }
                };

                var message = new PushMessage(payload)
                {
                    Urgency = PushMessageUrgency.High
                };

                _logger.LogInformation("[WebPush] Sending to user {UserId}, endpoint: {Endpoint}",
                    sub.UserId, sub.Endpoint[..Math.Min(80, sub.Endpoint.Length)]);

                await _client.RequestPushMessageDeliveryAsync(pushSubscription, message, cancellationToken);
                _logger.LogInformation("[WebPush] Successfully sent to user {UserId}", sub.UserId);
            }
            catch (PushServiceClientException ex) when (ex.StatusCode == HttpStatusCode.Gone || ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("[WebPush] Subscription expired ({StatusCode}) for user {UserId}, endpoint {Endpoint}",
                    ex.StatusCode, sub.UserId, sub.Endpoint[..Math.Min(80, sub.Endpoint.Length)]);
                sub.Deactivate();
                needsSave = true;
            }
            catch (PushServiceClientException ex)
            {
                _logger.LogError(ex, "[WebPush] PushServiceClientException for user {UserId}: StatusCode={StatusCode}, Headers={Headers}",
                    sub.UserId, ex.StatusCode, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WebPush] Failed to send to user {UserId}, endpoint {Endpoint}",
                    sub.UserId, sub.Endpoint[..Math.Min(80, sub.Endpoint.Length)]);
            }
        }

        if (needsSave)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
