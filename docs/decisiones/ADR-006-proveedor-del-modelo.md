# ADR-006 — Proveedor del modelo configurable (Qwen para desarrollo)

- **Estado:** Aceptado (pedido por Juan Pablo el 2026-11-11)
- **Fecha:** 2026-11-11

## Contexto

- Es un proyecto de portafolio sin presupuesto: cada corrida contra la API de Anthropic cuesta dinero, y desarrollar y probar requiere muchas corridas.
- Juan Pablo pidió usar **Qwen**, que se puede usar gratis o casi gratis: en la nube (Alibaba Cloud Model Studio, OpenRouter) o local con Ollama en su PC de escritorio.
- Esos proveedores hablan el formato de OpenAI Chat Completions, no la API de mensajes de Anthropic.
- El agente ya tiene el puerto `IClienteLlm`: el ciclo no conoce el formato del proveedor.

## Opciones evaluadas

| Opción | Encaje | Ventajas aquí | Desventajas aquí |
|---|---|---|---|
| Solo Anthropic | Medio | Un solo adaptador; el modelo más robusto ante los ataques de la suite | Cada prueba real cuesta dinero |
| Reemplazar Anthropic por Qwen | Bajo | Gratis | Se pierde el proveedor más robusto para el demo y las pruebas de seguridad |
| **Dos adaptadores del mismo puerto, elegidos por configuración** | **Alto** | Desarrollo gratis con Qwen; Claude para el demo y la suite adversaria real. Muestra en la práctica para qué sirve la arquitectura hexagonal | Un adaptador más que mantener y probar |

## Decisión

Dos adaptadores escritos a mano para `IClienteLlm`, elegidos con `Llm:Proveedor`:

- `anthropic`: `ClienteAnthropicHttp` (API de mensajes, salida estructurada, reserva automática del servidor ante rechazos).
- `openai_compatible`: `ClienteOpenAiCompatibleHttp` (Chat Completions). Sirve para Qwen en Alibaba Cloud Model Studio u OpenRouter, para Ollama local y para cualquier otro proveedor con ese formato.

Lo que no cambia con el proveedor: el ciclo, los límites, la verificación de enmascarado, la validación propia de la salida, la clasificación de errores y las métricas.

## Consecuencias

- La salida estructurada (`response_format`) viene apagada en `openai_compatible`, porque muchos proveedores no la combinan con herramientas. El esquema va en las instrucciones, y la validación propia sigue siendo la barrera real: si el modelo no cumple, el ticket queda `invalido`.
- El adaptador quita lo que agregan los modelos alrededor del JSON: el razonamiento de Qwen entre `<think>` y `</think>`, y los bloques de Markdown.
- Sin API key solo se acepta un servidor local (`localhost`), y `http` solo hacia `localhost`: una key nunca viaja sin cifrar.
- Los proveedores gratuitos pueden usar los prompts para entrenar sus modelos. No es un problema mientras se cumplan dos reglas que ya existen: datos 100 % sintéticos y enmascarado verificado antes de cada llamada.
- Los modelos chicos o gratuitos usan peor las herramientas y resisten peor la inyección de instrucciones. Por eso la suite adversaria se corre con el modelo que se vaya a mostrar, y el resultado se reporta junto con el nombre del modelo.
