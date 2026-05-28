const latestReleaseUrl = "https://github.com/tjcorp420/EMX-Clips/releases/latest";
const installButton = document.querySelector("#installButton");
const installTitle = document.querySelector("#installTitle");
const installText = document.querySelector("#installText");
const shareDemo = document.querySelector("#shareDemo");
const copyLink = document.querySelector("#copyLink");
const clipList = document.querySelector("#clipList");

const clips = [
  { title: "Replay 00:30", meta: "Ready for preview and share" },
  { title: "Replay 01:00", meta: "MP4 export for CapCut" },
  { title: "Latest clip", meta: "Phone pairing lands next" }
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
  installText.textContent = "Open EMX Clips on PC, then use the upcoming Phone Companion QR button to pair this app.";
}

if (isIos && !isStandalone) {
  installButton.textContent = "Show iPhone Steps";
  installTitle.textContent = "Install on iPhone";
  installText.textContent = "Open in Safari, tap Share, scroll down, then tap Add to Home Screen. iOS does not allow one-click PWA installs from a website.";
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

shareDemo?.addEventListener("click", async () => {
  const shareData = {
    title: "EMX Clips",
    text: "Download EMX Clips and pair the phone companion.",
    url: latestReleaseUrl
  };

  if (navigator.share) {
    await navigator.share(shareData).catch(() => null);
    return;
  }

  await navigator.clipboard?.writeText(latestReleaseUrl).catch(() => null);
  shareDemo.textContent = "Link Copied";
});

copyLink?.addEventListener("click", async () => {
  await navigator.clipboard?.writeText(latestReleaseUrl).catch(() => null);
  copyLink.textContent = "Copied";
  setTimeout(() => {
    copyLink.textContent = "Copy Release Link";
  }, 1800);
});
