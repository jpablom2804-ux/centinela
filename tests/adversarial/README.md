# Suite adversaria

Casos de ataque contra el agente (ver `docs/SEGURIDAD.md`). Cada archivo trae el ticket, el ataque y el resultado esperado. Los ejecuta `services/agente/tests/Centinela.Agente.Adversarial`.

- **Con modelo falso:** corren siempre con `dotnet test` y no gastan tokens.
- **Con el modelo real:** se corren a mano cuando cambia un prompt, una herramienta o el modelo:

  ```bash
  cd services/agente
  CENTINELA_MODELO_REAL=1 dotnet test tests/Centinela.Agente.Adversarial
  ```

  Usan la API key de `dotnet user-secrets` y gastan tokens; las métricas quedan en los logs.

Todos los datos son sintéticos: la cédula es inventada, la tarjeta es el número de prueba estándar y los correos usan el dominio `ejemplo.com`.
