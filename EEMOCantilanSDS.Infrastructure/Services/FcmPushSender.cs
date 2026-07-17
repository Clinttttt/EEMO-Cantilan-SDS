using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Infrastructure.Services;

/// <summary>
/// FCM HTTP v1 push sender (Firebase Admin SDK). The Firebase credential is read from configuration and the
/// <see cref="FirebaseApp"/> is created once per process. If no credential is configured the sender is a
/// safe no-op (returns 0, logs once) — so the API runs unchanged in any environment where push isn't set up,
/// and Cantilan is never affected.
///
/// <para>Credential resolution order (all optional): <c>Firebase:ServiceAccountJsonBase64</c> (preferred for
/// Azure app settings — no quoting/newline issues), <c>Firebase:ServiceAccountJson</c> (raw JSON), then
/// <c>Firebase:ServiceAccountPath</c> (a file path, convenient for local dev). The credential is a secret and
/// is never logged.</para>
/// </summary>
public sealed class FcmPushSender(
    ICollectorDeviceTokenRepository tokenRepository,
    IConfiguration configuration,
    ILogger<FcmPushSender> logger) : IPushSender
{
    private static readonly object InitLock = new();
    private static bool _initAttempted;
    private static bool _available;

    public async Task<int> SendToCollectorAsync(
        Guid collectorId,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        if (!EnsureInitialized())
        {
            return 0;
        }

        var tokens = await tokenRepository.GetByCollectorAsync(collectorId, ct);
        if (tokens.Count == 0)
        {
            return 0;
        }

        var message = new MulticastMessage
        {
            Tokens = tokens.Select(t => t.Token).ToList(),
            Notification = new Notification { Title = title, Body = body },
            Data = data?.ToDictionary(kv => kv.Key, kv => kv.Value),
            Android = new AndroidConfig { Priority = Priority.High }
        };

        BatchResponse response;
        try
        {
            response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FCM send failed for collector {CollectorId}.", collectorId);
            return 0;
        }

        // Prune tokens FCM reports as permanently invalid so we stop pushing to dead devices.
        for (var i = 0; i < response.Responses.Count; i++)
        {
            var r = response.Responses[i];
            if (!r.IsSuccess && IsPermanentlyInvalid(r.Exception))
            {
                try { await tokenRepository.RemoveByTokenAsync(tokens[i].Token, ct); }
                catch { /* best-effort cleanup */ }
            }
        }

        return response.SuccessCount;
    }

    private static bool IsPermanentlyInvalid(FirebaseMessagingException? ex) =>
        ex?.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch;

    private bool EnsureInitialized()
    {
        if (_initAttempted)
        {
            return _available;
        }

        lock (InitLock)
        {
            if (_initAttempted)
            {
                return _available;
            }

            _initAttempted = true;
            try
            {
                var json = ResolveCredentialJson();
                if (string.IsNullOrWhiteSpace(json))
                {
                    logger.LogWarning("FCM push disabled: no Firebase credential configured (Firebase:ServiceAccountJsonBase64 / Json / Path).");
                    _available = false;
                    return false;
                }

                if (FirebaseApp.DefaultInstance is null)
                {
                    FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(json) });
                }

                _available = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FCM push initialization failed; push is disabled for this process.");
                _available = false;
            }

            return _available;
        }
    }

    private string? ResolveCredentialJson()
    {
        var base64 = configuration["Firebase:ServiceAccountJsonBase64"];
        if (!string.IsNullOrWhiteSpace(base64))
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64.Trim()));
        }

        var json = configuration["Firebase:ServiceAccountJson"];
        if (!string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        var path = configuration["Firebase:ServiceAccountPath"];
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        return null;
    }
}
