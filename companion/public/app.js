const latestReleaseUrl = "https://github.com/tjcorp420/EMX-Clips/releases/latest";
const installButton = document.querySelector("#installButton");
const installTitle = document.querySelector("#installTitle");
const installText = document.querySelector("#installText");
const openPcPortal = document.querySelector("#openPcPortal");
const pcPortalUrl = document.querySelector("#pcPortalUrl");
const copyLink = document.querySelector("#copyLink");
const clipList = document.querySelector("#clipList");
const query = new URLSearchParams(window.location.search);
const pairedPortalUrl = query.get("portal") || query.get("pc") || "";

const clips = [
  { title: "Replay 00:30", meta: "Ready for preview and share" },
  { title: "Replay 01:00", meta: "MP4 export for CapCut" },
  { title: "Latest clip", meta: "Phone sharing is live from the PC app" }
];

let installPrompt = null;

for (const clip of clips) {
  const row = document.createElement("div");
  row.className = "clip";
  row.innerHTML = `
    <div class="thumb">EMX</div>
    <div>
      <strong>${clip.title}</strong>
      <span>${clip.meta}</span>
    </div>
  `;
  clipList.append(row);
}

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("/sw.js").catch(() => {});
}

const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent);
const isStandalone = window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone;

if (isStandalone) {
  installTitle.textContent = "Companion installed";
  installText.textContent = "Open EMX Clips on PC, then use Phone Companion to pair this app with your clip portal.";
}

if (isIos && !isStandalone) {
  installButton.textContent = "Show iPhone Steps";
  installTitle.textContent = "Install on iPhone";
  installText.textContent = "Open in Safari, tap Share, scroll down, then tap Add to Home Screen. iOS does not allow one-click PWA installs from a website.";
}

if (pairedPortalUrl && pcPortalUrl) {
  pcPortalUrl.value = pairedPortalUrl;
  openPcPortal.textContent = "Open My PC Clips";
  installTitle.textContent = "PC portal paired";
  installText.textContent = "Your QR came from EMX Clips. Tap Open My PC Clips while your phone is on the same Wi-Fi as the PC.";
}

window.addEventListener("beforeinstallprompt", event => {
  event.preventDefault();
  installPrompt = event;
  installButton.disabled = false;
  installTitle.textContent = "Install available";
  installText.textContent = "Tap Install Companion to add EMX Clips to your home screen or desktop.";
});

installButton?.addEventListener("click", async () => {
  if (installPrompt) {
    installPrompt.prompt();
    await installPrompt.userChoice.catch(() => null);
    installPrompt = null;
    return;
  }

  installTitle.textContent = isIos ? "iPhone install steps" : "Manual install";
  installText.textContent = isIos
    ? "In Safari: tap Share, choose Add to Home Screen, then tap Add."
    : "Use your browser menu and choose Install app. On Chrome/Edge, the install icon may appear in the address bar.";
});

openPcPortal?.addEventListener("click", () => {
  const value = pcPortalUrl?.value.trim();
  if (!value) {
    openPcPortal.textContent = "Paste Link First";
    setTimeout(() => {
      openPcPortal.textContent = "Open PC Clip Portal";
    }, 1600);
    return;
  }

  if (!/^https?:\/\//i.test(value)) {
    openPcPortal.textContent = "Bad Link";
    setTimeout(() => {
      openPcPortal.textContent = pairedPortalUrl ? "Open My PC Clips" : "Open PC Clip Portal";
    }, 1600);
    return;
  }

  window.location.href = value;
});

copyLink?.addEventListener("click", async () => {
  await navigator.clipboard?.writeText(latestReleaseUrl).catch(() => null);
  copyLink.textContent = "Copied";
  setTimeout(() => {
    copyLink.textContent = "Copy Release Link";
  }, 1800);
});
