# Agente de triaje (fase 1)

Agente de IA escrito desde cero en C# (.NET 10), sin frameworks de agentes. Recibe un ticket de la mesa de servicio, investiga con tres herramientas de solo lectura y devuelve una **sugerencia** de triaje: categoría, sistema afectado, prioridad, posibles duplicados, artículos y un borrador de respuesta. **No toma ninguna acción**: un técnico acepta, corrige o descarta la sugerencia.

## El ciclo

```mermaid
flowchart LR
    T[ticket.json] --> V{¿datos sin<br/>enmascarar?}
    V -- sí --> E[error: no se llama al modelo]
    V -- no --> M[modelo]
    M -- pide herramienta --> H[herramienta<br/>solo lectura] --> V
    M -- respuesta final --> S{¿cumple el<br/>contrato?}
    S -- sí --> C[completado]
    S -- no --> I[invalido]
    M -. límite de iteraciones,<br/>tokens o tiempo .-> P[presupuesto_agotado]
```

El ciclo (`Aplicacion/CicloAgente.cs`) y el cliente HTTP (`Infraestructura/Llm/ClienteAnthropicHttp.cs`) están escritos a mano: armar el pedido, reenviar la respuesta del modelo tal cual, ejecutar herramientas, devolver resultados y repetir. Los pasos están numerados con comentarios `[FLUJO TRIAJE_AGENTE N]` (ver `docs/FLUJOS.md`).

## Cómo correrlo

Requisitos: SDK de .NET 10 (`global.json` lo fija).

```bash
cd services/agente
dotnet test                                                      # 84 pruebas: 82 sin gastar tokens, 2 con el modelo real (saltadas)

# Primera vez: la API key va como secreto, fuera del repo (ADR-005)
dotnet user-secrets set "Llm:ApiKey" "<tu key>" --project src/Centinela.Agente

dotnet run --project src/Centinela.Agente -- ejemplos/ticket.json
```

El resultado sale por stdout. Los logs, en JSON y solo con IDs y métricas, salen por stderr.

### Usar Qwen en vez de Claude (gratis o casi, ADR-006)

El agente habla con dos formatos: la API de Anthropic y la de OpenAI (Chat Completions), que es la que usan Qwen y Ollama. Se elige por configuración, sin tocar código. Estos valores quedan en tu máquina, fuera del repo:

```bash
P=src/Centinela.Agente
dotnet user-secrets set "Llm:Proveedor" "openai_compatible" --project $P
dotnet user-secrets set "Llm:UrlBase"   "<URL de la tabla>" --project $P
dotnet user-secrets set "Llm:Modelo"    "<modelo>"          --project $P
dotnet user-secrets set "Llm:ApiKey"    "<tu key>"          --project $P   # no hace falta con Ollama local
```

| Dónde corre Qwen | `Llm:UrlBase` | Ejemplo de `Llm:Modelo` |
|---|---|---|
| Alibaba Cloud Model Studio (internacional) | `https://dashscope-intl.aliyuncs.com/compatible-mode/v1/` | `qwen-plus` |
| OpenRouter | `https://openrouter.ai/api/v1/` | un modelo Qwen con sufijo `:free` |
| Ollama en tu PC (sin key ni internet, gratis) | `http://localhost:11434/v1/` | `qwen3:4b` |
| Ollama Cloud (key en ollama.com/settings/keys; se paga por token, no entrena con los prompts) | `https://ollama.com/v1/` | `gpt-oss:120b` (no tiene Qwen oficial) |

Los nombres de modelos y las cuotas gratuitas cambian seguido: hay que confirmarlos en la página de cada proveedor. Con modelos chicos o gratuitos, más tickets pueden terminar en `invalido`, y eso es lo correcto: la validación no se relaja por el proveedor.

### Configuración

En `src/Centinela.Agente/appsettings.json`, y se puede sobrescribir con variables de entorno (`Llm__Modelo`, `Agente__MaxIteraciones`, ...):

| Clave | Por defecto | Para qué |
|---|---|---|
| `Llm:Proveedor` | `anthropic` | `anthropic` u `openai_compatible` (Qwen, Ollama, OpenRouter…) |
| `Llm:Modelo` | `claude-opus-5-5` | Modelo. `claude-haiku-5-5` cuesta unas 40 veces menos: medirlo con la suite antes de cambiar |
| `Llm:Esfuerzo` | `medium` | Profundidad del razonamiento (`low` … `max`) |
| `Llm:UsarFallbackServidor` | `true` | Si el modelo rechaza por sus filtros, la API reintenta en otro modelo |
| `Agente:MaxIteraciones` | 6 | Llamadas al modelo por ticket |
| `Agente:MaxTokensTicket` | 40000 | Tokens de entrada + salida por ticket |
| `Agente:TimeoutSegundos` | 90 | Tiempo máximo por ticket |
| `Agente:MaxReintentos` | 2 | Reintentos de errores `429`, `529`, `5xx` y de red |

## Ejemplo

Entrada (`ejemplos/ticket.json`, ya enmascarada como la mandaría mesa-servicio):

```json
{
  "ticketId": "5b0e4c7a-2d1f-4e8b-9c3a-6f7e8d9c0b1a",
  "tipo": "incidente",
  "titulo": "No puedo conectarme a la VPN desde casa",
  "descripcion": "Desde esta mañana el cliente de VPN se queda en 'conectando' y después dice que falló la autenticación. ...",
  "sistemaReportado": "vpn",
  "canal": "portal"
}
```

Salida: el contrato `TicketTriado` de `docs/EVENTOS.md`. Este ejemplo muestra la forma; se reemplaza por la salida de la primera corrida real.

```json
{
  "ticketId": "5b0e4c7a-2d1f-4e8b-9c3a-6f7e8d9c0b1a",
  "estadoAnalisis": "completado",
  "categoria": "red_y_conectividad",
  "sistemaAfectado": "vpn",
  "prioridadSugerida": "P3",
  "justificacion": "Afecta a una persona; hay un caso igual resuelto (TK-1001) y un incidente abierto de VPN (INC-501) que puede estar relacionado.",
  "duplicadosPosibles": ["TK-1001"],
  "articulosSugeridos": ["KB-001", "KB-002"],
  "borradorRespuesta": "Hola, recibimos tu reporte...",
  "metricas": { "modelo": "claude-opus-5-5", "tokensEntrada": 0, "tokensSalida": 0, "iteraciones": 0, "duracionMs": 0 }
}
```

Si el estado no es `completado` (`invalido`, `presupuesto_agotado` o `error`), todos los campos de sugerencia van en `null` y el ticket queda para triaje manual. **Nunca se sugiere ni se aprueba por defecto.**

## Defensas que ya prueba la suite

| Ataque | Defensa | Prueba |
|---|---|---|
| El ticket ordena "ponle P1" o intenta cerrar la etiqueta `<ticket>` | Ticket en el mensaje del usuario, serializado como JSON (el `<` sale escapado) y delimitado; instrucciones fijas en otro campo | `caso-01` |
| Pide revelar las instrucciones | Las instrucciones no dependen del ticket y prohíben revelarse | `caso-02` |
| Cédula, tarjeta o correo sin enmascarar | Verificación antes de **cada** llamada; si hay algo, no se llama al modelo | `caso-03` |
| Salida rota o manipulada (`"aprobado": true`, `P0`) | Esquema de la API + validación propia de catálogos, largos e IDs vistos | `caso-04` |
| Ciclo infinito para gastar tokens | Límites de iteraciones, tokens y tiempo | `caso-05` |
| El modelo pide datos de otro ticket o cliente | Las herramientas no tienen parámetro de ticket ni de cliente y rechazan parámetros extra | `CicloAgenteTests` |
| Saldo agotado o key inválida | No se reintenta; alerta crítica en el log y triaje manual | `CicloAgenteTests` |

## Qué se aprendió

- **El ciclo es poco código; los límites son la mayor parte.** El loop modelo → herramienta → resultado entra en pocas líneas. Lo que lo hace usable en un banco es todo lo demás: presupuesto, validación, enmascarado, reintentos clasificados y "ante la duda, triaje manual".
- **La respuesta del modelo se reenvía tal cual.** Con razonamiento adaptativo la respuesta trae bloques de pensamiento firmados; reconstruirla a mano los rompe. Por eso el ciclo guarda el contenido del proveedor como algo opaco.
- **El esquema de la API no alcanza.** Garantiza la forma del JSON, pero no los largos ni que los IDs sugeridos existan. El modelo solo puede sugerir IDs que vio en las herramientas durante esa conversación.
- **La suite adversaria encontró algo real en su primera corrida:** el mensaje mencionaba la etiqueta `</ticket>` dos veces (una en la frase de introducción), y eso deja ambiguo dónde termina el bloque de datos.
- **Costo:** con `claude-opus-5-5` un triaje de 2 o 3 llamadas ronda los 15.000 tokens de entrada y 2.000 de salida, unos USD 0,10. La fase 7 mide si un modelo más barato mantiene la calidad.
