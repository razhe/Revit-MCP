# Revit MCP

Conecta Claude con Autodesk Revit 2027 para modelar conversando: muros, pisos, techos, puertas, ventanas, mobiliario, habitaciones, vistas e imágenes del modelo.

```
Claude Desktop ──stdio──► mcp-server (Python) ──TCP 127.0.0.1:8765──► revit-addin (C#, dentro de Revit)
```

- **revit-addin/**: add-in de Revit (.NET 10). Abre un socket local y ejecuta cada petición en el hilo de Revit mediante `ExternalEvent`.
- **mcp-server/**: servidor MCP que expone las herramientas a Claude y las reenvía al add-in.

## Requisitos

- Revit 2027
- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0), solo para compilar el add-in
- [uv](https://docs.astral.sh/uv/getting-started/installation/)
- Claude Desktop

## Instalación

### 1. Add-in de Revit

Con Revit cerrado:

```bash
dotnet build revit-addin -c Release -p:Deploy=true
```

Esto copia el add-in en `%AppData%\Autodesk\Revit\Addins\2027\`. Al abrir Revit, acepta cargar "Revit MCP" (la primera vez pregunta). Aparece un panel **Revit MCP** en la pestaña **Complementos**, con un botón para iniciar o detener el servidor.

Para instalarlo en otro PC sin compilar: copia `revit-addin\bin\Release\net10.0-windows\` a `%AppData%\Autodesk\Revit\Addins\2027\RevitMcp\`, y el archivo `RevitMcp.addin` a `%AppData%\Autodesk\Revit\Addins\2027\`.

### 2. Servidor MCP en Claude Desktop

En Claude Desktop, ve a **Configuración → Desarrollador → Editar configuración** y agrega lo siguiente (ajusta la ruta):

```json
{
  "mcpServers": {
    "revit": {
      "command": "uv",
      "args": ["--directory", "G:\\Programacion\\revit-mcp\\mcp-server", "run", "revit-mcp"]
    }
  }
}
```

Reinicia Claude Desktop. Con Revit abierto y un modelo cargado, pídele por ejemplo:

> Crea una casa de 10 × 8 m con muros de 2,6 m, una puerta al centro de la fachada sur, dos ventanas por lado, un piso y un techo a dos aguas de 25°. Después muéstrame una vista 3D.

## Herramientas

| Herramienta | Qué hace |
|---|---|
| `get_model_info` | Niveles, vista activa y cantidad de elementos |
| `list_types` | Tipos cargados de una categoría (muros, puertas, mobiliario…) |
| `get_elements`, `get_element_info`, `get_selection` | Consultar elementos y sus parámetros |
| `create_level` | Nivel nuevo con su planta |
| `create_walls` | Muros rectos o curvos a partir de una polilínea, con altura fija o amarrados a un nivel superior, y base elevada opcional |
| `create_railing` | Barandas siguiendo una polilínea |
| `set_material` | Pinta caras de muros, losas, techos o cielos rasos con un material (lo crea si no existe) |
| `create_floor`, `create_ceiling` | Piso (con perforaciones opcionales) y cielo raso por contorno |
| `create_roof` | Cubierta por contorno, con pendiente por borde (dos o cuatro aguas) y alero |
| `attach_walls` | Une muros a un techo, losa o cielo raso |
| `create_stairs` | Escalera nativa recta o en U entre dos niveles |
| `search_family_library`, `load_family` | Buscar y cargar familias .rfa (mobiliario, sanitarios, vegetación…) |
| `create_room` | Habitación con nombre y número; devuelve el área |
| `place_door`, `place_window` | Puertas y ventanas sobre un muro |
| `place_family_instance` | Mobiliario, sanitarios, vegetación u otras familias cargadas |
| `move_elements`, `copy_elements`, `rotate_elements`, `delete_elements` | Edición (la copia informa los elementos alojados que Revit copia por su cuenta) |
| `set_parameter`, `change_type` | Cambiar parámetros (de uno o varios elementos) o el tipo de un elemento |
| `set_element_color` | Color sólido en una vista (para presentar o diferenciar) |
| `list_views`, `open_view`, `create_3d_view`, `orient_3d_view` | Vistas (las 3D salen sombreadas, sin niveles ni ejes, encuadradas al modelo y con cámara configurable) |
| `export_view_image` | Imagen PNG de una vista para que Claude vea el resultado |
| `execute_csharp` | Código C# libre contra la API de Revit, para lo que no cubren las demás |

Convenciones: metros y grados; puntos como `[x, y]` o `[x, y, z]`; sin nivel indicado se usa el de la vista activa. Cada acción aparece en **Deshacer** como `MCP: <herramienta>`.

## Configuración

| Variable de entorno | Por defecto | Dónde |
|---|---|---|
| `REVIT_MCP_PORT` | `8765` | Revit y servidor MCP (debe coincidir) |
| `REVIT_MCP_TIMEOUT` | `150` s | Servidor MCP |
| `REVIT_MCP_FAMILY_DIRS` | — | Revit: carpetas extra con familias .rfa, separadas por `;` |

Revit 2027 instala una biblioteca local casi vacía (las familias se cargan desde la nube con "Cargar familia de Autodesk", que no tiene API). Para que `search_family_library` encuentre mobiliario y otros objetos, descarga el paquete de contenido de Revit desde Autodesk y apunta `REVIT_MCP_FAMILY_DIRS` a esa carpeta antes de abrir Revit.

## Agregar una herramienta

1. En `revit-addin/Tools/`, crea una clase que implemente `IMcpTool` (se registra sola). Usa `ModelTransaction.Run` si modifica el modelo.
2. En `mcp-server/revit_mcp/server.py`, agrega una función `@mcp.tool()` que llame a `call("<nombre>", ...)`.
