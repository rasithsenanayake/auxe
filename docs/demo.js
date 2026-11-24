const outputs = ["System default", "Headset", "Monitor speakers", "Microlab speakers"];
const apps = [
  { id: "chrome", name: "Chrome", mark: "C", output: outputs[2] },
  { id: "discord", name: "Discord", mark: "D", output: outputs[1] },
  { id: "whatsapp", name: "WhatsApp", mark: "W", output: outputs[2] },
  { id: "music", name: "YouTube Music", mark: "M", output: outputs[3] }
];
const tabs = [
  { id: "music-stream", name: "Music stream", mark: "T", output: outputs[1] },
  { id: "video", name: "Video", mark: "T", output: outputs[2] }
];

function windowMarkup(title) {
  return '<div class="demo-heading"><img src="assets/auxe.png" width="27" height="27" alt=""><strong>' +
    title + '</strong><button class="demo-theme" type="button" aria-label="Switch demo to light appearance">Light</button></div>';
}

function appRow(app) {
  return '<div class="demo-row"><span class="demo-mark ' + app.id + '" aria-hidden="true">' + app.mark +
    '</span><div class="demo-identity"><strong>' + app.name + '</strong><small data-current="' + app.id + '">' +
    app.output + '</small></div><select data-route="' + app.id + '" aria-label="' + app.name + ' output">' +
    outputs.map(output => '<option' + (output === app.output ? ' selected' : '') + '>' + output + '</option>').join('') +
    '</select></div>';
}

function routeWindow(open) {
  const chromeTabs = '<details class="demo-tabs"' + (open ? ' open' : '') +
    '><summary>Chrome tabs</summary><div>' + tabs.map(appRow).join('') + '</div></details>';
  return windowMarkup("Auxe") + '<div class="demo-rows">' + appRow(apps[0]) + chromeTabs +
    apps.slice(1).map(appRow).join('') + '</div>';
}

function volumeWindow() {
  const levels = [63, 45, 72];
  return windowMarkup("Device volume") + '<div class="demo-rows">' + outputs.slice(1).map((name, index) => {
    const id = "demo-volume-" + index;
    return '<div class="demo-level"><label for="' + id + '">' + name +
      '</label><div><input id="' + id + '" type="range" min="0" max="100" value="' + levels[index] +
      '"><output for="' + id + '">' + levels[index] + '%</output></div></div>';
  }).join('') + '</div>';
}

for (const host of document.querySelectorAll("[data-demo]")) {
  const panel = document.createElement("div");
  panel.className = "demo-panel";
  const mode = host.dataset.demo;
  panel.innerHTML = mode === "volume" ? volumeWindow() : routeWindow(mode === "tabs");
  host.replaceChildren(panel);

  const theme = panel.querySelector(".demo-theme");
  theme.addEventListener("click", () => {
    const light = panel.classList.toggle("demo-light");
    theme.textContent = light ? "Dark" : "Light";
    theme.setAttribute("aria-label", light ? "Switch demo to dark appearance" : "Switch demo to light appearance");
  });

  panel.addEventListener("change", event => {
    const id = event.target.dataset.route;
    if (!id) return;
    const output = event.target.value;
    for (const select of document.querySelectorAll("select[data-route]")) {
      if (select.dataset.route === id) select.value = output;
    }
    for (const current of document.querySelectorAll("[data-current]")) {
      if (current.dataset.current === id) current.textContent = output;
    }
  });

  panel.addEventListener("input", event => {
    if (event.target.type !== "range") return;
    const level = event.target.parentElement.querySelector("output");
    level.value = event.target.value + "%";
  });
}
