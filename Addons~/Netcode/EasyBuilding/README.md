This addon is stored outside the base import in Addons~/Networking/EasyBuilding. Its package manifest/distribution is managed by the framework coordinator. Do not infer headset/device/production-network validation from source availability.

# Authoritative TCP reference transport

**Dependency:** Building module from EasyGameFramework. This is a TCP transport example, not Unity Netcode support.

Desktop localhost example without a networking package. WebGL is excluded.

```csharp
var registry = new BuildingRegistry();
registry.Register("base", catalog);
var authority = new BuildingAuthority(serverWorld, registry);
var server = new BuildingTcpServer(authority, i => ServerActor(i), port: 17863);
// Separate client world/process using the same definition assets:
var client = new BuildingTcpClient(replicaWorld, registry.Definitions, port: 17863);
// Update the corresponding service in its process:
server.Tick();
client.Tick();
client.Send(new BuildingCommand {
    Edits = new[] { new BuildingCommandEdit {
        Kind = BuildingCommandEditKind.Place,
        DefinitionId = definition.Id, Position = point, Rotation = rotation
    }}
});
```

ServerActor is application code providing authoritative identity/position. This sample assigns identity per connection, without an account service. Send assigns Sequence; resend the same command/sequence after a lost response. Changed payload with the same sequence is rejected. Subscribe to Completed; inspect Ready/Closed/LastError. Dispose all services when finished. Save replay watermarks when adding persistent reconnects to a game transport.

Tick uses nonblocking socket IO after connection, length-prefixed UTF-8 JSON, a 4 MiB frame cap, bounded outgoing queues, per-tick byte/frame limits, 8 clients and a 30-second receive timeout. Initial snapshots support late join; polls deliver other players' deltas; journal gaps request snapshots. Larger worlds need an interest/paging strategy. JSON allocations belong to this reference transport; core authority accepts other transports/serializers.

Real-socket and separate-process verification results are recorded in Documentation~/Validation.md. The sample does not provide internet hosting, authentication or lobby services.
