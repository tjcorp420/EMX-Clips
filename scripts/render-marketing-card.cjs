const path = require("node:path");
const { mkdtempSync } = require("node:fs");
const { tmpdir } = require("node:os");
const { spawnSync } = require("node:child_process");

async function main() {
  const root = path.resolve(__dirname, "..");
  const input = path.join(root, "marketing", "emx-clips-release-card.html");
  const output = path.join(root, "marketing", "emx-clips-release-card-v0.1.8.png");
  const chrome = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
  const profile = mkdtempSync(path.join(tmpdir(), "emx-card-chrome-"));

  const result = spawnSync(chrome, [
    "--headless=new",
    "--allow-file-access-from-files",
    "--disable-gpu",
    "--force-device-scale-factor=1",
    "--hide-scrollbars",
    `--user-data-dir=${profile}`,
    "--window-size=1080,1920",
    `--screenshot=${output}`,
    `file://${input.replaceAll("\\", "/")}`
  ], { encoding: "utf8" });

  if (result.status !== 0) {
    throw new Error(result.stderr || result.stdout || `Chrome exited with ${result.status}`);
  }

  console.log(output);
}

main().catch(error => {
  console.error(error);
  process.exit(1);
});
