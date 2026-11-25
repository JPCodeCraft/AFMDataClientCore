using Albion.Network;
using Serilog;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

// The reply to AuctionSellRequest; ReturnCode tells whether the sale went through.
public class AuctionSellRequestResponse : BaseOperation
{
    public AuctionSellRequestResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
    }
}
