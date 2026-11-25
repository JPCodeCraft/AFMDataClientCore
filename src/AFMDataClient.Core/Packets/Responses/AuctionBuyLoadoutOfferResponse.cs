using Albion.Network;
using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Responses;

// The reply to a loadout purchase. Its layout has not been recorded yet. When key 0 holds
// numbers, as the Quick Sell reply does, they are kept as the offers bought; Shape names the
// keys and types so a debug log shows what the game sent.
public class AuctionBuyLoadoutOfferResponse : BaseOperation
{
    public long[]? boughtOfferIds;
    public string Shape = string.Empty;

    public AuctionBuyLoadoutOfferResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            Shape = string.Join("; ", parameters
                .Where(parameter => parameter.Key != 253)
                .OrderBy(parameter => parameter.Key)
                .Select(parameter => parameter.Value switch
                {
                    null => $"{parameter.Key}:null",
                    string => $"{parameter.Key}:String",
                    ICollection items => $"{parameter.Key}:{parameter.Value.GetType().Name}[{items.Count}]",
                    _ => $"{parameter.Key}:{parameter.Value.GetType().Name}",
                }));

            if (parameters.TryGetValue(0, out object? ids) && ids is IEnumerable values and not string
                && values.Cast<object?>().All(value => value is byte or short or int or long))
            {
                boughtOfferIds = ids.ToLongArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
