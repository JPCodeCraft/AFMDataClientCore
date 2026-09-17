namespace Albion.Network
{
    public class ReceiverBuilder
    {
        private readonly AlbionParser parser;

        public ReceiverBuilder()
        {
            parser = new AlbionParser();
        }

        public static ReceiverBuilder Create()
        {
            return new ReceiverBuilder();
        }

        public ReceiverBuilder AddHandler<TPacket>(PacketHandler<TPacket> handler)
        {
            parser.AddHandler(handler);

            return this;
        }

        public ReceiverBuilder AddEventHandler<TEvent>(EventPacketHandler<TEvent> handler) where TEvent : BaseEvent
        {
            AddHandler(handler);

            return this;
        }

        public ReceiverBuilder AddRequestHandler<TOperation>(RequestPacketHandler<TOperation> handler) where TOperation : BaseOperation
        {
            AddHandler(handler);

            return this;
        }

        public ReceiverBuilder AddResponseHandler<TOperation>(ResponsePacketHandler<TOperation> handler) where TOperation : BaseOperation
        {
            AddHandler(handler);

            return this;
        }

        public ReceiverBuilder ObservePackets(Action<object> observer)
        {
            parser.PacketObserved += observer;
            return this;
        }

        public ReceiverBuilder SubscribeEvent<T>(int code, Func<T, Task> action, int priority = 0) where T : BaseEvent
            => AddEventHandler(new EventSubscription<T>(code, action) { Priority = priority });

        public ReceiverBuilder SubscribeRequest<T>(int code, Func<T, Task> action, int priority = 0) where T : BaseOperation
            => AddRequestHandler(new RequestSubscription<T>(code, action) { Priority = priority });

        public ReceiverBuilder SubscribeResponse<T>(int code, Func<T, Task> action, int priority = 0) where T : BaseOperation
            => AddResponseHandler(new ResponseSubscription<T>(code, action) { Priority = priority });

        private sealed class EventSubscription<T>(int code, Func<T, Task> action) : EventPacketHandler<T>(code) where T : BaseEvent
        {
            protected override Task OnActionAsync(T value) => action(value);
        }

        private sealed class RequestSubscription<T>(int code, Func<T, Task> action) : RequestPacketHandler<T>(code) where T : BaseOperation
        {
            protected override Task OnActionAsync(T value) => action(value);
        }

        private sealed class ResponseSubscription<T>(int code, Func<T, Task> action) : ResponsePacketHandler<T>(code) where T : BaseOperation
        {
            protected override Task OnActionAsync(T value) => action(value);
        }

        public IPhotonReceiver Build()
        {
            return parser;
        }
    }
}
