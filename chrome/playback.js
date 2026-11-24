const routes = new Map();
let changes = Promise.resolve();

function serialize(action) {
  const result = changes.then(action);
  changes = result.catch(() => {});
  return result;
}

async function listOutputs() {
  const devices = await navigator.mediaDevices.enumerateDevices();
  return devices.filter(device => device.kind === "audiooutput" &&
    device.deviceId && device.deviceId !== "default" && device.deviceId !== "communications")
    .map((device, index) => ({ Id: device.deviceId, Name: device.label || ("Output " + (index + 1)) }));
}

function stop(id) {
  const route = routes.get(id);
  if (!route) return;
  routes.delete(id);
  for (const track of route.stream.getTracks()) track.stop();
  void route.context.close();
}

async function start(id, streamId) {
  if (routes.has(id)) return;
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: { mandatory: { chromeMediaSource: "tab", chromeMediaSourceId: streamId } },
    video: false
  });
  const context = new AudioContext();
  try {
    context.createMediaStreamSource(stream).connect(context.destination);
    await context.resume();
    routes.set(id, { stream, context, selected: null, status: "Chrome default" });
    for (const track of stream.getTracks()) track.addEventListener("ended", () => {
      stop(id);
      void chrome.runtime.sendMessage({ kind: "playbackChanged" });
    });
  } catch (error) {
    for (const track of stream.getTracks()) track.stop();
    void context.close();
    throw error;
  }
}

async function select(id, output) {
  const route = routes.get(id);
  if (!route) return;
  const devices = await listOutputs();
  const device = devices.find(item => item.Id === output);
  try {
    if (output && !device) throw new Error("That output is unavailable.");
    if (typeof route.context.setSinkId !== "function") throw new Error("Chrome output switching is unavailable.");
    await route.context.setSinkId(output || "");
    if (route.context.state === "suspended") await route.context.resume();
    route.selected = output || null;
    route.status = device ? device.Name : "Chrome default";
  } catch (error) {
    route.status = error.name === "NotAllowedError"
      ? "Allow devices in the companion options." : error.message;
  }
}

navigator.mediaDevices.addEventListener("devicechange", () => serialize(async () => {
  const devices = await listOutputs();
  for (const [id, route] of routes) {
    if (route.selected && !devices.some(device => device.Id === route.selected)) {
      try {
        await route.context.setSinkId("");
        route.selected = null;
        route.status = "Output disconnected. Using Chrome default.";
      } catch {
        stop(id); // Restores ordinary tab playback.
      }
    }
  }
  void chrome.runtime.sendMessage({ kind: "playbackChanged" });
}).catch(() => {}));

chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (message.target !== "playback") return;
  serialize(async () => {
    switch (message.kind) {
      case "start": await start(message.id, message.streamId); break;
      case "stop": stop(message.id); break;
      case "select": await select(message.id, message.output); break;
      case "state": {
        const Devices = await listOutputs();
        return [...routes].map(([Id, route]) => ({ Id, Devices, Selected: route.selected, Status: Devices.length || route.selected ? route.status : "Allow playback devices in Chrome options" }));
      }
    }
    return true;
  }).then(value => reply({ value }), error => reply({ error: error.message }));
  return true;
});
