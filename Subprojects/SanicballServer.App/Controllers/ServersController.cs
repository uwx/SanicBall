using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SanicballCore.Server;
using SanicballServer.App.Model;
using SanicballServer.App.Services;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace SanicballServer.App.Controllers
{
    [Route("api/[controller]")]
    public class ServersController(ISanicballRoomsService roomsService,
          IHostApplicationLifetime lifetime) : Controller
    {

        // GET: api/<controller>
        [HttpGet]
        public IEnumerable<object> Get()
        {
            return roomsService.Rooms
                .Where(s => s.Value.Config.ShowOnList)
                .Select(s => new RoomInfo(s.Key.ToString(), s.Value));
        }

        // GET api/<controller>/5
        [HttpGet("{id}")]
        public async Task Get(Guid id, CancellationToken ct)
        {
            await Task.Yield();

            if (HttpContext.WebSockets.IsWebSocketRequest && roomsService.Rooms.TryGetValue(id, out var server))
            {
                var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                await server.ConnectClientAsync(socket, ct);
            }
            else
            {
                throw new InvalidOperationException();
            }
        }

        // POST api/<controller>
        [HttpPost]
        public async Task<string> Post([FromBody] string name)
        {
            var server = await roomsService.CreateRoomAsync(new RoomConfig() { MaxPlayers = 8, ServerName = name });
            return server.Id.ToString();
        }

        // DELETE api/<controller>/5
        [HttpDelete("{id}")]
        public void Delete(string id)
        {
        }
    }
}
