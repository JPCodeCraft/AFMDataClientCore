namespace AlbionDataAvalonia.ReferenceData;

public sealed record ReferenceDataOptions
{
    public int ReferenceDataFirstRetryDelaySeconds { get; init; } = 15;
    public int ReferenceDataSecondRetryDelaySeconds { get; init; } = 60;
    public int ReferenceDataRequestTimeoutSeconds { get; init; } = 30;
}
