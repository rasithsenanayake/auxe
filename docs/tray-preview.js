(() => {
  const styles = getComputedStyle(document.documentElement);
  if (styles.getPropertyValue("--tray-preview-ready").trim() !== "1") return;

  const names = ["System default", "Headset", "Monitor speakers", "Microlab speakers"];
  const apps = [
    { id: "chrome", name: "Chrome", device: 2, icon: "chrome" },
    { id: "discord", name: "Discord", device: 1, icon: "discord" },
    { id: "whatsapp", name: "WhatsApp", device: 2, icon: "whatsapp" },
    { id: "music", name: "YouTube Music", device: 3, icon: "music" }
  ];
  const tabs = [
    { id: "music-stream", name: "Music stream", device: 1, icon: "tab" },
    { id: "video", name: "Video", device: 2, icon: "tab" }
  ];
  const levels = [63, 45, 72];
  let panelNumber = 0;

  function row(app, controls = "") {
    const options = names.map((name, index) =>
      '<option value="' + index + '"' + (index === app.device ? ' selected' : '') + '>' + name + '</option>'
    ).join("");
    return '<div class="demo-row' + (controls ? ' includes-tabs' : '') +
      '"><span class="demo-app-icon icon-' + app.icon + '" aria-hidden="true"></span>' +
      '<div class="demo-identity"><strong>' + app.name + '</strong><small data-current="' + app.id +
      '">' + names[app.device] + '</small></div><span class="demo-select"><select data-route="' + app.id +
      '" aria-label="' + app.name + ' output">' + options + '</select></span>' + controls + '</div>';
  }

  function routing(id, expanded) {
    const tabButton = '<button type="button" class="demo-expand" aria-controls="' + id +
      '-tabs" aria-expanded="' + expanded + '" aria-label="Chrome tabs" title="Chrome tabs"></button>';
    return row(apps[0], tabButton) + '<div class="demo-tab-list" id="' + id + '-tabs"' + (expanded ? '' : ' hidden') +
      '>' + tabs.map(tab => row(tab)).join("") + '</div>' + apps.slice(1).map(app => row(app)).join("");
  }

  function volume(id) {
    return names.slice(1).map((name, index) => {
      const control = id + "-volume-" + index;
      return '<div class="demo-level"><label for="' + control + '">' + name +
        '</label><div><input id="' + control + '" data-level="' + index + '" type="range" min="0" max="100" value="' +
        levels[index] + '"><output for="' + control + '" data-percent="' + index + '">' + levels[index] + '%</output></div></div>';
    }).join("");
  }

  for (const host of document.querySelectorAll("[data-demo]")) {
    const id = "tray-preview-" + ++panelNumber;
    const volumeFirst = host.dataset.demo === "volume";
    const panel = document.createElement("div");
    panel.className = "demo-panel";
    panel.innerHTML =
      '<div class="demo-heading"><span class="demo-logo" aria-hidden="true"></span><strong>Auxe</strong>' +
      '<div class="demo-toolbar"><button type="button" class="demo-header-button demo-speaker" aria-label="Device volume" title="Device volume"></button>' +
      '<button type="button" class="demo-header-button demo-theme" aria-label="Switch to light appearance" title="Switch appearance"></button>' +
      '<details class="demo-settings"><summary class="demo-header-button demo-gear" aria-label="Demo settings" title="Demo settings"></summary>' +
      '<div class="demo-menu"><label><input type="checkbox" checked>Start with Windows</label>' +
      '<label><input type="checkbox" checked>Automatic updates</label>' +
      '<a href="https://github.com/rasithsenanayake/auxe/releases/latest/download/AuxeSetup.exe">Download latest update</a>' +
      '<a href="#chrome-setup">Chrome companion setup</a></div></details></div></div>' +
      '<div class="demo-rows demo-routing">' + routing(id, host.dataset.demo === "tabs") + '</div>' +
      '<div class="demo-rows demo-volumes" hidden>' + volume(id) + '</div>';
    host.replaceChildren(panel);

    const heading = panel.querySelector(".demo-heading > strong");
    const routes = panel.querySelector(".demo-routing");
    const volumes = panel.querySelector(".demo-volumes");
    const speaker = panel.querySelector(".demo-speaker");
    function showVolume(active) {
      heading.textContent = active ? "Device volume" : "Auxe";
      routes.hidden = active;
      volumes.hidden = !active;
      speaker.setAttribute("aria-pressed", String(active));
    }
    showVolume(volumeFirst);
    speaker.addEventListener("click", () => showVolume(volumes.hidden));

    const theme = panel.querySelector(".demo-theme");
    theme.addEventListener("click", () => {
      const light = panel.classList.toggle("demo-light");
      theme.setAttribute("aria-label", light ? "Switch to dark appearance" : "Switch to light appearance");
    });

    const expand = panel.querySelector(".demo-expand");
    const children = panel.querySelector(".demo-tab-list");
    expand.addEventListener("click", () => {
      children.hidden = !children.hidden;
      expand.setAttribute("aria-expanded", String(!children.hidden));
    });

    const menu = panel.querySelector(".demo-settings");
    menu.querySelectorAll("a").forEach(link => link.addEventListener("click", () => {
      menu.open = false;
      if (link.hash === "#chrome-setup") document.getElementById("chrome-setup").open = true;
    }));
    document.addEventListener("pointerdown", event => {
      if (!menu.contains(event.target)) menu.open = false;
    });
    menu.addEventListener("keydown", event => {
      if (event.key !== "Escape" || !menu.open) return;
      menu.open = false;
      menu.querySelector("summary").focus();
    });

    panel.addEventListener("change", event => {
      const id = event.target.dataset.route;
      if (!id) return;
      const value = event.target.value;
      for (const select of document.querySelectorAll("select[data-route]")) {
        if (select.dataset.route === id) select.value = value;
      }
      for (const current of document.querySelectorAll("[data-current]")) {
        if (current.dataset.current === id) current.textContent = names[Number(value)];
      }
    });

    panel.addEventListener("input", event => {
      const index = event.target.dataset.level;
      if (index === undefined) return;
      const value = event.target.value;
      for (const slider of document.querySelectorAll("[data-level]")) {
        if (slider.dataset.level === index) slider.value = value;
      }
      for (const percent of document.querySelectorAll("[data-percent]")) {
        if (percent.dataset.percent === index) percent.value = value + "%";
      }
    });
  }
})();
