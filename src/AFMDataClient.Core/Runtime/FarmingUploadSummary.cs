namespace AFMDataClient.Core;

public sealed record FarmingUploadSummary(IReadOnlyList<int> ServerIds, int Islands, int Objects, int Pickups, int Actions = 0);
