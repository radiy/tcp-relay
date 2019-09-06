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
        public TaskCompletionSource<Socket> Rendezvous;

        public RelayItem(Socket first)
        {
            First = first;
            Rendezvous = new TaskCompletionSource<Socket>();
        }

        public RelayItem(Socket first, Socket second, TaskCompletionSource<Socket> rendezvous) : this(first)
        {
            Second = second;
            Rendezvous = rendezvous;
        }
    }

    public class Program
    {
        public static ConcurrentDictionary<string, RelayItem> connections
            = new ConcurrentDictionary<string, RelayItem>();

        static async Task Main(string[] args)
        {
            CancellationToken token = default;
            var port = 10000;

            await Start(port, token);
        }

        public static async Task Start(int port, CancellationToken token)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            var endPoint = new IPEndPoint(IPAddress.Loopback, port);
            socket.Bind(endPoint);
            socket.Listen(10);
            token.Register(() => socket.Close());

            var tasks = new List<Task>();
            while (!token.IsCancellationRequested)
            {
                try {
                    var incomingSocket = await socket.AcceptAsync();
                    tasks.Add(HandleSocket(incomingSocket, token));
                }
                catch (SocketException e)
                {
                    if (token.IsCancellationRequested && e.SocketErrorCode == SocketError.OperationAborted)
                        break;
                    throw;
                }
            }
            await Task.WhenAll(tasks);
        }

        public static async Task HandleSocket(Socket socket, CancellationToken token)
        {
            using (socket) {
                //ReceiveAsync abort
                token.Register(() => socket.Close());
                var buffer = new byte[10_000];
                var readed = await socket.ReceiveAsync(buffer, SocketFlags.None, token);
                if (readed != 16)
                {
                    return;
                }
                var key = new Guid(buffer.AsSpan(0, 16)).ToString();
                var created = new RelayItem(socket);
                var current = connections.GetOrAdd(key, created);
                Socket target = null;
                if (current != created)
                {
                    if (!connections.TryUpdate(key, new RelayItem(current.First, socket, current.Rendezvous), current))
                    {
                        current.Rendezvous.SetException(new Exception("Internal state corruption"));
                        return;
                    }
                    current.Rendezvous.SetResult(socket);
                    target = current.First;
                }
                else
                {
                    //todo cancellation
                    target = await created.Rendezvous.Task;
                }

                try {
                    while (!token.IsCancellationRequested)
                    {
                        //https://github.com/dotnet/corefx/pull/36516 not working in 2.2 check 3.0
                        //cancellation not working, wat?
                        readed = await socket.ReceiveAsync(buffer, SocketFlags.None, token);
                        Console.WriteLine($"{socket.RemoteEndPoint} reader {readed}bytes");
                        await target.SendAsync(new ArraySegment<byte>(buffer, 0, readed), SocketFlags.None, token);
                    }
                }
                catch (SocketException e)
                {
                    if (token.IsCancellationRequested && e.SocketErrorCode == SocketError.OperationAborted)
                        return;
                    throw;
                }
            }
        }
    }
}
