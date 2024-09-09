using System.Collections.Concurrent;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SanicballCore.Server;
using System.Collections.Generic;
using System.Linq;

namespace SanicballServer.App.Services
{
    public class SanicballRoomsService(ILoggerFactory loggerFactory, IHostApplicationLifetime applicationLifetime) : IHostedService, ISanicballRoomsService
    {
        private readonly ConcurrentBag<Task> _tasks = new ConcurrentBag<Task>();
        private readonly ConcurrentDictionary<Guid, Room> _servers = new ConcurrentDictionary<Guid, Room>();
        private readonly ILoggerFactory _loggerFactory = loggerFactory;

        private CancellationTokenSource _cts;

        public IReadOnlyDictionary<Guid, Room> Rooms =>
            _servers;

        public Task<Room> CreateRoomAsync(RoomConfig config)
        {
            var id = Guid.NewGuid();
            var room = new Room(id, config, _loggerFactory.CreateLogger("Server_" + id));

            _tasks.Add(Task.Run(async () => await room.StartAsync(_cts.Token))
                .ContinueWith(t => _servers.TryRemove(id, out _)));

            _servers.TryAdd(id, room);
            return Task.FromResult(room);
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, applicationLifetime.ApplicationStopping);
            _cts.Token.Register(async () => await CleanupAsync(), true);
            return Task.CompletedTask;
        }

        async Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            if (_cts == null) return;

            try
            {
                _cts.Cancel();
            }
            finally
            {
                await Task.WhenAll(_tasks).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        private async Task CleanupAsync()
        {
            await Task.WhenAll(_servers.Values.Select(async s => await s.DisposeAsync()))
                    .WaitAsync(TimeSpan.FromSeconds(5000));
        }
    }
}
