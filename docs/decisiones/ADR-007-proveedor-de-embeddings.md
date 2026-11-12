# ADR-007 — Proveedor de embeddings: Voyage AI

- **Estado:** Aceptado (Juan Pablo eligió Voyage AI el 2026-11-12)
- **Fecha:** 2026-11-12

## Contexto

- La fase 3 agrega búsqueda por significado: tickets similares (posibles duplicados) y artículos de conocimiento. Hoy las herramientas del agente buscan por palabras clave sobre datos en memoria.
- Anthropic no ofrece API de embeddings (ADR-003, punto 4) y recomienda Voyage AI.
- Es un portafolio sin presupuesto: desarrollar y probar no debería costar dinero.
- Los textos están en español, con jerga de soporte bancario ("no me deja entrar", "acceso denegado", "SINPE"), así que el modelo tiene que ser multilingüe.
- Todo lo que va al proveedor de embeddings va enmascarado, igual que al LLM (`AGENTS.md`).
- `pgvector` vive en el esquema `mesa_servicio` (`docs/ARQUITECTURA.md`): quien calcula y guarda los vectores es `mesa-servicio`, no el agente.

## Opciones evaluadas

| Opción | Encaje | Ventajas aquí | Desventajas aquí |
|---|---|---|---|
| **Voyage AI (`voyage-4`)** | **Alto** | El proveedor que recomienda Anthropic; optimizado para búsqueda multilingüe; 200 millones de tokens gratis por cuenta, muchísimo más de lo que usa el proyecto; sin contenedores ni RAM extra | Otra API key que resguardar; el texto sale de la máquina (enmascarado); el CI no puede depender de la red |
| Ollama local con `bge-m3` | Medio | Gratis y sin que el texto salga de la máquina | Un contenedor más y del orden de 1 a 2 GB de RAM y disco; más lento en CPU |
| OpenAI `text-embedding-3-small` | Bajo | Barato y estable | Cobra desde el primer token |
| Modelo dentro de la JVM (ONNX) | Bajo | Sin servicio externo | Dependencias pesadas en el monolito; cada réplica carga el modelo |
| Solo búsqueda de texto de PostgreSQL | Bajo como única solución | Lo más simple | No encuentra lo que significa lo mismo con otras palabras, que es justo el valor de la fase |

## Decisión

1. **Puerto `ProveedorEmbeddings` en el módulo `conocimiento`** de `mesa-servicio`. Es una frontera real (un servicio externo), así que se justifica la interfaz.
2. **Adaptador escrito a mano, `ClienteEmbeddingsVoyage`,** que llama a `POST https://api.voyageai.com/v1/embeddings`. Usa `input_type: "document"` al guardar y `"query"` al buscar, como recomienda Voyage, y manda varios textos por petición.
3. **Modelo `voyage-4`, 1024 dimensiones** (la dimensión por defecto). Modelo, URL, dimensión y límites van por configuración, nunca en el código.
4. **La API key es un secreto en archivo** (`voyage_api_key`, solo para `mesa-servicio`; ADR-005). Nunca va en variables de entorno, en `.env`, en logs ni en el chat. La carga Juan Pablo en `secrets/`, y en el repo solo está cifrada en `secrets.enc.env`.
5. **Los vectores se calculan fuera de la transacción del usuario.** Crear un ticket nunca depende de Voyage: el ticket queda pendiente y un proceso programado (ShedLock, `SKIP LOCKED`) calcula en lotes los embeddings que faltan. Así se aguantan varias réplicas, una caída del proveedor y sus límites de peticiones.
6. **Búsqueda híbrida con respaldo.** Primero se busca por similitud de vectores, con un umbral mínimo y un límite de resultados. Se pasa a la búsqueda de texto de PostgreSQL (`tsvector` en español) en tres casos:
   - el ticket aún no tiene vector;
   - Voyage no responde;
   - no hay API key configurada.

   Quien clone el repo sin cuenta de Voyage igual puede correr todo.
7. **Errores clasificados, igual que en el agente.**
   - Key inválida, permiso o petición inválida (`401`, `403`, `400`): no se reintentan; se pausan las llamadas y se alerta.
   - `429`, `5xx` y timeouts: se reintentan con backoff, respetando `retry-after`, hasta un máximo.
8. **Solo texto enmascarado.** Se usa el mismo enmascarado que antes de publicar `TicketCreado`, y se verifica antes de cada llamada.
9. **Cada vector guarda el modelo que lo generó.** La dimensión queda fija en la migración (`vector(1024)`). Cambiar de modelo obliga a recalcular todo, y nunca se mezclan vectores de modelos distintos en una búsqueda.
10. **El agente no calcula embeddings.** Sus herramientas llaman a la API interna de solo lectura de `mesa-servicio`, que convierte la consulta en vector y busca. El agente no gana dependencias ni secretos nuevos.

## Consecuencias

- **Pruebas:** el CI no llama a Voyage. Las pruebas usan un proveedor falso determinista con PostgreSQL + pgvector real (Testcontainers). Una prueba opcional contra Voyage real se activa con una variable, como las del modelo real en el agente.
- **Costo:** los primeros 200 millones de tokens son gratis. Después, `voyage-4` cuesta USD 0,06 por millón (precios de la documentación de Voyage en noviembre de 2026). Los tokens de cada llamada (`usage.total_tokens`) se registran, y el precio va en `reportes.parametros`, junto al del LLM (`docs/KPIS.md`).
- **Límites de peticiones:** sin método de pago, la documentación de Voyage no publica los límites de la cuenta, y probablemente son bajos. No es un problema: el cálculo es en segundo plano y en lotes, y un `429` solo atrasa el vector.
- **Uso de los datos para entrenar:** Voyage permite excluirse de que use los textos para entrenar, pero solo con un método de pago registrado. Mientras no haya uno, se acepta por las mismas razones de ADR-006: datos 100 % sintéticos y enmascarado verificado antes de cada llamada.
- **Seguridad:** se agrega a `docs/SEGURIDAD.md`:
  - la key de Voyage como secreto en archivo;
  - enmascarado verificado antes de cada embedding;
  - los resultados similares siguen siendo datos no confiables para el agente: que algo sea "similar" no lo vuelve confiable.
- **Reversible:** pasar a otro proveedor (por ejemplo Ollama local) es un segundo adaptador del mismo puerto, como en ADR-006, más recalcular los vectores si cambia el modelo.
