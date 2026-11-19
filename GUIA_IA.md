# Guía para asistentes de IA

Cómo usar el MCP de Revit para entregar modelos de calidad profesional gastando pocos tokens. Está escrita para modelos de lenguaje que operan las herramientas, pero también le sirve a cualquier persona que quiera entender cómo sacarle provecho.

Si vas a **modificar el código** del repositorio, lee además la sección *Para desarrolladores y asistentes de IA* del [README](README.md).

---

## 1. Principios

1. **Planifica antes de llamar herramientas.** Un modelo bien pensado en texto cuesta pocos tokens; corregir un modelo mal hecho cuesta muchos.
2. **Consulta una vez y reutiliza.** Niveles, nombres de tipos e ids de elementos vienen en las respuestas: guárdalos y no vuelvas a consultarlos.
3. **Agrupa.** Una polilínea crea varios muros en una llamada, y `set_parameter` y `set_material` aceptan varios ids. Envía en paralelo las llamadas que no dependen entre sí.
4. **Verifica con datos, no con imágenes.** `get_elements` (ubicación, `bounds`, áreas) es barato; una imagen no. Exporta **una** imagen al final.
5. **Usa las herramientas antes que `execute_csharp`.** Son más seguras, más cortas y devuelven errores que ayudan a corregir.

---

## 2. Flujo de trabajo recomendado

### Paso 1. Entender el pedido y fijar un sistema de coordenadas

Antes de modelar, escribe en tu respuesta (no en herramientas) un plan breve:

- **Origen y orientación:** esquina inferior izquierda en `[0, 0]`, eje x a lo largo de la fachada principal y fachada principal en `y = 0` mirando hacia −y. Mantén esta convención en todo el modelo.
- **Niveles:** nombre y cota de cada uno.
- **Volúmenes por nivel:** rectángulos o polígonos con coordenadas.
- **Elementos especiales:** escaleras, voladizos, terrazas, techos.

Si el pedido viene de una imagen, deduce proporciones a partir de elementos de tamaño conocido: una puerta mide ~2,1 m de alto, un piso ~3 m de piso a piso y un peldaño ~0,18 m.

### Paso 2. Reconocer el modelo (2 a 4 llamadas)

1. `get_model_info`: niveles existentes, vista activa y si el modelo está vacío.
2. `list_types` **solo** para las categorías que vas a usar. Lo habitual es `walls`, y según el caso `floors`, `doors`, `windows`, `railings` o `roofs`.

Elige los tipos una vez y reutiliza sus nombres exactos (`"Familia: Tipo"` o solo `"Tipo"`).

### Paso 3. Modelar en este orden

| Orden | Qué | Por qué en ese orden |
|---|---|---|
| 1 | Niveles (`create_level`; ajusta cotas existentes con `set_parameter` sobre el nivel, parámetro `Elevation`) | Todo lo demás se apoya en ellos. |
| 2 | Muros con `top_level` | Quedan amarrados: si cambia la cota de un nivel, los muros la siguen. |
| 3 | Escaleras (`create_stairs`) | Su respuesta trae `bounds`, que define el hueco de la losa superior. |
| 4 | Losas con `holes` | El hueco usa los `bounds` de la escalera más ~0,1 m de holgura. |
| 5 | Puertas y ventanas | Necesitan el muro ya creado (`wall_id`). |
| 6 | Techo, y después `attach_walls` | Los muros se unen al techo ya existente (piñones en techos a dos aguas). |
| 7 | Habitaciones (`create_room`) | Requieren recintos ya cerrados por muros. |
| 8 | Barandas, mobiliario y materiales | Terminaciones. |
| 9 | Vista 3D e imagen | Una sola vez, al final. |

### Paso 4. Verificar

Revisa las respuestas que ya recibiste antes de pedir datos nuevos:

- `create_room`: un `area_m2` igual a 0 significa que el recinto no está cerrado.
- `copy_elements`: si `dependents_copied` es mayor que 0, Revit copió también puertas o ventanas; elimina las que sobren.
- `create_stairs`: compara sus `bounds` con el hueco de la losa.
- `get_model_info`: los conteos por categoría confirman que no falta ni sobra nada.

### Paso 5. Presentar

1. `create_3d_view` con `from_direction` y `elevation_degrees` adecuados (ver sección 5).
2. `export_view_image` con `width_px` entre 800 y 1000.
3. Resume en texto qué se modeló, las medidas clave y qué quedó pendiente o simplificado.

---

## 3. Ahorro de tokens

| Hacer | Evitar |
|---|---|
| Reutilizar ids y nombres de respuestas anteriores. | Repetir `get_model_info` o `list_types` sin que nada haya cambiado. |
| `get_elements` con `level` y un `limit` bajo. | `get_element_info`, que lista todos los parámetros (respuesta muy larga). Úsalo solo si necesitas el nombre exacto de un parámetro. |
| Polilíneas cerradas (`closed=true`) para perímetros completos. | Un tramo de muro por llamada. |
| `set_parameter` y `set_material` con varios `element_ids`. | Una llamada por elemento. |
| Una imagen final de 800–1000 px. | Imágenes intermedias o de 1600 px. |
| `orient_3d_view` para cambiar la cámara de una vista existente. | Crear una vista nueva por cada intento de encuadre. |
| Leer el mensaje de error: lista las opciones válidas. | Reintentar a ciegas o consultar todos los tipos para encontrar uno. |
| Llamadas en paralelo cuando no dependen entre sí. | Esperar cada respuesta cuando no la necesitas. |

Referencia aproximada: una casa de dos o tres niveles con escalera, terrazas, techo y materiales cabe en 30–40 llamadas.

---

## 4. Criterios profesionales

### Medidas de referencia

| Elemento | Valor habitual |
|---|---|
| Altura de piso a piso (vivienda) | 2,8–3,2 m |
| Altura libre interior | 2,4–2,7 m |
| Espesor de losa | 0,15–0,25 m |
| Muro exterior / tabique | 0,2–0,3 m / 0,1–0,15 m |
| Puerta de acceso / interior | 0,9–1,0 m / 0,7–0,8 m de ancho; 2,0–2,1 m de alto |
| Antepecho de ventana | 0,9–1,1 m (baño y cocina, más alto) |
| Escalera | Contrahuella ≤ 0,18 m, huella ≥ 0,28 m, ancho ≥ 0,9 m |
| Baranda | 0,9–1,1 m de alto |
| Alero | 0,4–0,8 m |
| Pendiente de techo | 15–35° (plano: `slope_degrees = 0`) |
| Portón de garaje | 2,4–3,0 m de ancho por puerta, 2,1–2,4 m de alto |

### Buenas prácticas de modelado

- **Muros por su eje.** Los puntos son el eje del muro; las áreas de las habitaciones descuentan medio espesor por lado. Para un recinto interior de 4 m con muros de 0,2 m, separa los ejes 4,2 m.
- **Uniones limpias.** Los muros que se tocan deben compartir exactamente el mismo punto. Un tabique debe llegar hasta el eje del muro perimetral, no a su cara.
- **Tipos adecuados.** Usa tipos exteriores (por ejemplo `M_Exterior - ...`) en fachadas, tipos `Interior - ...` en tabiques y muros cortina (`M_Storefront`, `M_Exterior Glazing`) para fachadas vidriadas. Un tramo vidriado es un muro aparte sobre la misma línea.
- **Voladizos y terrazas.** La losa sobresale de los muros del nivel inferior; las barandas van a ~0,15 m del borde, con `create_railing`.
- **Portones y dinteles.** El portón es un muro de 2,4 m de alto; el dintel, otro muro sobre la misma línea con `base_offset = 2.4` y `top_level` al nivel superior.
- **Techos.** Con `sloped_edges` solo en los bordes largos se obtiene un techo a dos aguas; después usa `attach_walls` con los muros del último piso para formar los piñones.
- **Puertas y ventanas.** `distance_along_wall` se mide desde el **primer punto** del muro hasta el centro de la abertura. Deja al menos medio ancho de abertura más 0,1 m hasta cada extremo y hasta los muros que se cruzan.
- **Materiales.** Para que se vean en la imagen usa `set_material`, no `set_element_color`: el color de sobrescritura se distorsiona con la iluminación del modo sombreado. Pinta solo las caras necesarias: `-y` para la fachada, `bottom` para plafones bajo voladizos, `top` para cubiertas. Reutiliza los mismos nombres de material en todo el modelo.

---

## 5. Imágenes y cámara

- `create_3d_view` encuadra la vista al modelo existente. Si después agregas elementos, usa `orient_3d_view` (sin `from_direction`, solo reencuadra).
- `from_direction` es la dirección en planta **desde el edificio hacia la cámara**. Con la fachada principal mirando a −y:

| Vista | `from_direction` | `elevation_degrees` |
|---|---|---|
| Frente-izquierda, a baja altura (presentación) | `[-0.45, -1]` | 6–18 |
| Frente-derecha, aérea | `[0.6, -1]` | 30–40 |
| Contrafachada | `[0.4, 1]` | 20 |
| Planta axonométrica (vista de pájaro) | `[-1, -1]` | 55–65 |

- Las vistas son axonométricas (sin perspectiva). Con poca elevación se ven mejor las fachadas y menos las cubiertas; con mucha, al revés. Los plafones (caras inferiores) no se ven desde ninguna elevación positiva.

---

## 6. Recetas

### Casa de dos pisos con escalera y techo a dos aguas

```
1. get_model_info                                → niveles (p. ej. L1 = 0, L2 = 3.6)
2. set_parameter(L2, "Elevation", "3000")         → piso a piso de 3 m
   create_level(6.0, "Cubierta")
3. list_types("walls")                            → elegir exterior, tabique y vidrio
4. create_walls(perímetro, closed, level=L1, top_level=L2)
   create_walls(tabiques, level=L1, top_level=L2)
5. create_stairs(start, shape="u", base_level=L1)  → bounds
6. create_floor(perímetro, level=L2, holes=[bounds + 0.1])
7. create_walls(perímetro, closed, level=L2, top_level=Cubierta)
8. place_door / place_window                      → con los wall_ids del paso 4
9. create_roof(perímetro, level=Cubierta, slope_degrees=25,
               overhang=0.5, sloped_edges=[0, 2])
   attach_walls(muros de L2, roof_id)
10. create_room(...) por recinto                  → revisar area_m2
11. create_3d_view(from_direction=[-0.45, -1], elevation_degrees=15)
    export_view_image(width_px=900)
```

### Replicar un edificio a partir de una imagen

1. Identifica niveles, volúmenes y voladizos; estima medidas con las referencias de la sección 4.
2. Escribe el plan con coordenadas por nivel (sección 2, paso 1).
3. Modela por nivel, de abajo hacia arriba: losa, muros, aberturas y barandas.
4. Aplica materiales que se acerquen a la imagen (madera, blanco, gris oscuro) con `set_material`.
5. Crea la vista 3D desde el mismo ángulo de la imagen y compara. Corrige solo lo importante.

### Curvas

`arc_points` va alineado con los tramos: para curvar el tercer tramo de un rectángulo, usa `[null, null, [x, y], null]`, donde `[x, y]` es un punto por el que pasa el arco. Pasa los **mismos** `arc_points` a los muros, la losa y el techo de ese volumen para que coincidan.

---

## 7. `execute_csharp`: cuándo y cómo

Úsalo solo cuando ninguna herramienta cubre la tarea. Reglas:

- Variables disponibles: `Doc`, `UiDoc` y `App`. La API usa **pies**; convierte con `UnitUtils.ConvertToInternalUnits(valor, UnitTypeId.Meters)`.
- Por defecto ya hay una transacción abierta: no crees otra. Usa `use_transaction=false` para APIs con transacción propia (`StairsEditScope`, `TransactionGroup`) o para solo leer.
- Devuelve un `string` breve con lo esencial (ids o medidas), no objetos de Revit.
- Escribe código corto y directo; si falla, el error de compilación indica la línea.

Ejemplo: leer la altura real de un muro.

```csharp
var wall = (Wall)Doc.GetElement(new ElementId(123456L));
return wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsValueString();
```

---

## 8. Limitaciones conocidas y alternativas

| Limitación | Alternativa |
|---|---|
| Sin perspectiva en las vistas 3D. | Elegir bien `from_direction` y `elevation_degrees`. |
| La biblioteca local de Revit 2027 está casi vacía: sin muebles, árboles ni portones. | `search_family_library` + `load_family` si el usuario instaló el paquete de contenido; si no, representar con muros o losas simples y avisarlo. |
| Sin herramienta de muros cortina con divisiones personalizadas. | Tipos de muro cortina existentes (`M_Storefront`, `M_Exterior Glazing`). |
| `set_material` no pinta familias (puertas, ventanas, mobiliario). | Cambiar el tipo con `change_type`, o usar `set_element_color` solo para diferenciar en una vista. |
| `create_room` no ubica puertas ni define usos. | Nombrar y numerar con `name` y `number`. |
| El encuadre de la vista se calcula al crearla. | `orient_3d_view` para reencuadrar. |

---

## 9. Lista de control antes de entregar

- [ ] Todos los niveles con nombre y cota correctos.
- [ ] Muros amarrados a su nivel superior (`top_level`) y uniones en puntos compartidos.
- [ ] Habitaciones con área mayor que 0.
- [ ] Hueco en la losa sobre cada escalera.
- [ ] Barandas en terrazas, balcones y bordes de huecos.
- [ ] Muros del último piso unidos al techo cuando tiene pendiente.
- [ ] Sin elementos duplicados (revisa `dependents_copied` y los conteos).
- [ ] Materiales principales aplicados con `set_material`.
- [ ] Una imagen final con un ángulo que muestre la fachada principal.
- [ ] Un resumen para el usuario: qué se hizo, medidas clave y qué quedó simplificado.
