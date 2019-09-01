using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace app
{
    public class RelayItem
    {

        public Socket First;
        public Socket Second;
        public TaskCompletionSource<object> Rendezvous;

        public RelayItem(Socket first)
        {
            First = first;
            Rendezvous = new TaskCompletionSource<object>();
        }

        public RelayItem(Socket first, Socket second, TaskCompletionSource<object> rendezvous) : this(first)
        {
            Second = second;
            Rendezvous = rendezvous;
        }
    }

    class Program
    {
        public static ConcurrentDictionary<string, RelayItem> connections
            = new ConcurrentDictionary<string, RelayItem>();

        static async Task Main(string[] args)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            var endPoint = new IPEndPoint(IPAddress.Loopback, 10000);
            socket.Bind(endPoint);
            socket.Listen(10);

            var tasks = new List<Task>();
            CancellationToken token = default;
            while(!token.IsCancellationRequested) {
                var incomingSocket = await socket.AcceptAsync();
                tasks.Add(HandleSocket(incomingSocket, token));
            }
            await Task.WhenAll(tasks);
        }

        public static async Task HandleSocket(Socket socket, CancellationToken token)
        {
            using (socket) {
                var buffer = new byte[10_000];
                var readed = await socket.ReceiveAsync(buffer, SocketFlags.None, token);
                if (readed != 16)
                {
                    return;
                }
                var key = new Guid(buffer.AsSpan(0, 16)).ToString();
                var created = new RelayItem(socket);
                var current = connections.GetOrAdd(key, created);
                if (current != default)
                {
                    if (!connections.TryUpdate(key, new RelayItem(current.First, socket, current.Rendezvous), current))
                    {
                        current.Rendezvous.SetResult(new Exception("Internal state corruption"));
                        return;
                    }
                    current.Rendezvous.SetResult(new object());
                }
                var target = current?.First;
                if (target == null)
                {
                    await created.Rendezvous.Task;
                }
                while(!token.IsCancellationRequested)
                {
                    readed = await socket.ReceiveAsync(buffer, SocketFlags.None, token);
                    await target.SendAsync(new ArraySegment<byte>(buffer, 0, readed), SocketFlags.None, token);
                }
            }
        }
    }
}
