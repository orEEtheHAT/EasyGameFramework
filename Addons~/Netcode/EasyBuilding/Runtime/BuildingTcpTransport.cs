using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace EasyBuilding.Networking.Sample
{
    [Serializable] internal sealed class Packet
    {
        public bool HasCommand, HasResult, HasSnapshot;
        public long KnownRevision = -1;
        public BuildingCommand Command;
        public BuildingCommandResult Result;
        public BuildingState Snapshot;
        public List<BuildingChangeSet> Deltas = new List<BuildingChangeSet>();
    }
    /// <summary>Bounded, framed, nonblocking TCP sample. All callbacks and Unity APIs run on the calling main thread.</summary>
    internal sealed class FramedPeer : IDisposable
    {
        internal const int MaximumFrameBytes = 4 * 1024 * 1024;
        private readonly Socket socket;
        private readonly byte[] header = new byte[4];
        private readonly Queue<byte[]> sends = new Queue<byte[]>();
        private byte[] body;
        private int headerRead, bodyRead, sendOffset, queuedBytes;
        internal bool Closed { get; private set; }
        internal double LastReceived { get; private set; }
        internal FramedPeer(Socket socket) { this.socket = socket; socket.Blocking = false; socket.NoDelay = true; LastReceived = Time.realtimeSinceStartupAsDouble; }
        internal void Send(Packet packet)
        {
            if (Closed) throw new InvalidOperationException("The connection is closed.");
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
            if (payload.Length > MaximumFrameBytes || queuedBytes + payload.Length + 4 > MaximumFrameBytes * 2 || sends.Count >= 32)
                throw new InvalidOperationException("Transport frame/queue limit exceeded. Send a smaller interest snapshot.");
            var frame = new byte[payload.Length + 4]; int length = payload.Length;
            frame[0] = (byte)length; frame[1] = (byte)(length >> 8); frame[2] = (byte)(length >> 16); frame[3] = (byte)(length >> 24);
            Buffer.BlockCopy(payload, 0, frame, 4, length); sends.Enqueue(frame); queuedBytes += frame.Length;
        }
        internal void Tick(Action<Packet> receive, int byteBudget = 262144, int frameBudget = 8)
        {
            if (Closed) return;
            int sent = 0;
            while (sends.Count != 0 && sent < byteBudget && socket.Poll(0, SelectMode.SelectWrite))
            {
                var frame = sends.Peek(); int count;
                try { count = socket.Send(frame, sendOffset, Math.Min(frame.Length - sendOffset, byteBudget - sent), SocketFlags.None); }
                catch (SocketException exception) when (exception.SocketErrorCode == SocketError.WouldBlock) { break; }
                if (count == 0) { Dispose(); return; }
                sent += count; sendOffset += count; queuedBytes -= count;
                if (sendOffset == frame.Length) { sends.Dequeue(); sendOffset = 0; }
            }
            int received = 0, frames = 0;
            while (!Closed && received < byteBudget && frames < frameBudget && socket.Poll(0, SelectMode.SelectRead))
            {
                if (socket.Available == 0) { Dispose(); return; }
                byte[] target = body ?? header; int offset = body != null ? bodyRead : headerRead, count;
                try { count = socket.Receive(target, offset, Math.Min(target.Length - offset, byteBudget - received), SocketFlags.None); }
                catch (SocketException exception) when (exception.SocketErrorCode == SocketError.WouldBlock) { break; }
                if (count == 0) { Dispose(); return; }
                received += count; LastReceived = Time.realtimeSinceStartupAsDouble;
                if (body == null)
                {
                    headerRead += count;
                    if (headerRead == 4)
                    {
                        int length = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
                        if (length <= 0 || length > MaximumFrameBytes) throw new InvalidOperationException("Invalid transport frame size.");
                        body = new byte[length]; bodyRead = 0;
                    }
                }
                else
                {
                    bodyRead += count;
                    if (bodyRead == body.Length)
                    {
                        string json = Encoding.UTF8.GetString(body); body = null; bodyRead = 0; headerRead = 0; frames++;
                        var packet = JsonUtility.FromJson<Packet>(json); if (packet == null) throw new InvalidOperationException("Missing transport packet."); receive(packet);
                    }
                }
            }
        }
        public void Dispose() { if (Closed) return; Closed = true; socket.Dispose(); sends.Clear(); body = null; }
    }
    /// <summary>Desktop loopback reference transport. Actor identity is supplied by the server's connection factory, never by packets.</summary>
    public sealed class BuildingTcpServer : IDisposable
    {
        private sealed class Client { internal FramedPeer Peer; internal BuildingAuthoritySession Session; }
        private readonly TcpListener listener;
        private readonly BuildingAuthority authority;
        private readonly Func<int, IBuildingActor> actors;
        private readonly List<Client> clients = new List<Client>();
        private int nextClient;
        private bool disposed;
        public int ClientCount => clients.Count;
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public string LastError { get; private set; }
        public BuildingTcpServer(BuildingAuthority authority, Func<int, IBuildingActor> serverActorFactory, int port = 0)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority)); actors = serverActorFactory ?? throw new ArgumentNullException(nameof(serverActorFactory));
            listener = new TcpListener(IPAddress.Loopback, port); listener.Start(8);
        }
        public void Tick()
        {
            if (disposed) return;
            if (listener.Pending())
            {
                var socket = listener.AcceptSocket();
                if (clients.Count >= 8) socket.Dispose();
                else
                {
                    Client client = null;
                    try
                    {
                        int index = ++nextClient; client = new Client { Peer = new FramedPeer(socket), Session = authority.OpenSession(Guid.NewGuid().ToString("N"), actors(index)) };
                        client.Peer.Send(new Packet { HasSnapshot = true, Snapshot = authority.CaptureSnapshot() }); clients.Add(client);
                    }
                    catch (Exception exception) { LastError = exception.Message; socket.Dispose(); if (client?.Session != null) authority.CloseSession(client.Session); }
                }
            }
            for (int i = clients.Count - 1; i >= 0; i--)
            {
                var client = clients[i];
                try
                {
                    client.Peer.Tick(packet =>
                    {
                        var reply = new Packet();
                        if (packet.HasCommand) { reply.HasResult = true; reply.Result = authority.Execute(client.Session, packet.Command); }
                        if (!authority.TryGetChangesSince(packet.KnownRevision, reply.Deltas)) { reply.HasSnapshot = true; reply.Snapshot = authority.CaptureSnapshot(); reply.Deltas.Clear(); }
                        client.Peer.Send(reply);
                    });
                    if (Time.realtimeSinceStartupAsDouble - client.Peer.LastReceived > 30) client.Peer.Dispose();
                }
                catch (Exception exception) { LastError = exception.Message; client.Peer.Dispose(); }
                if (client.Peer.Closed) { authority.CloseSession(client.Session); clients.RemoveAt(i); }
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; listener.Stop();
            foreach (var client in clients) { client.Peer.Dispose(); authority.CloseSession(client.Session); } clients.Clear();
        }
    }
    public sealed class BuildingTcpClient : IDisposable
    {
        private readonly FramedPeer peer;
        private readonly BuildingSystem replica;
        private readonly IReadOnlyList<BuildingDefinition> catalog;
        private double nextPoll;
        private long sequence;
        public bool Ready { get; private set; }
        public bool Closed => peer.Closed;
        public string LastError { get; private set; }
        public event Action<BuildingCommandResult> Completed;
        public BuildingTcpClient(BuildingSystem replica, IReadOnlyList<BuildingDefinition> catalog, int port)
        {
            this.replica = replica != null ? replica : throw new ArgumentNullException(nameof(replica)); this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            replica.SetReadOnly(true); var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try { socket.Connect(IPAddress.Loopback, port); peer = new FramedPeer(socket); }
            catch { socket.Dispose(); throw; }
        }
        /// <summary>Assign a new sequence automatically, or resend the same command/sequence after a lost response.</summary>
        public void Send(BuildingCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.Sequence <= 0) command.Sequence = ++sequence; else sequence = Math.Max(sequence, command.Sequence);
            peer.Send(new Packet { HasCommand = true, Command = command, KnownRevision = Ready ? replica.Revision : -1 });
        }
        public void Tick()
        {
            if (peer.Closed) return;
            try
            {
                peer.Tick(packet =>
                {
                    if (packet.HasSnapshot)
                    {
                        if (!replica.TryApplyReplicaSnapshot(packet.Snapshot, catalog, out var reason)) throw new InvalidOperationException(reason);
                        Ready = true;
                    }
                    if (packet.Deltas != null) foreach (var delta in packet.Deltas)
                    {
                        if (delta.Revision <= replica.Revision) continue;
                        if (!replica.TryApplyReplicaDelta(delta, catalog, out var reason)) { Ready = false; LastError = reason; break; }
                    }
                    if (packet.HasResult && packet.Result != null && Completed != null)
                        foreach (Action<BuildingCommandResult> handler in Completed.GetInvocationList()) try { handler(packet.Result); } catch (Exception exception) { Debug.LogException(exception); }
                });
                if (Time.realtimeSinceStartupAsDouble >= nextPoll)
                { nextPoll = Time.realtimeSinceStartupAsDouble + 0.1; peer.Send(new Packet { KnownRevision = Ready ? replica.Revision : -1 }); }
            }
            catch (Exception exception) { LastError = exception.Message; peer.Dispose(); Ready = false; }
        }
        public void Dispose() => peer.Dispose();
    }
}
