import asyncio
import shutil
from pathlib import Path
from typing import Any

from mcp.server.mcpserver import Image, MCPServer

from .connection import RevitConnection

Point = list[float]

INSTRUCTIONS = """Controla el modelo abierto en Autodesk Revit.

Convenciones:
- Longitudes en metros y ángulos en grados.
- Los puntos son [x, y] o [x, y, z] en coordenadas del proyecto. En planta, z es la altura sobre el nivel.
- Si no se indica nivel, se usa el nivel de la vista activa.
- Los nombres de tipo aceptan "Tipo" o "Familia: Tipo" (sin distinguir mayúsculas).
- Los contornos (muros, pisos, techos, cielos rasos) admiten tramos curvos con arc_points: una lista
  alineada con los tramos (tramo i = del punto i al i+1) donde cada elemento es null (recto) o un punto
  [x, y] por el que pasa el arco.
- Si falta una familia (mobiliario, sanitarios, etc.), búscala con search_family_library y cárgala con load_family.
- Categorías válidas: walls, floors, roofs, ceilings, doors, windows, rooms, furniture, casework,
  plumbing_fixtures, lighting_fixtures, columns, structural_columns, stairs, railings,
  generic_models, planting, o cualquier nombre OST_*.

Flujo recomendado:
1. Planifica en texto: origen, niveles y coordenadas de cada volumen (fachada principal en y = 0, mirando a -y).
2. get_model_info y list_types (solo las categorías que usarás) una vez; reutiliza nombres e ids.
3. Orden: niveles, muros con top_level, escaleras, losas con holes, puertas y ventanas, techo y attach_walls,
   habitaciones, barandas y materiales (set_material).
4. Verifica con las respuestas (area_m2, bounds, dependents_copied) en lugar de imágenes.
5. Al final, create_3d_view con from_direction y una sola export_view_image de 800-1000 px.

Ahorro de tokens: agrupa muros en polilíneas, pasa varios ids a set_parameter y set_material, evita
get_element_info salvo que necesites un nombre de parámetro, y lee los errores: listan las opciones válidas.
Guía completa con medidas de referencia, recetas y lista de control: recurso revit://guia-ia.

Cada operación queda en el historial de Deshacer de Revit como "MCP: <herramienta>"."""

GUIDE_PATH = Path(__file__).resolve().parents[2] / "GUIA_IA.md"

mcp = MCPServer("revit", instructions=INSTRUCTIONS)
revit = RevitConnection.from_environment()


@mcp.resource(
    "revit://guia-ia",
    name="guia-ia",
    description="Guía para usar el MCP de Revit con resultados profesionales y pocos tokens.",
    mime_type="text/markdown",
)
def guide() -> str:
    return GUIDE_PATH.read_text(encoding="utf-8")


async def call(command: str, **params: Any) -> Any:
    present = {key: value for key, value in params.items() if value is not None}
    result = await asyncio.to_thread(revit.send, command, present)
    return {"items": result} if isinstance(result, list) else result


@mcp.tool()
async def get_model_info() -> Any:
    """Resumen del modelo abierto: título, vista activa, niveles con su cota y cantidad de elementos por categoría."""
    return await call("get_model_info")


@mcp.tool()
async def list_types(category: str) -> Any:
    """Lista los tipos cargados para una categoría (ej. 'walls', 'doors', 'furniture', 'railings')
    como nombres 'Familia: Tipo', listos para usar en las demás herramientas."""
    return await call("list_types", category=category)


@mcp.tool()
async def get_elements(category: str, level: str | None = None, limit: int = 100) -> Any:
    """Lista los elementos de una categoría, opcionalmente filtrados por nivel, con id, tipo y ubicación.
    Los elementos sin punto o eje de ubicación (losas, techos, escaleras) informan su caja envolvente en bounds."""
    return await call("get_elements", category=category, level=level, limit=limit)


@mcp.tool()
async def get_element_info(element_id: int) -> Any:
    """Detalle de un elemento: ubicación y todos sus parámetros de instancia y de tipo."""
    return await call("get_element_info", element_id=element_id)


@mcp.tool()
async def get_selection() -> Any:
    """Elementos que el usuario tiene seleccionados actualmente en Revit."""
    return await call("get_selection")


@mcp.tool()
async def create_level(elevation: float, name: str | None = None, create_plan_view: bool = True) -> Any:
    """Crea un nivel a la cota indicada (m) y, por defecto, su vista de planta."""
    return await call("create_level", elevation=elevation, name=name, create_plan_view=create_plan_view)


@mcp.tool()
async def create_walls(
    points: list[Point],
    closed: bool = False,
    height: float = 2.6,
    level: str | None = None,
    top_level: str | None = None,
    wall_type: str | None = None,
    structural: bool = False,
    arc_points: list[Point | None] | None = None,
    base_offset: float = 0,
) -> Any:
    """Crea muros siguiendo una polilínea de puntos [x, y]. Con closed=true cierra el contorno
    (útil para el perímetro de un recinto). Con top_level la parte superior queda amarrada a ese nivel
    (sigue sus cambios de cota) y height se ignora. base_offset (m) eleva la base sobre el nivel, por
    ejemplo para un dintel sobre un portón. Devuelve los ids de los muros en el orden de los tramos."""
    return await call(
        "create_walls",
        points=points,
        closed=closed,
        height=height,
        base_offset=base_offset,
        level=level,
        top_level=top_level,
        wall_type=wall_type,
        structural=structural,
        arc_points=arc_points,
    )


@mcp.tool()
async def create_floor(
    points: list[Point],
    level: str | None = None,
    floor_type: str | None = None,
    offset: float = 0,
    holes: list[list[Point]] | None = None,
    arc_points: list[Point | None] | None = None,
) -> Any:
    """Crea un piso/losa con el contorno cerrado definido por los puntos [x, y].
    holes es una lista de contornos interiores que quedan perforados (paso de escaleras, shafts)."""
    return await call(
        "create_floor",
        points=points,
        level=level,
        floor_type=floor_type,
        offset=offset,
        holes=holes,
        arc_points=arc_points,
    )


@mcp.tool()
async def create_ceiling(
    points: list[Point],
    height: float = 2.4,
    level: str | None = None,
    ceiling_type: str | None = None,
    arc_points: list[Point | None] | None = None,
) -> Any:
    """Crea un cielo raso con el contorno cerrado [x, y], a la altura indicada (m) sobre el nivel."""
    return await call(
        "create_ceiling",
        points=points,
        height=height,
        level=level,
        ceiling_type=ceiling_type,
        arc_points=arc_points,
    )


@mcp.tool()
async def create_roof(
    points: list[Point],
    level: str | None = None,
    roof_type: str | None = None,
    slope_degrees: float = 30,
    overhang: float = 0,
    base_offset: float = 0,
    sloped_edges: list[int] | None = None,
    arc_points: list[Point | None] | None = None,
) -> Any:
    """Crea una cubierta por perímetro (footprint), normalmente en el nivel superior a los muros.
    sloped_edges indica qué bordes llevan pendiente (borde i = del punto i al i+1); sin indicarlo,
    todos (techo a cuatro aguas). Para dos aguas, solo los dos bordes largos. slope_degrees=0 = plano.
    El alero (overhang, m) amplía el contorno hacia afuera. Une los muros al techo con attach_walls."""
    return await call(
        "create_roof",
        points=points,
        level=level,
        roof_type=roof_type,
        slope_degrees=slope_degrees,
        overhang=overhang,
        base_offset=base_offset,
        sloped_edges=sloped_edges,
        arc_points=arc_points,
    )


@mcp.tool()
async def create_room(
    point: Point,
    level: str | None = None,
    name: str | None = None,
    number: str | int | None = None,
) -> Any:
    """Crea una habitación (Room) en el punto [x, y], que debe estar dentro de un recinto cerrado por muros.
    Devuelve el área calculada en m²; si es 0, el recinto no está cerrado."""
    return await call("create_room", point=point, level=level, name=name, number=number)


@mcp.tool()
async def place_door(wall_id: int, distance_along_wall: float, door_type: str | None = None) -> Any:
    """Inserta una puerta en un muro. distance_along_wall es la distancia (m) desde el inicio del muro al centro de la puerta."""
    return await call("place_door", wall_id=wall_id, distance_along_wall=distance_along_wall, door_type=door_type)


@mcp.tool()
async def place_window(
    wall_id: int,
    distance_along_wall: float,
    window_type: str | None = None,
    sill_height: float = 0.9,
) -> Any:
    """Inserta una ventana en un muro, con su centro a distance_along_wall (m) del inicio del muro y la altura de antepecho indicada."""
    return await call(
        "place_window",
        wall_id=wall_id,
        distance_along_wall=distance_along_wall,
        window_type=window_type,
        sill_height=sill_height,
    )


@mcp.tool()
async def place_family_instance(
    family_type: str,
    point: Point,
    level: str | None = None,
    rotation_degrees: float = 0,
    category: str | None = None,
) -> Any:
    """Coloca una familia (mobiliario, artefactos sanitarios, vegetación, etc.) en el punto [x, y] o [x, y, z].
    Usa list_types con la categoría correspondiente para ver los tipos cargados."""
    return await call(
        "place_family_instance",
        family_type=family_type,
        point=point,
        level=level,
        rotation_degrees=rotation_degrees,
        category=category,
    )


@mcp.tool()
async def create_stairs(
    start: Point,
    direction_degrees: float = 0,
    shape: str = "straight",
    turn: str = "left",
    width: float = 1.0,
    tread_depth: float = 0.28,
    max_riser_height: float = 0.18,
    base_level: str | None = None,
    top_level: str | None = None,
) -> Any:
    """Crea una escalera nativa de Revit entre dos niveles (por defecto, el del nivel base al siguiente).
    start es el punto [x, y] al centro del primer peldaño; direction_degrees, el sentido de subida
    (0 = +x, 90 = +y). shape 'straight' (recta) o 'u' (dos tramos paralelos con descanso; turn indica
    hacia qué lado gira). Devuelve bounds para ubicar el hueco en la losa superior."""
    return await call(
        "create_stairs",
        start=start,
        direction_degrees=direction_degrees,
        shape=shape,
        turn=turn,
        width=width,
        tread_depth=tread_depth,
        max_riser_height=max_riser_height,
        base_level=base_level,
        top_level=top_level,
    )


@mcp.tool()
async def search_family_library(query: str, limit: int = 30) -> Any:
    """Busca archivos de familia (.rfa) por palabras del nombre (en inglés suelen ser 'Sofa', 'Table',
    'Toilet', 'Sink', 'Bed', 'Tree') en las bibliotecas de Revit y en REVIT_MCP_FAMILY_DIRS."""
    return await call("search_family_library", query=query, limit=limit)


@mcp.tool()
async def load_family(path: str) -> Any:
    """Carga en el modelo una familia .rfa (ruta de search_family_library) y devuelve sus tipos,
    listos para place_family_instance."""
    return await call("load_family", path=path)


@mcp.tool()
async def move_elements(element_ids: list[int], translation: Point) -> Any:
    """Mueve elementos según el vector [dx, dy] o [dx, dy, dz] en metros."""
    return await call("move_elements", element_ids=element_ids, translation=translation)


@mcp.tool()
async def copy_elements(element_ids: list[int], translation: Point) -> Any:
    """Copia elementos desplazados según el vector [dx, dy] o [dx, dy, dz]. Devuelve los ids nuevos
    agrupados por categoría. Revit también copia los elementos alojados (puertas y ventanas de un muro):
    si dependents_copied > 0, revisa by_category y elimina los que no correspondan."""
    return await call("copy_elements", element_ids=element_ids, translation=translation)


@mcp.tool()
async def rotate_elements(element_ids: list[int], angle_degrees: float, center: Point | None = None) -> Any:
    """Rota elementos en planta (eje vertical) alrededor de center, o del centro del conjunto si no se indica."""
    return await call("rotate_elements", element_ids=element_ids, angle_degrees=angle_degrees, center=center)


@mcp.tool()
async def delete_elements(element_ids: list[int]) -> Any:
    """Elimina elementos (y los que dependen de ellos, como puertas de un muro). Se puede deshacer en Revit."""
    return await call("delete_elements", element_ids=element_ids)


@mcp.tool()
async def set_parameter(element_ids: list[int], parameter: str, value: str | float | int | bool) -> Any:
    """Cambia un parámetro de instancia, por su nombre visible en Revit, en uno o varios elementos a la vez.
    Las longitudes se escriben en unidades del proyecto (ej. '1050' en un proyecto en mm, o '105 cm');
    Sí/No como true/false. Si falla en un elemento, no se cambia ninguno."""
    return await call("set_parameter", element_ids=element_ids, parameter=parameter, value=value)


@mcp.tool()
async def set_material(
    element_ids: list[int],
    material: str,
    color: str | None = None,
    faces: str = "all",
) -> Any:
    """Pinta caras de muros, losas, techos o cielos rasos con un material, sin cambiar su tipo.
    Si el material no existe, indica color '#RRGGBB' y se crea; si existe y das color, se actualiza.
    faces elige qué caras: 'all', 'top', 'bottom' (p. ej. plafón bajo un voladizo), 'sides',
    o según hacia dónde miran: '+x', '-x', '+y', '-y' (p. ej. solo la cara de fachada)."""
    return await call("set_material", element_ids=element_ids, material=material, color=color, faces=faces)


@mcp.tool()
async def create_railing(
    points: list[Point],
    level: str | None = None,
    railing_type: str | None = None,
    closed: bool = False,
    base_offset: float = 0,
    arc_points: list[Point | None] | None = None,
) -> Any:
    """Crea una baranda siguiendo una polilínea [x, y] sobre el nivel (y base_offset en m).
    Usa list_types('railings') para ver los tipos (p. ej. de vidrio)."""
    return await call(
        "create_railing",
        points=points,
        level=level,
        railing_type=railing_type,
        closed=closed,
        base_offset=base_offset,
        arc_points=arc_points,
    )


@mcp.tool()
async def attach_walls(wall_ids: list[int], target_id: int, location: str = "top") -> Any:
    """Une la parte superior ('top') o la base ('base') de los muros a un techo, losa o cielo raso,
    para que sigan su forma (por ejemplo, piñones bajo un techo a dos aguas)."""
    return await call("attach_walls", wall_ids=wall_ids, target_id=target_id, location=location)


@mcp.tool()
async def change_type(element_id: int, type_name: str) -> Any:
    """Cambia el tipo de un elemento por otro de su misma categoría (ej. un muro a otro tipo de muro)."""
    return await call("change_type", element_id=element_id, type_name=type_name)


@mcp.tool()
async def set_element_color(element_ids: list[int], color: str | None = None, view_name: str | None = None) -> Any:
    """Pinta elementos con un color sólido '#RRGGBB' en una vista (por defecto la activa).
    Sin color, quita la sobrescritura. No cambia el material, solo cómo se ven en esa vista."""
    return await call("set_element_color", element_ids=element_ids, color=color, view_name=view_name)


@mcp.tool()
async def list_views() -> Any:
    """Lista las vistas del modelo (plantas, cortes, elevaciones, 3D, láminas) e indica cuál está activa."""
    return await call("list_views")


@mcp.tool()
async def open_view(view_name: str) -> Any:
    """Abre una vista en Revit."""
    return await call("open_view", view_name=view_name)


@mcp.tool()
async def create_3d_view(
    name: str | None = None,
    open: bool = True,
    from_direction: Point | None = None,
    elevation_degrees: float = 30,
) -> Any:
    """Crea una vista 3D sombreada, sin niveles ni ejes visibles, encuadrada al modelo, y por defecto la abre.
    from_direction es la dirección en planta desde el edificio hacia la cámara, p. ej. [-0.5, -1] =
    desde el frente (-y) un poco a la izquierda; elevation_degrees, cuánto mira desde arriba
    (10-20 = altura de peatón alejado, 30-45 = vista aérea). Sin from_direction, isométrica estándar.
    Si después agregas elementos, reencuadra con orient_3d_view."""
    return await call(
        "create_3d_view",
        name=name,
        open=open,
        from_direction=from_direction,
        elevation_degrees=elevation_degrees,
    )


@mcp.tool()
async def orient_3d_view(
    view_name: str | None = None,
    from_direction: Point | None = None,
    elevation_degrees: float = 30,
) -> Any:
    """Cambia la cámara de una vista 3D existente (mismos parámetros que create_3d_view) y la
    reencuadra al modelo actual. Sin from_direction, solo reencuadra."""
    return await call(
        "orient_3d_view",
        view_name=view_name,
        from_direction=from_direction,
        elevation_degrees=elevation_degrees,
    )


@mcp.tool()
async def export_view_image(view_name: str | None = None, width_px: int = 1600) -> Image:
    """Exporta una vista (por defecto la activa) como imagen PNG para revisar visualmente el modelo."""
    result = await call("export_view_image", view_name=view_name, width_px=width_px)
    path = Path(result["path"])
    try:
        data = path.read_bytes()
    finally:
        shutil.rmtree(path.parent, ignore_errors=True)
    return Image(data=data, format="png")


@mcp.tool()
async def execute_csharp(code: str, use_transaction: bool = True) -> Any:
    """Ejecuta código C# en Revit para lo que no cubren las otras herramientas. Variables disponibles:
    Doc (Document), UiDoc (UIDocument) y App (UIApplication). Con use_transaction=true (por defecto) ya hay
    una transacción abierta: no crees otra. Usa false para APIs que manejan su propia transacción
    (StairsEditScope, TransactionGroup) o para solo leer. El valor de la última expresión (o de un 'return')
    se devuelve como texto. Unidades internas de Revit: pies."""
    return await call("execute_csharp", code=code, use_transaction=use_transaction)


def main() -> None:
    mcp.run()


if __name__ == "__main__":
    main()
