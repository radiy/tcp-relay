using app;
using NUnit.Framework;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Tests
{
    public class RelayFixture
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Relay()
        {
            var cancel = new CancellationTokenSource();
            var port = 10_345;
            var task = Program.Start(port, cancel.Token);
            var endPoint = new IPEndPoint(IPAddress.Loopback, port);

            var routeToken = Guid.NewGuid().ToByteArray();
            var leftSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            leftSocket.Connect(endPoint);
            Assert.AreEqual(routeToken.Length, leftSocket.Send(routeToken));

            var rightSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            rightSocket.Connect(endPoint);
            Assert.AreEqual(routeToken.Length, rightSocket.Send(routeToken));

            leftSocket.Send(new byte[] { 1, 2, 3 });
            var buffer = new byte[100];
            Assert.AreEqual(3, rightSocket.Receive(buffer));
            Assert.AreEqual("1, 2, 3", String.Join(", ", buffer.Take(3)));

            rightSocket.Send(new byte[] { 4, 5, 6 });
            Assert.AreEqual(3, leftSocket.Receive(buffer));
            Assert.AreEqual("4, 5, 6", String.Join(", ", buffer.Take(3)));

            cancel.Cancel();
            Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(5)));
        }
    }
}