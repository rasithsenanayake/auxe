document.querySelector("#allow").addEventListener("click", async () => {
  const result = document.querySelector("#result");
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    for (const track of stream.getTracks()) track.stop();
    const devices = await navigator.mediaDevices.enumerateDevices();
    const outputs = devices.filter(device => device.kind === "audiooutput" && device.label);
    result.textContent = outputs.length
      ? "Available outputs: " + outputs.map(device => device.label).join(", ")
      : "Chrome did not expose named outputs.";
    await chrome.runtime.sendMessage({ kind: "devicesChanged" });
  } catch (error) {
    result.textContent = error.message;
  }
});
