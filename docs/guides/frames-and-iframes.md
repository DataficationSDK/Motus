# Frames and iframes

A page is a tree of frames. The main frame is the document you navigated to; every `<iframe>` adds another, each with its own document, its own scripts, and its own globals. Motus exposes each one as an `IFrame`, and a locator built from a frame resolves inside that frame and nowhere else.

Some of those frames run in a renderer process of their own. That changes a great deal underneath and nothing at all in the API: an out-of-process frame is discovered, traversed, evaluated, and clicked exactly like any other.

---

## Traversing

```csharp
IFrame main = page.MainFrame;

foreach (IFrame child in main.ChildFrames)
{
    Console.WriteLine($"{child.Name}: {child.Url}");
}

// Or flat, every frame in the page including the main one.
foreach (IFrame frame in page.Frames)
{
    Console.WriteLine(frame.Url);
}
```

`page.Frames` is a flat list; `MainFrame` plus `ChildFrames` is the tree. `IFrame.ParentFrame` walks back up and is null for the main frame.

Frames arrive as the page loads them, and a frame in its own process arrives as a separate event after its parent already knows about it. Code that reads `page.Frames` immediately after `GotoAsync` may see the tree before it is complete. Wait for the frame you want rather than assuming it is there:

```csharp
IFrame checkout = await WaitForFrameAsync(page, url => url.Contains("/checkout"));

static async Task<IFrame> WaitForFrameAsync(IPage page, Func<string, bool> match, int timeoutMs = 10_000)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        IFrame? found = page.Frames.FirstOrDefault(f => match(f.Url));
        if (found is not null)
            return found;

        await Task.Delay(50);
    }

    throw new TimeoutException("No frame matched.");
}
```

The `IPage.FrameAttached`, `FrameNavigated` and `FrameDetached` events report the same changes as they happen, which is the better fit for a long-lived session.

---

## Acting inside a frame

Every locator factory on `IPage` exists on `IFrame` and scopes to that frame:

```csharp
IFrame payment = page.Frames.Single(f => f.Name == "payment");

await payment.GetByLabel("Card number").FillAsync("4111111111111111");
await payment.GetByRole("button", "Pay").ClickAsync();

string status = await payment.Locator("#status").TextContentAsync();
```

A selector that matches in two frames matches only the frame the locator came from. `page.Locator("#status")` and `frame.Locator("#status")` are different queries against different documents, even when the selector text is identical.

Actions dispatch real browser input in page coordinates and the browser decides which frame receives them, so nothing about clicking inside a frame is special. `ILocator.BoundingBoxAsync` answers in page coordinates too, which is what makes a box usable with `page.Mouse` directly.

### Navigating and reading a frame

```csharp
await frame.GotoAsync("https://example.com/embedded");
string html = await frame.ContentAsync();
string title = await frame.TitleAsync();
```

Navigating the main frame navigates the page. Navigating any other frame leaves the rest of the page where it is.

A frame other than the main frame reports only that it finished loading, with no separate signal for its DOM being ready, so `WaitUntil.DOMContentLoaded` and `WaitUntil.Load` wait for the same point. `WaitUntil.NetworkIdle` is measured across the whole page rather than for one frame.

### A frame that has gone away

```csharp
if (frame.IsDetached)
{
    // The frame was removed from its page. Operations on it will fail.
}
```

`IsDetached` is how a removed frame is told apart from a selector that simply did not match. A held `IFrame` answers rather than failing obscurely several calls later.

---

## Frames in their own process

For security, browsers put a frame from a different site into its own renderer process. Whether a given frame lands there depends on the browser's own rules and can change between versions; it is not something to design around.

What changes underneath is substantial. Such a frame has its own protocol target, its own session, its own numbering of execution contexts, and it does not appear in its parent's frame tree at all. Motus attaches to those targets automatically, recursively, so a frame inside a frame inside a frame is reachable, and stitches each one into the page's frame tree under its real parent.

None of that reaches the caller. The frame is an ordinary `IFrame`, the API is the same, and code written against a same-process frame works unchanged when the browser decides to isolate it.

Two consequences are visible, and both are covered under [Known traps](#known-traps) below.

---

## Choosing an execution world

Every frame has a main world, where the page's own scripts run, and can be given an isolated world, which shares the frame's document but not its globals.

```csharp
// Main world (the default): sees what the application defined.
string version = await frame.EvaluateAsync<string>("window.__APP__.version");

// Isolated world: the same DOM, none of the page's globals.
int count = await frame.EvaluateAsync<int>(
    "document.querySelectorAll('li').length",
    null,
    new EvaluateOptions { World = ExecutionWorld.Isolated });
```

The main world is the default, and it is what you want for reading application state: a framework's runtime handle, a store, a version marker, anything the page put on `window`.

Reach for an isolated world when the page's own scripts would get in the way. Its variables cannot collide with the page's, and a page that has replaced a built-in such as `Array.prototype.map` or `JSON.stringify` cannot affect what your expression does. It is the safer place for DOM queries in a hostile or heavily instrumented page.

The isolated world belongs to the frame's current document. Navigating the frame discards it, and the next evaluation makes a fresh one.

---

## Through the MCP server

A page snapshot describes each `iframe` element but not what is inside it, so an agent selects a frame before it can perceive or act on its content:

```
frame_list                  lists the frames in document order, index 0 is the page
frame_select <index>        scopes the session to that frame; 0 returns to the page
```

Scope covers `snapshot`, `evaluate`, and the `wait_for` text conditions. The refs a scoped snapshot hands out keep working for every interaction tool afterwards, and keep addressing the frame they came from even after the scope moves on. Selection resets on navigation and on switching tab or context, since the frame it named is gone by then.

The coordinate tools stay in page coordinates whatever is selected, because their input is dispatched at the page level and the browser decides which frame is under the point.

A page snapshot says how many frames its tree does not describe, so an agent that reads an empty-looking `iframe` is told where the rest of the content is rather than left to conclude it is missing.

---

## Known traps

Both of these cost real time to rediscover.

**An out-of-process frame is absent from the endpoint's target list.** `http://127.0.0.1:9222/json/list` lists pages and workers. A frame in its own renderer process is a separate target but is not in that list, so a tool that enumerates targets over HTTP will not find it and neither will a person checking by hand. Motus finds these frames through auto-attach on the page's own session instead, so they are in `page.Frames` regardless. An empty target list is not evidence that a frame is unreachable.

**Application globals are absent from isolated worlds.** An isolated world shares the frame's document and nothing else, so the DOM is fully present while everything the application put on `window` is not. An expression that reads a framework handle returns `undefined` there and the failure looks like the application not having loaded. Use `ExecutionWorld.Main`, which is the default, whenever you are reading application state.

---

## What's next

- [Attaching to a Running Browser](attaching-to-a-running-browser.md) -- the case where frames matter most, since a desktop application's interface is often several of them
- [Selectors and Assertions](../architecture/selectors-and-assertions.md) -- how a locator resolves, and how a frame root changes it
- [Transport and Protocol](../architecture/transport-and-protocol.md) -- sessions, flattened targets, and how a frame reaches the session that owns it
- [MCP Server](mcp-server.md) -- the full tool catalog
