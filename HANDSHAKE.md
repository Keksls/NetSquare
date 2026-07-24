# NetSquare handshake and transport security

NetSquare performs its own version and capability handshake before raising a connected event. TLS, when enabled, runs first and protects the complete TCP session.

## Connection sequence

1. The Client opens TCP.
2. If `UseTLS` is enabled, the Client validates the Server certificate and both peers establish TLS 1.2.
3. The Client sends its handshake version, Core assembly version, requested transport, capabilities, and a random nonce.
4. The Server validates protocol compatibility, capacity, blacklist state, and transport requirements.
5. The Server returns the negotiated transport, capabilities, transcript hash, and a random 128-bit UDP session key.
6. The Client proves that it received the Server challenge.
7. The Server assigns the authenticated TCP session its Client ID.
8. For TCP-plus-UDP connections, both peers exchange empty MAC-authenticated UDP registration frames.
9. Connected events are raised only after every required step succeeds.

Malformed first frames and capacity excesses are closed silently. Recognized NetSquare Clients receive typed rejection feedback for failures such as protocol mismatch, timeout, temporary ban, or permanent ban.

The handshake identifies a compatible NetSquare peer. It does not authenticate an application account.

## TLS

`UseTLS` must match on both peers. When enabled, TLS authenticates and encrypts TCP before the first NetSquare handshake frame is sent.

Server configuration:

```json
{
  "UseTLS": true,
  "TLSCertificatePath": "[current]\\certificates\\netsquare.pfx",
  "TLSCertificatePassword": "load-this-from-an-appropriate-secret-source"
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

The Client validates the certificate chain and checks `TLSServerName`, or `Host` when it is empty, against the certificate.

A private certificate authority can use a code-only validation callback:

```csharp
client.TLSCertificateValidationCallback = ValidatePrivateCertificate;
```

Production code must not accept every certificate. Doing so removes Server identity validation and makes TLS vulnerable to interception.

When TLS is disabled, NetSquare uses its raw socket transport. UDP authentication still detects forged or modified datagrams, but an on-path attacker can observe the UDP session key while it crosses the unencrypted TCP handshake.

## Authenticated UDP

Every UDP `NetworkMessage` retains its serialized `ClientID` header. The Server uses that value only to locate a candidate session, validates the datagram, and then replaces it with the Client ID owned by the authenticated TCP connection.

Each datagram appends:

- a 32-bit sequence;
- a 64-bit truncated HMAC-SHA256 tag.

The fixed overhead is 12 bytes. Client-to-Server and Server-to-Client keys are derived independently from the handshake session key. A 64-datagram sliding window rejects duplicates while allowing normal reordering.

UDP endpoint registration never sends the session key in the UDP body. Invalid tags, invalid sequences, and unknown Client IDs are dropped silently. They do not disconnect a legitimate Client because UDP source addresses can be spoofed.

UDP remains unreliable: authenticated datagrams may still be lost, delayed, duplicated before filtering, or reordered. Use TCP for information that must arrive.

## Compatibility

Handshake V2 currently requires exact equality of the `NetSquare.Core` assembly version. `NetSquare.Core`, `NetSquare.Client`, and `NetSquare.Server` must therefore share the same release version.

The wire protocol version is tracked separately from the package version. NetSquare `1.0.15` does not change the handshake wire version or authenticated UDP capability bit.

## Server tuning

The Server exposes these handshake controls:

- `ListenBacklog`: pending socket backlog, default `1024`;
- `ClientHelloTimeoutMilliseconds`: maximum time to receive the first Client hello, default `2000`;
- `HandshakeTimeoutMilliseconds`: maximum time after a valid hello, default `5000`;
- `MaxConcurrentHandshakes`: global in-progress limit, default `256`;
- `MaxConcurrentHandshakesPerAddress`: per-address in-progress limit, default `4`;
- `ProofOfWorkActivationThreshold`: load threshold that enables handshake proof of work, default `32`.

Choose limits from expected traffic and deployment topology. If many legitimate users share a NAT address, keep per-address limits high enough for that environment.

These controls make generic connection floods more expensive. They do not replace operating-system limits, a reverse proxy, rate limiting at the network edge, application authentication, or TLS.
