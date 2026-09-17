using System;
using System.Threading.Tasks;

namespace Albion.Network
{
    public abstract class ResponsePacketHandler<TOperation> : PacketHandler<ResponsePacket> where TOperation : BaseOperation
    {
        private readonly int operationCode;

        public ResponsePacketHandler(int operationCode)
        {
            this.operationCode = operationCode;
        }

        protected abstract Task OnActionAsync(TOperation value);

        protected internal override Task OnHandleAsync(ResponsePacket packet)
        {
            if (operationCode != packet.OperationCode)
            {
                return Task.CompletedTask;
            }
            else
            {
                TOperation instance = packet.GetDecoded<TOperation>(packet.Parameters);

                instance.ReturnCode = packet.ReturnCode;
                return OnActionAsync(instance);
            }
        }
    }
}
