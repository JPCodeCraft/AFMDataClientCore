using System.Net.Http.Json;
using AlbionDataAvalonia.Network.Models;
using Serilog;

namespace AFMDataClient.Core;

/// <summary>On-demand AFM sharing management; constructing this client starts no worker or network request.</summary>
public sealed class PrivateOrderSharingClient(ClientCore core)
{
    public Task<PrivateOrderSharesResponse?> GetAsync(CancellationToken cancellationToken = default)
        => SendAsync<PrivateOrderSharesResponse>(HttpMethod.Get, null, cancellationToken);

    public Task<SavePrivateOrderSharesResponse?> SaveAsync(IEnumerable<string> sharedUsers, CancellationToken cancellationToken = default)
        => SendAsync<SavePrivateOrderSharesResponse>(HttpMethod.Put, new PrivateOrderSharesRequest { SharedUsers = sharedUsers.ToList() }, cancellationToken);

    private async Task<T?> SendAsync<T>(HttpMethod method, object? payload, CancellationToken cancellationToken)
    {
        if (core.Auth.AccountId is not { Length: > 0 } accountId) return default;
        try
        {
            using var response = await core.Uploads.SendAfmAsync(method, "privateOrderShares", payload, accountId, cancellationToken).ConfigureAwait(false);
            if (response is null || !response.IsSuccessStatusCode) return default;
            return await response.Content.ReadFromJsonAsync<T>(UploadCoordinator.AfmJson, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Warning(exception, "Unable to update private-order sharing settings");
            return default;
        }
    }
}
