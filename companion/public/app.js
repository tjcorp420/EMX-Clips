const latestReleaseUrl = "https://github.com/tjcorp420/EMX-Clips/releases/latest";
const installButton = document.querySelector("#installButton");
const installTitle = document.querySelector("#installTitle");
const installText = document.querySelector("#installText");
const openPcPortal = document.querySelector("#openPcPortal");
const pcPortalUrl = document.querySelector("#pcPortalUrl");
const copyLink = document.querySelector("#copyLink");
const clipList = document.querySelector("#clipList");
const scanQrButton = document.querySelector("#scanQrButton");
const stopQrButton = document.querySelector("#stopQrButton");
const scannerFrame = document.querySelector("#scannerFrame");
const scannerVideo = document.querySelector("#scannerVideo");
const scannerCanvas = document.querySelector("#scannerCanvas");
const scannerStatus = document.querySelector("#scannerStatus");
const query = new URLSearchParams(window.location.search);
let currentPortalUrl = query.get("portal") || query.get("pc") || "";
const cloudIndexUrl = query.get("cloudIndex") || "";
let scannerStream = null;
let scannerFrameId = 0;
let scannerActive = false;

const clips = [
  { title: "Replay 00:30", meta: "Ready for preview and share" },
  { title: "Replay 01:00", meta: "MP4 export for CapCut" },
  { title: "Latest clip", meta: "Phone sharing is live from the PC app" }
];

let installPrompt = null;

if (cloudIndexUrl) {
  loadCloudClips(cloudIndexUrl);
} else {
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

if (currentPortalUrl && pcPortalUrl) {
  pairPortal(currentPortalUrl, "Connected to EMX Clips. Opening your PC clip library now...", true);
}

if (cloudIndexUrl) {
  installTitle.textContent = "Cloud library connected";
  installText.textContent = "Your clips are loading inside the EMX Companion app from Firebase Cloud Share.";
  setScannerStatus("Firebase Cloud Share connected.");
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
      openPcPortal.textContent = currentPortalUrl ? "Open My PC Clips" : "Open PC Clip Portal";
    }, 1600);
    return;
  }

  if (!isHttpUrl(value)) {
    openPcPortal.textContent = "Bad Link";
    setTimeout(() => {
      openPcPortal.textContent = currentPortalUrl ? "Open My PC Clips" : "Open PC Clip Portal";
    }, 1600);
    return;
  }

  currentPortalUrl = value;
  window.location.href = value;
});

scanQrButton?.addEventListener("click", startQrScanner);
stopQrButton?.addEventListener("click", () => stopQrScanner("Scanner stopped."));

copyLink?.addEventListener("click", async () => {
  await navigator.clipboard?.writeText(latestReleaseUrl).catch(() => null);
  copyLink.textContent = "Copied";
  setTimeout(() => {
    copyLink.textContent = "Copy Release Link";
  }, 1800);
});

function pairPortal(url, message, autoOpen = false) {
  currentPortalUrl = url;

  if (pcPortalUrl) {
    pcPortalUrl.value = url;
  }

  if (openPcPortal) {
    openPcPortal.textContent = "Open My PC Clips";
  }

  installTitle.textContent = "PC portal paired";
  installText.textContent = message;
  setScannerStatus(autoOpen ? "Connected. Opening clip library..." : "QR paired. Tap Open My PC Clips.");

  if (autoOpen) {
    window.setTimeout(() => {
      window.location.href = url;
    }, 650);
  }
}

function isHttpUrl(value) {
  try {
    const parsed = new URL(value);
    return parsed.protocol === "http:" || parsed.protocol === "https:";
  } catch {
    return false;
  }
}

function portalFromQr(value) {
  try {
    const parsed = new URL(value.trim());
    const nestedPortal = parsed.searchParams.get("portal") || parsed.searchParams.get("pc");
    if (nestedPortal && isHttpUrl(nestedPortal)) {
      return nestedPortal;
    }

    if (isHttpUrl(parsed.href)) {
      return parsed.href;
    }
  } catch {
    return "";
  }

  return "";
}

function setScannerStatus(message) {
  if (scannerStatus) {
    scannerStatus.textContent = message;
  }
}

async function startQrScanner() {
  if (!navigator.mediaDevices?.getUserMedia) {
    setScannerStatus("Camera scanner needs Safari/Chrome over HTTPS.");
    return;
  }

  if (!window.jsQR) {
    setScannerStatus("Scanner library is still loading. Try again in a second.");
    return;
  }

  try {
    stopQrScanner("");
    setScannerStatus("Camera opening...");
    scannerStream = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: {
        facingMode: { ideal: "environment" },
        width: { ideal: 1280 },
        height: { ideal: 720 }
      }
    });

    scannerFrame.hidden = false;
    scannerVideo.srcObject = scannerStream;
    await scannerVideo.play();
    scannerActive = true;
    scanQrButton.disabled = true;
    stopQrButton.disabled = false;
    setScannerStatus("Point camera at the EMX Clips QR on your PC.");
    scanLoop();
  } catch (error) {
    stopQrScanner("");
    setScannerStatus(error?.name === "NotAllowedError"
      ? "Camera blocked. Allow camera access in Safari/Chrome settings."
      : "Could not open camera scanner.");
  }
}

function scanLoop() {
  if (!scannerActive) {
    return;
  }

  if (scannerVideo.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
    const width = scannerVideo.videoWidth;
    const height = scannerVideo.videoHeight;

    if (width > 0 && height > 0) {
      scannerCanvas.width = width;
      scannerCanvas.height = height;
      const context = scannerCanvas.getContext("2d", { willReadFrequently: true });
      context.drawImage(scannerVideo, 0, 0, width, height);
      const frame = context.getImageData(0, 0, width, height);
      const code = window.jsQR(frame.data, width, height, { inversionAttempts: "attemptBoth" });

      if (code?.data) {
        const portal = portalFromQr(code.data);
        if (portal) {
          pairPortal(portal, "QR scanned. Opening your PC clip library now...", true);
          stopQrScanner("");
          return;
        }

        setScannerStatus("QR found, but it was not an EMX Clips link.");
      }
    }
  }

  scannerFrameId = requestAnimationFrame(scanLoop);
}

function stopQrScanner(message) {
  scannerActive = false;

  if (scannerFrameId) {
    cancelAnimationFrame(scannerFrameId);
    scannerFrameId = 0;
  }

  if (scannerStream) {
    for (const track of scannerStream.getTracks()) {
      track.stop();
    }
    scannerStream = null;
  }

  if (scannerVideo) {
    scannerVideo.pause();
    scannerVideo.srcObject = null;
  }

  if (scannerFrame) {
    scannerFrame.hidden = true;
  }

  if (scanQrButton) {
    scanQrButton.disabled = false;
  }

  if (stopQrButton) {
    stopQrButton.disabled = true;
  }

  if (message) {
    setScannerStatus(message);
  }
}

async function loadCloudClips(indexUrl) {
  clipList.replaceChildren();
  clipList.append(statusRow("Loading cloud clips...", "Firebase Cloud Share"));

  try {
    const response = await fetch(indexUrl, { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Firebase returned ${response.status}`);
    }

    const library = await response.json();
    const cloudClips = Array.isArray(library.clips) ? library.clips : [];
    clipList.replaceChildren();

    if (cloudClips.length === 0) {
      clipList.append(statusRow("No cloud clips found yet.", "Save a clip on PC, then use Phone Share again."));
      return;
    }

    for (const clip of cloudClips) {
      clipList.append(cloudClipRow(clip));
    }
  } catch (error) {
    clipList.replaceChildren();
    clipList.append(statusRow("Could not load cloud clips.", error?.message || "Check Firebase rules and try Phone Share again."));
  }
}

function statusRow(title, meta) {
  const row = document.createElement("div");
  row.className = "clip";
  row.innerHTML = `
    <div class="thumb">EMX</div>
    <div>
      <strong>${title}</strong>
      <span>${meta}</span>
    </div>
  `;
  return row;
}

function cloudClipRow(clip) {
  const row = document.createElement("div");
  row.className = "cloud-clip";
  const url = clip.url || "";
  const name = clip.name || "EMX clip";
  const size = clip.size || "";
  row.innerHTML = `
    <video class="cloud-video" src="${escapeAttr(url)}" controls playsinline preload="metadata"></video>
    <div class="cloud-meta">
      <strong>${escapeHtml(name)}</strong>
      <span>${escapeHtml(size)}</span>
    </div>
    <div class="actions compact">
      <a class="button primary" href="${escapeAttr(url)}" download="${escapeAttr(name)}">Download</a>
      <button class="button" type="button">Share / Save</button>
    </div>
  `;

  const share = row.querySelector("button");
  share?.addEventListener("click", () => shareCloudClip(url, name, clip.contentType || "video/mp4"));
  return row;
}

async function shareCloudClip(url, name, contentType) {
  try {
    const response = await fetch(url);
    const blob = await response.blob();
    const file = new File([blob], name, { type: contentType });
    if (navigator.canShare?.({ files: [file] })) {
      await navigator.share({ files: [file], title: name, text: "EMX Clips" });
      return;
    }
  } catch {
    // Fall back to opening the file below.
  }

  window.open(url, "_blank", "noopener,noreferrer");
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, char => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;"
  })[char]);
}

function escapeAttr(value) {
  return escapeHtml(value);
}
