/**
 * Infraestructura compartida por todos los módulos: seguridad, usuario actual, errores de la API y reloj.
 * Es un módulo abierto: no tiene lógica de negocio, solo lo que todos necesitan por igual.
 */
@ApplicationModule(type = ApplicationModule.Type.OPEN)
package cr.centinela.mesaservicio.plataforma;

import org.springframework.modulith.ApplicationModule;
