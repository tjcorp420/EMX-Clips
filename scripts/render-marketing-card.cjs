const path = require("node:path");
const { chromium } = require("playwright");

async function main() {
  const root = path.resolve(__dirname, "..");
  const input = path.join(root, "marketing", "emx-clips-release-card.html");
  const output = path.join(root, "marketing", "emx-clips-release-card-v0.1.5.png");

  const browser = await chromium.launch({
    executablePath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    args: ["--allow-file-access-from-files"]
  });

  try {
    const page = await browser.newPage({
      viewport: { width: 1080, height: 1920 },
      deviceScaleFactor: 1
    });

    await page.goto(`file://${input.replaceAll("\\", "/")}`, { waitUntil: "networkidle" });
    await page.screenshot({ path: output, type: "png", fullPage: false });
    console.log(output);
  } finally {
    await browser.close();
  }
}

main().catch(error => {
  console.error(error);
  process.exit(1);
});
