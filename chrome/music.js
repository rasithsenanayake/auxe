chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (message.kind === "musicWindow") {
    reply(matchMedia("(display-mode: standalone)").matches ||
      matchMedia("(display-mode: minimal-ui)").matches ||
      matchMedia("(display-mode: window-controls-overlay)").matches);
  }
});
