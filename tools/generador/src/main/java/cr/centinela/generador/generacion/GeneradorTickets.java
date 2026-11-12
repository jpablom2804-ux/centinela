package cr.centinela.generador.generacion;

import java.util.random.RandomGenerator;
import java.util.random.RandomGeneratorFactory;

/** Arma tickets sintéticos a partir del catálogo. Con la misma semilla genera siempre la misma secuencia. */
public final class GeneradorTickets {

    private final RandomGenerator azar;

    public GeneradorTickets(long semilla) {
        this.azar = RandomGeneratorFactory.of("L64X128MixRandom").create(semilla);
    }

    public TicketSintetico siguiente() {
        var caso = elegir(CatalogoCasos.CASOS);
        // Una de cada tres veces el usuario no indica el sistema, como pasa en la realidad.
        var sistema = azar.nextInt(3) == 0 ? null : caso.sistema();
        return new TicketSintetico(caso.tipo(), elegir(caso.titulos()), elegir(caso.descripciones()), sistema,
                elegir(CatalogoCasos.CANALES));
    }

    private <T> T elegir(java.util.List<T> opciones) {
        return opciones.get(azar.nextInt(opciones.size()));
    }
}
