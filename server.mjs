import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import { extname, join, resolve } from "node:path";

const root = resolve(process.cwd(), "public");
const port = Number(process.env.PORT || 8787);
const rooms = new Map();

const mimeTypes = new Map([
  [".html", "text/html; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".png", "image/png"],
  [".jpg", "image/jpeg"],
  [".jpeg", "image/jpeg"],
  [".webp", "image/webp"],
  [".zip", "application/zip"],
]);

function json(res, status, body) {
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
  });
  res.end(JSON.stringify(body));
}

function safeRoom(value) {
  return String(value || "")
    .toUpperCase()
    .replace(/[^A-Z0-9]/g, "")
    .slice(0, 8);
}

function getRoom(code) {
  if (!rooms.has(code)) {
    rooms.set(code, {
      state: null,
      clients: new Map(),
      updatedAt: Date.now(),
    });
  }
  return rooms.get(code);
}

function writeEvent(res, event, payload) {
  res.write(`event: ${event}\n`);
  res.write(`data: ${JSON.stringify(payload)}\n\n`);
}

function broadcast(room, event, payload) {
  for (const client of room.clients.values()) {
    writeEvent(client.res, event, payload);
  }
}

async function readBody(req, limit = 64 * 1024) {
  let body = "";
  for await (const chunk of req) {
    body += chunk;
    if (body.length > limit) {
      throw new Error("body too large");
    }
  }
  return body ? JSON.parse(body) : {};
}

function cleanupRooms() {
  const cutoff = Date.now() - 6 * 60 * 60 * 1000;
  for (const [code, room] of rooms.entries()) {
    if (!room.clients.size && room.updatedAt < cutoff) {
      rooms.delete(code);
    }
  }
}

async function handleApi(req, res, url) {
  if (url.pathname === "/api/health") {
    json(res, 200, { ok: true });
    return true;
  }

  const match = url.pathname.match(/^\/api\/rooms\/([A-Za-z0-9]+)\/(events|state)$/);
  if (!match) return false;

  const code = safeRoom(match[1]);
  const action = match[2];
  if (!code) {
    json(res, 400, { error: "invalid room" });
    return true;
  }

  const room = getRoom(code);
  room.updatedAt = Date.now();

  if (action === "events" && req.method === "GET") {
    const clientId = String(url.searchParams.get("client") || Math.random().toString(36).slice(2));
    res.writeHead(200, {
      "Content-Type": "text/event-stream; charset=utf-8",
      "Cache-Control": "no-store",
      Connection: "keep-alive",
    });
    res.write("\n");

    const client = { id: clientId, res };
    room.clients.set(clientId, client);
    writeEvent(res, "hello", { room: code, clients: room.clients.size });
    if (room.state) writeEvent(res, "state", room.state);

    const heartbeat = setInterval(() => {
      writeEvent(res, "ping", { at: Date.now() });
    }, 25000);

    req.on("close", () => {
      clearInterval(heartbeat);
      room.clients.delete(clientId);
      room.updatedAt = Date.now();
    });
    return true;
  }

  if (action === "state" && req.method === "GET") {
    json(res, 200, { ok: true, state: room.state });
    return true;
  }

  if (action === "state" && req.method === "POST") {
    try {
      const payload = await readBody(req);
      if (!Number.isInteger(payload.index)) {
        json(res, 400, { error: "invalid map index" });
        return true;
      }

      room.state = {
        type: "state",
        index: payload.index,
        map: String(payload.map || ""),
        name: String(payload.name || ""),
        artist: String(payload.artist || ""),
        realm: String(payload.realm || ""),
        clientId: String(payload.clientId || ""),
        at: Date.now(),
      };
      room.updatedAt = Date.now();
      broadcast(room, "state", room.state);
      json(res, 200, { ok: true });
    } catch {
      json(res, 400, { error: "bad request" });
    }
    return true;
  }

  json(res, 405, { error: "method not allowed" });
  return true;
}

async function serveStatic(req, res, url) {
  let pathname;
  try {
    pathname = decodeURIComponent(url.pathname);
  } catch {
    res.writeHead(400);
    res.end("Bad request");
    return;
  }

  const requested = pathname === "/" ? "/index.html" : pathname;
  const target = resolve(root, `.${requested}`);
  if (!target.startsWith(root)) {
    res.writeHead(403);
    res.end("Forbidden");
    return;
  }

  let filePath = target;
  let info;
  try {
    info = await stat(filePath);
    if (info.isDirectory()) {
      filePath = join(filePath, "index.html");
      info = await stat(filePath);
    }
  } catch {
    res.writeHead(404);
    res.end("Not found");
    return;
  }

  const extension = extname(filePath).toLowerCase();
  const type = mimeTypes.get(extension) || "application/octet-stream";
  const cacheControl = [".png", ".jpg", ".jpeg", ".webp"].includes(extension)
    ? "public, max-age=86400"
    : "no-store";
  res.writeHead(200, {
    "Content-Type": type,
    "Content-Length": info.size,
    "Cache-Control": cacheControl,
  });
  createReadStream(filePath).pipe(res);
}

const server = createServer(async (req, res) => {
  const url = new URL(req.url || "/", `http://${req.headers.host || "localhost"}`);
  if (await handleApi(req, res, url)) return;
  await serveStatic(req, res, url);
});

setInterval(cleanupRooms, 30 * 60 * 1000).unref();

server.listen(port, () => {
  console.log(`DBD Clock Maps listening on http://localhost:${port}`);
});
