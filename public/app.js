(() => {
  const maps = Array.isArray(window.DBD_MAPS) ? window.DBD_MAPS : [];
  const params = new URLSearchParams(window.location.search);
  const clientId = Math.random().toString(36).slice(2, 10);

  const els = {
    artist: document.getElementById("artistSelect"),
    realm: document.getElementById("realmSelect"),
    search: document.getElementById("searchInput"),
    suggestionMenu: document.getElementById("suggestionMenu"),
    list: document.getElementById("mapList"),
    image: document.getElementById("mapImage"),
    title: document.getElementById("mapTitle"),
    meta: document.getElementById("mapMeta"),
    count: document.getElementById("mapCount"),
    prev: document.getElementById("prevButton"),
    next: document.getElementById("nextButton"),
    room: document.getElementById("roomInput"),
    host: document.getElementById("hostButton"),
    join: document.getElementById("joinButton"),
    leave: document.getElementById("leaveButton"),
    invite: document.getElementById("inviteButton"),
    status: document.getElementById("syncStatus"),
    copy: document.getElementById("copyButton"),
    clean: document.getElementById("cleanButton"),
    opacity: document.getElementById("opacityInput"),
    overlayTitle: document.getElementById("overlayTitle"),
    overlayOpacity: document.getElementById("overlayOpacityInput"),
    overlayLess: document.getElementById("overlayLessButton"),
    overlayMore: document.getElementById("overlayMoreButton"),
    overlayUi: document.getElementById("overlayUiButton"),
    auto: document.getElementById("autoButton"),
    autoStatus: document.getElementById("autoStatus"),
    video: document.getElementById("captureVideo"),
    canvas: document.getElementById("captureCanvas"),
  };

  const state = {
    current: 0,
    artist: "",
    artistQuery: "",
    realm: "all",
    realmQuery: "",
    query: "",
    clean: params.get("ui") === "0",
    sync: {
      mode: "offline",
      role: "",
      room: "",
      peer: null,
      conns: [],
      eventSource: null,
      reconnectTimer: null,
    },
    auto: {
      stream: null,
      worker: null,
      timer: null,
      running: false,
      busy: false,
    },
  };

  function unique(values) {
    return [...new Set(values)].filter(Boolean).sort((a, b) => a.localeCompare(b));
  }

  function cleanText(value) {
    return String(value || "")
      .toLowerCase()
      .replace(/&/g, " and ")
      .replace(/[^a-z0-9]+/g, " ")
      .replace(/\s+/g, " ")
      .trim();
  }

  const REALM_ALIASES = new Map([
    ["Autohaven Wreckers", "Autohaven Wreckers"],
    ["Backwater Swamp", "Backwater Swamp"],
    ["Swamp", "Backwater Swamp"],
    ["Badham", "Springwood"],
    ["Springwood", "Springwood"],
    ["Coldwind", "Coldwind Farm"],
    ["Coldwind Farm", "Coldwind Farm"],
    ["Crotus Pen", "Crotus Prenn Asylum"],
    ["Crotus Prenn Asylum", "Crotus Prenn Asylum"],
    ["Disturbed Ward", "Crotus Prenn Asylum"],
    ["Borgo", "The Decimated Borgo"],
    ["Decimated Borgo", "The Decimated Borgo"],
    ["The Decimated Borgo", "The Decimated Borgo"],
    ["Dvarka Deepwood", "Dvarka Deepwood"],
    ["Forsaken Boneyard", "Forsaken Boneyard"],
    ["Gideon Meat Plant", "Gideon Meat Plant"],
    ["Grave of Glenvale", "Grave of Glenvale"],
    ["Haddonfield", "Haddonfield"],
    ["Hawkins National Laboratory", "Hawkins National Laboratory"],
    ["Lary's Memorial Institute", "Lery's Memorial Institute"],
    ["Lery's Memorial Institute", "Lery's Memorial Institute"],
    ["MacMillan Estate", "The MacMillan Estate"],
    ["McMillan", "The MacMillan Estate"],
    ["The Macmillan Estate", "The MacMillan Estate"],
    ["The MacMillan Estate", "The MacMillan Estate"],
    ["Ormond", "Ormond"],
    ["Raccoon City", "Raccoon City"],
    ["Red Forest", "Red Forest"],
    ["Silent Hill", "Silent Hill"],
    ["Sleepless District", "Sleepless District"],
    ["Withered Isle", "Withered Isle"],
    ["Yamaoka", "Yamaoka Estate"],
    ["Yamaoka Estate", "Yamaoka Estate"],
  ]);

  const REALM_ORDER = [
    "Autohaven Wreckers",
    "Backwater Swamp",
    "Coldwind Farm",
    "Crotus Prenn Asylum",
    "Dvarka Deepwood",
    "Forsaken Boneyard",
    "Gideon Meat Plant",
    "Grave of Glenvale",
    "Haddonfield",
    "Hawkins National Laboratory",
    "Lery's Memorial Institute",
    "The MacMillan Estate",
    "Ormond",
    "Raccoon City",
    "Red Forest",
    "Silent Hill",
    "Springwood",
    "The Decimated Borgo",
    "Withered Isle",
    "Yamaoka Estate",
    "Sleepless District",
  ];

  function canonicalRealm(map) {
    if (map.realm === "Other") {
      const name = cleanText(map.name);
      if (name.includes("dead dawg")) return "Grave of Glenvale";
      if (name.includes("haddonfield") || name.includes("lampkin")) return "Haddonfield";
      if (name.includes("hawkins") || name.includes("underground complex")) return "Hawkins National Laboratory";
      if (name.includes("lery") || name.includes("treatment theatre")) return "Lery's Memorial Institute";
      if (name.includes("midwich")) return "Silent Hill";
      if (name.includes("the game")) return "Gideon Meat Plant";
    }

    return REALM_ALIASES.get(map.realm) || map.realm;
  }

  function realmSortValue(realm) {
    const index = REALM_ORDER.indexOf(realm);
    return index === -1 ? REALM_ORDER.length : index;
  }

  function creatorList() {
    const artists = unique(maps.map((map) => map.artist));
    const preferred = ["KaiserAleex", "Hens333", "SamoelColt", "EagerFace"];
    return artists.sort((a, b) => {
      const ai = preferred.indexOf(a);
      const bi = preferred.indexOf(b);
      if (ai !== -1 || bi !== -1) return (ai === -1 ? 99 : ai) - (bi === -1 ? 99 : bi);
      return a.localeCompare(b);
    });
  }

  function canonicalRealmList() {
    return unique(maps.map(canonicalRealm)).sort((a, b) => {
      const diff = realmSortValue(a) - realmSortValue(b);
      return diff || a.localeCompare(b);
    });
  }

  function syncFilterInputs() {
    els.artist.value = state.artist === "all" ? "" : state.artist;
    els.realm.value = state.realm === "all" ? "" : state.realm;
  }

  function updateArtistFilter(value) {
    const text = value.trim();
    const match = creatorList().find((artist) => cleanText(artist) === cleanText(text));
    if (!text || cleanText(text) === "all sets") {
      state.artist = "all";
      state.artistQuery = "";
    } else if (match) {
      state.artist = match;
      state.artistQuery = "";
    } else {
      state.artist = "all";
      state.artistQuery = cleanText(text);
    }
  }

  function updateRealmFilter(value) {
    const text = value.trim();
    const match = canonicalRealmList().find((realm) => cleanText(realm) === cleanText(text));
    if (!text || cleanText(text) === "all realms") {
      state.realm = "all";
      state.realmQuery = "";
    } else if (match) {
      state.realm = match;
      state.realmQuery = "";
    } else {
      state.realm = "all";
      state.realmQuery = cleanText(text);
    }
  }

  function imageUrl(map) {
    return encodeURI(map.path).replace(/#/g, "%23");
  }

  function setStatus(text, type = "") {
    els.status.textContent = text;
    els.status.className = `status ${type}`.trim();
  }

  function setAutoStatus(text) {
    els.autoStatus.textContent = text || "";
  }

  function sanitizeRoom(value) {
    return String(value || "")
      .toUpperCase()
      .replace(/[^A-Z0-9]/g, "")
      .slice(0, 8);
  }

  function randomRoom() {
    return Math.random().toString(36).slice(2, 6).toUpperCase();
  }

  function defaultIndex() {
    const fromUrl = Number(params.get("m"));
    if (Number.isInteger(fromUrl) && maps[fromUrl]) return fromUrl;
    const kaiser = maps.findIndex((map) => map.artist === "KaiserAleex");
    return kaiser >= 0 ? kaiser : 0;
  }

  function initialFilterValue(paramName, fallback, allowed) {
    const value = params.get(paramName);
    if (!value) return fallback;
    return allowed.includes(value) ? value : fallback;
  }

  function syncControls(active) {
    els.leave.disabled = !active;
    els.invite.disabled = !active || !state.sync.room;
    els.host.disabled = active;
    els.join.disabled = active;
  }

  function populateRealms() {
    const realms = canonicalRealmList();
    if (state.realm !== "all" && !realms.includes(state.realm)) {
      state.realm = "all";
      state.realmQuery = "";
    }
  }

  function closeSuggestions() {
    els.suggestionMenu.hidden = true;
    els.suggestionMenu.replaceChildren();
  }

  function showSuggestions(input, options, onPick, queryText = input.value) {
    const query = cleanText(queryText);
    const visible = options
      .filter((option) => !query || cleanText(option).includes(query))
      .slice(0, 80);

    if (!visible.length) {
      closeSuggestions();
      return;
    }

    const rect = input.getBoundingClientRect();
    els.suggestionMenu.style.left = `${Math.round(rect.left)}px`;
    els.suggestionMenu.style.top = `${Math.round(rect.bottom + 4)}px`;
    els.suggestionMenu.style.width = `${Math.round(rect.width)}px`;

    const buttons = visible.map((option) => {
      const button = document.createElement("button");
      button.type = "button";
      button.textContent = option;
      button.addEventListener("mousedown", (event) => {
        event.preventDefault();
        onPick(option);
        closeSuggestions();
      });
      return button;
    });

    els.suggestionMenu.replaceChildren(...buttons);
    els.suggestionMenu.hidden = false;
  }

  function attachSuggestionMenu(input, getOptions, onPick) {
    const openAll = () => showSuggestions(input, getOptions(), onPick, "");
    const openFiltered = () => showSuggestions(input, getOptions(), onPick);
    input.addEventListener("focus", openAll);
    input.addEventListener("click", openAll);
    input.addEventListener("input", openFiltered);
    input.addEventListener("keydown", (event) => {
      if (event.key === "Escape") closeSuggestions();
      if (event.key === "ArrowDown") {
        event.preventDefault();
        openAll();
        els.suggestionMenu.querySelector("button")?.focus();
      }
    });
  }

  function filteredIndexes() {
    const query = cleanText(state.query);
    const tokens = query ? query.split(" ") : [];

    return maps
      .map((map, index) => ({ map, index }))
      .filter(({ map }) => {
        if (state.artist !== "all" && map.artist !== state.artist) return false;
        if (state.artistQuery && !cleanText(map.artist).includes(state.artistQuery)) return false;
        const realm = canonicalRealm(map);
        if (state.realm !== "all" && realm !== state.realm) return false;
        if (state.realmQuery && !cleanText(realm).includes(state.realmQuery)) return false;
        if (!tokens.length) return true;
        const haystack = cleanText(`${map.name} ${realm} ${map.realm} ${map.artist}`);
        return tokens.every((token) => haystack.includes(token));
      })
      .sort((a, b) => {
        const realmDiff = realmSortValue(canonicalRealm(a.map)) - realmSortValue(canonicalRealm(b.map));
        if (realmDiff) return realmDiff;
        const nameDiff = a.map.name.localeCompare(b.map.name);
        if (nameDiff) return nameDiff;
        return a.map.artist.localeCompare(b.map.artist);
      })
      .map(({ index }) => index);
  }

  function renderList() {
    const indexes = filteredIndexes();
    const rows = indexes.map((index) => {
      const map = maps[index];
      const row = document.createElement("button");
      row.type = "button";
      row.className = `map-row${index === state.current ? " active" : ""}`;
      row.addEventListener("click", () => setCurrent(index));

      const thumb = document.createElement("img");
      thumb.loading = "lazy";
      thumb.src = imageUrl(map);
      thumb.alt = "";

      const text = document.createElement("span");
      const name = document.createElement("strong");
      name.textContent = map.name;
      const meta = document.createElement("span");
      const realm = canonicalRealm(map);
      meta.textContent = state.artist === "all" ? `${map.artist} - ${realm}` : `${realm} - ${map.artist}`;
      text.append(name, meta);
      row.append(thumb, text);
      return row;
    });

    els.list.replaceChildren(...rows);
    els.count.textContent = `${indexes.length} shown / ${maps.length}`;
  }

  function renderCurrent() {
    const map = maps[state.current];
    if (!map) return;

    els.image.src = imageUrl(map);
    els.image.alt = `${map.name} clock callout map`;
    els.title.textContent = map.name;
    els.overlayTitle.textContent = map.name;
    els.meta.textContent = `${canonicalRealm(map)} - ${map.artist}`;

    renderList();
  }

  function updateUrl() {
    const next = new URL(window.location.href);
    next.searchParams.set("m", String(state.current));
    if (state.clean) next.searchParams.set("ui", "0");
    else next.searchParams.delete("ui");
    if (state.artist && state.artist !== "all") next.searchParams.set("set", state.artist);
    else next.searchParams.delete("set");
    if (state.realm && state.realm !== "all") next.searchParams.set("realm", state.realm);
    else next.searchParams.delete("realm");
    if (state.sync.room) next.searchParams.set("room", state.sync.room);
    window.history.replaceState(null, "", next);
  }

  function setCurrent(index, options = {}) {
    if (!maps[index]) return;
    state.current = index;
    renderCurrent();
    updateUrl();
    if (!options.remote && !options.silent) sendSelection(index);
  }

  function chooseFirstFiltered() {
    const indexes = filteredIndexes();
    if (indexes.length && !indexes.includes(state.current)) {
      setCurrent(indexes[0]);
    } else {
      renderList();
    }
  }

  function selectionPayload(index) {
    const map = maps[index];
    return {
      type: "state",
      index,
      map: map?.path || "",
      name: map?.name || "",
      artist: map?.artist || "",
      realm: map ? canonicalRealm(map) : "",
      at: Date.now(),
      clientId,
    };
  }

  async function backendAvailable() {
    if (window.location.protocol === "file:") return false;
    const controller = new AbortController();
    const timer = window.setTimeout(() => controller.abort(), 5000);
    try {
      const response = await fetch("/api/health", {
        cache: "no-store",
        signal: controller.signal,
      });
      if (!response.ok) return false;
      const body = await response.json();
      return body && body.ok === true;
    } catch {
      return false;
    } finally {
      window.clearTimeout(timer);
    }
  }

  async function postBackendState(index) {
    if (state.sync.mode !== "backend" || !state.sync.room) return;
    try {
      await fetch(`/api/rooms/${state.sync.room}/state`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(selectionPayload(index)),
      });
    } catch {
      setStatus("Backend lost", "error");
    }
  }

  function openBackendEvents(room, role) {
    if (state.sync.eventSource) state.sync.eventSource.close();
    const source = new EventSource(`/api/rooms/${room}/events?client=${clientId}`);
    state.sync.eventSource = source;

    source.onopen = () => {
      setStatus(`server ${role} ${room}`, "live");
      if (role === "host") {
        postBackendState(state.current);
      }
    };

    source.addEventListener("state", (event) => {
      const payload = JSON.parse(event.data);
      if (payload.clientId === clientId) return;
      if (Number.isInteger(payload.index) && maps[payload.index]) {
        setCurrent(payload.index, { remote: true });
      }
    });

    source.addEventListener("hello", () => {
      setStatus(`server ${role} ${room}`, "live");
    });

    source.onerror = () => {
      if (state.sync.mode !== "backend") return;
      setStatus(`server ${role} reconnecting`, "error");
      source.close();
      if (state.sync.reconnectTimer) return;
      state.sync.reconnectTimer = window.setTimeout(() => {
        state.sync.reconnectTimer = null;
        if (state.sync.mode === "backend" && state.sync.room === room) {
          openBackendEvents(room, role);
        }
      }, 1500);
    };
  }

  function startBackendRoom(room, role) {
    cleanupSync();
    state.sync.mode = "backend";
    state.sync.role = role;
    state.sync.room = room;
    els.room.value = room;
    syncControls(true);
    setStatus(`server ${role} ${room}`, "live");

    openBackendEvents(room, role);

    if (role === "host") {
      postBackendState(state.current);
    }

    updateUrl();
  }

  function refreshBackendConnection() {
    if (state.sync.mode !== "backend" || !state.sync.room) return;
    const closed = !state.sync.eventSource || state.sync.eventSource.readyState === EventSource.CLOSED;
    if (closed) {
      openBackendEvents(state.sync.room, state.sync.role);
    }
    if (state.sync.role === "host") {
      postBackendState(state.current);
    }
  }

  function peerId(room) {
    return `dbd-clock-${room.toLowerCase()}`;
  }

  async function startPeerHost(room) {
    cleanupSync();
    if (!(await loadPeer())) {
      setStatus("No sync lib", "error");
      return;
    }

    state.sync.mode = "peer-host";
    state.sync.role = "host";
    state.sync.room = room;
    els.room.value = room;
    syncControls(true);
    setStatus("Opening room");

    const peer = new Peer(peerId(room), { debug: 0 });
    state.sync.peer = peer;

    peer.on("open", () => {
      setStatus(`peer host ${room}`, "live");
      updateUrl();
    });

    peer.on("connection", (conn) => {
      state.sync.conns.push(conn);
      conn.on("open", () => conn.send(selectionPayload(state.current)));
      conn.on("data", (payload) => {
        if (payload?.type === "set-map" && Number.isInteger(payload.index) && maps[payload.index]) {
          setCurrent(payload.index, { remote: true });
          broadcastPeer(selectionPayload(payload.index));
        }
      });
      conn.on("close", () => {
        state.sync.conns = state.sync.conns.filter((item) => item !== conn);
      });
    });

    peer.on("error", (error) => {
      setStatus(error.type === "unavailable-id" ? "Room busy" : "Peer error", "error");
    });
  }

  async function startPeerJoin(room) {
    cleanupSync();
    if (!(await loadPeer())) {
      setStatus("No sync lib", "error");
      return;
    }

    state.sync.mode = "peer-join";
    state.sync.role = "join";
    state.sync.room = room;
    els.room.value = room;
    syncControls(true);
    setStatus("Joining");

    const peer = new Peer(null, { debug: 0 });
    state.sync.peer = peer;

    peer.on("open", () => {
      const conn = peer.connect(peerId(room), { reliable: true });
      state.sync.conns = [conn];
      conn.on("open", () => {
        setStatus(`peer join ${room}`, "live");
        updateUrl();
      });
      conn.on("data", (payload) => {
        if (payload?.type === "state" && Number.isInteger(payload.index) && maps[payload.index]) {
          setCurrent(payload.index, { remote: true });
        }
      });
      conn.on("close", () => setStatus("Peer closed", "error"));
      conn.on("error", () => setStatus("Peer error", "error"));
    });

    peer.on("error", () => setStatus("Join failed", "error"));
  }

  function broadcastPeer(payload) {
    state.sync.conns.forEach((conn) => {
      if (conn.open) conn.send(payload);
    });
  }

  function sendSelection(index) {
    if (state.sync.mode === "backend") {
      postBackendState(index);
      return;
    }

    if (state.sync.mode === "peer-host") {
      broadcastPeer(selectionPayload(index));
      return;
    }

    if (state.sync.mode === "peer-join") {
      state.sync.conns.forEach((conn) => {
        if (conn.open) conn.send({ type: "set-map", index, clientId, at: Date.now() });
      });
    }
  }

  function cleanupSync() {
    if (state.sync.reconnectTimer) window.clearTimeout(state.sync.reconnectTimer);
    if (state.sync.eventSource) state.sync.eventSource.close();
    if (state.sync.peer) state.sync.peer.destroy();
    state.sync.conns.forEach((conn) => conn.close?.());
    state.sync.mode = "offline";
    state.sync.role = "";
    state.sync.room = "";
    state.sync.peer = null;
    state.sync.conns = [];
    state.sync.eventSource = null;
    state.sync.reconnectTimer = null;
    syncControls(false);
    setStatus("Offline");
  }

  async function hostRoom() {
    const room = sanitizeRoom(els.room.value) || randomRoom();
    els.room.value = room;
    if (await backendAvailable()) startBackendRoom(room, "host");
    else await startPeerHost(room);
  }

  async function joinRoom() {
    const room = sanitizeRoom(els.room.value);
    if (!room) {
      setStatus("Need room", "error");
      return;
    }
    els.room.value = room;
    if (await backendAvailable()) startBackendRoom(room, "join");
    else await startPeerJoin(room);
  }

  async function copyText(value, label) {
    try {
      await navigator.clipboard.writeText(value);
      setStatus(label, "live");
    } catch {
      setStatus("Copy failed", "error");
    }
  }

  function copyStateLink() {
    const url = new URL(window.location.href);
    url.searchParams.set("m", String(state.current));
    url.searchParams.delete("join");
    copyText(url.toString(), "Link copied");
  }

  function copyInviteLink() {
    if (!state.sync.room) return;
    const url = new URL(window.location.href);
    url.searchParams.set("room", state.sync.room);
    url.searchParams.set("join", "1");
    url.searchParams.set("ui", "0");
    url.searchParams.set("m", String(state.current));
    copyText(url.toString(), "Invite copied");
  }

  function setCleanMode(clean) {
    state.clean = clean;
    document.body.classList.toggle("clean", clean);
    els.clean.textContent = clean ? "UI" : "Clean";
    updateUrl();
  }

  function setImageOpacity(value) {
    const opacity = Math.min(100, Math.max(20, Number(value) || 100));
    els.opacity.value = String(opacity);
    els.overlayOpacity.value = String(opacity);
    document.documentElement.style.setProperty("--image-opacity", String(opacity / 100));
  }

  async function loadTesseract() {
    if (window.Tesseract) return;
    await new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = "https://cdn.jsdelivr.net/npm/tesseract.js@5/dist/tesseract.min.js";
      script.async = true;
      script.onload = resolve;
      script.onerror = reject;
      document.head.appendChild(script);
    });
  }

  async function loadPeer() {
    if (window.Peer) return true;
    try {
      await new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = "https://unpkg.com/peerjs@1.5.5/dist/peerjs.min.js";
        script.async = true;
        script.onload = resolve;
        script.onerror = reject;
        document.head.appendChild(script);
      });
      return Boolean(window.Peer);
    } catch {
      return false;
    }
  }

  function scoreMap(map, text) {
    const name = cleanText(map.name);
    const realm = cleanText(canonicalRealm(map));
    const haystack = cleanText(text);
    if (!haystack) return 0;

    let score = 0;
    if (name && haystack.includes(name)) score += 120;
    if (realm && haystack.includes(realm)) score += 35;

    const nameTokens = name.split(" ").filter((token) => token.length > 2);
    for (const token of nameTokens) {
      if (haystack.includes(token)) score += token.length >= 6 ? 18 : 10;
    }

    const realmTokens = realm.split(" ").filter((token) => token.length > 3);
    for (const token of realmTokens) {
      if (haystack.includes(token)) score += 5;
    }

    return score;
  }

  function bestOcrMatch(text) {
    const candidates = maps
      .map((map, index) => ({ index, score: scoreMap(map, text) }))
      .sort((a, b) => b.score - a.score);

    const best = candidates[0];
    const second = candidates[1];
    if (!best || best.score < 34) return null;
    if (second && best.score - second.score < 8 && best.score < 80) return null;
    return best;
  }

  async function scanFrame() {
    if (!state.auto.running || state.auto.busy || !state.auto.worker) return;
    state.auto.busy = true;
    try {
      const video = els.video;
      if (!video.videoWidth || !video.videoHeight) return;

      const maxWidth = 960;
      const scale = Math.min(1, maxWidth / video.videoWidth);
      const width = Math.round(video.videoWidth * scale);
      const height = Math.round(video.videoHeight * scale);
      els.canvas.width = width;
      els.canvas.height = height;
      const ctx = els.canvas.getContext("2d");
      ctx.drawImage(video, 0, 0, width, height);

      setAutoStatus("Scanning");
      const result = await state.auto.worker.recognize(els.canvas);
      const match = bestOcrMatch(result.data.text);
      if (match && maps[match.index]) {
        setCurrent(match.index);
        setAutoStatus(`Auto ${maps[match.index].name}`);
      } else {
        setAutoStatus("Auto waiting");
      }
    } catch {
      setAutoStatus("Auto error");
    } finally {
      state.auto.busy = false;
    }
  }

  async function startAuto() {
    if (!navigator.mediaDevices?.getDisplayMedia) {
      setAutoStatus("Capture unavailable");
      return;
    }

    try {
      setAutoStatus("Loading OCR");
      await loadTesseract();
      if (!state.auto.worker) {
        state.auto.worker = await window.Tesseract.createWorker("eng");
      }

      state.auto.stream = await navigator.mediaDevices.getDisplayMedia({
        video: { frameRate: 2 },
        audio: false,
      });
      els.video.srcObject = state.auto.stream;
      await els.video.play();

      state.auto.running = true;
      els.auto.textContent = "Stop";
      setAutoStatus("Auto on");
      state.auto.timer = window.setInterval(scanFrame, 8000);
      scanFrame();

      state.auto.stream.getVideoTracks()[0]?.addEventListener("ended", stopAuto);
    } catch {
      setAutoStatus("Auto denied");
      stopAuto();
    }
  }

  async function stopAuto() {
    state.auto.running = false;
    if (state.auto.timer) window.clearInterval(state.auto.timer);
    state.auto.timer = null;
    if (state.auto.stream) {
      state.auto.stream.getTracks().forEach((track) => track.stop());
    }
    state.auto.stream = null;
    els.video.srcObject = null;
    els.auto.textContent = "Auto";
    setAutoStatus("");
  }

  function bindEvents() {
    const onArtistInput = () => {
      updateArtistFilter(els.artist.value);
      chooseFirstFiltered();
    };

    const onRealmInput = () => {
      updateRealmFilter(els.realm.value);
      chooseFirstFiltered();
    };

    els.artist.addEventListener("input", onArtistInput);
    els.artist.addEventListener("change", onArtistInput);

    els.realm.addEventListener("input", onRealmInput);
    els.realm.addEventListener("change", onRealmInput);

    els.search.addEventListener("input", () => {
      state.query = els.search.value;
      renderList();
    });

    attachSuggestionMenu(els.artist, () => ["All sets", ...creatorList()], (value) => {
      els.artist.value = value === "All sets" ? "" : value;
      updateArtistFilter(value);
      chooseFirstFiltered();
    });

    attachSuggestionMenu(els.realm, () => ["All realms", ...canonicalRealmList()], (value) => {
      els.realm.value = value === "All realms" ? "" : value;
      updateRealmFilter(value);
      chooseFirstFiltered();
    });

    attachSuggestionMenu(els.search, () => ["All maps", ...unique(maps.map((map) => map.name))], (value) => {
      els.search.value = value === "All maps" ? "" : value;
      state.query = els.search.value;
      const exact = filteredIndexes().find((index) => cleanText(maps[index].name) === cleanText(value));
      if (Number.isInteger(exact)) setCurrent(exact);
      else renderList();
    });

    els.prev.addEventListener("click", () => {
      const indexes = filteredIndexes();
      const pos = indexes.indexOf(state.current);
      const next = pos > 0 ? indexes[pos - 1] : indexes[indexes.length - 1];
      if (Number.isInteger(next)) setCurrent(next);
    });

    els.next.addEventListener("click", () => {
      const indexes = filteredIndexes();
      const pos = indexes.indexOf(state.current);
      const next = pos >= 0 && pos < indexes.length - 1 ? indexes[pos + 1] : indexes[0];
      if (Number.isInteger(next)) setCurrent(next);
    });

    els.room.addEventListener("input", () => {
      els.room.value = sanitizeRoom(els.room.value);
    });

    els.host.addEventListener("click", hostRoom);
    els.join.addEventListener("click", joinRoom);
    els.leave.addEventListener("click", cleanupSync);
    els.copy.addEventListener("click", copyStateLink);
    els.invite.addEventListener("click", copyInviteLink);
    els.clean.addEventListener("click", () => setCleanMode(!state.clean));
    els.auto.addEventListener("click", () => {
      if (state.auto.running) stopAuto();
      else startAuto();
    });

    els.opacity.addEventListener("input", () => setImageOpacity(els.opacity.value));
    els.overlayOpacity.addEventListener("input", () => setImageOpacity(els.overlayOpacity.value));
    els.overlayLess.addEventListener("click", () => setImageOpacity(Number(els.overlayOpacity.value) - 10));
    els.overlayMore.addEventListener("click", () => setImageOpacity(Number(els.overlayOpacity.value) + 10));
    els.overlayUi.addEventListener("click", () => setCleanMode(false));

    window.addEventListener("keydown", (event) => {
      if (event.key === "Escape" || event.key.toLowerCase() === "h") setCleanMode(!state.clean);
      if (event.key === "ArrowLeft") els.prev.click();
      if (event.key === "ArrowRight") els.next.click();
    });

    window.addEventListener("online", refreshBackendConnection);
    document.addEventListener("visibilitychange", () => {
      if (!document.hidden) refreshBackendConnection();
    });

    document.addEventListener("mousedown", (event) => {
      if (els.suggestionMenu.hidden) return;
      if (els.suggestionMenu.contains(event.target)) return;
      if ([els.artist, els.realm, els.search].includes(event.target)) return;
      closeSuggestions();
    });
  }

  function init() {
    if (!maps.length) {
      els.title.textContent = "No maps found";
      return;
    }

    state.current = defaultIndex();
    const artists = ["all", ...creatorList()];
    const realms = ["all", ...unique(maps.map(canonicalRealm))];
    state.artist = initialFilterValue("set", "all", artists);
    state.realm = initialFilterValue("realm", "all", realms);
    populateRealms();
    syncFilterInputs();
    syncControls(false);
    setCleanMode(state.clean);
    bindEvents();
    setCurrent(state.current, { silent: true });

    const room = sanitizeRoom(params.get("room"));
    if (room) {
      els.room.value = room;
      if (params.get("join") === "1") {
        window.setTimeout(joinRoom, 250);
      }
    }
  }

  init();
})();
