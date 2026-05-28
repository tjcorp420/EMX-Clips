const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");

const root = path.join(__dirname, "public");
const port = Number(process.env.PORT || 4177);

const types = new Map([
  [".html", "text/html; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".svg", "image/svg+xml"],
  [".png", "image/png"],
  [".webmanifest", "application/manifest+json; charset=utf-8"]
]);

const server = http.createServer((request, response) => {
  const url = new URL(request.url || "/", `http://${request.headers.host || "localhost"}`);
  let filePath = path.normalize(decodeURIComponent(url.pathname));
  if (filePath === "\\" || filePath === "/") {
    filePath = "index.html";
  }

  const absolutePath = path.join(root, filePath);
  if (!absolutePath.startsWith(root)) {
    response.writeHead(403);
    response.end("Forbidden");
    return;
  }

  fs.readFile(absolutePath, (error, data) => {
    if (error) {
      fs.readFile(path.join(root, "index.html"), (fallbackError, fallback) => {
        if (fallbackError) {
          response.writeHead(404);
          response.end("Not found");
          return;
        }

        response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
        response.end(fallback);
      });
      return;
    }

    response.writeHead(200, {
      "Content-Type": types.get(path.extname(absolutePath).toLowerCase()) || "application/octet-stream"
    });
    response.end(data);
  });
});

server.listen(port, () => {
  console.log(`EMX Clips Companion running at http://localhost:${port}`);
});
