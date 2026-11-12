package cr.centinela.generador.generacion;

import java.util.List;

/**
 * Casos típicos de soporte de TI en una entidad financiera, inventados a partir de tipos de casos reales
 * (nunca copiados de tickets reales). Sin datos personales: ni nombres, ni cédulas, ni cuentas, ni correos.
 */
final class CatalogoCasos {

    record Caso(String tipo, String sistema, List<String> titulos, List<String> descripciones) {}

    private CatalogoCasos() {}

    static final List<Caso> CASOS = List.of(
            new Caso("incidente", "vpn",
                    List.of("No puedo conectarme a la VPN", "La VPN se desconecta a cada rato", "Error de autenticación en la VPN"),
                    List.of("Desde esta mañana el cliente de VPN se queda en conectando y luego da error.",
                            "Trabajo desde casa y la VPN se cae cada pocos minutos, pierdo lo que estaba haciendo.",
                            "La VPN dice que falló la autenticación aunque la contraseña es la misma de ayer.")),
            new Caso("incidente", "directorio_activo",
                    List.of("Cuenta bloqueada", "No puedo iniciar sesión en la computadora"),
                    List.of("Al encender la computadora me dice que la cuenta está bloqueada por intentos fallidos.",
                            "Cambié la contraseña ayer y hoy no me deja entrar a la computadora.")),
            new Caso("incidente", "correo",
                    List.of("Outlook no sincroniza", "No me llegan correos externos"),
                    List.of("Los correos nuevos no aparecen en Outlook, pero sí en el celular.",
                            "Desde ayer no recibo correos de proveedores, los internos sí llegan.")),
            new Caso("incidente", "correo",
                    List.of("Correo sospechoso pidiendo validar la cuenta", "Posible phishing"),
                    List.of("Recibí un correo que pide validar mi cuenta del banco con un enlace extraño. No hice clic.",
                            "Varios compañeros recibimos el mismo correo con un archivo adjunto que no esperábamos.")),
            new Caso("incidente", "impresion",
                    List.of("La impresora del piso no imprime", "Atasco permanente en la impresora"),
                    List.of("Los trabajos quedan en cola y la impresora compartida no imprime nada.",
                            "La impresora marca atasco aunque no hay papel trabado.")),
            new Caso("incidente", "core_bancario",
                    List.of("Lentitud en el core bancario", "El core bancario no responde en la agencia"),
                    List.of("Las consultas de clientes tardan más de un minuto en toda la agencia.",
                            "En la agencia no podemos registrar depósitos: el sistema central no responde.")),
            new Caso("incidente", "sinpe",
                    List.of("Transferencias SINPE rechazadas", "SINPE Móvil no confirma pagos"),
                    List.of("Las transferencias SINPE salientes quedan rechazadas con un código de error del canal.",
                            "Los clientes reportan que los pagos por SINPE Móvil quedan pendientes sin confirmación.")),
            new Caso("incidente", "app_movil",
                    List.of("La app móvil se cierra al iniciar sesión", "Clientes no pueden entrar a la app"),
                    List.of("Varios clientes reportan que la app se cierra justo después de ingresar el usuario.",
                            "Desde la actualización de anoche los clientes no pueden entrar a la app móvil.")),
            new Caso("incidente", "banca_en_linea",
                    List.of("Banca en línea muy lenta", "Error al consultar movimientos en banca en línea"),
                    List.of("La consulta de movimientos en banca en línea tarda mucho y a veces da error.",
                            "Los clientes ven un error genérico al abrir el detalle de sus movimientos.")),
            new Caso("incidente", "estaciones_de_trabajo",
                    List.of("La computadora no enciende", "Windows Update falla en mi equipo"),
                    List.of("La computadora no enciende desde que hubo un corte de luz en el edificio.",
                            "Las actualizaciones de Windows fallan siempre al reiniciar.")),
            new Caso("solicitud", "directorio_activo",
                    List.of("Acceso a carpeta compartida", "Alta de usuario para persona nueva"),
                    List.of("Necesito acceso de lectura a la carpeta compartida del área de cumplimiento.",
                            "Ingresa una persona nueva al equipo el lunes y necesita usuario y correo.")),
            new Caso("solicitud", "estaciones_de_trabajo",
                    List.of("Instalar software de análisis", "Cambio de monitor"),
                    List.of("Necesito que me instalen la herramienta de análisis de datos aprobada por el área.",
                            "El monitor parpadea y me cuesta trabajar, solicito un cambio.")),
            new Caso("problema", "core_bancario",
                    List.of("Lentitud recurrente al cierre de mes"),
                    List.of("Cada cierre de mes el core bancario se pone lento por las tardes. Ya pasó tres meses seguidos.")),
            new Caso("problema", "vpn",
                    List.of("Desconexiones de VPN con un proveedor de internet"),
                    List.of("Los usuarios con el mismo proveedor de internet reportan desconexiones de VPN desde hace semanas.")),
            new Caso("cambio", "red_interna",
                    List.of("Actualizar firmware de los switches del piso 3"),
                    List.of("Se propone actualizar el firmware de los switches en la ventana del sábado, con plan de reversa.")));

    static final List<String> CANALES = List.of("portal", "portal", "portal", "correo", "telefono");
}
