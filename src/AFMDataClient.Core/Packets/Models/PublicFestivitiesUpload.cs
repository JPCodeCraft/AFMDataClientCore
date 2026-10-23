using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

/// <summary>The AODP festivities.ingest message, in the shape the AODP data client sends: game ticks, not dates.</summary>
public class PublicFestivitiesUpload : BaseUpload
{
    public List<PublicFestivity> Events { get; set; } = [];
}

public class PublicFestivity
{
    public byte Kind { get; set; }
    public string Category { get; set; } = string.Empty;
    public string UniqueName { get; set; } = string.Empty;
    public long StartTime { get; set; }
    public long EndTime { get; set; }
}
