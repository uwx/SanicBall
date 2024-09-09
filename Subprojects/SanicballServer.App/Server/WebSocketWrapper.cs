using Priority_Queue;
using SanicballCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SanicballServer
{
    public class WebSocketWrapper
    {
        private struct SocketMessage
        {
            public byte[] data;
            public MessageTypes type;
        }

        private readonly WebSocket _socket;
        private readonly CancellationTokenSource _source;
        private readonly CancellationToken _token;
        private readonly ConcurrentQueue<MessageWrapper> _socketReceiveQueue;
        private readonly SimplePriorityQueue<SocketMessage> _socketSendQueue;
        private const int BUFFER_SIZE = 8 * 1024;
        private Task _socketQueueManager;
        private bool _started;

        public Guid Id { get; private set; }
        public long HeartbeatTimestamp { get; internal set; }
        public int TicksSinceLastMessage { get; internal set; }
        public double Ping { get; internal set; }

        public WebSocketWrapper(WebSocket socket, CancellationToken token)
        {
            _socket = socket;
            _source = CancellationTokenSource.CreateLinkedTokenSource(token);
            _token = _source.Token;
            _socketReceiveQueue = new ConcurrentQueue<MessageWrapper>();
            _socketSendQueue = new SimplePriorityQueue<SocketMessage>();

            Id = Guid.NewGuid();
        }


        public bool Dequeue(out MessageWrapper message)
        {
            return _socketReceiveQueue.TryDequeue(out message);
        }

        public void Send(MessageTypes type, byte[] data, float priority = 0)
        {
            using (var stream = new MemoryStream())
            {
                stream.Write(new[] { (byte)type }, 0, 1);
                stream.Write(data, 0, data.Length);
                _socketSendQueue.Enqueue(new SocketMessage() { data = stream.ToArray(), type = type }, -priority);
            }

            if (_socketQueueManager == null || _socketQueueManager.IsCompleted)
                _socketQueueManager = Task.Run(SendLoop);
        }

        public void Send(MessageWrapper wrapper, float priority = 0)
        {
            _socketSendQueue.Enqueue(new SocketMessage() { data = wrapper.GetBytes(), type = wrapper.Type }, -priority);

            if (_socketQueueManager == null || _socketQueueManager.IsCompleted)
                _socketQueueManager = Task.Run(SendLoop);
        }

        public void Send(MessageTypes type, BinaryWriter writer, MemoryStream dest)
        {
            writer.Flush();
            Send(type, dest.ToArray());
        }

        public async Task ReceiveLoop()
        {
            _started = true;
            var buff = new byte[BUFFER_SIZE];
            var buffseg = new ArraySegment<byte>(buff);

            byte[] resultbuff = null;
            WebSocketReceiveResult result = null;

            try
            {
                while (!_token.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();

                    do
                    {
                        result = await _socket.ReceiveAsync(buffseg, _token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            var wrapper = new MessageWrapper(MessageTypes.Disconnect);
                            wrapper.Writer.Write(result.CloseStatusDescription ?? "Client disconnected");
                            wrapper.Source = Id;
                            _socketReceiveQueue.Enqueue(wrapper);
                            return;
                        }
                        else
                            ms.Write(buff, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    resultbuff = ms.ToArray();
                    _socketReceiveQueue.Enqueue(new MessageWrapper(resultbuff) { Source = Id });
                    TicksSinceLastMessage = 0;
                }

            }
            catch (Exception)
            {
            }

            try
            {
                await DisconnectAsync();
            }
            catch { }
        }

        internal async Task SendLoop()
        {
            try
            {
                while (_socket.State == WebSocketState.Open)
                {
                    if (_socketSendQueue.Count == 0)
                        break;

                    if (!_socketSendQueue.TryDequeue(out var message))
                        break;

                    if (_token.IsCancellationRequested)
                        return;

                    Debug.WriteLine(message.type);

                    await _socket.SendAsync(message.data, WebSocketMessageType.Binary, true, _token);
                }
            }
            catch (Exception)
            {
                try
                {
                    await DisconnectAsync();
                }
                catch { }
            }

            _socketQueueManager = null;
        }

        public async Task DisconnectAsync(string reason = "Abnormal Shutdown")
        {
            if (!_source.IsCancellationRequested)
                _source.Cancel();

            using var wrapper = new MessageWrapper(MessageTypes.Disconnect);
            wrapper.Writer.Write(reason);
            var data = wrapper.GetBytes();

            try { await _socket.SendAsync(data, WebSocketMessageType.Binary, true, default); } catch { }
            try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, default); } catch { }
        }

    }
}
