# NetSquare handshake V2

NetSquare uses a strict client-first handshake. The legacy public `Random` challenge is not supported.

## Connection sequence

1. The client sends a fixed-size hello containing its Core assembly version, wire protocol range, requested transport, capabilities, and a cryptographic nonce.
2. The server validates blacklist and handshake capacity limits, then checks the exact NetSquare version and required capabilities.
3. The server returns a cryptographic challenge. Proof of work is zero-cost below the configured activation threshold and uses leading SHA-256 zero bits above it.
4. The client sends a proof bound to the hello and challenge.
5. The server returns the negotiated transport, capabilities, transcript hash, and a random 128-bit UDP session key.
6. The client validates the transcript and sends `ReadyAck`.
7. The server allocates the client ID and returns a confirmation bound to `ReadyAck`.
8. For TCP and UDP connections, the client and server exchange empty MAC-authenticated UDP registration frames before either public connected event is raised.

Malformed first frames and capacity excesses are closed silently. Recognized NetSquare clients continue to receive typed rejection feedback such as `ProtocolMismatch` and `HandshakeTimeout`.

## TLS

`UseTLS` applies to the complete TCP connection. When enabled, TLS 1.2 authenticates and encrypts the TCP channel before the first NetSquare handshake frame is sent.

Server configuration:

```json
{
  "UseTLS": true,
  "TLSCertificatePath": "[current]\\certificates\\netsquare.pfx",
  "TLSCertificatePassword": "change-me"
}
```

The certificate must be a PFX or PKCS#12 file containing its private key. Server construction fails immediately when TLS is enabled and the certificate is missing or unusable.

Client configuration:

```json
{
  "Host": "game.example.com",
  "Port": 5555,
  "UseTLS": true,
  "TLSServerName": "game.example.com"
}
```

```csharp
NetSquareClientConfigurationManager.Initialize<NetSquareClientConfiguration>();
NetSquareClientConfiguration configuration =
    NetSquareClientConfigurationManager.Get<NetSquareClientConfiguration>();

NetSquareClient client = new NetSquareClient(configuration);
client.Connect();
```

The client validates the certificate chain and checks `TLSServerName`, or `Host` when it is empty, against the certificate. `TLSCertificateValidationCallback` is available in code for a private certificate authority; production code should not use it to accept every certificate.

`UseTLS` must match on both peers. When disabled, NetSquare preserves its raw Socket transport. TLS protects TCP and the UDP session key carried by the handshake, while UDP datagrams keep their separate MAC authentication.

## Authenticated UDP datagrams

Every UDP `NetworkMessage` keeps its serialized `ClientID` header. The server uses that value only to select a candidate session, verifies the datagram MAC, then replaces it with the client ID owned by the authenticated TCP connection.

Each datagram appends a 32-bit sequence followed by a 64-bit truncated HMAC-SHA256 tag, for a fixed 12-byte overhead. Client-to-server and server-to-client keys are derived independently from the handshake session key. A 64-datagram sliding window rejects duplicates while allowing normal UDP reordering.

Registration contains no session key in its UDP body. Invalid tags, invalid sequences and unknown client IDs are dropped silently; they do not disconnect a legitimate client because UDP source addresses can be spoofed.

When `UseTLS` is false, an on-path attacker can still observe the UDP session key carried by the TCP handshake. When TLS is enabled, the key remains confidential on TCP and is never transmitted directly in an UDP registration body.

## Compatibility

Handshake V2 currently requires exact equality of the `NetSquare.Core` assembly version. The centralized release version therefore needs to remain identical across Core, Client, Server, and their NuGet packages.

The wire protocol version is tracked separately from the package version so a future release can introduce an explicit compatibility range without weakening the current checks. The authenticated UDP capability retains its existing wire bit; this change does not increment the protocol or package version.

## Server tuning

The following `TcpListener` static settings can be changed before starting a server:

- `ClientHelloTimeoutMilliseconds`, default `2000`;
- `HandshakeTimeoutMilliseconds`, default `5000` after a valid hello;
- `MaxConcurrentHandshakes`, default `256`;
- `MaxConcurrentHandshakesPerAddress`, default `4`;
- `ProofOfWorkActivationThreshold`, default `32`;
- `ProofOfWorkDifficulty`, default `18`, capped by the protocol at `24`.

This handshake filters generic crawlers and makes connection floods more expensive. It does not authenticate a user account or replace TLS when server identity, TCP integrity, or confidentiality is required.
