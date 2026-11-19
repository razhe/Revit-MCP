import json
import os
import socket
from typing import Any

DEFAULT_PORT = 8765
DEFAULT_TIMEOUT_SECONDS = 150
CONNECT_TIMEOUT_SECONDS = 5


class RevitError(Exception):
    pass


class RevitConnection:
    """Cliente del add-in de Revit: una petición JSON por línea sobre TCP local."""

    def __init__(self, host: str, port: int, timeout: float) -> None:
        self.host = host
        self.port = port
        self.timeout = timeout

    @classmethod
    def from_environment(cls) -> "RevitConnection":
        return cls(
            host=os.environ.get("REVIT_MCP_HOST", "127.0.0.1"),
            port=int(os.environ.get("REVIT_MCP_PORT", DEFAULT_PORT)),
            timeout=float(os.environ.get("REVIT_MCP_TIMEOUT", DEFAULT_TIMEOUT_SECONDS)),
        )

    def send(self, command: str, params: dict[str, Any]) -> Any:
        request = json.dumps({"command": command, "params": params}, ensure_ascii=False) + "\n"
        with self._connect() as sock:
            sock.settimeout(self.timeout)
            try:
                sock.sendall(request.encode("utf-8"))
                with sock.makefile("r", encoding="utf-8") as stream:
                    line = stream.readline()
            except TimeoutError as exc:
                raise RevitError("Revit no respondió a tiempo.") from exc

        if not line:
            raise RevitError("Revit cerró la conexión sin responder.")

        response = json.loads(line)
        if not response.get("ok"):
            raise RevitError(response.get("error") or "Error desconocido en Revit.")
        return response.get("result")

    def _connect(self) -> socket.socket:
        try:
            return socket.create_connection((self.host, self.port), timeout=CONNECT_TIMEOUT_SECONDS)
        except OSError as exc:
            raise RevitError(
                f"No se pudo conectar con Revit en {self.host}:{self.port}. "
                "Abre Revit con un modelo y verifica que el servidor MCP esté activo (pestaña Complementos)."
            ) from exc
