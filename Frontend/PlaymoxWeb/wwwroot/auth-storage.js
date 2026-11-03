window.playmoxAuth = {
    set: function (key, value) {
        localStorage.setItem(key, value);
    },
    get: function (key) {
        return localStorage.getItem(key);
    },
    remove: function (key) {
        localStorage.removeItem(key);
    },
    getLanguage: function () {
        return localStorage.getItem("playmox_lang") || "en";
    },
    setLanguage: function (lang) {
        localStorage.setItem("playmox_lang", lang);
    }
};

window.playmoxFiles = {
    downloadBase64: function (fileName, mimeType, base64Content) {
        const link = document.createElement("a");
        link.href = `data:${mimeType};base64,${base64Content}`;
        link.download = fileName || "download.bin";
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    },
    downloadStream: async function (fileName, mimeType, streamRef) {
        const arrayBuffer = await streamRef.arrayBuffer();
        const blob = new Blob([arrayBuffer], { type: mimeType || "application/octet-stream" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName || "download.bin";
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    },
    downloadByUrl: function (url) {
        const link = document.createElement("a");
        link.href = url;
        link.style.display = "none";
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    }
};

window.playmoxUi = {
    clickById: function (id) {
        const element = document.getElementById(id);
        if (element) {
            element.click();
        }
    }
};

window.playmoxSsh = (function () {
    let socket = null;
    let dotnetRef = null;
    let activeConnectionId = 0;

    function toWsUrl(path) {
        const wsProtocol = window.location.protocol === "https:" ? "wss:" : "ws:";
        return `${wsProtocol}//${window.location.host}${path}`;
    }

    function safeInvoke(method, ...args) {
        if (!dotnetRef) {
            return;
        }

        try {
            dotnetRef.invokeMethodAsync(method, ...args);
        } catch (_) {
            // ignore disposed .NET ref during teardown
        }
    }

    return {
        connect: function (path, dotnet) {
            const connectionId = ++activeConnectionId;
            const previousSocket = socket;
            dotnetRef = dotnet;
            const nextSocket = new WebSocket(toWsUrl(path));
            socket = nextSocket;

            if (previousSocket) {
                try {
                    previousSocket.onopen = null;
                    previousSocket.onmessage = null;
                    previousSocket.onclose = null;
                    previousSocket.onerror = null;
                    previousSocket.close(1000, "Replaced");
                } catch (_) { }
            }

            nextSocket.onopen = () => {
                if (connectionId !== activeConnectionId) {
                    return;
                }

                safeInvoke("OnSshWsOpen");
            };
            nextSocket.onmessage = (evt) => {
                if (connectionId !== activeConnectionId) {
                    return;
                }

                safeInvoke("OnSshWsMessage", evt.data ?? "");
            };
            nextSocket.onclose = (evt) => {
                if (connectionId !== activeConnectionId) {
                    return;
                }

                const reason = evt.reason || `Connection closed (code=${evt.code}, clean=${evt.wasClean}).`;
                safeInvoke("OnSshWsClosed", reason);
                socket = null;
            };
            nextSocket.onerror = () => {
                if (connectionId !== activeConnectionId) {
                    return;
                }

                const stateText = nextSocket.readyState === WebSocket.CONNECTING ? "CONNECTING"
                    : nextSocket.readyState === WebSocket.OPEN ? "OPEN"
                    : nextSocket.readyState === WebSocket.CLOSING ? "CLOSING"
                    : "CLOSED";
                safeInvoke("OnSshWsError", `WebSocket connection error (state=${stateText}).`);
            };
        },
        send: function (text) {
            if (socket && socket.readyState === WebSocket.OPEN) {
                socket.send(text ?? "");
                return true;
            }

            return false;
        },
        close: function () {
            activeConnectionId++;
            if (socket) {
                try {
                    socket.onopen = null;
                    socket.onmessage = null;
                    socket.onclose = null;
                    socket.onerror = null;
                    socket.close(1000, "User closed");
                } catch (_) { }
                socket = null;
            }
        },
        scrollToBottom: function (elementId) {
            const el = document.getElementById(elementId);
            if (el) {
                el.scrollTop = el.scrollHeight;
            }
        }
    };
})();
