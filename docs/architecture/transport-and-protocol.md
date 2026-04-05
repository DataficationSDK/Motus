# Transport and Protocol Layer

The transport and protocol layer is the lowest-level subsystem in Motus. It owns the WebSocket connection to the browser, serializes and deserializes protocol messages, and routes responses and events to their respective awaiters. Everything above this layer (the engine, page, and element APIs) calls `IMotusSession` and never touches raw JSON or WebSocket frames directly.

Two concrete transports exist: `CdpTransport` for Chromium-family browsers using the Chrome DevTools Protocol, and `BiDiTransport` for Firefox using the WebDriver BiDi protocol. Both share the same `ICdpSocket` abstraction for WebSocket I/O, and both expose the same `IMotusSession` interface to the engine layer, so the rest of Motus does not need to branch on transport type at runtime.

---

## WebSocket Communication Design

Motus connects directly to the browser's debugging WebSocket endpoint with no proxy, relay, or WebDriver HTTP server in the path. Chrome exposes a CDP WebSocket endpoint when launched with `--remote-debugging-port`; Firefox exposes a BiDi WebSocket endpoint on the same port. Motus opens a single `ClientWebSocket` to that endpoint and keeps it open for the lifetime of the browser instance.

This direct connection model means:

- There is no HTTP round-trip per command. Latency is bounded by serialization and a single WebSocket frame pair.
- All multiplexing of pages and workers onto the single connection is handled in-process by the transport layer via session IDs (CDP) or browsing context IDs (BiDi).
- The transport is solely responsible for detecting disconnect. When the browser closes without a clean WebSocket close handshake, the receive loop catches the resulting exception, faults all pending command awaiters, and raises the `Disconnected` event.

---

## CDP Transport

### CdpSocket

`CdpSocket` wraps `ClientWebSocket` and handles low-level frame assembly. CDP messages can be arbitrarily large (base64-encoded screenshots are common), so a single logical message may span multiple WebSocket frames. `CdpSocket` reassembles these before returning a contiguous `ReadOnlyMemory<byte>` to the caller.

Buffer management uses `ArrayPool<byte>.Shared` to avoid per-receive allocations. The receive buffer starts at 16 KB. When a frame would overflow the current buffer, `CdpSocket` rents a buffer twice the current size, copies the already-received bytes into the new buffer, and returns the old one to the pool. The loop continues until `result.EndOfMessage` is true, at which point the complete message is returned. On disposal, the buffer is returned to the pool and a best-effort graceful WebSocket close is attempted with a 5-second timeout.

A `WebSocketMessageType.Close` frame from the browser results in `ReadOnlyMemory<byte>.Empty` being returned, which the receive loop in `CdpTransport` treats as a clean disconnect signal.

### CdpTransport

`CdpTransport` is the coordinator. It owns the receive loop, maps outbound command IDs to awaiting callers, and dispatches inbound events to typed channels.

**Request/response correlation.** Every outbound command is assigned a monotonically incrementing integer ID via `Interlocked.Increment`. A `TaskCompletionSource<JsonElement>` is stored in a `ConcurrentDictionary<int, TaskCompletionSource<JsonElement>>` keyed by that ID. When the receive loop sees an inbound message with a matching `id` field, it resolves the TCS with the result or faults it with a `CdpProtocolException`. The entry is removed from the dictionary in both paths.

**Command timeout.** Every call to `SendRawAsync` creates a linked `CancellationTokenSource` that always cancels after 60 seconds, regardless of whether the caller passes `CancellationToken.None`. This prevents indefinite hangs if Chrome becomes unresponsive without closing the WebSocket. If the linked token fires, the TCS is cancelled through the cancellation registration and the pending entry is cleaned up in the `finally` block.

**Slow-motion mode.** An optional `slowMo` `TimeSpan` can be injected at construction time. When non-zero, `SendRawAsync` delays by that duration (respecting the effective cancellation token) before serializing and sending the command. This is used for visual debugging and test stability.

**Event dispatch.** Inbound messages without an `id` field are treated as events. Events are keyed by the composite string `"Domain.eventName|sessionId"` (empty string for the browser-level session). `CdpTransport` holds a `ConcurrentDictionary<string, Channel<RawCdpEvent>>` of unbounded channels. When an event arrives, the matching channel's writer calls `TryWrite` with a `RawCdpEvent` containing the raw `JsonElement` params and the session ID. Channels are created on demand via `GetOrCreateEventChannel` and are completed and removed when a session is disposed via `RemoveChannelsForSession`.

**Disconnect handling.** The receive loop has three termination paths:

- Clean disconnect: `ReceiveAsync` returns an empty buffer. All pending TCSes are faulted with `CdpDisconnectedException`, all channels are completed, and `Disconnected` is raised with a `null` exception.
- Unexpected exception: All pending TCSes are faulted with a wrapping `CdpDisconnectedException`, all channels are completed, and `Disconnected` is raised with the exception.
- Normal shutdown via `DisposeAsync`: The `CancellationTokenSource` is cancelled, the receive loop exits via `OperationCanceledException`, and `DisposeAsync` then faults any remaining pending TCSes with `ObjectDisposedException` and completes all channels.

### CdpSession

`CdpSession` is the typed wrapper that the engine layer calls. It holds a reference to `CdpTransport` and an optional `SessionId` string (null for the browser-level session, a CDP target session ID for pages and workers).

The `SendAsync` overloads accept `JsonTypeInfo<T>` from `CdpJsonContext.Default` for both the command params and the response type. This is required for NativeAOT compatibility; see the NativeAOT section below. The params are serialized to `JsonElement`, passed to `CdpTransport.SendRawAsync`, and the resulting `JsonElement` is deserialized into the response type. Transport-internal exceptions (`CdpProtocolException`, `CdpDisconnectedException`) are translated to the public `MotusProtocolException` and `MotusTargetClosedException` types before propagating.

Event subscriptions are surfaced as `IAsyncEnumerable<TEvent>` via `SubscribeAsync`. The channel key is built as `"{eventKey}|{sessionId}"` where `sessionId` is the empty string for the browser-level session. The raw `JsonElement` params from each `RawCdpEvent` are deserialized using the provided `JsonTypeInfo<TEvent>` as each item is yielded.

Properties are serialized and deserialized using `[property: JsonPropertyName("...")]` on all generated record parameters. The `CdpCommandEnvelope` sent over the wire always carries `id`, `method`, `params`, and an optional `sessionId` field.

---

## BiDi Transport

`BiDiTransport` mirrors `CdpTransport` structurally but differs in several protocol-level details.

### No Session ID in Commands

The CDP `sessionId` field in outbound command envelopes has no BiDi equivalent. `BiDiCommandEnvelope` carries only `id`, `method`, and `params`. Context scoping (the BiDi equivalent of CDP sessions) is expressed inside the params payload of each command, not as an envelope field.

### BiDiInboundDiscriminator

CDP uses the presence or absence of the `id` field to distinguish responses from events. BiDi uses an explicit `type` field with three values: `"success"`, `"error"`, and `"event"`. The `BiDiInboundDiscriminator` record captures all envelope fields and the receive loop inspects `type` to route the message:

- `"success"` or `"error"` with a non-null `id`: the message is a command response and is dispatched to the matching pending TCS.
- `"event"` with a non-null `method`: the message is an event and is dispatched to event channels.

Error responses carry `error` (a short error code string) and `message` (a human-readable description) rather than CDP's numeric `code` field.

### Context-Based Event Routing

Where CDP scopes events to a session ID, BiDi scopes events to a browsing context ID carried inside the event params as the `"context"` property. `BiDiTransport.DispatchEvent` extracts this field when present and builds a channel key of `"biDiEventName|contextId"`.

Additionally, `DispatchEvent` routes every context-specific event to a second wildcard channel keyed as `"biDiEventName|"` (empty context ID). This allows browser-level subscribers to receive events from any context without knowing the context ID in advance.

Channel cleanup uses `RemoveChannelsForContext` rather than `RemoveChannelsForSession`, but the mechanics are identical: all keys with a matching suffix are removed and their writers are completed.

### CreateSessionAsync

BiDi requires an explicit `session.new` handshake after the WebSocket connects. `BiDiTransport.CreateSessionAsync` sends this command, parses the returned `sessionId`, and returns it to the caller. This must be called before sending any other BiDi commands. The CDP transport has no equivalent step; the CDP protocol is ready immediately after the WebSocket connects.

### BiDiSession and Translation

`BiDiSession` implements `IMotusSession` using a translation registry. Rather than calling BiDi natively for each CDP method, `BiDiSession.TranslateAndSendAsync` looks up the CDP method name in `BiDiTranslationRegistry` to obtain a translation object that rewrites the params, sends the BiDi command via `BiDiTransport.SendRawAsync`, and rewrites the result back into the CDP response shape. This allows the engine layer to issue CDP-shaped calls uniformly without branching on transport type.

Event subscriptions in `BiDiSession` also pass through `BiDiEventMap.GetEventTranslation` to remap CDP event names to BiDi event names and translate event payloads. Before yielding the first event for a given BiDi event name, `BiDiSession.EnsureSubscribedAsync` sends a `session.subscribe` command with the event name and, where applicable, the context ID. This subscription is tracked per-session in a `HashSet<string>` so it is only sent once.

---

## Source-Generated Protocol Bindings

The `Motus.Codegen` project is a Roslyn incremental source generator that reads the two CDP protocol JSON files distributed with Chrome (`browser_protocol.json` and `js_protocol.json`) and emits typed C# records for every domain.

### CdpGenerator

`CdpGenerator` is the entry point, implementing `IIncrementalGenerator`. It registers two `AdditionalTextsProvider` pipelines that select each protocol file by filename. The two pipelines are combined with `Combine` and fed into a single `RegisterSourceOutput` callback. Parsing is deferred into the output callback (rather than into a `Select` transform) to avoid equality comparison issues with `ImmutableArray` in the incremental pipeline. For each domain, the generator calls `DomainEmitter.Emit` and adds the result as a source file named `Motus.Protocol.{DomainName}Domain.g.cs`.

### CdpSchemaParser

`CdpSchemaParser.Parse` reads a protocol JSON string using `System.Text.Json.JsonDocument` and returns an `ImmutableArray<CdpDomain>`. The parser walks the `domains` array and for each domain extracts:

- **Types**: classified into `StringEnum` (string type with an `enum` array), `Object` (object type with `properties`), `ArrayType` (array type), or `Alias` (scalar alias). String enums additionally capture the list of allowed string values.
- **Commands**: each command captures `parameters` and `returns` property arrays.
- **Events**: each event captures a `parameters` property array.

All items carry `deprecated` and `experimental` boolean flags read from the JSON.

Properties include `$ref` (cross-domain type references use `Domain.TypeId` notation), `type`, `optional`, `items.$ref`, `items.type`, and inline `enum` values.

### DomainEmitter

`DomainEmitter.Emit` produces a complete C# source file for a single domain. The generated file contains one `public static partial class {DomainName}Domain` and within it:

- **Enums**: one `public enum` per `StringEnum` type, decorated with `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`. Each member carries `[JsonStringEnumMemberName("...")]` with the original camelCase protocol string.
- **Object records**: one `public sealed record` per `Object` type, with constructor parameters that mirror the CDP properties. Required parameters are emitted before optional ones. Each parameter carries `[property: JsonPropertyName("...")]` when the CDP property name differs from the PascalCase C# parameter name.
- **Command params and response records**: for each command, a `{CommandName}Params` record and a `{CommandName}Response` record. Empty parameter or return lists produce a no-argument record.
- **Event records**: one `{EventName}Event` record per event, following the same parameter conventions.

Deprecated and experimental items receive `[System.Obsolete]` annotations.

### TypeResolver

`TypeResolver` maps CDP type references to C# type strings in two passes. Pass one registers all non-array types. Pass two registers array alias types, which may reference types from pass one. At usage sites, `Resolve(CdpProperty, currentDomain)` returns the C# type string including a trailing `?` for optional properties.

Primitive mappings: `string` to `string`, `integer` to `long`, `number` to `double`, `boolean` to `bool`, `object` and `any` to `System.Text.Json.JsonElement`. Unknown `$ref` values fall back to `System.Text.Json.JsonElement`.

---

## Capability Flags

`MotusCapabilities` is a `[Flags]` enum that expresses what a given transport natively supports. `CdpTransport.Capabilities` returns `MotusCapabilities.AllCdp`; `BiDiTransport.Capabilities` returns `MotusCapabilities.AllBiDi`.

| Flag | Value | Transport |
|---|---|---|
| `TargetMultiplexing` | `1 << 0` | CDP |
| `FetchInterception` | `1 << 1` | CDP |
| `EmulationOverrides` | `1 << 2` | CDP |
| `Tracing` | `1 << 3` | CDP |
| `BiDiNetworkIntercept` | `1 << 4` | BiDi |
| `BiDiScriptEvaluation` | `1 << 5` | BiDi |
| `BiDiInputActions` | `1 << 6` | BiDi |
| `SecurityOverrides` | `1 << 7` | CDP |
| `AccessibilityTree` | `1 << 8` | CDP |
| `AllCdp` | composite | CDP |
| `AllBiDi` | composite | BiDi |

`AllCdp` combines `TargetMultiplexing`, `FetchInterception`, `EmulationOverrides`, `Tracing`, `SecurityOverrides`, and `AccessibilityTree`. `AllBiDi` combines `BiDiNetworkIntercept`, `BiDiScriptEvaluation`, and `BiDiInputActions`.

Feature code that requires a specific capability calls `CapabilityGuard.Require(session.Capabilities, requiredFlag, featureName)`. If the flag is absent, `NotSupportedException` is thrown with a message that names both the feature and the active transport (for example, `"'Tracing' is not supported by the current browser transport (Firefox/WebDriver BiDi)."`).

`CapabilityGuard.GetTransportDescription` provides the human-readable transport name used in that message. It matches on `CdpTransport` to return `"Chrome/CDP"` and on `BiDiTransport` to return `"Firefox/WebDriver BiDi"`, with a fallback to the runtime type name for any future transports.

---

## NativeAOT Serialization

All JSON serialization in Motus uses `System.Text.Json` source generators rather than reflection. `CdpJsonContext` and `BiDiJsonContext` are `JsonSerializerContext` subclasses annotated with `[JsonSerializable]` attributes for every envelope type and generated protocol record. All `SendAsync` and `SubscribeAsync` overloads on `CdpSession` and `BiDiSession` accept `JsonTypeInfo<T>` parameters that callers must supply from `CdpJsonContext.Default` (or `BiDiJsonContext.Default`).

This design has two consequences:

- There is no reflection at serialization time, which is required for NativeAOT publishing and reduces startup cost.
- Adding a new command or event type to the engine layer requires a corresponding `[JsonSerializable]` entry in the context. The Roslyn generator handles this automatically for generated protocol types; hand-written types must be registered manually.

---

## See Also

- [Plugin System Architecture](../architecture/plugin-system.md)
- [Engine Layer](../architecture/engine.md)
- [BiDi Translation Registry](../architecture/bidi-translation.md)
