const host = "com.auxe.companion";
const errors = new Map();
let nativePort;
let queue = Promise.resolve();
let snapshotTimer;
const musicMenu = "auxe-youtube-music";

function enqueue(callback) {
  queue = queue.then(callback).catch(error => console.warn("Auxe:", error.message));
  return queue;
}

async function hasPlayback() {
  return chrome.offscreen.hasDocument();
}

async function playback(kind, details = {}) {
  const reply = await chrome.runtime.sendMessage({ target: "playback", kind, ...details });
  if (reply?.error) throw new Error(reply.error);
  return reply?.value;
}

function publishSoon() {
  clearTimeout(snapshotTimer);
  snapshotTimer = setTimeout(() => enqueue(publish), 120);
}

function connect() {
  if (nativePort) return nativePort;
  const port = chrome.runtime.connectNative(host);
  nativePort = port;
  port.onDisconnect.addListener(() => {
    void chrome.runtime.lastError;
    if (nativePort !== port) return;
    nativePort = undefined;
    enqueue(async () => {
      if (nativePort || !await hasPlayback()) return;
      const captured = await playback("state");
      await chrome.offscreen.closeDocument();
      for (const source of captured) {
        await chrome.action.setBadgeText({ tabId: source.Id, text: "" }).catch(() => {});
      }
    });
  });
  port.onMessage.addListener(message => enqueue(async () => {
    if (!Number.isInteger(message.Id) || (message.Output !== null && typeof message.Output !== "string")) return;
    if (await hasPlayback()) await playback("select", { id: message.Id, output: message.Output });
    await publish();
  }));
  return port;
}

async function publish() {
  const [queried, windows] = await Promise.all([
    chrome.tabs.query({}),
    chrome.windows.getAll({ populate: true, windowTypes: ["normal", "popup", "app"] })
  ]);
  const tabs = new Map(queried.map(tab => [tab.id, tab]));
  const appTabs = new Set();
  for (const window of windows) {
    for (const tab of window.tabs || []) {
      if (Number.isInteger(tab.id)) {
        tabs.set(tab.id, tab);
        if (window.type === "app") appTabs.add(tab.id);
      }
    }
  }
  const captured = await hasPlayback() ? await playback("state") : [];
  const active = new Map(captured.map(source => [source.Id, source]));
  const sources = await Promise.all([...tabs.values()].filter(tab => Number.isInteger(tab.id) &&
    (tab.audible || active.has(tab.id) || tab.url?.startsWith("https://music.youtube.com/")))
    .slice(0, 128).map(async tab => {
      let standalone = false;
      if (tab.url?.startsWith("https://music.youtube.com/")) {
        standalone = appTabs.has(tab.id);
        if (!standalone) {
          try {
            const [check] = await chrome.scripting.executeScript({
              target: { tabId: tab.id },
              func: () => matchMedia("(display-mode: standalone), (display-mode: minimal-ui), (display-mode: window-controls-overlay)").matches
            });
            standalone = Boolean(check?.result);
          } catch { /* A page may still be loading. */ }
        }
      }
      if (!standalone && !tab.audible && !active.has(tab.id)) return null;
      return {
        Id: tab.id,
        Name: standalone ? "YouTube Music" : (tab.title || "Chrome tab").slice(0, 180),
        Standalone: standalone,
        Enabled: active.has(tab.id),
        Status: errors.get(tab.id) || null,
        ...active.get(tab.id)
      };
    }));
  const visible = sources.filter(Boolean);
  if (visible.length) connect().postMessage(visible);
  else if (nativePort) { nativePort.disconnect(); nativePort = undefined; }
  if (!captured.length && await hasPlayback()) await chrome.offscreen.closeDocument();
}

function toggleCapture(tab) {
  if (!Number.isInteger(tab?.id)) return;
  // Request the capture token while Chrome still has the extension invocation.
  const token = chrome.tabCapture.getMediaStreamId({ targetTabId: tab.id })
    .then(streamId => ({ streamId }), error => ({ error }));
  enqueue(async () => {
    try {
      if (await hasPlayback()) {
        const current = await playback("state");
        if (current.some(item => item.Id === tab.id)) {
          await playback("stop", { id: tab.id });
          await chrome.action.setBadgeText({ tabId: tab.id, text: "" });
          return;
        }
      }
      const result = await token;
      if (result.error) throw result.error;
      if (!await hasPlayback()) {
        await chrome.offscreen.createDocument({
          url: "playback.html", reasons: ["USER_MEDIA"],
          justification: "Send tab playback to the local output selected by the user."
        });
      }
      await playback("start", { id: tab.id, streamId: result.streamId });
      errors.delete(tab.id);
      await chrome.action.setBadgeText({ tabId: tab.id, text: "ON" });
    } catch (error) {
      errors.set(tab.id, "Could not enable tab audio: " + error.message);
      await chrome.action.setBadgeText({ tabId: tab.id, text: "!" });
    } finally {
      await publish();
    }
  });
}

chrome.action.onClicked.addListener(toggleCapture);
chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === musicMenu) toggleCapture(tab);
});

chrome.tabs.onUpdated.addListener((id, change) => {
  if ("audible" in change || "title" in change || "status" in change) publishSoon();
});
chrome.tabs.onRemoved.addListener(id => enqueue(async () => {
  errors.delete(id);
  if (await hasPlayback()) await playback("stop", { id });
  await publish();
}));
chrome.windows.onFocusChanged.addListener(publishSoon);
chrome.runtime.onStartup.addListener(publishSoon);
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => chrome.contextMenus.create({
    id: musicMenu, title: "Toggle Auxe output for YouTube Music", contexts: ["page", "audio", "video"],
    documentUrlPatterns: ["https://music.youtube.com/*"]
  }));
  publishSoon();
});
chrome.runtime.onMessage.addListener(message => {
  if (message.kind === "playbackChanged" || message.kind === "devicesChanged") publishSoon();
});
publishSoon();
