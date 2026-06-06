const encoder = new TextEncoder();

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
    },
  });
}

function safeRoom(value) {
  return String(value || "")
    .toUpperCase()
    .replace(/[^A-Z0-9]/g, "")
    .slice(0, 8);
}

function eventChunk(event, payload) {
  return encoder.encode(`event: ${event}\ndata: ${JSON.stringify(payload)}\n\n`);
}

async function readJson(request, limit = 64 * 1024) {
  const text = await request.text();
  if (text.length > limit) throw new Error("body too large");
  return text ? JSON.parse(text) : {};
}

export class Room {
  constructor(state) {
    this.state = state;
    this.clients = new Map();
    this.current = null;
    this.state.blockConcurrencyWhile(async () => {
      this.current = (await this.state.storage.get("state")) || null;
    });
  }

  async fetch(request) {
    const url = new URL(request.url);
    const action = url.pathname.endsWith("/events") ? "events" : "state";

    if (action === "events" && request.method === "GET") {
      return this.openEvents(url);
    }

    if (action === "state" && request.method === "GET") {
      return json({ ok: true, state: this.current });
    }

    if (action === "state" && request.method === "POST") {
      return this.setState(request);
    }

    return json({ error: "method not allowed" }, 405);
  }

  openEvents(url) {
    const clientId = String(url.searchParams.get("client") || crypto.randomUUID());
    let heartbeat = null;

    const stream = new ReadableStream({
      start: (controller) => {
        const client = { id: clientId, controller };
        this.clients.set(clientId, client);
        this.send(client, "hello", { clients: this.clients.size });
        if (this.current) this.send(client, "state", this.current);
        heartbeat = setInterval(() => {
          this.send(client, "ping", { at: Date.now() });
        }, 25000);
      },
      cancel: () => {
        if (heartbeat) clearInterval(heartbeat);
        this.clients.delete(clientId);
      },
    });

    return new Response(stream, {
      headers: {
        "Content-Type": "text/event-stream; charset=utf-8",
        "Cache-Control": "no-store",
        "Connection": "keep-alive",
      },
    });
  }

  async setState(request) {
    let payload;
    try {
      payload = await readJson(request);
    } catch {
      return json({ error: "bad request" }, 400);
    }

    if (!Number.isInteger(payload.index)) {
      return json({ error: "invalid map index" }, 400);
    }

    this.current = {
      type: "state",
      index: payload.index,
      map: String(payload.map || ""),
      name: String(payload.name || ""),
      artist: String(payload.artist || ""),
      realm: String(payload.realm || ""),
      clientId: String(payload.clientId || ""),
      at: Date.now(),
    };

    await this.state.storage.put("state", this.current);
    this.broadcast("state", this.current);
    return json({ ok: true });
  }

  send(client, event, payload) {
    try {
      client.controller.enqueue(eventChunk(event, payload));
    } catch {
      this.clients.delete(client.id);
    }
  }

  broadcast(event, payload) {
    for (const client of this.clients.values()) {
      this.send(client, event, payload);
    }
  }
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/api/health") {
      return json({ ok: true, runtime: "cloudflare-worker" });
    }

    const match = url.pathname.match(/^\/api\/rooms\/([A-Za-z0-9]+)\/(events|state)$/);
    if (match) {
      const code = safeRoom(match[1]);
      if (!code) return json({ error: "invalid room" }, 400);
      const id = env.ROOMS.idFromName(code);
      return env.ROOMS.get(id).fetch(request);
    }

    return env.ASSETS.fetch(request);
  },
};
