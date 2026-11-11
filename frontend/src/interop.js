// Browser glue used by App.fs: API calls, the blurred image preview, and PayPal's checkout buttons.

async function readResponse(response) {
  let data = null;
  try {
    data = await response.json();
  } catch {
    data = null;
  }
  if (!response.ok) {
    const message =
      (data && (data.error || data.detail || data.title)) ||
      `The server returned ${response.status}. Is the API running on port 5080?`;
    throw new Error(message);
  }
  return data;
}

export async function apiGet(path) {
  return readResponse(await fetch(path, { headers: { Accept: "application/json" } }));
}

export async function apiPostJson(path, body) {
  return readResponse(
    await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(body ?? {}),
    })
  );
}

// Builds the only rendition of an image a client can see before paying: the picture is
// reduced to a 40-pixel thumbnail, stretched back up (which blurs it) and watermarked.
async function buildImagePreview(file) {
  const url = URL.createObjectURL(file);
  try {
    const image = await new Promise((resolve, reject) => {
      const element = new Image();
      element.onload = () => resolve(element);
      element.onerror = () => reject(new Error("The image could not be read."));
      element.src = url;
    });

    const longest = Math.max(image.naturalWidth, image.naturalHeight) || 1;
    const tinyScale = 40 / longest;
    const tiny = document.createElement("canvas");
    tiny.width = Math.max(1, Math.round(image.naturalWidth * tinyScale));
    tiny.height = Math.max(1, Math.round(image.naturalHeight * tinyScale));
    tiny.getContext("2d").drawImage(image, 0, 0, tiny.width, tiny.height);

    const outScale = 480 / longest;
    const out = document.createElement("canvas");
    out.width = Math.max(1, Math.round(image.naturalWidth * outScale));
    out.height = Math.max(1, Math.round(image.naturalHeight * outScale));
    const ctx = out.getContext("2d");
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = "high";
    ctx.drawImage(tiny, 0, 0, out.width, out.height);

    ctx.fillStyle = "rgba(0, 0, 0, 0.35)";
    ctx.fillRect(0, 0, out.width, out.height);
    ctx.save();
    ctx.translate(out.width / 2, out.height / 2);
    ctx.rotate(-Math.PI / 6);
    ctx.font = "bold 22px monospace";
    ctx.textAlign = "center";
    ctx.fillStyle = "rgba(255, 255, 255, 0.85)";
    for (let y = -out.height; y <= out.height; y += 70) {
      ctx.fillText("PLUMBLINE PREVIEW  -  PLUMBLINE PREVIEW  -  PLUMBLINE PREVIEW", 0, y);
    }
    ctx.restore();

    return await new Promise((resolve) => out.toBlob(resolve, "image/jpeg", 0.7));
  } finally {
    URL.revokeObjectURL(url);
  }
}

// fields: { title, requirementsText, priceUsd, assetText }. file: a File, or null for pasted text.
export async function uploadProject(fields, file) {
  const form = new FormData();
  form.append("title", fields.title ?? "");
  form.append("requirementsText", fields.requirementsText ?? "");
  form.append("priceUsd", fields.priceUsd ?? "");

  if (file) {
    form.append("assetFile", file, file.name);
    if (file.type && file.type.startsWith("image/")) {
      const preview = await buildImagePreview(file);
      if (preview) form.append("previewImage", preview, "preview.jpg");
    }
  } else {
    form.append("assetText", fields.assetText ?? "");
  }

  return readResponse(await fetch("/api/projects/upload", { method: "POST", body: form }));
}

let paypalSdkPromise = null;

function loadPayPalSdk(clientId, currency) {
  if (!paypalSdkPromise) {
    paypalSdkPromise = new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src =
        "https://www.paypal.com/sdk/js?client-id=" +
        encodeURIComponent(clientId) +
        "&currency=" +
        encodeURIComponent(currency) +
        "&intent=capture";
      script.onload = () => resolve(window.paypal);
      script.onerror = () => {
        paypalSdkPromise = null;
        reject(new Error("PayPal checkout could not be loaded. Check your connection and the PayPal client ID."));
      };
      document.head.appendChild(script);
    });
  }
  return paypalSdkPromise;
}

function waitForElement(id, attempts = 60) {
  return new Promise((resolve, reject) => {
    const check = (left) => {
      const element = document.getElementById(id);
      if (element) resolve(element);
      else if (left <= 0) reject(new Error("The PayPal checkout area is not on the page."));
      else setTimeout(() => check(left - 1), 50);
    };
    check(attempts);
  });
}

// Renders PayPal's buttons into the container. The order is created and captured by the
// backend; onCaptured receives the updated project once the money is in escrow.
export function mountPayPalButtons(containerId, clientId, currency, projectId, onCaptured, onError) {
  (async () => {
    try {
      const container = await waitForElement(containerId);
      if (container.dataset.projectId === projectId) return;
      container.dataset.projectId = projectId;
      container.innerHTML = "";

      const paypal = await loadPayPalSdk(clientId, currency);
      if (!paypal || !document.body.contains(container)) return;

      await paypal
        .Buttons({
          style: { color: "gold", shape: "rect", label: "pay" },
          createOrder: async () => {
            const order = await apiPostJson(`/api/payments/create-order/${projectId}`, {});
            return order.orderId;
          },
          onApprove: async (data) => {
            const project = await apiPostJson(
              `/api/payments/capture-order/${projectId}/${encodeURIComponent(data.orderID)}`,
              {}
            );
            onCaptured(project);
          },
          onCancel: () => onError("Payment cancelled. Nothing was charged."),
          onError: (err) => onError((err && err.message) || "PayPal reported a payment error. Nothing was charged."),
        })
        .render(container);
    } catch (err) {
      const container = document.getElementById(containerId);
      if (container) delete container.dataset.projectId;
      onError((err && err.message) || "PayPal checkout failed to start.");
    }
  })();
}
