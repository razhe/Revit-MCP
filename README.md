# Revit MCP

Servidor [MCP](https://modelcontextprotocol.io) que permite a Claude modelar en **Autodesk Revit 2027** conversando: niveles, muros rectos y curvos, losas, cielos rasos, techos, escaleras, barandas, puertas, ventanas, mobiliario, habitaciones, materiales, vistas 3D e imágenes del modelo.

> Ejemplo: *"Crea una casa de 10 × 8 m con muros de 2,6 m, una puerta al centro de la fachada sur, dos ventanas por lado, una losa y un techo a dos aguas de 25°. Después muéstrame una vista 3D desde el frente."*

## Cómo funciona

```
Claude Desktop ──stdio──► mcp-server (Python) ──TCP 127.0.0.1:8765──► revit-addin (C#, dentro de Revit)
```

| Carpeta | Qué es |
|---|---|
| `revit-addin/` | Add-in de Revit (C#, .NET 10). Al abrir Revit inicia un servidor TCP local y ejecuta cada petición en el hilo principal de Revit. |
| `mcp-server/` | Servidor MCP (Python). Expone las herramientas a Claude y reenvía cada llamada al add-in. |

Las dos partes son necesarias: Claude habla con el servidor Python y este con Revit. Revit tiene que estar abierto, con un modelo cargado.

## Requisitos

| Requisito | Para qué | Notas |
|---|---|---|
| Windows 10/11 | — | |
| **Autodesk Revit 2027** | Ejecutar el add-in | La versión completa, la de prueba de 30 días o la educativa. **Revit LT no sirve**: no admite add-ins. |
| [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) | Compilar el add-in | Solo en el PC donde se compila. |
| [uv](https://docs.astral.sh/uv/getting-started/installation/) | Ejecutar el servidor MCP | Instala Python y las dependencias por su cuenta. |
| [Claude Desktop](https://claude.ai/download) | Conversar con Claude | |
| Git | Clonar el repositorio | |

Para instalar uv en PowerShell:

```bash
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

## Instalación

### 1. Clonar el repositorio

```bash
git clone https://github.com/razhe/Revit-MCP.git
```

Las rutas de esta guía usan `C:\ruta\a\Revit-MCP` como ejemplo; reemplázala por la carpeta donde lo clonaste.

### 2. Compilar e instalar el add-in

**Con Revit cerrado**, desde la carpeta del repositorio:

```bash
dotnet build revit-addin -c Release -p:Deploy=true
```

Esto compila y copia el add-in en tu carpeta de usuario de Revit:

```
%AppData%\Autodesk\Revit\Addins\2027\
├── RevitMcp.addin          ← manifiesto que Revit lee al iniciar
└── RevitMcp\               ← RevitMcp.dll y sus dependencias
```

Si Revit está abierto, la copia falla porque la DLL está en uso: ciérralo y repite el comando.

**En otro PC sin el SDK de .NET:** compílalo en un PC que lo tenga (sin `-p:Deploy=true` basta), copia el contenido de `revit-addin\bin\Release\net10.0-windows\` a `%AppData%\Autodesk\Revit\Addins\2027\RevitMcp\` en el otro PC, y `revit-addin\RevitMcp.addin` a `%AppData%\Autodesk\Revit\Addins\2027\`.

### 3. Abrir Revit y cargar el add-in

1. Abre Revit 2027. La primera vez pregunta si quieres cargar **"Revit MCP"**: elige **Cargar siempre**.
2. Crea o abre un proyecto. Para un proyecto nuevo usa **Metric Multi-discipline**: el MCP trabaja en metros y la plantilla métrica evita confusiones de unidades.
3. En la pestaña **Complementos (Add-Ins)** aparece el panel **Revit MCP**. El servidor arranca solo; el botón **Servidor MCP** lo detiene o lo vuelve a iniciar y muestra su estado.

### 4. Configurar Claude Desktop

1. En Claude Desktop, abre **Configuración → Desarrollador → Editar configuración**. Se abre `claude_desktop_config.json` (si no existe, se crea).
2. Agrega el servidor `revit` dentro de `mcpServers`. Si el archivo ya tiene otras claves, agrega solo el bloque `"revit"` sin borrar lo demás:

```json
{
  "mcpServers": {
    "revit": {
      "command": "C:\\Users\\TU_USUARIO\\.local\\bin\\uv.exe",
      "args": ["--directory", "C:\\ruta\\a\\Revit-MCP\\mcp-server", "run", "revit-mcp"]
    }
  }
}
```

- `command`: ruta completa de `uv.exe`. Claude Desktop no siempre hereda el `PATH`, así que la ruta completa evita el error "uv no encontrado". Para verla, ejecuta `(Get-Command uv).Source` en PowerShell.
- `args`: ajusta la ruta de `mcp-server`. En JSON las barras invertidas se escriben dobles (`\\`).

3. **Cierra Claude Desktop por completo**, también desde el ícono junto al reloj, y vuelve a abrirlo. La configuración solo se lee al iniciar.

### 5. Comprobar que funciona

Con Revit abierto y un modelo cargado, abre un chat nuevo en Claude Desktop:

1. En el menú de herramientas del chat debe aparecer **revit**.
2. Pide: *"Usa get_model_info y dime qué niveles tiene el modelo"*. Claude debe responder con los niveles del proyecto.

Para probar la conexión con Revit sin Claude, desde la carpeta del repositorio:

```bash
uv run --directory mcp-server python -c "from revit_mcp.connection import RevitConnection; print(RevitConnection.from_environment().send('get_model_info', {}))"
```

## Uso

Habla con Claude en lenguaje normal. Funciona mejor si:

- Das medidas en metros y una referencia de origen ("esquina en 0,0", "fachada principal hacia el sur").
- Pides una sola imagen al final (`export_view_image`). Cada imagen consume bastantes tokens.
- Revisas el resultado en Revit: cada acción queda en **Deshacer** como `MCP: <herramienta>` y se puede revertir.

Ejemplos:

> Crea un nivel "L2" a 3 m. En L1, muros perimetrales de 8 × 6 m amarrados a L2, con una puerta centrada en la fachada sur y ventanas de 1,05 m de antepecho.

> Agrega una escalera en U entre L1 y L2 dentro del living, y deja el hueco en la losa de L2 justo sobre ella.

> Techo a dos aguas de 20° con alero de 0,6 m, une los muros del último piso al techo y muéstrame una vista 3D desde el frente-izquierda a baja altura.

### Convenciones del modelo

| Concepto | Convención |
|---|---|
| Unidades | Metros y grados. Internamente Revit usa pies; la conversión es automática. |
| Puntos | `[x, y]` o `[x, y, z]` en coordenadas del proyecto. En planta, `z` es la altura sobre el nivel. |
| Nivel por defecto | Si no se indica, el de la vista activa. |
| Nombres de tipo | `"Tipo"` o `"Familia: Tipo"`, sin distinguir mayúsculas. `list_types` los devuelve en ese formato. |
| Tramos curvos | `arc_points`: lista alineada con los tramos (tramo *i* = del punto *i* al *i+1*); cada elemento es `null` (recto) o el punto `[x, y]` por el que pasa el arco. Sirve en muros, losas, cielos rasos, techos y barandas. |
| Bordes de techo | `sloped_edges`: índices de los bordes con pendiente, con la misma numeración. Sin indicarlo, todos (cuatro aguas). |
| Caras (`set_material`) | `all`, `top`, `bottom`, `sides`, o según hacia dónde miran: `+x`, `-x`, `+y`, `-y`. |
| Cámara 3D | `from_direction`: dirección en planta desde el edificio hacia la cámara (`[-0.5, -1]` = frente-izquierda si el frente mira a −y). `elevation_degrees`: 10–20 a baja altura, 30–45 aérea. |
| Categorías | `walls`, `floors`, `roofs`, `ceilings`, `doors`, `windows`, `rooms`, `furniture`, `casework`, `plumbing_fixtures`, `lighting_fixtures`, `columns`, `structural_columns`, `stairs`, `railings`, `generic_models`, `planting`, o cualquier nombre `OST_*`. |
| Parámetros (`set_parameter`) | Nombre visible en Revit. Las longitudes van en unidades del proyecto (`"1050"` en un proyecto en mm, o `"105 cm"`); Sí/No como `true`/`false`. |

## Herramientas

**Consulta**

| Herramienta | Qué hace |
|---|---|
| `get_model_info` | Título, vista activa, niveles con su cota y cantidad de elementos por categoría. |
| `list_types` | Tipos cargados de una categoría, como `"Familia: Tipo"`. |
| `get_elements` | Elementos de una categoría (opcionalmente de un nivel), con id, tipo, niveles y ubicación o caja envolvente (`bounds`). |
| `get_element_info` | Todos los parámetros de instancia y de tipo de un elemento. Su respuesta es extensa. |
| `get_selection` | Lo que el usuario tiene seleccionado en Revit. |

**Modelado**

| Herramienta | Qué hace |
|---|---|
| `create_level` | Nivel a una cota, con su vista de planta. |
| `create_walls` | Muros por polilínea, rectos o curvos; altura fija o amarrados a un nivel superior (`top_level`); base elevada (`base_offset`). |
| `create_floor` | Losa por contorno, con perforaciones (`holes`) para escaleras o shafts. |
| `create_ceiling` | Cielo raso por contorno, a una altura sobre el nivel. |
| `create_roof` | Techo por contorno: plano, a dos o a cuatro aguas, con alero. |
| `attach_walls` | Une la parte superior o la base de muros a un techo, losa o cielo raso. |
| `create_stairs` | Escalera nativa recta o en U entre dos niveles; calcula las contrahuellas. |
| `create_railing` | Baranda por polilínea (por ejemplo, de vidrio). |
| `create_room` | Habitación con nombre y número; devuelve su área. |
| `place_door`, `place_window` | Puerta o ventana en un muro, a una distancia desde su inicio. |
| `place_family_instance` | Cualquier familia cargada (mobiliario, sanitarios, columnas, vegetación) en un punto, con rotación. |
| `search_family_library`, `load_family` | Buscar archivos `.rfa` y cargarlos en el modelo. |

**Edición**

| Herramienta | Qué hace |
|---|---|
| `move_elements`, `copy_elements`, `rotate_elements`, `delete_elements` | Mover, copiar, rotar y eliminar. La copia informa también las puertas y ventanas que Revit copia junto con sus muros. |
| `set_parameter` | Cambia un parámetro en uno o varios elementos a la vez (todos o ninguno). |
| `change_type` | Cambia el tipo de un elemento por otro de su categoría. |
| `set_material` | Pinta caras con un material real; lo crea desde un color si no existe. |
| `set_element_color` | Color de sobrescritura solo en una vista (para diferenciar, no es material). |

**Vistas e imagen**

| Herramienta | Qué hace |
|---|---|
| `list_views`, `open_view` | Listar y abrir vistas. |
| `create_3d_view` | Vista 3D sombreada, sin niveles ni ejes, encuadrada al modelo y con cámara configurable. |
| `orient_3d_view` | Cambia la cámara de una vista 3D y la reencuadra al modelo actual. |
| `export_view_image` | Imagen PNG de una vista, para que Claude vea el resultado. |

**Avanzado**

| Herramienta | Qué hace |
|---|---|
| `execute_csharp` | Ejecuta C# contra la API de Revit para lo que no cubren las demás. Por defecto dentro de una transacción; con `use_transaction=false` para APIs que manejan la suya. |

## Configuración

| Variable de entorno | Por defecto | Dónde se lee |
|---|---|---|
| `REVIT_MCP_PORT` | `8765` | Revit y servidor MCP. Si la cambias, debe coincidir en ambos. |
| `REVIT_MCP_HOST` | `127.0.0.1` | Servidor MCP. |
| `REVIT_MCP_TIMEOUT` | `150` s | Servidor MCP: espera máxima por respuesta de Revit. |
| `REVIT_MCP_FAMILY_DIRS` | — | Revit: carpetas extra con familias `.rfa`, separadas por `;`. |

Las variables que lee Revit deben existir antes de abrirlo (variables de entorno de Windows). Las del servidor MCP también pueden ir en un bloque `"env"` del servidor `revit` en `claude_desktop_config.json`.

**Familias (mobiliario, sanitarios, árboles):** Revit 2027 instala una biblioteca local casi vacía, porque las familias se cargan desde la nube con *Cargar familia de Autodesk*, que no tiene API. Para que `search_family_library` encuentre contenido, descarga el paquete de contenido de Revit 2027 desde Autodesk y apunta `REVIT_MCP_FAMILY_DIRS` a esa carpeta.

## Problemas comunes

| Síntoma | Causa y solución |
|---|---|
| *"No se pudo conectar con Revit en 127.0.0.1:8765"* | Revit no está abierto, no hay un modelo cargado o el servidor está detenido. Actívalo con el botón **Servidor MCP** del panel Revit MCP. |
| *"Revit no respondió a tiempo"* | Hay un diálogo abierto en Revit o un comando en curso. Ciérralo y repite. |
| No aparece el panel Revit MCP | El add-in no se instaló o se rechazó al cargarlo. Revisa que exista `%AppData%\Autodesk\Revit\Addins\2027\RevitMcp.addin` y reinicia Revit. |
| `dotnet build ... -p:Deploy=true` falla al copiar | Revit está abierto y bloquea la DLL. Ciérralo. |
| *"No se pudo iniciar el servidor en el puerto 8765"* al abrir Revit | Otro programa usa el puerto. Define `REVIT_MCP_PORT` con otro valor, en Windows y en la configuración de Claude Desktop. |
| Claude no muestra la herramienta **revit** | El JSON tiene un error o la ruta de `uv.exe` o de `mcp-server` es incorrecta. Revisa el archivo y reinicia Claude Desktop por completo. |
| Claude no ve una herramienta o un parámetro nuevo | El servidor MCP se carga al iniciar Claude Desktop. Reinícialo después de actualizar el repositorio. |
| Longitudes con valores extraños | El proyecto está en unidades imperiales. Usa una plantilla métrica. |
| `search_family_library` no encuentra nada | La biblioteca local está vacía; ver **Familias** en Configuración. |

## Seguridad

- El add-in solo escucha en `127.0.0.1`: no acepta conexiones de otros equipos.
- `execute_csharp` ejecuta código arbitrario dentro de Revit, con los permisos de tu usuario. Claude Desktop pide aprobación antes de usar cada herramienta; revisa el código antes de aprobarlo.
- Todos los cambios al modelo se pueden deshacer desde Revit.

## Para desarrolladores y asistentes de IA

### Estructura

```
revit-addin/
├── App.cs                    IExternalApplication: crea el panel e inicia el servidor
├── ToggleServerCommand.cs    Botón que inicia o detiene el servidor
├── RevitMcp.addin            Manifiesto (contexto de dependencias aislado)
├── Server/                   TCP, protocolo y despacho al hilo de Revit
├── Revit/                    Utilidades: unidades, búsqueda por nombre, geometría, transacciones, encuadre de vistas
└── Tools/                    Una clase por herramienta (implementan IMcpTool)
mcp-server/
└── revit_mcp/
    ├── connection.py         Cliente TCP del add-in
    └── server.py             Definición de las herramientas MCP (MCPServer, mcp >= 2.3)
```

### Protocolo

Una petición y una respuesta JSON por línea, sobre TCP local:

```json
{"command": "create_walls", "params": {"points": [[0, 0], [5, 0]], "height": 2.6}}
{"ok": true, "result": {"wall_ids": [12345]}}
{"ok": false, "error": "No se encontró el nivel 'L9'. Disponibles: L1 | L2"}
```

Las claves de los resultados van en `snake_case`. Los errores llevan mensajes en español que listan las opciones válidas, para que el modelo de lenguaje pueda corregirse.

### Reglas del add-in

- La API de Revit solo es válida en su hilo principal. `ToolDispatcher` encola cada petición y la ejecuta mediante un `ExternalEvent`; ninguna herramienta debe llamar a la API desde otro hilo.
- Toda modificación va dentro de `ModelTransaction.Run(doc, Name, ...)`. Así queda en Deshacer como `MCP: <herramienta>`, las advertencias se descartan sin diálogos y los errores revierten la operación.
- Las herramientas reciben y devuelven metros y grados. Convierte con `UnitConverter`, porque Revit usa pies internamente.
- Para buscar niveles, tipos, vistas o familias por nombre, usa `ModelLookup`: unifica la búsqueda y los mensajes de error.

### Agregar una herramienta

1. En `revit-addin/Tools/`, crea una clase que implemente `IMcpTool`, con un `Name` en `snake_case`. Se registra sola por reflexión.
2. En `mcp-server/revit_mcp/server.py`, agrega una función `@mcp.tool()` con el **mismo nombre** que llame a `call("<nombre>", ...)`. Su docstring es lo que lee Claude: explica unidades, valores por defecto y cuándo usarla.
3. Compila e instala (`dotnet build revit-addin -c Release -p:Deploy=true`, con Revit cerrado) y reinicia Claude Desktop.

Para verificar que los nombres coinciden entre C# y Python:

```bash
uv run --directory mcp-server python -c "import asyncio,re,pathlib; from revit_mcp import server; py={t.name for t in asyncio.run(server.mcp.list_tools())}; cs={n for f in pathlib.Path('../revit-addin').rglob('*.cs') for n in re.findall(r'Name => \"([a-z_0-9]+)\"', f.read_text(encoding='utf-8'))}; print('solo python:', py-cs, '| solo c#:', cs-py)"
```

### Particularidades de la API de Revit 2027

- Revit 2027 usa .NET 10; el add-in apunta a `net10.0-windows` y referencia la API mediante los paquetes NuGet `Nice3point.Revit.Api.*`, así que se compila sin Revit instalado.
- `NewFootPrintRoof` exige que la `ModelCurveArray` de salida venga inicializada, aunque sea un parámetro `out`.
- El alero (`set_Overhang`) solo existe en techos creados seleccionando muros; para techos por contorno, el alero se logra ampliando el contorno.
- Las escaleras se crean dentro de un `StairsEditScope`, que no admite una transacción ya abierta.
- `CopyElements` copia las puertas y ventanas de un muro, pero no devuelve sus ids.
- La exportación de imágenes encuadra según la región de recorte (crop) de la vista, no según la caja de sección.
